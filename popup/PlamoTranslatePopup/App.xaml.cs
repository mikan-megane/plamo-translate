using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using PlamoTranslatePopup.Models;
using PlamoTranslatePopup.Services;

namespace PlamoTranslatePopup;

/// <summary>
/// トレイ常駐 + グローバルホットキーで翻訳するアプリ。
/// - 既定 Ctrl+F7: 選択中テキストを翻訳してカーソル近くに表示
/// - 既定 Ctrl+F9: 画面の一部を範囲選択して OCR → 翻訳して表示
/// ホットキー・ポップアップの表示はトレイメニュー「設定...」から変更できる。
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
    private System.Windows.Forms.ContextMenuStrip? _trayMenu;
    private SettingsWindow? _settingsWindow;
    private LlamaTranslateService? _translator;
    private OcrService? _ocr;
    private bool _busy;
    private DateTime _serverLastUsedUtc = DateTime.UtcNow;
    private readonly System.Windows.Threading.DispatcherTimer _serverIdleTimer = new()
    {
        Interval = TimeSpan.FromSeconds(15),
    };

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        CurrentSettings = PopupSettings.Load();
        _translator = new LlamaTranslateService(CurrentSettings);
        _ocr = new OcrService();

        // --settings: 設定画面だけを開いて終了(テスト/直接起動用)
        if (e.Args.Any(a => string.Equals(a, "--settings", StringComparison.OrdinalIgnoreCase)))
        {
            var window = new SettingsWindow(CurrentSettings, ApplySettings);
            window.Closed += (_, _) => Shutdown();
            window.Show();
            return;
        }

        // --trigger: クリップボードの内容を翻訳してポップアップし即終了(テスト/CLI用)
        if (e.Args.Any(a => string.Equals(a, "--trigger", StringComparison.OrdinalIgnoreCase)))
        {
            RunTriggerModeAsync();
            return;
        }

        // --selection: 選択テキストの取得→翻訳を1回だけ実行して終了(テスト用)
        if (e.Args.Any(a => string.Equals(a, "--selection", StringComparison.OrdinalIgnoreCase)))
        {
            RunSelectionTestAsync();
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
        _hotkey.HotkeyPressed += OnHotkeyPressed;
        RegisterHotkeysFromSettings(CurrentSettings);
        Log($"起動完了: ホットキー ({CurrentSettings.HotkeyTranslate} / {CurrentSettings.HotkeyOcr}, OCR={(_ocr.IsAvailable ? "利用可能" : "利用不可")})");

        // トレイメニューは起動時に構築しておく(右クリック時の初回表示を速くする)
        _trayMenu = BuildTrayMenu();
        _notifyIcon = new NotifyIconService(BuildTrayTooltip(), _trayMenu);
        _notifyIcon.LeftClick += ShowTrayMenu;

        // 翻訳に使われない llama-server をアイドルタイムアウトで停止する
        _serverIdleTimer.Tick += OnServerIdleTimerTick;
        _serverIdleTimer.Start();
    }

    /// <summary>放置された llama-server を自動停止する</summary>
    private async void OnServerIdleTimerTick(object? sender, EventArgs e)
    {
        var timeout = CurrentSettings.ServerIdleTimeoutSeconds;
        if (timeout <= 0 || _translator is null)
            return;

        // 翻訳中は基準を延長して止めない
        if (_busy)
        {
            _serverLastUsedUtc = DateTime.UtcNow;
            return;
        }

        if (DateTime.UtcNow - _serverLastUsedUtc < TimeSpan.FromSeconds(timeout))
            return;

        if (!await _translator.IsServerRunningAsync())
            return;

        var stopped = _translator.StopServer();
        Log($"アイドルタイムアウト({timeout}秒): llama-server を停止しました ({stopped} プロセス)");
    }

    /// <summary>トレイメニューを構築する(起動時に1回。サーバー状態は Opening で更新)</summary>
    private System.Windows.Forms.ContextMenuStrip BuildTrayMenu()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();

        var settingsItem = new System.Windows.Forms.ToolStripMenuItem("設定...");
        settingsItem.Click += (_, _) => OpenSettings();
        menu.Items.Add(settingsItem);

        var statusItem = new System.Windows.Forms.ToolStripMenuItem("サーバー: 確認中...") { Enabled = false };
        menu.Items.Add(statusItem);

        var startItem = new System.Windows.Forms.ToolStripMenuItem("サーバーを起動");
        startItem.Click += async (_, _) =>
        {
            if (_translator is null)
                return;
            statusItem.Text = "サーバー: 起動中...";
            var ok = await _translator.EnsureServerRunningAsync();
            statusItem.Text = ok
                ? "サーバー: 稼働中 (llama-server)"
                : "サーバー: 起動できませんでした";
            if (ok)
                _serverLastUsedUtc = DateTime.UtcNow;
        };
        menu.Items.Add(startItem);

        var stopItem = new System.Windows.Forms.ToolStripMenuItem("サーバーを停止");
        stopItem.Click += (_, _) =>
        {
            if (_translator is null)
                return;
            var stopped = _translator.StopServer();
            Log($"メニューから llama-server を停止しました ({stopped} プロセス)");
            statusItem.Text = "サーバー: 未起動 (次回翻訳時に自動起動)";
        };
        menu.Items.Add(stopItem);

        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var exitItem = new System.Windows.Forms.ToolStripMenuItem("終了");
        exitItem.Click += (_, _) => Shutdown();
        menu.Items.Add(exitItem);

        menu.Opening += async (_, _) =>
        {
            if (_translator is { } t)
            {
                var running = await t.IsServerRunningAsync();
                statusItem.Text = running
                    ? "サーバー: 稼働中 (llama-server)"
                    : "サーバー: 未起動 (次回翻訳時に自動起動)";
                startItem.Enabled = !running;
                stopItem.Enabled = running;
            }
        };
        return menu;
    }

    private string BuildTrayTooltip() =>
        $"PLaMo翻訳ポップアップ ({CurrentSettings.HotkeyTranslate} / {CurrentSettings.HotkeyOcr})";

    /// <summary>設定からホットキーを登録する。失敗したら現行設定で再登録して復元する</summary>
    private bool RegisterHotkeysFromSettings(PopupSettings settings)
    {
        if (_hotkey is null)
            return true;

        _hotkey.UnregisterAll();
        var ok = TryRegisterHotkey(settings.HotkeyTranslate, HotkeyTranslateId);
        ok &= TryRegisterHotkey(settings.HotkeyOcr, HotkeyOcrId);

        if (!ok)
        {
            // 競合等で登録できない場合は元の設定に戻す
            Log("ホットキー再登録に失敗: 元の設定に戻します");
            _hotkey.UnregisterAll();
            TryRegisterHotkey(CurrentSettings.HotkeyTranslate, HotkeyTranslateId);
            TryRegisterHotkey(CurrentSettings.HotkeyOcr, HotkeyOcrId);
        }
        return ok;
    }

    private bool TryRegisterHotkey(string text, int id)
    {
        if (!HotkeyUtil.TryParse(text, out var modifiers, out var vk))
        {
            Log($"ホットキー設定が無効: '{text}'");
            return false;
        }
        return RegisterHotkeySafe(modifiers | HotkeyService.MOD_NOREPEAT, vk, id, text);
    }

    private bool RegisterHotkeySafe(uint modifiers, uint virtualKey, int id, string description)
    {
        try
        {
            _hotkey!.Register(modifiers, virtualKey, id);
            Log($"ホットキー登録: {description}");
            return true;
        }
        catch (InvalidOperationException ex)
        {
            Log($"ホットキー登録失敗 {description}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 設定画面からの適用。ホットキー登録に失敗した場合は false を返し、
    /// 設定は保存されない(保存は競合がないと確認できてから)。
    /// </summary>
    internal bool ApplySettings(PopupSettings settings)
    {
        if (!RegisterHotkeysFromSettings(settings))
        {
            Log("設定の適用を中止: ホットキーが登録できません(競合?)");
            return false;
        }

        CurrentSettings = settings;
        settings.Save();
        _notifyIcon?.UpdateTooltip(BuildTrayTooltip());
        Log($"設定を適用: 選択={settings.HotkeyTranslate}, OCR={settings.HotkeyOcr}, " +
            $"自動クローズ={settings.AutoCloseSeconds}秒, 不透明度={settings.PopupOpacity:P0}");
        return true;
    }

    private void OpenSettings()
    {
        if (_settingsWindow is { IsLoaded: true } window)
        {
            window.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(CurrentSettings, ApplySettings);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ShowTrayMenu(object? sender, EventArgs e)
    {
        if (_trayMenu is { } menu)
            _notifyIcon?.ShowMenu(menu);
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

    /// <summary>ホットキー(選択翻訳): 選択テキストを取得して翻訳する</summary>
    private async Task RunSelectionTranslateAsync()
    {
        Log("ホットキー検知: 選択翻訳");
        _busy = true;
        try
        {
            await SelectionTranslateCoreAsync();
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

    /// <summary>選択テキストを取得して翻訳し、ポップアップ表示。表示したウィンドウを返す</summary>
    private async Task<TranslateWindow> SelectionTranslateCoreAsync()
    {
        var selected = await _clipboard.GetSelectedTextAsync(_keyboard);
        Log($"選択テキスト: {(string.IsNullOrEmpty(selected) ? "なし" : $"{selected.Length}文字: {selected[..Math.Min(30, selected.Length)]}")} / フォアグラウンド: {GetForegroundDescription()}");
        if (string.IsNullOrWhiteSpace(selected))
            return ShowHint("選択文字列が取得できませんでした。テキストを選択してから実行してください。");

        return await TranslateAndShowAsync(selected);
    }

    /// <summary>--selection モード: 選択テキストの取得→翻訳を1回実行して終了(テスト用)</summary>
    private async void RunSelectionTestAsync()
    {
        Log("--selection モード起動");
        try
        {
            var popup = await SelectionTranslateCoreAsync();
            popup.Closed += (_, _) => Shutdown();
        }
        catch (Exception ex)
        {
            Log($"エラー: {ex.Message}");
            Shutdown(1);
        }
    }

    /// <summary>ホットキー(OCR翻訳): 画面の範囲を選択して OCR → 翻訳する</summary>
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

        // OCR では翻訳前の文字列もポップアップに表示する
        return await TranslateAndShowAsync(ocrText, showSource: true);
    }

    /// <summary>テキストの言語を判定して翻訳し、カーソル近くにストリーミング表示する。表示したウィンドウを返す</summary>
    private async Task<TranslateWindow> TranslateAndShowAsync(string text, bool showSource = false)
    {
        _serverLastUsedUtc = DateTime.UtcNow; // アイドルタイムアウトの基準を更新

        var sourceLang = LlamaTranslateService.DetectLanguage(text);
        var targetLang = sourceLang == "ja" ? "en" : "ja";

        var popup = new TranslateWindow();
        if (showSource)
            popup.SetSource(text);
        popup.ShowAtCursor(sourceLang, targetLang);

        var started = await _translator!.EnsureServerRunningAsync();
        if (!started)
        {
            popup.SetResult(
                "llama-server を起動できませんでした。\n" +
                "serverUrl / modelRoot の設定 (popup.json) を確認してください。");
            return popup;
        }

        // ストリーミングで逐次表示する
        var first = true;
        var translated = await _translator.TranslateStreamAsync(
            text, sourceLang, targetLang, delta =>
            {
                if (first)
                {
                    popup.BeginStream();
                    first = false;
                }
                popup.AppendResult(delta);
            });
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

    private static readonly UTF8Encoding LogEncoding = new(encoderShouldEmitUTF8Identifier: true);

    internal static void Log(string message)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "plamo-popup.log");
            var line = $"{DateTime.Now:HH:mm:ss.fff} {message}" + Environment.NewLine;
            if (!File.Exists(path))
                File.WriteAllText(path, line, LogEncoding); // 新規作成時のみ BOM が書かれる
            else
                File.AppendAllText(path, line, LogEncoding);
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
