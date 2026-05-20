using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoRAW.Models;

namespace AutoRAW.Services;

/// <summary>Выбор фотографа в %AppData%\AutoRAW\photographer_prefs.json.</summary>
public static class PhotographerPreferenceStore
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
        public PhotographerKind Photographer { get; set; } = PhotographerKind.Standard;
    }

    private static string FilePath => AppPaths.PhotographerPreferencesFile;

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

    public static PhotographerKind Get() => Load().Photographer;

    public static void Set(PhotographerKind photographer)
    {
        var root = Load();
        root.Photographer = photographer;
        Save(root);
    }
}
