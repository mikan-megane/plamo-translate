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
    /// クリップボードのシーケンス番号の変化でコピーの成否を判定する。
    /// 失敗時は1回リトライし、それでも変化しなければ null を返す。
    /// </summary>
    public async Task<string?> GetSelectedTextAsync(KeyboardService keyboard)
    {
        var savedClipboard = GetText();
        var initialSeq = GetClipboardSequenceNumber();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await keyboard.SendCtrlCAsync();

            // クリップボードが変化するまで待つ(1試行あたり最大 900ms)
            var waited = 0;
            while (waited < 900)
            {
                await Task.Delay(30);
                waited += 30;

                if (GetClipboardSequenceNumber() == initialSeq)
                    continue;

                // シーケンスが変化したらテキストを読む(直後は未セットの場合もあり得るので読めなければ待ち続ける)
                var current = GetText();
                if (!string.IsNullOrEmpty(current))
                {
                    // 元のクリップボードを復元
                    if (savedClipboard != null)
                    {
                        await Task.Delay(60);
                        SetText(savedClipboard);
                    }
                    return current;
                }
            }
        }

        return null;
    }
}
