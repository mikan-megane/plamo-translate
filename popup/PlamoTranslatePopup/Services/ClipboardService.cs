using System.Runtime.InteropServices;
using System.Windows;

namespace PlamoTranslatePopup.Services;

/// <summary>
/// WPF Clipboard によるクリップボード操作(translate-popup2 と同一方式)
/// </summary>
public class ClipboardService
{
    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
    public string? GetText()
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            string? result = null;
            var thread = new Thread(() =>
            {
                try
                {
                    if (Clipboard.ContainsText())
                        result = Clipboard.GetText();
                }
                catch
                {
                    // クリップボードがロックされていた場合は無視
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            return result;
        }

        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch
        {
            return null;
        }
    }

    public void SetText(string text)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            var thread = new Thread(() =>
            {
                try { Clipboard.SetText(text); } catch { }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            return;
        }

        try { Clipboard.SetText(text); } catch { }
    }

    /// <summary>
    /// Ctrl+C を送って選択テキストを取得し、クリップボードを元に戻す。
    /// クリップボードのシーケンス番号の変化でコピーの成否を判定する
    /// (同じテキストでもシーケンスは増えるため、テキスト比較より確実)。
    /// 変化しなかった(=選択が無かった)場合は null を返す。
    /// </summary>
    public async Task<string?> GetSelectedTextAsync(KeyboardService keyboard)
    {
        var savedClipboard = GetText();
        var initialSeq = GetClipboardSequenceNumber();

        keyboard.SendCtrlC();

        // クリップボードが変化するまで待つ(最大 1.5 秒)
        string? selectedText = null;
        var changed = false;
        var deadline = Environment.TickCount64 + 1500;
        while (Environment.TickCount64 < deadline)
        {
            await Task.Delay(30);

            if (GetClipboardSequenceNumber() == initialSeq)
                continue;

            // シーケンスが変化したらテキストを読む(直後は未セットの場合もあり得るので読めなければ待ち続ける)
            var current = GetText();
            if (!string.IsNullOrEmpty(current))
            {
                selectedText = current;
                changed = true;
                break;
            }
        }

        // 実際に変わった場合のみ元のクリップボードを復元
        if (changed && savedClipboard != null)
        {
            await Task.Delay(80);
            SetText(savedClipboard);
        }

        return changed ? selectedText : null;
    }
}
