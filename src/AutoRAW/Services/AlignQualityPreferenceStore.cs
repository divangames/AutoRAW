using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoRAW.Services;

/// <summary>Порог оценки совпадения композиции с референсом (фаза 3).</summary>
public static class AlignQualityPreferenceStore
{
    private const int DefaultMinPercent = 68;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class Root
    {
        /// <summary>Минимальная оценка 0–100; ниже — ⚠ в очереди и пакете.</summary>
        public int MinAlignQualityPercent { get; set; } = DefaultMinPercent;
    }

    private static string FilePath => AppPaths.AlignQualityPreferencesFile;

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

    public static int GetMinAlignQualityPercent()
    {
        var v = Load().MinAlignQualityPercent;
        return Math.Clamp(v, 40, 95);
    }

    public static void SetMinAlignQualityPercent(int value)
    {
        var root = Load();
        root.MinAlignQualityPercent = Math.Clamp(value, 40, 95);
        Save(root);
    }
}
