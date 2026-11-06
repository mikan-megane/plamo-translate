using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace PlamoTranslatePopup.Services;

/// <summary>
/// 非公式 Google 翻訳 API (translate.googleapis.com, client=gtx) を使う翻訳サービス。
/// llama-server 不要で高速だが、非公式 API のため予告なく使えなくなる可能性があり、
/// テキストは Google へ送信される。
/// </summary>
public sealed class GoogleTranslateService : IDisposable
{
    /// <summary>1 リクエストに送れるおおよその上限(API 制限は ~5k 文字。URL 長も避けたいので余裕を取る)</summary>
    private const int MaxChunkChars = 1800;

    private readonly HttpClient _http;

    public GoogleTranslateService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        // 既定の UA だと 429 (Too Many Requests) を返されやすいためブラウザの UA を使う
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
    }

    /// <summary>テキストを翻訳する。長文はチャンク分割して連結する。失敗時は例外</summary>
    public async Task<string> TranslateAsync(
        string text, string sourceLang, string targetLang, CancellationToken ct = default)
    {
        // 段落・文の境界を優先しつつ上限文字数で分割
        var chunks = SplitIntoChunks(text, MaxChunkChars);
        var results = new StringBuilder();

        foreach (var chunk in chunks)
        {
            if (results.Length > 0)
                await Task.Delay(200, ct); // チャンク間も軽く間隔を空ける

            results.Append(await TranslateChunkAsync(chunk, sourceLang, targetLang, ct));
        }

        return results.ToString().Trim();
    }

    private async Task<string> TranslateChunkAsync(
        string text, string sourceLang, string targetLang, CancellationToken ct)
    {
        var url = "https://translate.googleapis.com/translate_a/single" +
                  $"?client=gtx&sl={Uri.EscapeDataString(sourceLang)}&tl={Uri.EscapeDataString(targetLang)}" +
                  $"&dt=t&q={Uri.EscapeDataString(text)}";

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var json = await _http.GetStringAsync(url, ct);
                using var doc = JsonDocument.Parse(json);

                // レスポンス: [[["訳文","原文",...], ...], ...] の最初の配列の各セグメントを連結
                var sb = new StringBuilder();
                foreach (var segment in doc.RootElement[0].EnumerateArray())
                {
                    if (segment[0].ValueKind == JsonValueKind.String)
                        sb.Append(segment[0].GetString());
                }
                return sb.ToString();
            }
            catch (HttpRequestException) when (attempt < 2)
            {
                // 429 や一時的なネットワークエラーは少し待って再試行
                await Task.Delay(1500 * (attempt + 1), ct);
            }
        }
    }

    /// <summary>上限文字数ごとに分割する(改行・文末の句読点で切れる位置を優先)</summary>
    internal static List<string> SplitIntoChunks(string text, int maxChars)
    {
        if (text.Length <= maxChars)
            return [text];

        var chunks = new List<string>();
        var rest = text;
        while (rest.Length > maxChars)
        {
            // maxChars 以内で切れる一番良い位置を探す(改行 > 。. !? > 空白)
            var cut = -1;
            for (var i = maxChars; i >= maxChars / 2; i--)
            {
                if (rest[i - 1] == '\n' || "。.!!?？\r".Contains(rest[i - 1]))
                {
                    cut = i;
                    break;
                }
            }
            if (cut < 0)
            {
                for (var i = maxChars; i >= maxChars / 2; i--)
                {
                    if (rest[i - 1] == ' ' || rest[i - 1] == '　')
                    {
                        cut = i;
                        break;
                    }
                }
            }

            chunks.Add(rest[..(cut > 0 ? cut : maxChars)]);
            rest = rest[(cut > 0 ? cut : maxChars)..];
        }
        if (rest.Length > 0)
            chunks.Add(rest);
        return chunks;
    }

    public void Dispose() => _http.Dispose();
}
