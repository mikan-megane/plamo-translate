using System.Runtime.InteropServices;

namespace PlamoTranslatePopup.Services;

/// <summary>
/// キー注入による選択テキストの取得(Ctrl+C 送信)。
/// ホットキー押下直後はユーザーの物理的な修飾キーが押され続けており、
/// そのまま Ctrl+C を送ると Ctrl+Alt+C などと解釈されるため、
/// まず修飾キーを強制的に離してから間隔を空けて注入する。
/// </summary>
public class KeyboardService
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(
        byte bVk,
        byte bScan,
        uint dwFlags,
        UIntPtr dwExtraInfo);

    private const byte SC_LCONTROL = 0x1D;
    private const byte SC_LSHIFT = 0x2A;
    private const byte SC_LMENU = 0x38;  // Alt
    private const byte SC_LWIN = 0x5B;
    private const byte SC_C = 0x2E;

    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;

    /// <summary>選択中テキストをクリップボードへコピーさせるため Ctrl+C を送信する</summary>
    public async Task SendCtrlCAsync()
    {
        // ホットキーの修飾キーが物理的に押されたままの場合に備えて離す
        // (押されていなければ影響なし)
        KeyUp(SC_LCONTROL);
        KeyUp(SC_LMENU);
        KeyUp(SC_LSHIFT);
        KeyUp(SC_LWIN);
        await Task.Delay(40);

        // 間隔を空けて Ctrl+C を注入(一括注入すると修飾キーを認識しないアプリがある)
        KeyDown(SC_LCONTROL);
        await Task.Delay(30);
        KeyDown(SC_C);
        await Task.Delay(30);
        KeyUp(SC_C);
        await Task.Delay(20);
        KeyUp(SC_LCONTROL);
    }

    private static void KeyDown(byte scanCode) =>
        keybd_event(0, scanCode, KEYEVENTF_SCANCODE, UIntPtr.Zero);

    private static void KeyUp(byte scanCode) =>
        keybd_event(0, scanCode, KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP, UIntPtr.Zero);
}
