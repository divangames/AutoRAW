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
}
