using AutoRAW.Models;
using ImageMagick;

namespace AutoRAW.Services;

public sealed class AutoCropBatchService
{
    /// <summary>
    /// Каждая пара: (inputPath, referencePath). Если zonaFolder задан и файл зоны существует —
    /// кроп берётся напрямую из красного прямоугольника zona-изображения, референс игнорируется.
    /// </summary>
    /// <param name="outputFolder">
    /// Явная папка выхода для всех файлов; если null или пусто — для каждого входа:
    /// <c>DirectoryName(input)\webp</c> или <c>DirectoryName(input)\jpg</c> в зависимости от <paramref name="saveAsWebP"/>.
    /// </param>
    public void RunMappings(
        IReadOnlyList<(string inputPath, string referencePath)> pairs,
        string? outputFolder,
        int analysisMaxEdge,
        Action<string> log,
        CancellationToken cancellationToken,
        string? zonaFolder = null,
        ColorCorrectionSettings? colorCorrection = null,
        bool applyColorCorrection = false,
        bool saveAsWebP = false)
    {
        if (pairs.Count == 0)
        {
            log("Нет строк для обработки.");
            return;
        }

        var refCache = new Dictionary<string, AutoCropComputation.ReferenceMetrics>(StringComparer.OrdinalIgnoreCase);
        log($"Заданий: {pairs.Count}");

        foreach (var (inputPath, referencePath) in pairs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = Path.GetFileName(inputPath);
            var stem = Path.GetFileNameWithoutExtension(inputPath);

            try
            {
                var zonaPath = zonaFolder is not null
                    ? FindZonaFile(zonaFolder, stem)
                    : null;

                var outDir = ResolveOutputDirectory(outputFolder, inputPath, saveAsWebP);

                if (zonaPath is not null)
                {
                    log($"Zona-кроп: {name}");
                    ProcessWithZona(inputPath, outDir, zonaPath, log, colorCorrection, applyColorCorrection, saveAsWebP);
                }
                else
                {
                    var refName = Path.GetFileName(referencePath);

                    if (string.Equals(inputPath, referencePath, StringComparison.OrdinalIgnoreCase))
                    {
                        log($"Пропуск (файл совпадает с референсом): {name}");
                        continue;
                    }

                    if (!refCache.TryGetValue(referencePath, out var metrics))
                    {
                        metrics = AutoCropComputation.AnalyzeReference(referencePath, analysisMaxEdge);
                        refCache[referencePath] = metrics;
                        log($"Референс (анализ): {refName}");
                    }

                    ProcessWithReference(inputPath, outDir, metrics, analysisMaxEdge, colorCorrection, applyColorCorrection, saveAsWebP);
                    log($"OK: {name}  (референс: {refName})");
                }
            }
            catch (Exception ex)
            {
                log($"Ошибка «{name}»: {ex.Message}");
            }
        }

        log("Готово.");
    }

    private static string ResolveOutputDirectory(string? outputFolderRoot, string inputPath, bool saveAsWebP)
    {
        if (!string.IsNullOrWhiteSpace(outputFolderRoot))
            return Path.GetFullPath(outputFolderRoot.Trim());

        var inputDir = Path.GetDirectoryName(Path.GetFullPath(inputPath));
        if (string.IsNullOrEmpty(inputDir))
            throw new InvalidOperationException($"Не удалось определить каталог для входного файла: {inputPath}");

        var sub = saveAsWebP ? "webp" : "jpg";
        return Path.Combine(inputDir, sub);
    }

    private static string? FindZonaFile(string zonaFolder, string stem)
    {
        foreach (var f in Directory.EnumerateFiles(zonaFolder))
        {
            if (string.Equals(Path.GetFileNameWithoutExtension(f), stem, StringComparison.OrdinalIgnoreCase)
                && ImageFileCatalog.IsImageFile(f))
                return f;
        }
        return null;
    }

    private static void ProcessWithZona(
        string inputPath,
        string outputFolder,
        string zonaPath,
        Action<string> log,
        ColorCorrectionSettings? colorCorrection,
        bool applyColorCorrection,
        bool saveAsWebP)
    {
        var zona = ZonaCropService.Detect(zonaPath);
        if (zona is null)
        {
            log($"  Красный прямоугольник не найден в zona, пропуск: {Path.GetFileName(zonaPath)}");
            return;
        }

        using var full = RasterImageLoader.Load(inputPath);

        Directory.CreateDirectory(outputFolder);

        var outName = Path.GetFileNameWithoutExtension(inputPath) + (saveAsWebP ? ".webp" : ".jpg");
        var outPath = Path.Combine(outputFolder, outName);

        using var cropped = ZonaCropService.Crop(full, zona.Value);
        if (colorCorrection is not null)
            ColorCorrectionService.ApplyIfEnabled(cropped, colorCorrection, applyColorCorrection);
        WriteCroppedFile(cropped, outPath, saveAsWebP);

        log($"  OK zona: {Path.GetFileName(inputPath)} → угол={zona.Value.RectInZonaCoords.Angle:F1}°, rotated={zona.Value.IsRotated}, цвет={(applyColorCorrection ? "да" : "нет")}");
    }

    private static void ProcessWithReference(
        string inputPath,
        string outputFolder,
        AutoCropComputation.ReferenceMetrics reference,
        int analysisMaxEdge,
        ColorCorrectionSettings? colorCorrection,
        bool applyColorCorrection,
        bool saveAsWebP)
    {
        using var full = RasterImageLoader.Load(inputPath);
        var target = AutoCropComputation.AnalyzeTarget(full, analysisMaxEdge);
        var crop = AutoCropComputation.ComputeCropBox(reference, target);
        var (x, y, w, h) = CropGeometryService.ToIntegers(crop, (int)full.Width, (int)full.Height);

        using var cropped = (MagickImage)full.Clone();
        cropped.Crop(new MagickGeometry { X = x, Y = y, Width = (uint)w, Height = (uint)h });
        cropped.ResetPage();

        if (colorCorrection is not null)
            ColorCorrectionService.ApplyIfEnabled(cropped, colorCorrection, applyColorCorrection);

        Directory.CreateDirectory(outputFolder);

        var outName = Path.GetFileNameWithoutExtension(inputPath) + (saveAsWebP ? ".webp" : ".jpg");
        var outPath = Path.Combine(outputFolder, outName);
        WriteCroppedFile(cropped, outPath, saveAsWebP);
    }

    private static void WriteCroppedFile(MagickImage cropped, string outPath, bool saveAsWebP)
    {
        if (saveAsWebP)
        {
            cropped.Format = MagickFormat.WebP;
            cropped.Quality = 90;
        }
        else
        {
            cropped.Format = MagickFormat.Jpeg;
            cropped.Quality = 92;
        }

        cropped.Write(outPath);
    }
}
