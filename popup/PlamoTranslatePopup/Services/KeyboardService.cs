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
        UIntPtr dwExtraInfo);

    private const byte SC_CONTROL = 0x1D;
    private const byte SC_C = 0x2E;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;

    /// <summary>選択中テキストをクリップボードへコピーさせるため Ctrl+C を送信する</summary>
    public void SendCtrlC()
    {
        // スキャンコード + KEYEVENTF_SCANCODE 指定(VKのみの注入を無視するアプリがあるため)
        keybd_event(0, SC_CONTROL, KEYEVENTF_SCANCODE, UIntPtr.Zero);
        keybd_event(0, SC_C, KEYEVENTF_SCANCODE, UIntPtr.Zero);
        keybd_event(0, SC_C, KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(0, SC_CONTROL, KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}
