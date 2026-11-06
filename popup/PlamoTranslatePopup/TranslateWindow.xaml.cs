using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using PlamoTranslatePopup.Utilities;

namespace PlamoTranslatePopup;

/// <summary>
/// カーソル近くに表示する翻訳結果ポップアップ。
/// - 通常は設定の透明度で表示し、ホバー中は不透明にして読みやすくする
/// - 一定時間後または閉じる操作で自動的に消える(ホバー中は閉じない)
/// - タイトル部分をドラッグして移動できる
/// - ポップアップの外をクリックすると閉じる
/// </summary>
public partial class TranslateWindow : Window
{
    private const int WH_MOUSE_LL = 14;
    private const long WM_LBUTTONDOWN = 0x0201;
    private const long WM_RBUTTONDOWN = 0x0204;

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    private System.Windows.Threading.DispatcherTimer? _autoCloseTimer;
    private TimeSpan _autoCloseDelay = TimeSpan.FromSeconds(30);
    private double _baseOpacity = 1.0;
    private IntPtr _mouseHook = IntPtr.Zero;
    private readonly LowLevelMouseProc _mouseHookProc;

    public TranslateWindow()
    {
        InitializeComponent();
        ContentRendered += TranslateWindow_ContentRendered;

        _baseOpacity = Math.Clamp(App.CurrentSettings.PopupOpacity, 0.3, 1.0);
        Opacity = _baseOpacity;

        // ホバー中は不透明にして読みやすくし、自動クローズも止める
        MouseEnter += (_, _) =>
        {
            Opacity = 1.0;
            _autoCloseTimer?.Stop();
        };
        MouseLeave += (_, _) =>
        {
            if (IsLoaded)
            {
                Opacity = _baseOpacity;
                RestartAutoCloseTimer();
            }
        };

        // ポップアップの外をクリックしたら閉じる(低レベルマウスフックで検知)
        _mouseHookProc = MouseHookProc;
        SourceInitialized += (_, _) =>
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseHookProc, GetModuleHandle(null), 0);
    }

    private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsLoaded
            && (wParam.ToInt64() == WM_LBUTTONDOWN || wParam.ToInt64() == WM_RBUTTONDOWN)
            && !IsCursorOverWindow())
        {
            Dispatcher.BeginInvoke(Close);
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    /// <summary>カーソルがこのウィンドウの上にあるか</summary>
    private bool IsCursorOverWindow()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
            return false;

        var (dipX, dipY) = CursorUtilities.GetCursorDipPosition();
        return dipX >= Left && dipX <= Left + ActualWidth
            && dipY >= Top && dipY <= Top + ActualHeight;
    }

    /// <summary>タイトル部分をドラッグして移動する</summary>
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled)
            return;
        try
        {
            DragMove();
        }
        catch
        {
            // マウスボタンが既に離れていた場合は無視
        }
    }

    /// <summary>カーソルの近くに表示する(結果は後で SetResult で差し込む)</summary>
    public void ShowAtCursor(string sourceLang, string targetLang)
    {
        DirectionText.Text = $"{sourceLang} → {targetLang}";
        ResultBox.Text = "翻訳中...";

        // 一度表示してサイズを確定させてから位置を決める
        var (dipX, dipY) = CursorUtilities.GetCursorDipPosition();
        Left = dipX + 12;
        Top = dipY + 12;
        Show();

        _autoCloseDelay = App.CurrentSettings.AutoCloseSeconds > 0
            ? TimeSpan.FromSeconds(Math.Min(3600, App.CurrentSettings.AutoCloseSeconds))
            : TimeSpan.Zero; // 0 = 自動で閉じない
        StartAutoCloseTimer();
    }

    /// <summary>短いメッセージをカーソル近くに表示し、2.5秒で自動的に閉じる(選択なし等のフィードバック用)</summary>
    public void ShowHintAtCursor(string message)
    {
        DirectionText.Text = "情報";
        ResultBox.Text = message;

        var (dipX, dipY) = CursorUtilities.GetCursorDipPosition();
        Left = dipX + 12;
        Top = dipY + 12;
        Show();

        _autoCloseDelay = TimeSpan.FromSeconds(2.5);
        StartAutoCloseTimer();
    }

    /// <summary>原文(OCR結果)をヘッダーの下に表示する。ShowAtCursor の前に呼ぶ</summary>
    public void SetSource(string text)
    {
        SourceBox.Text = text;
        SourceBox.Visibility = Visibility.Visible;
    }

    /// <summary>ストリーミング表示の開始(プレースホルダを消す)</summary>
    public void BeginStream() =>
        ResultBox.Text = string.Empty;

    /// <summary>ヘッダーの言語方向表示を差し替える(フォールバック時の告知用)</summary>
    public void SetDirectionText(string text) =>
        DirectionText.Text = text;

    /// <summary>ストリーミングで訳文を追記する(位置は変更しない)</summary>
    public void AppendResult(string delta)
    {
        ResultBox.AppendText(delta);
        ResultBox.CaretIndex = ResultBox.Text.Length;
    }

    /// <summary>翻訳結果を差し込む。ウィンドウ位置は変えない(ユーザーが動かした位置も保持)</summary>
    public void SetResult(string text)
    {
        ResultBox.Text = text;
        RestartAutoCloseTimer();
    }

    private void TranslateWindow_ContentRendered(object? sender, EventArgs e)
    {
        RepositionNearCursor();
    }

    /// <summary>画面からはみ出ないよう位置を補正する(初回表示時のみ)</summary>
    private void RepositionNearCursor()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
            return;

        var (dipX, dipY) = CursorUtilities.GetCursorDipPosition();
        var (left, top) = WindowPositionCalculator.CalculateWindowPosition(
            dipX, dipY, ActualWidth, ActualHeight);
        Left = left;
        Top = top;
    }

    private void StartAutoCloseTimer()
    {
        if (_autoCloseDelay <= TimeSpan.Zero)
            return;

        _autoCloseTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = _autoCloseDelay
        };
        _autoCloseTimer.Tick += (_, _) => Close();
        _autoCloseTimer.Start();
    }

    private void RestartAutoCloseTimer()
    {
        _autoCloseTimer?.Stop();
        StartAutoCloseTimer();
    }

    protected override void OnClosed(EventArgs e)
    {
        _autoCloseTimer?.Stop();
        _autoCloseTimer = null;

        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        base.OnClosed(e);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
