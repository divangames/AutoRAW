using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoRAW.Services;

public enum RawLoaderMode
{
    Magick = 0,
    LibRawPreferred = 1,
    LibRawOnly = 2
}

/// <summary>Как открывать RAW: ImageMagick или LibRaw (Sdcb.LibRaw).</summary>
public static class RawLoaderPreferenceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class Root
    {
        public RawLoaderMode Mode { get; set; } = RawLoaderMode.LibRawPreferred;
    }

    private static string FilePath => AppPaths.RawLoaderPreferencesFile;

    public static RawLoaderMode GetMode() => Load().Mode;

    public static void SetMode(RawLoaderMode mode)
    {
        var root = Load();
        root.Mode = mode;
        Save(root);
    }

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
}
