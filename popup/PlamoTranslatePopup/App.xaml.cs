using System.IO;
using System.Windows;
using System.Windows.Controls;
using PlamoTranslatePopup.Models;
using PlamoTranslatePopup.Services;

namespace PlamoTranslatePopup;

/// <summary>
/// トレイ常駐 + グローバルホットキー (Ctrl+Alt+T) で選択テキストを翻訳して
/// カーソル近くにポップアップ表示する。ファイルから起動して exe を直接実行する用途。
/// </summary>
public partial class App : Application
{
    public static PopupSettings CurrentSettings { get; private set; } = new();

    private readonly KeyboardService _keyboard = new();
    private readonly ClipboardService _clipboard = new();
    private HotkeyService? _hotkey;
    private NotifyIconService? _notifyIcon;
    private LlamaTranslateService? _translator;
    private bool _busy;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        CurrentSettings = PopupSettings.Load();
        _translator = new LlamaTranslateService(CurrentSettings);

        // --trigger: クリップボードの内容を翻訳してポップアップし即終了(テスト/CLI用)
        if (e.Args.Any(a => string.Equals(a, "--trigger", StringComparison.OrdinalIgnoreCase)))
        {
            RunTriggerModeAsync();
            return;
        }

        // 隠しウィンドウ経由でホットキーを受け取る
        _hotkey = new HotkeyService();
        _hotkey.Register(
            HotkeyService.MOD_CONTROL | HotkeyService.MOD_ALT | HotkeyService.MOD_NOREPEAT,
            virtualKey: 0x54 /* 'T' */);
        _hotkey.HotkeyPressed += OnHotkeyPressed;
        Log("起動完了: ホットキー登録済み (Ctrl+Alt+T)");

        _notifyIcon = new NotifyIconService("PLaMo翻訳ポップアップ (Ctrl+Alt+T)");
        _notifyIcon.ContextMenuRequested += ShowTrayMenu;
        _notifyIcon.LeftClick += ShowTrayMenu;
    }

    /// <summary>
    /// --trigger モード: クリップボードの内容を翻訳してカーソル近くに表示し、
    /// 自動クローズ後にプロセスごと終了する(テスト/CLI 用)。
    /// </summary>
    private async void RunTriggerModeAsync()
    {
        Log("--trigger モード起動");
        try
        {
            var text = _clipboard.GetText();
            Log($"クリップボード: {(string.IsNullOrEmpty(text) ? "なし" : $"{text.Length}文字: {text[..Math.Min(30, text.Length)]}")}");
            if (string.IsNullOrWhiteSpace(text) || _translator is null)
            {
                Shutdown(1);
                return;
            }

            var sourceLang = LlamaTranslateService.DetectLanguage(text);
            var targetLang = sourceLang == "ja" ? "en" : "ja";

            var popup = new TranslateWindow();
            popup.ShowAtCursor(sourceLang, targetLang);
            popup.Closed += (_, _) => Shutdown();

            var started = await _translator.EnsureServerRunningAsync();
            if (!started)
            {
                popup.SetResult(
                    "llama-server を起動できませんでした。\n" +
                    "serverUrl / modelRoot の設定 (popup.json) を確認してください。");
            }
            else
            {
                var translated = await _translator.TranslateAsync(text, sourceLang, targetLang);
                Log($"翻訳完了: {translated[..Math.Min(50, translated.Length)]}");
                popup.SetResult(translated);
            }
        }
        catch (Exception ex)
        {
            Log($"エラー: {ex.Message}");
            Shutdown(1);
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

    private async void OnHotkeyPressed(object? sender, EventArgs e)
    {
        Log("ホットキー検知");
        if (_busy || _translator is null)
            return;
        _busy = true;

        TranslateWindow? popup = null;
        try
        {
            var selected = await _clipboard.GetSelectedTextAsync(_keyboard);
            Log($"選択テキスト: {(string.IsNullOrEmpty(selected) ? "なし" : $"{selected.Length}文字: {selected[..Math.Min(30, selected.Length)]}")}");
            if (string.IsNullOrWhiteSpace(selected))
                return;

            var sourceLang = LlamaTranslateService.DetectLanguage(selected);
            var targetLang = sourceLang == "ja" ? "en" : "ja";
            if (sourceLang == targetLang)
                return;

            popup = new TranslateWindow();
            popup.ShowAtCursor(sourceLang, targetLang);

            var started = await _translator.EnsureServerRunningAsync();
            if (!started)
            {
                popup.SetResult(
                    "llama-server を起動できませんでした。\n" +
                    "serverUrl / modelRoot の設定 (popup.json) を確認してください。");
                return;
            }

            var translated = await _translator.TranslateAsync(selected, sourceLang, targetLang);
            Log($"翻訳完了: {translated[..Math.Min(50, translated.Length)]}");
            popup.SetResult(translated);
        }
        catch (Exception ex)
        {
            popup?.SetResult($"翻訳中にエラーが発生しました:\n{ex.Message}");
        }
        finally
        {
            _busy = false;
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
        _hotkey?.Dispose();
        _notifyIcon?.Dispose();
        base.OnExit(e);
    }
}
