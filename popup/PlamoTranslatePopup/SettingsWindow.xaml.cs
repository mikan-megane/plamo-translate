using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PlamoTranslatePopup.Models;
using PlamoTranslatePopup.Services;

namespace PlamoTranslatePopup;

/// <summary>
/// 設定画面: ショートカットキー・自動クローズ秒数・不透明度を編集し、
/// 保存で popup.json への書き込みと即時適用を行う。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly PopupSettings _base;
    private readonly Func<PopupSettings, bool> _apply;
    private string _translateHotkey;
    private string _ocrHotkey;

    public SettingsWindow(PopupSettings current, Func<PopupSettings, bool> apply)
    {
        InitializeComponent();
        _base = current.Clone();
        _apply = apply;
        _translateHotkey = current.HotkeyTranslate;
        _ocrHotkey = current.HotkeyOcr;
        HotkeyTranslateBox.Text = _translateHotkey;
        HotkeyOcrBox.Text = _ocrHotkey;
        AutoCloseBox.Text = current.AutoCloseSeconds.ToString();
        IdleTimeoutBox.Text = current.ServerIdleTimeoutSeconds.ToString();
        OpacitySlider.Value = Math.Clamp(current.PopupOpacity * 100, 30, 100);
        OpacityValueText.Text = $"{(int)OpacitySlider.Value}%";
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityValueText is { } text)
            text.Text = $"{(int)OpacitySlider.Value}%";
    }

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box)
            box.Text = "キーを押してください (Esc: キャンセル)";
    }

    private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box)
            RestoreText(box);
    }

    private void RestoreText(TextBox box) =>
        box.Text = ReferenceEquals(box, HotkeyTranslateBox) ? _translateHotkey : _ocrHotkey;

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box)
            return;
        e.Handled = true;

        // Alt などは System キーとして来るため読み替える
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        switch (key)
        {
            case Key.Escape:
                RestoreText(box);
                return;

            // 修飾キー単独は確定せず「Ctrl + ...」のように進捗表示のみ
            case Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin:
                box.Text = HotkeyUtil.ToDisplayString(Keyboard.Modifiers, key) + " ...";
                return;
        }

        var display = HotkeyUtil.ToDisplayString(Keyboard.Modifiers, key);
        if (!HotkeyUtil.TryParse(display, out _, out _))
        {
            box.Text = $"{display} (このキーは登録できません)";
            return;
        }

        box.Text = display;
        if (ReferenceEquals(box, HotkeyTranslateBox))
            _translateHotkey = display;
        else
            _ocrHotkey = display;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_translateHotkey == _ocrHotkey)
        {
            MessageBox.Show(this,
                "2つのショートカットキーに同じキーは割り当てられません。",
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(AutoCloseBox.Text.Trim(), out var seconds) || seconds is < 0 or > 3600)
        {
            MessageBox.Show(this,
                "自動で閉じる秒数は 0〜3600 の整数で入力してください。",
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(IdleTimeoutBox.Text.Trim(), out var idleTimeout) || idleTimeout is < 0 or > 86400)
        {
            MessageBox.Show(this,
                "放置したら自動終了の秒数は 0〜86400 の整数で入力してください。",
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var settings = _base.Clone();
        settings.HotkeyTranslate = _translateHotkey;
        settings.HotkeyOcr = _ocrHotkey;
        settings.AutoCloseSeconds = seconds;
        settings.ServerIdleTimeoutSeconds = idleTimeout;
        settings.PopupOpacity = OpacitySlider.Value / 100.0;

        if (!_apply(settings))
        {
            MessageBox.Show(this,
                "ショートカットキーが他のアプリと競合しているため保存できませんでした。\n別のキーを指定してください。",
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Close();
    }
}
