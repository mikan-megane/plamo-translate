using System.Runtime.InteropServices;

namespace PlamoTranslatePopup.Utilities;

/// <summary>
/// 高DPIに対応したカーソル位置→DIP変換と文字列表記のユーティリティ
/// </summary>
public static class CursorUtilities
{
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpInfo);

    [DllImport("shcore.dll")]
    private static extern uint GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private class MONITORINFO
    {
        public int cbSize = Marshal.SizeOf(typeof(MONITORINFO));
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private const int MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

    /// <summary>カーソルの物理ピクセル位置を取得</summary>
    public static (int x, int y) GetCursorPhysicalPosition()
    {
        GetCursorPos(out var pt);
        return (pt.X, pt.Y);
    }

    /// <summary>カーソル位置モニターのDPI比(96=1.0)</summary>
    public static double GetDpiScaleAt(int x, int y)
    {
        var pt = new POINT { X = x, Y = y };
        var hMonitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
        if (hMonitor == IntPtr.Zero)
            return 1.0;

        if (GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) != 0)
            return 1.0;

        return dpiX / 96.0;
    }

    /// <summary>
    /// カーソル位置を WPF の DIP 座標へ変換(相対座標近似)。単一DPI環境を想定。
    /// </summary>
    public static (double dipX, double dipY) GetCursorDipPosition()
    {
        var (x, y) = GetCursorPhysicalPosition();
        var scale = GetDpiScaleAt(x, y);
        // 物理座標の原点(仮想画面左上)は WPF DIP の原点と一致すると近似
        return (x / scale, y / scale);
    }

    /// <summary>カーソル位置モニターの作業領域を DIP で取得(失敗時は null)</summary>
    public static (double left, double top, double right, double bottom)?
        GetWorkAreaDipAtCursor()
    {
        var (x, y) = GetCursorPhysicalPosition();
        var pt = new POINT { X = x, Y = y };
        var hMonitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
        if (hMonitor == IntPtr.Zero)
            return null;

        var mi = new MONITORINFO();
        if (!GetMonitorInfo(hMonitor, ref mi))
            return null;

        var scale = GetDpiScaleAt(x, y);
        return (
            mi.rcWork.Left / scale,
            mi.rcWork.Top / scale,
            mi.rcWork.Right / scale,
            mi.rcWork.Bottom / scale);
    }
}
