using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using PlamoTranslatePopup.Models;

namespace PlamoTranslatePopup.Services;

/// <summary>
/// ローカル llama-server (PLaMo 3 Translate 2B GGUF) と通信して翻訳するサービス。
/// サーバーが未起動なら起動を試みる。
/// </summary>
public sealed class LlamaTranslateService
{
    private static readonly string[] StopStrings = ["<|plamo:tag|>", "<|plamo:bos|>"];

    private readonly HttpClient _http;
    private PopupSettings Settings { get; }

    public LlamaTranslateService(PopupSettings settings)
    {
        Settings = settings;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    }

    /// <summary>PFML3 形式の翻訳プロンプトを組み立てる</summary>
    public static string BuildPrompt(string text, string sourceLang, string targetLang) =>
        "<|plamo:tag|>dataset<|plamo:msg|>translation"
        + $"<|plamo:tag|>input<|plamo:key|>lang<|plamo:val|>{sourceLang}<|plamo:msg|>{text}"
        + $"<|plamo:tag|>output<|plamo:key|>lang<|plamo:val|>{targetLang}<|plamo:msg|>";

    /// <summary>ひらがな・カタカナ・漢字を含むテキストは日本語とみなす</summary>
    public static string DetectLanguage(string text)
    {
        foreach (var ch in text)
        {
            if (('\u3040' <= ch && ch <= '\u309f')   // ひらがな
                || ('\u30a0' <= ch && ch <= '\u30ff') // カタカナ
                || ('\u4e00' <= ch && ch <= '\u9fff')) // 漢字
            {
                return "ja";
            }
        }
        return "en";
    }

    public async Task<bool> EnsureServerRunningAsync(CancellationToken ct = default)
    {
        if (await IsHealthyAsync(ct))
            return true;

        var serverExe = Path.Combine(Settings.ModelRoot, "llama-bin", "llama-server.exe");
        var modelGguf = Path.Combine(
            Settings.ModelRoot, "models", "plamo-3-translate-2b-Q4_K_M.gguf");
        if (!File.Exists(serverExe) || !File.Exists(modelGguf))
            return false;

        var port = new Uri(Settings.ServerUrl).Port;
        var host = new Uri(Settings.ServerUrl).Host;

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = serverExe,
            Arguments = $"-m \"{modelGguf}\" -c 8192 --port {port} --host {host}",
            WorkingDirectory = Settings.ModelRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        if (process is null)
            return false;

        // health が通るまで最大 30 秒待つ
        for (var i = 0; i < 60; i++)
        {
            await Task.Delay(500, ct);
            if (await IsHealthyAsync(ct))
            {
                return true;
            }
        }
        return false;
    }

    private async Task<bool> IsHealthyAsync(CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync(
                new Uri(new Uri(Settings.ServerUrl), "/health"), ct);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> IsServerRunningAsync(CancellationToken ct = default) =>
        await IsHealthyAsync(ct);

    /// <summary>
    /// llama-server プロセスを終了させる(アプリ終了時に VRAM を解放するため)。
    /// 戻り値は終了させたプロセス数。
    /// </summary>
    public int StopServer()
    {
        var stopped = 0;
        foreach (var proc in Process.GetProcessesByName("llama-server"))
        {
            try
            {
                proc.Kill(entireProcessTree: true);
                stopped++;
            }
            catch
            {
                // 既に終了している場合は無視
            }
        }
        return stopped;
    }

    /// <summary>
    /// llama-server のストリーミング API(/v1/completions, stream=true)で翻訳する。
    /// 生成トークンごとに onDelta が呼ばれる。戻り値は全文(Trim 済み)。
    /// </summary>
    public async Task<string> TranslateStreamAsync(
        string text, string sourceLang, string targetLang,
        Action<string> onDelta, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            // 長文でも止まらないよう出力上限を広めに取る(コンテキスト 8192 内)
            prompt = BuildPrompt(text, sourceLang, targetLang),
            temperature = 0,
            max_tokens = 2048,
            stop = StopStrings,
            cache_prompt = true,
            stream = true,
        });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync(
            new Uri(new Uri(Settings.ServerUrl), "/v1/completions"), content, ct);
        resp.EnsureSuccessStatusCode();

        // SSE(text/event-stream)を1行ずつ読む
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        var full = new StringBuilder();
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
                continue;
            var data = line["data: ".Length..];
            if (data == "[DONE]")
                break;

            StreamChunkDto? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<StreamChunkDto>(data);
            }
            catch
            {
                continue;
            }

            var delta = chunk?.choices is { Length: > 0 } choices ? choices[0].text : null;
            if (!string.IsNullOrEmpty(delta))
            {
                full.Append(delta);
                onDelta(delta);
            }
        }

        // 念のため終端トークンを除去
        var result = full.ToString();
        foreach (var stop in StopStrings)
        {
            var idx = result.IndexOf(stop, StringComparison.Ordinal);
            if (idx >= 0)
                result = result[..idx];
        }
        return result.Trim();
    }

    private sealed class StreamChunkDto
    {
        public StreamChoiceDto[]? choices { get; set; }
    }

    private sealed class StreamChoiceDto
    {
        public string? text { get; set; }
    }
}
