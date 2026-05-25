using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoRAW.Services;

/// <summary>
/// Выбранное имя файла эталона в папке reference для входного файла (%AppData%\AutoRAW\crop_frame_refs.json).
/// Нужно, чтобы после «выкинуть» часть файлов из очереди номер выхода не пересчитывался только по порядку (1→01 для первого оставшегося).
/// </summary>
public static class PersistedCropFrameChoiceStore
{
    private static readonly object Gate = new();
    private static RootDto? _root;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class RootDto
    {
        /// <summary>Ключ — полный нормализованный путь входа.</summary>
        public Dictionary<string, string> ReferenceFileByInputPath { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private static string StorePath => AppPaths.CropFrameReferenceChoicesFile;

    private static string NormPath(string path) =>
        Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static void EnsureLoaded()
    {
        if (_root is not null)
            return;

        try
        {
            if (File.Exists(StorePath))
            {
                var json = File.ReadAllText(StorePath);
                _root = JsonSerializer.Deserialize<RootDto>(json, Json) ?? new RootDto();
            }
            else
            {
                _root = new RootDto();
            }
        }
        catch
        {
            _root = new RootDto();
        }

        if (_root.ReferenceFileByInputPath is null)
        {
            _root.ReferenceFileByInputPath =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            _root.ReferenceFileByInputPath =
                new Dictionary<string, string>(_root.ReferenceFileByInputPath, StringComparer.OrdinalIgnoreCase);
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

    public static bool TryGet(string inputPath, out string referenceFileName)
    {
        lock (Gate)
        {
            EnsureLoaded();
            return _root!.ReferenceFileByInputPath.TryGetValue(NormPath(inputPath), out referenceFileName!);
        }
    }

    /// <summary>Имя файла как в каталоге reference (с расширением).</summary>
    public static void Set(string inputPath, string referenceFileName)
    {
        if (string.IsNullOrWhiteSpace(referenceFileName))
            return;
        var v = referenceFileName.Trim();
        lock (Gate)
        {
            EnsureLoaded();
            var key = NormPath(inputPath);
            if (_root!.ReferenceFileByInputPath.TryGetValue(key, out var cur)
                && string.Equals(cur, v, StringComparison.OrdinalIgnoreCase))
                return;

            _root.ReferenceFileByInputPath[key] = v;
            Save();
        }
    }

    public static void Remove(string inputPath)
    {
        lock (Gate)
        {
            EnsureLoaded();
            if (_root!.ReferenceFileByInputPath.Remove(NormPath(inputPath)))
                Save();
        }
    }

    public static void RenamePathKey(string oldInputPath, string newInputPath)
    {
        lock (Gate)
        {
            EnsureLoaded();
            var ko = NormPath(oldInputPath);
            var kn = NormPath(newInputPath);
            if (string.Equals(ko, kn, StringComparison.OrdinalIgnoreCase))
                return;

            if (_root!.ReferenceFileByInputPath.TryGetValue(ko, out var dto))
            {
                _root.ReferenceFileByInputPath.Remove(ko);
                _root.ReferenceFileByInputPath[kn] = dto;
                Save();
            }
        }
    }
}
