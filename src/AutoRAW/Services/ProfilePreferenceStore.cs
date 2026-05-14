using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoRAW.Models;

namespace AutoRAW.Services;

/// <summary>Сохранение галочки «Применить» цветокоррекцию по имени профиля.</summary>
public static class ProfilePreferenceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class Root
    {
        public Dictionary<string, bool> ApplyColorCorrection { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static string FilePath => AppPaths.ProfilePreferencesFile;

    private static Root Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new Root();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<Root>(json, JsonOptions) ?? new Root();
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

    public static bool GetApplyColorCorrection(string profileName, bool defaultValue = true)
    {
        var root = Load();
        return root.ApplyColorCorrection.TryGetValue(profileName, out var v) ? v : defaultValue;
    }

    public static void SetApplyColorCorrection(string profileName, bool value)
    {
        var root = Load();
        root.ApplyColorCorrection[profileName] = value;
        Save(root);
    }
}
