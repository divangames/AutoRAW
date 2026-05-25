using System.Diagnostics;
using System.Text;
using AutoRAW.Models;

namespace AutoRAW.Services;

/// <summary>
/// Запуск дроплетов Photoshop после записи экспортного файла пакета.
/// Ожидаемые имена: <c>01_drop.exe</c>, <c>02-03-04-08_drop.exe</c>, <c>05-06-07_drop.exe</c>
/// в папке <c>droplets\</c> или <c>droples\</c> рядом с программой.
/// </summary>
public static class PhotoshopDropletLauncher
{
    private static readonly string[] FolderNames = ["droplets", "droples"];

    private static readonly string[] ExpectedExeNames =
    [
        "01_drop.exe",
        "02-03-04-08_drop.exe",
        "05-06-07_drop.exe"
    ];

    /// <summary>Каталог с дроплетами, если найден (иначе путь по умолчанию <c>droplets</c> для сообщений об ошибке).</summary>
    public static string ResolveDropletsDirectory()
    {
        foreach (var name in FolderNames)
        {
            var dir = Path.Combine(AppPaths.AppRoot, name);
            if (Directory.Exists(dir))
                return Path.GetFullPath(dir);
        }

        return AppPaths.DropletsFolder;
    }

    /// <summary>Строка для журнала при старте пакета: где ищем exe.</summary>
    public static string DescribeInstallationForLog()
    {
        var dir = ResolveDropletsDirectory();
        if (!Directory.Exists(dir))
        {
            return $"Дроплеты: папка не найдена (ожидается «{AppPaths.DropletsFolder}» или «{Path.Combine(AppPaths.AppRoot, "droples")}»).";
        }

        var found = ExpectedExeNames.Where(n => FindExePath(dir, n) is not null).ToList();
        var missing = ExpectedExeNames.Where(n => FindExePath(dir, n) is null).ToList();
        var sb = new StringBuilder();
        sb.Append($"Дроплеты: каталог «{dir}».");
        if (found.Count > 0)
            sb.Append(" Найдены: ").Append(string.Join(", ", found)).Append('.');
        if (missing.Count > 0)
            sb.Append(" Нет файлов: ").Append(string.Join(", ", missing)).Append('.');
        return sb.ToString();
    }

    /// <summary>Пробует передать готовый jpeg/webp в дроплет, соответствующий номеру кадра (01–08).</summary>
    public static void TryLaunch(
        string outputFileStem,
        string exportedFileFullPath,
        string? inputPathForStem,
        Action<string, LogLineKind>? log)
    {
        exportedFileFullPath = Path.GetFullPath(exportedFileFullPath.Trim());
        if (!File.Exists(exportedFileFullPath))
        {
            log?.Invoke($"Дроплет: файл не найден «{exportedFileFullPath}».", LogLineKind.Error);
            return;
        }

        var stemNorm = ZonaOperationGuideParser.NormalizeShotStem(outputFileStem.Trim(), inputPathForStem)
            ?? ZonaOperationGuideParser.NormalizeShotStem(null, inputPathForStem);
        if (string.IsNullOrEmpty(stemNorm) || !int.TryParse(stemNorm, out var shot) || shot is < 1 or > 8)
        {
            log?.Invoke(
                $"Дроплет: номер кадра «{outputFileStem}» не распознан как 01–08 — пропуск Photoshop.",
                LogLineKind.Normal);
            return;
        }

        var exeName = DropletExeNameForShot(shot);
        if (string.IsNullOrEmpty(exeName))
            return;

        var exeDir = ResolveDropletsDirectory();
        var exeFull = FindExePath(exeDir, exeName);
        if (exeFull is null)
        {
            log?.Invoke(
                $"Дроплет: не найден «{exeName}» в «{exeDir}» (положите exe в droplets или droples рядом с AutoRAW.exe).",
                LogLineKind.Error);
            return;
        }

        var ext = Path.GetExtension(exportedFileFullPath);
        if (ext.Equals(".webp", StringComparison.OrdinalIgnoreCase))
        {
            log?.Invoke(
                "Дроплет: входной файл WebP — многие дроплеты Photoshop принимают только JPEG/TIFF; при сбое снимите галочку WebP или сохраняйте в jpg.",
                LogLineKind.Normal);
        }

        try
        {
            // UseShellExecute=true игнорирует Arguments — дроплет открывался без файла.
            var psi = new ProcessStartInfo
            {
                FileName = exeFull,
                Arguments = QuoteForProcessArgument(exportedFileFullPath),
                WorkingDirectory = Path.GetDirectoryName(exeFull) ?? exeDir,
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                log?.Invoke($"Дроплет: Process.Start вернул null для «{exeName}».", LogLineKind.Error);
                return;
            }

            log?.Invoke(
                $"Дроплет: запущен {exeName} ← {Path.GetFileName(exportedFileFullPath)} (кадр {stemNorm}, pid {process.Id})",
                LogLineKind.Normal);
        }
        catch (Exception ex)
        {
            log?.Invoke($"Дроплет: не удалось запустить «{exeName}»: {ex.Message}", LogLineKind.Error);
        }
    }

    private static string? FindExePath(string directory, string exeName)
    {
        if (!Directory.Exists(directory))
            return null;

        var exact = Path.Combine(directory, exeName);
        if (File.Exists(exact))
            return Path.GetFullPath(exact);

        foreach (var path in Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly))
        {
            if (string.Equals(Path.GetFileName(path), exeName, StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(path);
        }

        return null;
    }

    private static string QuoteForProcessArgument(string path) =>
        path.Contains(' ') || path.Contains('"') ? $"\"{path.Replace("\"", "\\\"")}\"" : path;

    private static string? DropletExeNameForShot(int shot) =>
        shot switch
        {
            1 => "01_drop.exe",
            2 or 3 or 4 or 8 => "02-03-04-08_drop.exe",
            5 or 6 or 7 => "05-06-07_drop.exe",
            _ => null
        };
}
