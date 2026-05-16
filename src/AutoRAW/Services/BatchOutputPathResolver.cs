namespace AutoRAW.Services;

/// <summary>
/// Пути выхода при пакетном кадрировании с подпапками во входе.
/// Явный выход: <c>{outputRoot}\{относительный путь от входа}</c>.
/// Без выхода: <c>{inputRoot}\webp|jpg\{относительный путь}</c>.
/// </summary>
public static class BatchOutputPathResolver
{
    public static string Resolve(string inputRoot, string inputPath, string? outputFolderRoot, bool saveAsWebP)
    {
        var inputRootFull = Path.GetFullPath(inputRoot);
        var inputPathFull = Path.GetFullPath(inputPath);
        var inputDir = Path.GetDirectoryName(inputPathFull)
            ?? throw new InvalidOperationException($"Не удалось определить каталог для: {inputPath}");

        var relDir = Path.GetRelativePath(inputRootFull, inputDir);
        if (relDir is "." or "")
            relDir = string.Empty;

        if (!string.IsNullOrWhiteSpace(outputFolderRoot))
        {
            var outRoot = Path.GetFullPath(outputFolderRoot.Trim());
            return string.IsNullOrEmpty(relDir) ? outRoot : Path.Combine(outRoot, relDir);
        }

        var formatDir = saveAsWebP ? "webp" : "jpg";
        return string.IsNullOrEmpty(relDir)
            ? Path.Combine(inputRootFull, formatDir)
            : Path.Combine(inputRootFull, formatDir, relDir);
    }
}
