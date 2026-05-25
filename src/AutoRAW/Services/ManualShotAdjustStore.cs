using System.Text.Json;
using System.Text.Json.Serialization;
using AutoRAW.Models;

namespace AutoRAW.Services;

/// <summary>Сохранение правок: per-file, профиль по номеру кадра, профиль по имени файла.</summary>
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

        /// <summary>Внешний ключ — профиль; внутренний — нормализованное имя файла (basename, lower-case).</summary>
        public Dictionary<string, Dictionary<string, ManualShotAdjustDto>> ProfileByBaseFileName { get; set; } = new(StringComparer.OrdinalIgnoreCase);

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
        {
            EnsureProfileByBaseFileNameDict();
            return;
        }

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

        EnsureProfileByBaseFileNameDict();
    }

    private static void EnsureProfileByBaseFileNameDict()
    {
        if (_root!.ProfileByBaseFileName is null)
            _root.ProfileByBaseFileName = new Dictionary<string, Dictionary<string, ManualShotAdjustDto>>(StringComparer.OrdinalIgnoreCase);
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

    /// <summary>Имя файла с расширением, без пути, lower-case (стабильный ключ в json).</summary>
    private static string NormalizeBaseFileName(string inputPathOrFileName)
    {
        var raw = inputPathOrFileName.Trim();
        var name = Path.GetFileName(raw);
        return string.IsNullOrEmpty(name) ? string.Empty : name.ToLowerInvariant();
    }

    public static bool TryGetPerFile(string inputPath, out ManualShotAdjust adjust)
    {
        lock (Gate)
        {
            EnsureLoaded();
            if (_root!.PerFile.TryGetValue(NormFileKey(inputPath), out var dto))
            {
                adjust = dto.ToModel();
                return true;
            }
        }

        adjust = new ManualShotAdjust();
        return false;
    }

    public static bool TryGetProfileBasename(string? profileDisplayName, string inputPath, out ManualShotAdjust adjust)
    {
        adjust = new ManualShotAdjust();
        if (string.IsNullOrWhiteSpace(profileDisplayName))
            return false;

        var baseKey = NormalizeBaseFileName(inputPath);
        if (string.IsNullOrEmpty(baseKey))
            return false;

        lock (Gate)
        {
            EnsureLoaded();
            if (_root!.ProfileByBaseFileName.TryGetValue(profileDisplayName.Trim(), out var byName)
                && byName.TryGetValue(baseKey, out var dto))
            {
                adjust = dto.ToModel();
                return true;
            }
        }

        return false;
    }

    public static bool TryGetProfileStem(string? profileDisplayName, string shotStem, out ManualShotAdjust adjust)
    {
        adjust = new ManualShotAdjust();
        if (string.IsNullOrWhiteSpace(profileDisplayName))
            return false;

        lock (Gate)
        {
            EnsureLoaded();
            if (_root!.ProfileDefaults.TryGetValue(profileDisplayName.Trim(), out var byStem)
                && byStem.TryGetValue(shotStem, out var dto))
            {
                adjust = dto.ToModel();
                return true;
            }
        }

        if (SneakersBuiltInManualShotDefaults.TryGetAdjust(profileDisplayName, shotStem, out adjust))
            return true;

        return false;
    }

    /// <summary>Per-file переопределяет значение профиля по ном кадру.</summary>
    public static ManualShotAdjust Resolve(string? profileDisplayName, string inputPath, string? outputStem)
    {
        lock (Gate)
        {
            EnsureLoaded();
            var key = NormFileKey(inputPath);
            if (_root!.PerFile.TryGetValue(key, out var dto))
                return dto.ToModel();

            var baseKey = NormalizeBaseFileName(inputPath);
            if (!string.IsNullOrWhiteSpace(profileDisplayName)
                && baseKey.Length > 0
                && _root.ProfileByBaseFileName.TryGetValue(profileDisplayName.Trim(), out var byBase)
                && byBase.TryGetValue(baseKey, out var baseDto))
                return baseDto.ToModel();

            var stem = ZonaOperationGuideParser.NormalizeShotStem(outputStem, inputPath);
            if (stem is not null
                && !string.IsNullOrWhiteSpace(profileDisplayName)
                && _root.ProfileDefaults.TryGetValue(profileDisplayName.Trim(), out var byStem)
                && byStem.TryGetValue(stem, out var pd))
            {
                return pd.ToModel();
            }

            if (stem is not null
                && SneakersBuiltInManualShotDefaults.TryGetAdjust(profileDisplayName, stem, out var builtIn))
                return builtIn;

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

    /// <summary>Правило профиля для всех файлов с таким именем (без пути), пока не удалите через меню.</summary>
    public static void SetForProfileBasename(string profileDisplayName, string baseFileNameOrPath, ManualShotAdjust value)
    {
        lock (Gate)
        {
            EnsureLoaded();
            var name = profileDisplayName.Trim();
            var bk = NormalizeBaseFileName(baseFileNameOrPath);
            if (string.IsNullOrEmpty(bk))
                return;

            if (!_root!.ProfileByBaseFileName.TryGetValue(name, out var inner))
            {
                inner = new Dictionary<string, ManualShotAdjustDto>(StringComparer.OrdinalIgnoreCase);
                _root.ProfileByBaseFileName[name] = inner;
            }

            if (value.HasPersistableState)
                inner[bk] = ManualShotAdjustDto.From(value);
            else
                inner.Remove(bk);

            if (inner.Count == 0)
                _root.ProfileByBaseFileName.Remove(name);

            Save();
        }
    }

    public static void ClearForProfileBasename(string profileDisplayName, string inputPathOrBaseName)
    {
        lock (Gate)
        {
            EnsureLoaded();
            var bk = NormalizeBaseFileName(inputPathOrBaseName);
            if (string.IsNullOrEmpty(bk))
                return;

            var name = profileDisplayName.Trim();
            if (!_root!.ProfileByBaseFileName.TryGetValue(name, out var inner))
                return;

            inner.Remove(bk);
            if (inner.Count == 0)
                _root.ProfileByBaseFileName.Remove(name);

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

    /// <summary>
    /// Массовая запись per-file одним <see cref="Save"/> — для фоновой авто-подгонки при загрузке папки в редакторе.
    /// Пропускает ключи, которые уже есть (ручные правки пользователя).
    /// </summary>
    public static int UpsertPerFileBatchSkipExisting(IReadOnlyList<(string Path, ManualShotAdjust Adjust)> items)
    {
        if (items.Count == 0)
            return 0;

        lock (Gate)
        {
            EnsureLoaded();
            var n = 0;
            foreach (var (path, adj) in items)
            {
                if (!adj.HasPersistableState)
                    continue;

                var key = NormFileKey(path);
                if (_root!.PerFile.ContainsKey(key))
                    continue;

                _root.PerFile[key] = ManualShotAdjustDto.From(adj);
                n++;
            }

            if (n > 0)
                Save();

            return n;
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

    /// <summary>Перенести per-file правки при переименовании входного файла на диске.</summary>
    public static void RenamePerFileKey(string oldInputPath, string newInputPath)
    {
        lock (Gate)
        {
            EnsureLoaded();
            var ko = NormFileKey(oldInputPath);
            var kn = NormFileKey(newInputPath);
            if (string.Equals(ko, kn, StringComparison.OrdinalIgnoreCase))
                return;

            if (_root!.PerFile.TryGetValue(ko, out var dto))
            {
                _root.PerFile.Remove(ko);
                _root.PerFile[kn] = dto;
                Save();
            }
        }
    }
}
