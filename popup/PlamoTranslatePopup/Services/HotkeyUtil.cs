using System.Text;
using System.Windows.Input;

namespace PlamoTranslatePopup.Services;

/// <summary>
/// ホットキー文字列("Ctrl+F7" 等)と Win32 の修飾フラグ/VK コードの相互変換。
/// popup.json と設定画面で共有する表記形式。
/// </summary>
public static class HotkeyUtil
{
    /// <summary>"Ctrl+Alt+F7" 形式の文字列を修飾フラグと VK コードに変換する</summary>
    public static bool TryParse(string? text, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;

        foreach (var part in parts[..^1])
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    modifiers |= HotkeyService.MOD_CONTROL;
                    break;
                case "alt":
                    modifiers |= HotkeyService.MOD_ALT;
                    break;
                case "shift":
                    modifiers |= HotkeyService.MOD_SHIFT;
                    break;
                case "win" or "windows":
                    modifiers |= HotkeyService.MOD_WIN;
                    break;
                default:
                    return false;
            }
        }

        if (!TryParseKey(parts[^1], out var key) || !IsRegistrable(key))
            return false;

        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        return vk != 0;
    }

    /// <summary>表示名("0"〜"9"、F1〜F24、A〜Z など)から Key へ変換する</summary>
    private static bool TryParseKey(string name, out Key key)
    {
        if (name.Length == 1 && name[0] is >= '0' and <= '9')
        {
            key = Key.D0 + (name[0] - '0');
            return true;
        }
        return Enum.TryParse(name, true, out key) && Enum.IsDefined(key);
    }

    /// <summary>RegisterHotKey に登録できない/すべきでないキー(F12 はデバッガ予約)</summary>
    private static bool IsRegistrable(Key key) =>
        key is not (Key.None or Key.F12 or Key.DeadCharProcessed);

    /// <summary>修飾キーと Key から "Ctrl+F7" 形式の表示文字列を作る</summary>
    public static string ToDisplayString(ModifierKeys modifiers, Key key)
    {
        var sb = new StringBuilder();
        if (modifiers.HasFlag(ModifierKeys.Control))
            sb.Append("Ctrl+");
        if (modifiers.HasFlag(ModifierKeys.Alt))
            sb.Append("Alt+");
        if (modifiers.HasFlag(ModifierKeys.Shift))
            sb.Append("Shift+");
        if (modifiers.HasFlag(ModifierKeys.Windows))
            sb.Append("Win+");
        sb.Append(KeyToDisplay(key));
        return sb.ToString();
    }

    private static string KeyToDisplay(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        _ => key.ToString(),
    };
}
