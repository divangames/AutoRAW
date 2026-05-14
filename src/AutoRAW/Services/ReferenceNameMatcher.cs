namespace AutoRAW.Services;

/// <summary>Сопоставление входного файла и референса по имени (без расширения).</summary>
public static class ReferenceNameMatcher
{
    public static string? TryMatch(string inputPath, IEnumerable<string> referenceFileNames)
    {
        var list = referenceFileNames as IList<string> ?? referenceFileNames.ToList();
        if (list.Count == 0)
            return null;

        var stem = Path.GetFileNameWithoutExtension(inputPath);

        foreach (var name in list)
        {
            if (string.Equals(Path.GetFileNameWithoutExtension(name), stem, StringComparison.OrdinalIgnoreCase))
                return name;
        }

        foreach (var name in list)
        {
            var rs = Path.GetFileNameWithoutExtension(name);
            if (rs.Length < 2 || stem.Length < 2)
                continue;

            if (stem.Contains(rs, StringComparison.OrdinalIgnoreCase)
                || rs.Contains(stem, StringComparison.OrdinalIgnoreCase))
                return name;
        }

        return null;
    }
}
