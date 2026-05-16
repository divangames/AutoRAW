namespace AutoRAW.Services;

/// <summary>Пароль для смены сохранённых токена и ID чата Telegram на этом ПК.</summary>
internal static class ZonaTelegramCredentialsGuard
{
    internal const string EditPassword = "0211";

    internal static bool IsEditPassword(string? value) =>
        string.Equals(value?.Trim(), EditPassword, StringComparison.Ordinal);
}
