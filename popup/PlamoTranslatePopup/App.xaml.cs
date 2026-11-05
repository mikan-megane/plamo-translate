using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using PlamoTranslatePopup.Models;
using PlamoTranslatePopup.Services;

namespace PlamoTranslatePopup;

/// <summary>
/// トレイ常駐 + グローバルホットキーで翻訳するアプリ。
/// - Ctrl+Alt+T: 選択中テキストを翻訳してカーソル近くに表示
/// - Ctrl+Alt+O: 画面の一部を範囲選択して OCR → 翻訳して表示
/// いずれのモードでもプロセス終了時に llama-server を停止する。
/// </summary>
public partial class App : Application
{
    private const int HotkeyTranslateId = 1;
    private const int HotkeyOcrId = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    public static PopupSettings CurrentSettings { get; private set; } = new();

    private readonly KeyboardService _keyboard = new();
    private readonly ClipboardService _clipboard = new();
    private readonly ScreenCaptureService _screenCapture = new();
    private HotkeyService? _hotkey;
    private NotifyIconService? _notifyIcon;
    private LlamaTranslateService? _translator;
    private OcrService? _ocr;
    private bool _busy;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        CurrentSettings = PopupSettings.Load();
        _translator = new LlamaTranslateService(CurrentSettings);
        _ocr = new OcrService();

        // --trigger: クリップボードの内容を翻訳してポップアップし即終了(テスト/CLI用)
        if (e.Args.Any(a => string.Equals(a, "--trigger", StringComparison.OrdinalIgnoreCase)))
        {
            RunTriggerModeAsync();
            return;
        }

        // --ocr x,y,w,h : 指定領域をOCR翻訳してポップアップし即終了(テスト/CLI用)
        var ocrIndex = Array.FindIndex(e.Args, a => string.Equals(a, "--ocr", StringComparison.OrdinalIgnoreCase));
        if (ocrIndex >= 0 && ocrIndex + 1 < e.Args.Length)
        {
            RunOcrRegionModeAsync(e.Args[ocrIndex + 1]);
            return;
        }

        // 隠しウィンドウ経由でホットキーを受け取る
        _hotkey = new HotkeyService();
        RegisterHotkeySafe(HotkeyService.MOD_CONTROL | HotkeyService.MOD_ALT | HotkeyService.MOD_NOREPEAT,
            0x54 /* 'T' */, HotkeyTranslateId, "Ctrl+Alt+T (選択翻訳)");
        RegisterHotkeySafe(HotkeyService.MOD_CONTROL | HotkeyService.MOD_ALT | HotkeyService.MOD_NOREPEAT,
            0x4F /* 'O' */, HotkeyOcrId, "Ctrl+Alt+O (OCR翻訳)");
        _hotkey.HotkeyPressed += OnHotkeyPressed;
        Log($"起動完了: ホットキー登録済み (Ctrl+Alt+T / Ctrl+Alt+O, OCR={(_ocr.IsAvailable ? "利用可能" : "利用不可")})");

