using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using AutoRAW.Models;

namespace AutoRAW.Services;

/// <summary>Отправка сообщений от ZONA через Telegram Bot API.</summary>
public static class ZonaTelegramNotifyService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    public sealed record SendResult(bool Ok, string? ErrorMessage);

    /// <summary>Отправляет текст, если уведомления включены и бот настроен. Ошибки не пробрасывает.</summary>
    public static async Task<SendResult> TrySendAsync(string text, CancellationToken cancellationToken = default)
    {
        var settings = ZonaTelegramPreferenceStore.Get();
        return await SendAsync(settings, text, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<SendResult> SendAsync(
        ZonaTelegramSettings settings,
        string text,
        CancellationToken cancellationToken = default)
    {
        if (!settings.Enabled)
            return new SendResult(false, "Уведомления выключены.");

        if (!settings.IsConfigured)
            return new SendResult(false, "Укажите токен бота и ID чата.");

        var body = NormalizeMessage(text);
        if (body.Length == 0)
            return new SendResult(false, "Пустое сообщение.");

        if (body.Length > 4000)
            body = body[..3997] + "…";

        try
        {
            var token = settings.BotToken.Trim();
            var url = $"https://api.telegram.org/bot{token}/sendMessage";
            var payload = new SendMessageRequest
            {
                ChatId = settings.ChatId.Trim(),
                Text = body,
                DisableWebPagePreview = true,
            };

            using var response = await Http.PostAsJsonAsync(url, payload, cancellationToken)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
                return new SendResult(true, null);

            var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new SendResult(false, $"HTTP {(int)response.StatusCode}: {TrimApiError(detail)}");
        }
        catch (Exception ex)
        {
            return new SendResult(false, ex.Message);
        }
    }

    /// <summary>Фоновая отправка без блокировки UI.</summary>
    public static void TrySendFireAndForget(string text) =>
        _ = Task.Run(async () => await TrySendAsync(text).ConfigureAwait(false));

    private static string NormalizeMessage(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.Replace("**", string.Empty))
        {
            if (ch is '\r' or '\n' or >= ' ')
                sb.Append(ch);
        }

        return sb.ToString().Trim();
    }

    private static string TrimApiError(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "неизвестная ошибка";

        const int max = 240;
        var oneLine = raw.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return oneLine.Length <= max ? oneLine : oneLine[..max] + "…";
    }

    private sealed class SendMessageRequest
    {
        [JsonPropertyName("chat_id")]
        public string ChatId { get; set; } = string.Empty;

        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("disable_web_page_preview")]
        public bool DisableWebPagePreview { get; set; }
    }
}
