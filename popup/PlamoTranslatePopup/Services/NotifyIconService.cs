using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PlamoTranslatePopup.Services;

/// <summary>
/// WinForms の NotifyIcon によるシステムトレイアイコン。
/// 右クリックは WinForms が ContextMenuStrip をフォーカス処理込みで自動表示する。
/// 左クリックでは ShowMenu() で同じメニューを表示する。
/// </summary>
public sealed class NotifyIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private readonly IntPtr _hIcon;
    private readonly NativeWindow _focusHelper;
    private bool _disposed;

    public event EventHandler? LeftClick;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public NotifyIconService(string tooltip, ContextMenuStrip menu)
    {
        (_icon, _hIcon) = CreateAppIcon();

        // 左クリック時にメニューへフォーカスを与えるための隠しウィンドウ
        // (フォーカスが無いとメニューの外クリックで閉じなくなるため)
        _focusHelper = new NativeWindow();
        _focusHelper.CreateHandle(new CreateParams
        {
            Caption = "PlamoTranslateTrayHelper",
            ClassName = "STATIC", // システム登録済みクラスを借用
            X = -200,
            Y = -200,
            Width = 0,
            Height = 0,
            ExStyle = 0x00000080, // WS_EX_TOOLWINDOW
        });

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = ClampTooltip(tooltip),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.MouseClick += OnMouseClick;
    }

    private void OnMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            LeftClick?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>左クリック用: フォーカスを与えてからメニューを表示する</summary>
    public void ShowMenu(ContextMenuStrip menu)
    {
        if (_focusHelper.Handle != IntPtr.Zero)
            SetForegroundWindow(_focusHelper.Handle);
        menu.Show(System.Windows.Forms.Cursor.Position);
    }

    public void UpdateTooltip(string tooltip) =>
        _notifyIcon.Text = ClampTooltip(tooltip);

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
        _notifyIcon.Visible = false;
        _notifyIcon.Icon = null;
        _notifyIcon.Dispose();

        _focusHelper.DestroyHandle();

        if (_hIcon != IntPtr.Zero)
            DestroyIcon(_hIcon);
        _icon.Dispose();

        _disposed = true;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
