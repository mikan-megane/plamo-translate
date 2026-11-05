using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PlamoTranslatePopup.Services;

/// <summary>
/// Win32 API によるグローバルホットキー(translate-popup2 と同一方式)
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    private HwndSource? _hwndSource;
    private readonly List<int> _registeredIds = new();
    private bool _disposed;

    public event Action<int>? HotkeyPressed;

    public HotkeyService()
    {
        // メッセージ処理専用の隠しウィンドウ
        var helperWindow = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Width = 0,
            Height = 0,
            ShowInTaskbar = false,
            ShowActivated = false,
            Background = System.Windows.Media.Brushes.Transparent
        };
        helperWindow.Show();

        _hwndSource = PresentationSource.FromVisual(helperWindow) as HwndSource;
        _hwndSource?.AddHook(WndProc);
    }

    public void Register(uint modifiers, uint virtualKey, int id = 1)
    {
        if (_hwndSource == null)
            throw new InvalidOperationException("HotkeyService is not initialized.");

        if (!RegisterHotKey(_hwndSource.Handle, id, modifiers, virtualKey))
            throw new InvalidOperationException(
                $"ホットキーの登録に失敗しました (error {Marshal.GetLastWin32Error()})");

        _registeredIds.Add(id);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            HotkeyPressed?.Invoke(wParam.ToInt32());
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_hwndSource != null)
        {
            foreach (var id in _registeredIds)
                UnregisterHotKey(_hwndSource.Handle, id);
            _hwndSource.RemoveHook(WndProc);
            _hwndSource.Dispose();
            _hwndSource = null;
        }

        _disposed = true;
    }
}
