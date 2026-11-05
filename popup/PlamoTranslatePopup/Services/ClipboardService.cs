using System.Windows;

namespace PlamoTranslatePopup.Services;

/// <summary>
/// WPF Clipboard によるクリップボード操作(translate-popup2 と同一方式)
/// </summary>
public class ClipboardService
{
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
    /// クリップボードが変化しなかった(=選択が無かった)場合は null を返す。
    /// </summary>
    public async Task<string?> GetSelectedTextAsync(KeyboardService keyboard)
    {
        var savedClipboard = GetText();

        keyboard.SendCtrlC();

        // クリップボードが実際に変わるまでポーリング(最大 600ms)
        string? selectedText = null;
        var changed = false;
        for (var i = 0; i < 24; i++)
        {
            await Task.Delay(25);
            var current = GetText();
            if (!string.IsNullOrEmpty(current) && current != savedClipboard)
            {
                selectedText = current;
                changed = true;
                break;
            }
        }

        // 元のクリップボードを復元
        if (savedClipboard != null)
            SetText(savedClipboard);

        return changed ? selectedText : null;
    }
}
