using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoRAW.Services;

/// <summary>Глобальные настройки экспорта (формат файла после кадрирования).</summary>
public static class ExportPreferenceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class Root
    {
        /// <summary>По умолчанию true — сохранять в WebP; false — JPEG как раньше.</summary>
        public bool SaveAsWebP { get; set; } = true;

        /// <summary>После записи каждого кадра пакета — передать путь дроплету Photoshop (папка droplets).</summary>
        public bool RunThroughPhotoshopDroplets { get; set; }
    }

    private static string FilePath => AppPaths.ExportPreferencesFile;

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

    public static bool GetSaveAsWebP() => Load().SaveAsWebP;

    public static void SetSaveAsWebP(bool value)
    {
        var root = Load();
        root.SaveAsWebP = value;
        Save(root);
    }

    public static bool GetRunThroughPhotoshopDroplets() => Load().RunThroughPhotoshopDroplets;

    public static void SetRunThroughPhotoshopDroplets(bool value)
    {
        var root = Load();
        root.RunThroughPhotoshopDroplets = value;
        Save(root);
    }
}
