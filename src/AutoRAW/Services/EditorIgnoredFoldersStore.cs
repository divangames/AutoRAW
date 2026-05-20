using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoRAW.Services;

/// <summary>Подпапки «Товар», исключённые из очереди обработки (редактор и пакетная обработка).</summary>
public static class EditorIgnoredFoldersStore
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
        public List<string> IgnoredFolders { get; set; } = [];
    }

    private static string StoreFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AutoRAW",
        "editor_ignored_folders.json");

    public static HashSet<string> GetIgnoredFolders(string inputRoot)
    {
        if (string.IsNullOrWhiteSpace(inputRoot) || !Directory.Exists(inputRoot))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var rootKey = Path.GetFullPath(inputRoot.Trim());
        var dto = Load();
        var entry = dto.Entries.FirstOrDefault(e =>
            string.Equals(Path.GetFullPath(e.InputRoot.Trim()), rootKey, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return entry.IgnoredFolders
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.GetFullPath(p.Trim()))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static void SetIgnoredFolders(string inputRoot, IEnumerable<string> folders)
    {
        if (string.IsNullOrWhiteSpace(inputRoot))
            return;

        var rootKey = Path.GetFullPath(inputRoot.Trim());
        var dto = Load();
        dto.Entries.RemoveAll(e =>
            string.Equals(Path.GetFullPath(e.InputRoot.Trim()), rootKey, StringComparison.OrdinalIgnoreCase));

        var list = folders
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.GetFullPath(p.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (list.Count > 0)
        {
            dto.Entries.Add(new EntryDto
            {
                InputRoot = rootKey,
                IgnoredFolders = list
            });
        }

        Save(dto);
    }

    public static bool IsUnderIgnoredFolder(string inputRoot, string fileOrFolderPath, IReadOnlySet<string>? ignored = null)
    {
        ignored ??= GetIgnoredFolders(inputRoot);
        if (ignored.Count == 0)
            return false;

        var full = Path.GetFullPath(fileOrFolderPath);
        foreach (var ig in ignored)
        {
            if (full.Equals(ig, StringComparison.OrdinalIgnoreCase))
                return true;
            if (full.StartsWith(ig + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(ig + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
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
