using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoRAW.Models;

namespace AutoRAW.Services;

/// <summary>Переопределение цвета для профилей из комплекта приложения (в т.ч. «Кроссовки»): хранится в %AppData%, т.к. каталог программы может быть только для чтения.</summary>
public static class ProfileColorOverrideStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class Root
    {
        public Dictionary<string, ColorRowDto> Colors { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static string FilePath => AppPaths.ProfileColorOverridesFile;

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

    /// <summary>Только явно сохранённые в файле; иначе null — использовать цвет из <see cref="ProductProfile.Color"/>.</summary>
    public static ColorCorrectionSettings? TryGet(string profileName)
    {
        var root = Load();
        return root.Colors.TryGetValue(profileName, out var dto)
            ? ColorCorrectionSettings.FromDto(dto)
            : null;
    }

    public static void Set(string profileName, ColorCorrectionSettings settings)
    {
        var root = Load();
        root.Colors[profileName] = settings.ToDto();
        Save(root);
    }
}
