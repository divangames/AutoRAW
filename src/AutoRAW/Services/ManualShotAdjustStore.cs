using System.Text.Json;
using System.Text.Json.Serialization;
using AutoRAW.Models;

namespace AutoRAW.Services;

/// <summary>Сохранение ручных правок: для всего профиля по номеру кадра и отдельно per-file.</summary>
public static class ManualShotAdjustStore
{
    private static readonly object Gate = new();
    private static ManualShotAdjustRoot? _root;
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class ManualShotAdjustRoot
    {
        public Dictionary<string, Dictionary<string, ManualShotAdjustDto>> ProfileDefaults { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, ManualShotAdjustDto> PerFile { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ManualShotAdjustDto
    {
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        public double ZoomPercent { get; set; } = 100;
        public double RotationDeg { get; set; }
        public ZonaGridOverlayKind GridOverlay { get; set; }

        public static ManualShotAdjustDto From(ManualShotAdjust m) => new()
        {
            OffsetX = m.OffsetX,
            OffsetY = m.OffsetY,
            ZoomPercent = m.ZoomPercent,
            RotationDeg = m.RotationDeg,
            GridOverlay = m.GridOverlay
        };

        public ManualShotAdjust ToModel() => new()
        {
            OffsetX = OffsetX,
            OffsetY = OffsetY,
            ZoomPercent = ZoomPercent,
            RotationDeg = RotationDeg,
            GridOverlay = GridOverlay
        };
    }

    private static string StorePath => AppPaths.ManualShotAdjustStoreFile;

    private static void EnsureLoaded()
    {
        if (_root is not null)
            return;

        try
        {
            if (File.Exists(StorePath))
            {
                var json = File.ReadAllText(StorePath);
                _root = JsonSerializer.Deserialize<ManualShotAdjustRoot>(json, Json) ?? new ManualShotAdjustRoot();
            }
            else
            {
                _root = new ManualShotAdjustRoot();
            }
        }
        catch
        {
            _root = new ManualShotAdjustRoot();
        }
    }

    private static void Save()
    {
        EnsureLoaded();
        var dir = Path.GetDirectoryName(StorePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(StorePath, JsonSerializer.Serialize(_root, Json));
    }

    private static string NormFileKey(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>Per-file переопределяет значение профиля по ном кадру.</summary>
    public static ManualShotAdjust Resolve(string? profileDisplayName, string inputPath, string? outputStem)
    {
        lock (Gate)
        {
            EnsureLoaded();
            var key = NormFileKey(inputPath);
            if (_root!.PerFile.TryGetValue(key, out var dto))
                return dto.ToModel();

            var stem = ZonaOperationGuideParser.NormalizeShotStem(outputStem, inputPath);
            if (stem is not null
                && !string.IsNullOrWhiteSpace(profileDisplayName)
                && _root.ProfileDefaults.TryGetValue(profileDisplayName.Trim(), out var byStem)
                && byStem.TryGetValue(stem, out var pd))
            {
                return pd.ToModel();
            }

            return new ManualShotAdjust();
        }
    }

    public static void SetForProfile(string profileDisplayName, string shotStem, ManualShotAdjust value)
    {
        lock (Gate)
        {
            EnsureLoaded();
            var name = profileDisplayName.Trim();
            if (!_root!.ProfileDefaults.TryGetValue(name, out var map))
            {
                map = new Dictionary<string, ManualShotAdjustDto>(StringComparer.OrdinalIgnoreCase);
                _root.ProfileDefaults[name] = map;
            }

            if (value.HasPersistableState)
                map[shotStem] = ManualShotAdjustDto.From(value);
            else
                map.Remove(shotStem);

            Save();
        }
    }

    public static void SetForFile(string inputPath, ManualShotAdjust value)
    {
        lock (Gate)
        {
            EnsureLoaded();
            var key = NormFileKey(inputPath);
            if (value.HasPersistableState)
                _root!.PerFile[key] = ManualShotAdjustDto.From(value);
            else
                _root!.PerFile.Remove(key);

            Save();
        }
    }

    /// <summary>Сброс per-file и при необходимости профильного значения для этого стема.</summary>
    public static void ClearForFile(string inputPath)
    {
        lock (Gate)
        {
            EnsureLoaded();
            _root!.PerFile.Remove(NormFileKey(inputPath));
            Save();
        }
    }

    public static void ClearForProfileStem(string profileDisplayName, string shotStem)
    {
        lock (Gate)
        {
            EnsureLoaded();
            if (_root!.ProfileDefaults.TryGetValue(profileDisplayName.Trim(), out var map))
            {
                map.Remove(shotStem);
                if (map.Count == 0)
                    _root.ProfileDefaults.Remove(profileDisplayName.Trim());
            }

            Save();
        }
    }
}
