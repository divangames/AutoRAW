namespace AutoRAW.Services;

public static class ImageFileCatalog
{
    public static readonly string[] Extensions =
    [
        ".jpg", ".jpeg", ".png", ".webp", ".tif", ".tiff", ".bmp",
        ".nef", ".nrw", ".arw", ".srf", ".sr2", ".dng", ".cr2", ".cr3",
        ".orf", ".pef", ".raf", ".rw2", ".raw", ".heic", ".heif"
    ];

    public static bool IsImageFile(string path)
    {
        if (ShotLineGuideParser.IsLineGuideFile(path))
            return false;

        var ext = Path.GetExtension(path);
        return Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> ListImagesInFolder(string folder)
    {
        if (!Directory.Exists(folder))
            return [];

        return Directory.EnumerateFiles(folder)
            .Where(IsImageFile)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Все изображения в папке и во вложенных подпапках (без каталогов webp/jpg на выходе).</summary>
    public static IReadOnlyList<string> ListImagesRecursive(string folder)
    {
        if (!Directory.Exists(folder))
            return [];

        var root = Path.GetFullPath(folder);
        var formatDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(root, "webp"),
            Path.Combine(root, "jpg")
        };

        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(p =>
            {
                if (!IsImageFile(p))
                    return false;
                var dir = Path.GetDirectoryName(Path.GetFullPath(p));
                if (dir is null)
                    return true;
                foreach (var skip in formatDirs)
                {
                    if (dir.Equals(skip, StringComparison.OrdinalIgnoreCase)
                        || dir.StartsWith(skip + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        || dir.StartsWith(skip + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                return true;
            })
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
