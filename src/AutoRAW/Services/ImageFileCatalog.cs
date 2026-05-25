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

    public static IReadOnlyList<string> ListImagesRecursive(string folder)
    {
        return ListImagesRecursiveUnder(folder, recursiveRootForOutputSkip: folder);
    }

    /// <summary>
    /// Все изображения под <paramref name="subtreeFolder"/> (рекурсивно), но без деревьев
    /// <c>{recursiveRootForOutputSkip}\webp\…</c> и <c>…\jpg\…</c> — чтобы не смешивать экспорт программы с исходниками при повторной загрузке папки.
    /// Используется редактором: листинг по выбранной подпапке «Товар» с глубиной &gt;1.
    /// </summary>
    public static IReadOnlyList<string> ListImagesRecursiveUnderSubtree(
        string subtreeFolder,
        string recursiveRootForOutputSkip)
    {
        return ListImagesRecursiveUnder(subtreeFolder, recursiveRootForOutputSkip);
    }

    private static IReadOnlyList<string> ListImagesRecursiveUnder(
        string folder,
        string recursiveRootForOutputSkip)
    {
        if (!Directory.Exists(folder))
            return [];

        var root = Path.GetFullPath(recursiveRootForOutputSkip);
        var subtree = Path.GetFullPath(folder);

        var webpRoot = Path.Combine(root, "webp");
        var jpgRoot = Path.Combine(root, "jpg");

        var formatDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { webpRoot, jpgRoot };

        var subtreeBrowsesExportedFormat =
            IsSameOrDescendantDirectory(subtree, webpRoot)
            || IsSameOrDescendantDirectory(subtree, jpgRoot);

        return Directory.EnumerateFiles(subtree, "*", SearchOption.AllDirectories)
            .Where(p =>
            {
                if (!IsImageFile(p))
                    return false;

                var dir = Path.GetDirectoryName(Path.GetFullPath(p));
                if (dir is null)
                    return true;

                if (!subtreeBrowsesExportedFormat)
                {
                    foreach (var skip in formatDirs)
                    {
                        if (dir.Equals(skip, StringComparison.OrdinalIgnoreCase)
                            || dir.StartsWith(skip + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                            || dir.StartsWith(skip + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                            return false;
                    }
                }

                return true;
            })
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsSameOrDescendantDirectory(string maybeDescendant, string ancestorDirectory)
    {
        try
        {
            var d = Path.GetFullPath(maybeDescendant).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var a = Path.GetFullPath(ancestorDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (d.Equals(a, StringComparison.OrdinalIgnoreCase))
                return true;
            return d.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || d.StartsWith(a + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
