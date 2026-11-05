using System.Windows;
using System.Windows.Input;
using PlamoTranslatePopup.Utilities;

namespace PlamoTranslatePopup;

/// <summary>
/// カーソル近くに表示する翻訳結果ポップアップ。
/// 一定時間後または閉じる操作で自動的に消える。
/// </summary>
public partial class TranslateWindow : Window
{
    private System.Windows.Threading.DispatcherTimer? _autoCloseTimer;

    public TranslateWindow()
    {
        InitializeComponent();
        ContentRendered += TranslateWindow_ContentRendered;
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

        StartAutoCloseTimer();
    }

    public void SetResult(string text)
    {
        ResultBox.Text = text;
        RepositionNearCursor();
        RestartAutoCloseTimer();
    }

    private void TranslateWindow_ContentRendered(object? sender, EventArgs e)
    {
        RepositionNearCursor();
    }

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
        var seconds = App.CurrentSettings.AutoCloseSeconds;
        if (seconds <= 0)
            return;

        _autoCloseTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(seconds)
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
        base.OnClosed(e);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
