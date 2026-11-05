using System.Runtime.InteropServices;

namespace PlamoTranslatePopup.Services;

/// <summary>
/// Win32 API を使用したキーボード操作サービス(translate-popup2 と同一方式)
/// </summary>
public class KeyboardService
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(
        byte bVk,
        byte bScan,
        uint dwFlags,
        uint dwExtraInfo);

    private const byte VK_CONTROL = 0x11;
    private const byte VK_C = 0x43;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    /// <summary>選択中テキストをクリップボードへコピーさせるため Ctrl+C を送信する</summary>
    public void SendCtrlC()
    {
        keybd_event(VK_CONTROL, 0, 0, 0);
        keybd_event(VK_C, 0, 0, 0);
        keybd_event(VK_C, 0, KEYEVENTF_KEYUP, 0);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, 0);
    }
}
