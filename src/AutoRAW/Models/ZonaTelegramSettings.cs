namespace AutoRAW.Models;

/// <summary>Настройки бота ZONA для уведомлений в Telegram.</summary>
public sealed class ZonaTelegramSettings
{
    public bool Enabled { get; set; }

    public string BotToken { get; set; } = string.Empty;

    /// <summary>ID чата или @channel (число или строка).</summary>
    public string ChatId { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BotToken) && !string.IsNullOrWhiteSpace(ChatId);
}
