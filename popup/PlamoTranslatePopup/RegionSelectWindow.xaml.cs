using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PlamoTranslatePopup.Models;

namespace PlamoTranslatePopup;

/// <summary>
/// 画面全体に被さる半透明オーバーレイ。ドラッグで範囲を選択する。
/// 選択結果は物理ピクセル座標の ScreenRegion で返す。
/// </summary>
public partial class RegionSelectWindow : Window
{
    private Point _start;
    private bool _selecting;
    private TaskCompletionSource<ScreenRegion?>? _tcs;

    public RegionSelectWindow()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            // 仮想スクリーン(全モニター)全体に配置
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;
            Focus();
        };

        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        KeyDown += OnKey;
        Closed += (_, _) => _tcs?.TrySetResult(null);
    }

    /// <summary>表示して範囲選択を待つ。キャンセル(Esc/クリック未満)なら null</summary>
    public Task<ScreenRegion?> SelectRegionAsync()
    {
        _tcs = new TaskCompletionSource<ScreenRegion?>();
        Show();
        return _tcs.Task;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _selecting = true;
        _start = e.GetPosition(this);
        SelectionRect.Visibility = Visibility.Visible;
        CaptureMouse();
        Update(_start);
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_selecting)
            Update(e.GetPosition(this));
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_selecting)
            return;
        _selecting = false;
        ReleaseMouseCapture();

        var end = e.GetPosition(this);

        // 小さすぎる選択はキャンセル扱い
        if (Math.Abs(end.X - _start.X) < 10 || Math.Abs(end.Y - _start.Y) < 10)
        {
            Close();
            return;
        }

        // WPF の DIP 座標 → 物理ピクセル座標(PerMonitorV2 対応)
        var a = PointToScreen(_start);
        var b = PointToScreen(end);
        var region = new ScreenRegion(
            (int)Math.Min(a.X, b.X),
            (int)Math.Min(a.Y, b.Y),
            (int)Math.Abs(a.X - b.X),
            (int)Math.Abs(a.Y - b.Y));

        _tcs?.TrySetResult(region);
        Close();
    }

    private void Update(Point current)
    {
        var x = Math.Min(_start.X, current.X);
        var y = Math.Min(_start.Y, current.Y);
        var w = Math.Abs(current.X - _start.X);
        var h = Math.Abs(current.Y - _start.Y);

        Canvas.SetLeft(SelectionRect, x);
        Canvas.SetTop(SelectionRect, y);
        SelectionRect.Width = w;
        SelectionRect.Height = h;

        Canvas.SetLeft(SizeLabel, x);
        Canvas.SetTop(SizeLabel, Math.Max(SystemParameters.VirtualScreenTop, y - 30));
        SizeText.Text = $"{(int)w} × {(int)h}";
        SizeLabel.Visibility = Visibility.Visible;
    }
}
