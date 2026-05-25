using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoRAW.Services;

/// <summary>
/// Отдельные файлы изображений в «Товар», исключённые из очереди пакетной обработки (галочки в редакторе кадра).
/// </summary>
public static class EditorSkippedFilesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class RootDto
    {
        public List<EntryDto> Entries { get; set; } = [];
    }

    private sealed class EntryDto
    {
        public string InputRoot { get; set; } = string.Empty;
        public List<string> SkippedFiles { get; set; } = [];
    }

    private static string StoreFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AutoRAW",
        "editor_skipped_files.json");

    public static HashSet<string> GetSkippedFiles(string inputRoot)
    {
        if (string.IsNullOrWhiteSpace(inputRoot) || !Directory.Exists(inputRoot))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var rootKey = Path.GetFullPath(inputRoot.Trim());
        var dto = Load();
        var entry = dto.Entries.FirstOrDefault(e =>
            string.Equals(Path.GetFullPath(e.InputRoot.Trim()), rootKey, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return entry.SkippedFiles
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.GetFullPath(p.Trim()))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsSkipped(string inputRoot, string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        var set = GetSkippedFiles(inputRoot);
        return set.Contains(Path.GetFullPath(filePath));
    }

    public static void AddSkipped(string inputRoot, string filePath)
    {
        if (string.IsNullOrWhiteSpace(inputRoot) || string.IsNullOrWhiteSpace(filePath))
            return;

        var rootKey = Path.GetFullPath(inputRoot.Trim());
        var fp = Path.GetFullPath(filePath.Trim());

        var dto = Load();
        var entry = dto.Entries.FirstOrDefault(e =>
            string.Equals(Path.GetFullPath(e.InputRoot.Trim()), rootKey, StringComparison.OrdinalIgnoreCase));

        HashSet<string> normalized;
        if (entry is null)
        {
            entry = new EntryDto { InputRoot = rootKey, SkippedFiles = [] };
            dto.Entries.Add(entry);
            normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            normalized = entry.SkippedFiles
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => Path.GetFullPath(p.Trim()))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        normalized.Add(fp);
        entry.SkippedFiles = normalized.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        Save(dto);
    }

    public static void RemoveSkipped(string inputRoot, string filePath)
    {
        if (string.IsNullOrWhiteSpace(inputRoot) || string.IsNullOrWhiteSpace(filePath))
            return;

        var rootKey = Path.GetFullPath(inputRoot.Trim());
        var fp = Path.GetFullPath(filePath.Trim());

        var dto = Load();
        var entry = dto.Entries.FirstOrDefault(e =>
            string.Equals(Path.GetFullPath(e.InputRoot.Trim()), rootKey, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            return;

        var normalized = entry.SkippedFiles
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.GetFullPath(p.Trim()))
            .Where(p => !p.Equals(fp, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
            dto.Entries.RemoveAll(e =>
                string.Equals(Path.GetFullPath(e.InputRoot.Trim()), rootKey, StringComparison.OrdinalIgnoreCase));
        else
            entry.SkippedFiles = normalized;

        Save(dto);
    }

    /// <summary>При переименовании файла заменить путь в списке пропусков для корня «Товар».</summary>
    public static void RenameSkippedPath(string inputRoot, string oldFilePath, string newFilePath)
    {
        if (string.IsNullOrWhiteSpace(inputRoot) || string.IsNullOrWhiteSpace(oldFilePath) ||
            string.IsNullOrWhiteSpace(newFilePath))
            return;

        var rootKey = Path.GetFullPath(inputRoot.Trim());
        var fo = Path.GetFullPath(oldFilePath.Trim());
        var fn = Path.GetFullPath(newFilePath.Trim());
        if (fo.Equals(fn, StringComparison.OrdinalIgnoreCase))
            return;

        var dto = Load();
        var entry = dto.Entries.FirstOrDefault(e =>
            string.Equals(Path.GetFullPath(e.InputRoot.Trim()), rootKey, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            return;

        var normalized = entry.SkippedFiles
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.GetFullPath(p.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(p => p.Equals(fo, StringComparison.OrdinalIgnoreCase) ? fn : p)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        entry.SkippedFiles = normalized;
        Save(dto);
    }

    private static RootDto Load()
    {
        try
        {
            if (!File.Exists(StoreFile))
                return new RootDto();
            var json = File.ReadAllText(StoreFile);
            return JsonSerializer.Deserialize<RootDto>(json, JsonOptions) ?? new RootDto();
        }
        catch
        {
            return new RootDto();
        }
    }

    private static void Save(RootDto dto)
    {
        try
        {
            var dir = Path.GetDirectoryName(StoreFile);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(StoreFile, JsonSerializer.Serialize(dto, JsonOptions));
        }
        catch
        {
            /* ignore */
        }
    }
}
