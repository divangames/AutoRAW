using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoRAW.Models;

namespace AutoRAW.Services;

/// <summary>Сохранение выбранной темы в %AppData%\AutoRAW\theme_prefs.json (как остальные настройки в Roaming).</summary>
public static class ThemePreferenceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class Root
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public AppUiTheme Theme { get; set; } = AppUiTheme.Light;
    }

    private static string FilePath => AppPaths.ThemePreferencesFile;

    private static Root Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new Root();
            return JsonSerializer.Deserialize<Root>(File.ReadAllText(FilePath), JsonOptions) ?? new Root();
        }
        catch
        {
            return new Root();
        }
    }

    private static void Save(Root root)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(root, JsonOptions));
    }

    public static AppUiTheme Get() => Load().Theme;

    public static void Set(AppUiTheme theme)
    {
        var root = Load();
        root.Theme = theme;
        Save(root);
    }
}
