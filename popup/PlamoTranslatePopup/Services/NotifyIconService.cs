using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PlamoTranslatePopup.Services;

/// <summary>
/// WinForms の NotifyIcon によるシステムトレイアイコン。
/// Win32 直呼びに比べ、NOTIFYICONDATA のマーシャリングや
/// Explorer 再起動時の再登録(TaskbarCreated)をコンポーネント側で処理してくれるため確実。
/// </summary>
public sealed class NotifyIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private readonly IntPtr _hIcon;
    private bool _disposed;

    public event EventHandler? LeftClick;
    public event EventHandler? ContextMenuRequested;

    public NotifyIconService(string tooltip)
    {
        (_icon, _hIcon) = CreateAppIcon();
        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = ClampTooltip(tooltip),
            Visible = true,
        };
        _notifyIcon.MouseClick += OnMouseClick;
        _notifyIcon.MouseUp += OnMouseUp;
    }

    private void OnMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            LeftClick?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 右クリック(WM_CONTEXTMENU の直前)。ここでメニューを差し込むと
    /// NotifyIcon が自動的にフォーカス処理込みで表示してくれる。
    /// </summary>
    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
            ContextMenuRequested?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateTooltip(string tooltip) =>
        _notifyIcon.Text = ClampTooltip(tooltip);

    public void ShowBalloon(string title, string message, int timeoutMs = 8000)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.ShowBalloonTip(timeoutMs);
    }

    /// <summary>右クリックで自動表示されるメニューを差し替える</summary>
    public void ShowContextMenu(ContextMenuStrip menu) =>
        _notifyIcon.ContextMenuStrip = menu;

    private static string ClampTooltip(string tooltip) =>
        tooltip.Length > 63 ? tooltip[..63] : tooltip; // NotifyIcon.Text は 63 文字まで

    /// <summary>System.Drawing でアプリアイコン(青地に「訳」)を描画する</summary>
    private static (Icon icon, IntPtr hIcon) CreateAppIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            // 角丸の青い背景
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            var r = 7;
            var rect = new Rectangle(1, 1, 30, 30);
            path.AddArc(rect.X, rect.Y, r * 2, r * 2, 180, 90);
            path.AddArc(rect.Right - r * 2, rect.Y, r * 2, r * 2, 270, 90);
            path.AddArc(rect.Right - r * 2, rect.Bottom - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(rect.X, rect.Bottom - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            using var bg = new System.Drawing.Drawing2D.LinearGradientBrush(
                rect, Color.FromArgb(63, 130, 246), Color.FromArgb(29, 78, 216), 60f);
            g.FillPath(bg, path);

            // 中央に「訳」
            using var font = new Font("Yu Gothic UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Color.White);
            var fmt = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            g.DrawString("訳", font, textBrush, new RectangleF(0, -1, 32, 32), fmt);
        }

        var hIcon = bmp.GetHicon();
        return (Icon.FromHandle(hIcon), hIcon);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _notifyIcon.MouseClick -= OnMouseClick;
        _notifyIcon.MouseUp -= OnMouseUp;
        _notifyIcon.Visible = false;
        _notifyIcon.Icon = null;
        _notifyIcon.Dispose();

        if (_hIcon != IntPtr.Zero)
            DestroyIcon(_hIcon);
        _icon.Dispose();

        _disposed = true;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
