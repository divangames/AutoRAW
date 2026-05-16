using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoRAW.Models;

namespace AutoRAW.Services;

/// <summary>Сохранение настроек Telegram в %AppData%\AutoRAW\telegram_zona.json.</summary>
public static class ZonaTelegramPreferenceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static ZonaTelegramSettings Get()
    {
        try
        {
            var path = AppPaths.TelegramZonaPreferencesFile;
            if (!File.Exists(path))
                return new ZonaTelegramSettings();

            return JsonSerializer.Deserialize<ZonaTelegramSettings>(
                       File.ReadAllText(path), JsonOptions)
                   ?? new ZonaTelegramSettings();
        }
        catch
        {
            return new ZonaTelegramSettings();
        }
    }

    public static void Set(ZonaTelegramSettings settings)
    {
        var path = AppPaths.TelegramZonaPreferencesFile;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
