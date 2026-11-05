namespace PlamoTranslatePopup.Utilities;

/// <summary>
/// カーソル近くにウィンドウを収まる位置で配置するための計算
/// (translate-popup2 の WindowPositionCalculator を DIP 版として簡略化)
/// </summary>
public static class WindowPositionCalculator
{
    /// <summary>
    /// カーソルの右下へオフセットして配置し、モニター作業領域からはみ出す場合は内側へ調整する。
    /// </summary>
    public static (double left, double top) CalculateWindowPosition(
        double cursorX,
        double cursorY,
        double windowWidth,
        double windowHeight,
        int offset = 12)
    {
        double left = cursorX + offset;
        double top = cursorY + offset;

        var workArea = CursorUtilities.GetWorkAreaDipAtCursor();
        if (workArea is { } area)
        {
            var (areaLeft, areaTop, areaRight, areaBottom) = area;

            if (left + windowWidth > areaRight)
            {
                left = cursorX - windowWidth - offset;
                if (left < areaLeft)
                    left = areaLeft;
            }

            if (top + windowHeight > areaBottom)
            {
                top = cursorY - windowHeight - offset;
                if (top < areaTop)
                    top = areaTop;
            }

            if (left < areaLeft)
                left = areaLeft;
            if (top < areaTop)
                top = areaTop;
        }

        return (left, top);
    }
}
