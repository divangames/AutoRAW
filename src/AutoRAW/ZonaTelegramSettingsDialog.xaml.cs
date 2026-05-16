using System.Windows;
using System.Windows.Media;
using AutoRAW.Models;
using AutoRAW.Services;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;

namespace AutoRAW;

public partial class ZonaTelegramSettingsDialog : Window
{
    private ZonaTelegramSettings _settings = new();
    private bool _credentialsUnlocked;

    public ZonaTelegramSettingsDialog()
    {
        InitializeComponent();
        LoadSettings();
    }

    private void LoadSettings()
    {
        _settings = ZonaTelegramPreferenceStore.Get();
        EnabledCheck.IsChecked = _settings.Enabled;
        _credentialsUnlocked = !_settings.IsConfigured;
        ApplyCredentialFields();
    }

    private void ApplyCredentialFields()
    {
        var locked = _settings.IsConfigured && !_credentialsUnlocked;

        TokenBox.IsEnabled = !locked;
        ChatIdBox.IsReadOnly = locked;
        UnlockCredentialsBtn.Visibility = _settings.IsConfigured ? Visibility.Visible : Visibility.Collapsed;
        CredentialsHintText.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;

        if (locked)
        {
            TokenBox.Password = string.Empty;
            ChatIdBox.Text = _settings.ChatId;
        }
        else
        {
            ChatIdBox.Text = _settings.ChatId;
            if (!string.IsNullOrEmpty(_settings.BotToken))
                TokenBox.Password = _settings.BotToken;
        }
    }

    private void UnlockCredentialsClick(object sender, RoutedEventArgs e)
    {
        if (_credentialsUnlocked)
            return;

        var dlg = new PromptDialog(
            "Изменение доступа",
            "Введите пароль для изменения токена бота и ID чата:",
            isPassword: true)
        {
            Owner = this,
        };

        if (dlg.ShowDialog() != true)
            return;

        if (!ZonaTelegramCredentialsGuard.IsEditPassword(dlg.Result))
        {
            ShowStatus("Неверный пароль. Токен и ID чата не изменены.", isError: true);
            return;
        }

        _credentialsUnlocked = true;
        ApplyCredentialFields();
        TokenBox.Focus();
        ShowStatus("Можно изменить токен и ID чата. Нажмите «Сохранить», чтобы записать на этом ПК.", isError: false);
    }

    private ZonaTelegramSettings ReadFromForm()
    {
        if (_settings.IsConfigured && !_credentialsUnlocked)
        {
            return new ZonaTelegramSettings
            {
                Enabled = EnabledCheck.IsChecked == true,
                BotToken = _settings.BotToken,
                ChatId = _settings.ChatId,
            };
        }

        var token = TokenBox.Password.Trim();
        if (string.IsNullOrEmpty(token))
            token = _settings.BotToken;

        return new ZonaTelegramSettings
        {
            Enabled = EnabledCheck.IsChecked == true,
            BotToken = token,
            ChatId = ChatIdBox.Text.Trim(),
        };
    }

    private async void TestClick(object sender, RoutedEventArgs e)
    {
        var draft = ReadFromForm();
        if (!draft.IsConfigured)
        {
            ShowStatus("Укажите токен и ID чата или разблокируйте сохранённые значения.", isError: true);
            return;
        }

        TestBtn.IsEnabled = false;
        ShowStatus("Отправка…", isError: false);

        var testText = ZonaMessages.NextTelegramTest();
        var result = await ZonaTelegramNotifyService.SendAsync(draft, testText).ConfigureAwait(true);

        TestBtn.IsEnabled = true;
        ShowStatus(
            result.Ok ? "Сообщение отправлено. Проверьте чат." : $"Ошибка: {result.ErrorMessage}",
            isError: !result.Ok);
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        var draft = ReadFromForm();
        if (draft.Enabled && !draft.IsConfigured)
        {
            ShowStatus("Включены уведомления, но не заполнены токен или ID чата.", isError: true);
            return;
        }

        ZonaTelegramPreferenceStore.Set(draft);
        _settings = draft;
        _credentialsUnlocked = !draft.IsConfigured;
        ApplyCredentialFields();
        DialogResult = true;
        Close();
    }

    private void ShowStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
        StatusText.Foreground = isError
            ? (WpfBrush?)FindResource("Theme.BubbleTextError") ?? new SolidColorBrush(WpfColor.FromRgb(0xF0, 0x70, 0x70))
            : (WpfBrush?)FindResource("Theme.TextMuted") ?? new SolidColorBrush(WpfColor.FromRgb(0xA0, 0xA0, 0xA0));
    }
}
