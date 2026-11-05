using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PlamoTranslatePopup.Services;

/// <summary>
/// Win32 API によるシステムトレイアイコン(translate-popup2 と同一方式)
/// </summary>
public sealed class NotifyIconService : IDisposable
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr Shell_NotifyIcon(uint message, ref NOTIFYICONDATA pnid);

    [StructLayout(LayoutKind.Sequential)]
    private class NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip = string.Empty;
        public uint dwState;
        public uint dwStateMask;
        public uint dwInfoFlags;
    }

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint WM_USER = 0x0400;
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint WM_LBUTTONUP = 0x0202;

    private readonly HwndSource? _hwndSource;
    private readonly Window _messageWindow;
    private NOTIFYICONDATA _nid;
    private bool _disposed;

    public event EventHandler? LeftClick;
    public event EventHandler? ContextMenuRequested;

    public NotifyIconService(string tooltip)
    {
        _messageWindow = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Width = 0,
            Height = 0,
            ShowInTaskbar = false,
            ShowActivated = false,
            Background = System.Windows.Media.Brushes.Transparent
        };
        _messageWindow.Show();

        _hwndSource = PresentationSource.FromVisual(_messageWindow) as HwndSource;
        _hwndSource?.AddHook(WndProc);

        _nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA)),
            hWnd = _hwndSource?.Handle ?? IntPtr.Zero,
            uID = 0,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_USER,
            szTip = tooltip,
        };

        // アプリ独自のアイコン(青地に「訳」)を描画して使用する
        _nid.hIcon = CreateAppIcon();
        Shell_NotifyIcon(NIM_ADD, ref _nid);
    }

    /// <summary>System.Drawing でアプリアイコンを描画し HICON を返す</summary>
    private static IntPtr CreateAppIcon()
    {
        const int size = 32;
        using var bmp = new System.Drawing.Bitmap(size, size);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            // 角丸の青い背景
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            var r = 7;
            var rect = new System.Drawing.Rectangle(1, 1, size - 2, size - 2);
            path.AddArc(rect.X, rect.Y, r * 2, r * 2, 180, 90);
            path.AddArc(rect.Right - r * 2, rect.Y, r * 2, r * 2, 270, 90);
            path.AddArc(rect.Right - r * 2, rect.Bottom - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(rect.X, rect.Bottom - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            using var bg = new System.Drawing.Drawing2D.LinearGradientBrush(
                rect, System.Drawing.Color.FromArgb(63, 130, 246), System.Drawing.Color.FromArgb(29, 78, 216), 60f);
            g.FillPath(bg, path);

            // 中央に「訳」
            using var font = new System.Drawing.Font("Yu Gothic UI", 15f, System.Drawing.FontStyle.Bold,
                System.Drawing.GraphicsUnit.Pixel);
            using var textBrush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
            var fmt = new System.Drawing.StringFormat
            {
                Alignment = System.Drawing.StringAlignment.Center,
                LineAlignment = System.Drawing.StringAlignment.Center,
            };
            g.DrawString("訳", font, textBrush, new System.Drawing.RectangleF(0, -1, size, size), fmt);
        }
        return bmp.GetHicon();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_USER)
        {
            switch ((uint)lParam.ToInt32())
            {
                case WM_LBUTTONUP:
                    LeftClick?.Invoke(this, EventArgs.Empty);
                    handled = true;
                    break;

                case WM_CONTEXTMENU:
                    ContextMenuRequested?.Invoke(this, EventArgs.Empty);
                    handled = true;
                    break;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_nid.hWnd != IntPtr.Zero)
            Shell_NotifyIcon(NIM_DELETE, ref _nid);

        if (_nid.hIcon != IntPtr.Zero)
            DestroyIcon(_nid.hIcon);

        _hwndSource?.RemoveHook(WndProc);
        _hwndSource?.Dispose();
        _messageWindow?.Close();

        _disposed = true;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