        _notifyIcon = new NotifyIconService("PLaMo翻訳ポップアップ (Ctrl+Alt+T / Ctrl+Alt+O)");
        _notifyIcon.ContextMenuRequested += ShowTrayMenu;
        _notifyIcon.LeftClick += ShowTrayMenu;
    }

    private void RegisterHotkeySafe(uint modifiers, uint virtualKey, int id, string description)
    {
        try
        {
            _hotkey!.Register(modifiers, virtualKey, id);
        }
        catch (InvalidOperationException ex)
        {
            Log($"ホットキー登録失敗 {description}: {ex.Message}");
        }
    }

    private void ShowTrayMenu(object? sender, EventArgs e)
    {
        var menu = new ContextMenu();

        var statusItem = new MenuItem
        {
            Header = _translator is { } t && t.IsServerRunningAsync().GetAwaiter().GetResult()
                ? "サーバー: 稼働中 (llama-server)"
                : "サーバー: 未起動 (次回ホットキー時自動起動)",
            IsEnabled = false,
        };
        menu.Items.Add(statusItem);
        menu.Items.Add(new Separator());

        var exitItem = new MenuItem { Header = "終了" };
        exitItem.Click += (_, _) => Shutdown();
        menu.Items.Add(exitItem);

        menu.IsOpen = true;
    }

    private async void OnHotkeyPressed(int id)
    {
        if (_busy)
            return;

        if (id == HotkeyOcrId)
            await RunOcrTranslateAsync();
        else
            await RunSelectionTranslateAsync();
    }

    /// <summary>ホットキー(Ctrl+Alt+T): 選択テキストを取得して翻訳する</summary>
    private async Task RunSelectionTranslateAsync()
    {
        Log("ホットキー検知: 選択翻訳");
        _busy = true;
        try
        {
            var selected = await _clipboard.GetSelectedTextAsync(_keyboard);
            Log($"選択テキスト: {(string.IsNullOrEmpty(selected) ? "なし" : $"{selected.Length}文字: {selected[..Math.Min(30, selected.Length)]}")} / フォアグラウンド: {GetForegroundDescription()}");
            if (string.IsNullOrWhiteSpace(selected))
            {
                ShowHint("選択文字列が取得できませんでした。テキストを選択してから実行してください。");
                return;
            }

            await TranslateAndShowAsync(selected);
        }
        catch (Exception ex)
        {
            Log($"エラー: {ex.Message}");
            ShowHint($"翻訳中にエラーが発生しました:\n{ex.Message}");
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>ホットキー(Ctrl+Alt+O): 画面の範囲を選択して OCR → 翻訳する</summary>
    private async Task RunOcrTranslateAsync()
    {
        Log("ホットキー検知: OCR翻訳");
        _busy = true;
        try
        {
            if (_ocr is null || !_ocr.IsAvailable)
            {
                Log("OCR: エンジン利用不可");
                ShowHint("OCR エンジンが利用できません。\nWindows の OCR 言語パック(日本語)を確認してください。");
                return;
            }

            var region = await new RegionSelectWindow().SelectRegionAsync();
            Log($"OCR: 領域選択 {(region is null ? "キャンセル" : $"{region.Value.X},{region.Value.Y} {region.Value.Width}x{region.Value.Height}")}");
            if (region is null)
                return;

            await OcrTranslateCoreAsync(region.Value);
        }
        catch (Exception ex)
        {
            Log($"OCRエラー: {ex.Message}");
            ShowHint($"OCR 処理でエラーが発生しました:\n{ex.Message}");
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>領域をキャプチャして OCR → 翻訳 → ポップアップ表示。表示したウィンドウを返す</summary>
    private async Task<TranslateWindow> OcrTranslateCoreAsync(ScreenRegion region)
    {
        using var captured = _screenCapture.CaptureRegion(region);
        var ocrText = await _ocr!.RecognizeAsync(captured);
        Log($"OCR結果: {(string.IsNullOrEmpty(ocrText) ? "空" : $"{ocrText.Length}文字: {ocrText[..Math.Min(30, ocrText.Length)]}")}");

        if (string.IsNullOrWhiteSpace(ocrText))
            return ShowHint("文字を認識できませんでした。");

        return await TranslateAndShowAsync(ocrText);
    }

    /// <summary>テキストの言語を判定して翻訳し、カーソル近くにポップアップ表示。表示したウィンドウを返す</summary>
    private async Task<TranslateWindow> TranslateAndShowAsync(string text)
    {
        var sourceLang = LlamaTranslateService.DetectLanguage(text);
        var targetLang = sourceLang == "ja" ? "en" : "ja";

        var popup = new TranslateWindow();
        popup.ShowAtCursor(sourceLang, targetLang);

        var started = await _translator!.EnsureServerRunningAsync();
        if (!started)
        {
            popup.SetResult(
                "llama-server を起動できませんでした。\n" +
                "serverUrl / modelRoot の設定 (popup.json) を確認してください。");
            return popup;
        }

        var translated = await _translator.TranslateAsync(text, sourceLang, targetLang);
        Log($"翻訳完了: {translated[..Math.Min(50, translated.Length)]}");
        popup.SetResult(translated);
        return popup;
    }

    /// <summary>短いメッセージをカーソル近くに表示する(フィードバック用)。表示したウィンドウを返す</summary>
    private static TranslateWindow ShowHint(string message)
    {
        var hint = new TranslateWindow();
        hint.ShowHintAtCursor(message);
        return hint;
    }

    /// <summary>--trigger モード: クリップボードの内容を翻訳して表示し、自動クローズ後に終了</summary>
    private async void RunTriggerModeAsync()
    {
        Log("--trigger モード起動");
        try
        {
            var text = _clipboard.GetText();
            Log($"クリップボード: {(string.IsNullOrEmpty(text) ? "なし" : $"{text.Length}文字: {text[..Math.Min(30, text.Length)]}")}");
            if (string.IsNullOrWhiteSpace(text))
            {
                Shutdown(1);
                return;
            }

            var popup = await TranslateAndShowAsync(text);
            popup.Closed += (_, _) => Shutdown();
        }
        catch (Exception ex)
        {
            Log($"エラー: {ex.Message}");
            Shutdown(1);
        }
    }

    /// <summary>--ocr x,y,w,h モード: 指定領域(物理ピクセル)を OCR 翻訳して表示し、自動クローズ後に終了</summary>
    private async void RunOcrRegionModeAsync(string regionArg)
    {
        Log($"--ocr モード起動: {regionArg}");
        try
        {
            var parts = regionArg.Split(',');
            if (parts.Length != 4
                || !int.TryParse(parts[0], out var x) || !int.TryParse(parts[1], out var y)
                || !int.TryParse(parts[2], out var w) || !int.TryParse(parts[3], out var h)
                || w <= 0 || h <= 0)
            {
                Log("--ocr 引数が不正: x,y,w,h 形式で指定してください");
                Shutdown(1);
                return;
            }

            var popup = await OcrTranslateCoreAsync(new ScreenRegion(x, y, w, h));
            popup.Closed += (_, _) => Shutdown();
        }
        catch (Exception ex)
        {
            Log($"エラー: {ex.Message}");
            Shutdown(1);
        }
    }

    /// <summary>ホットキー押下時のフォアグラウンドウィンドウの概要(診断用)</summary>
    private static string GetForegroundDescription()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return "(なし)";

            var sb = new StringBuilder(256);
            GetWindowText(hwnd, sb, 256);
            _ = GetWindowThreadProcessId(hwnd, out var pid);
            var procName = Process.GetProcessById((int)pid).ProcessName;
            return $"{procName} \"{sb}\"";
        }
        catch
        {
            return "(不明)";
        }
    }

    internal static void Log(string message)
    {
        try
        {
            var line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "plamo-popup.log"),
                line + Environment.NewLine);
        }
        catch
        {
            // ログ出力は失敗しても無視
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 終了時に llama-server を停止して VRAM を解放する(常駐・単発モード共通)
        if (_translator is { } t)
        {
            var stopped = t.StopServer();
            Log($"終了: llama-server を停止しました ({stopped} プロセス)");
        }

        _hotkey?.Dispose();
        _notifyIcon?.Dispose();
        base.OnExit(e);
    }
}
