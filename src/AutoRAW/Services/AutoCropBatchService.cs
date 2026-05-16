using AutoRAW.Models;
using ImageMagick;

namespace AutoRAW.Services;

public sealed class AutoCropBatchService
{
    public delegate void BatchLogHandler(string message, LogLineKind kind = LogLineKind.Normal);

    public delegate void BatchProgressHandler(int completed, int total, int succeeded, int errors);

    /// <summary>
    /// Если задана папка zona — кроп по технологии «Zona» (красный маркёр); иначе по референсу.
    /// </summary>
    public BatchRunResult RunMappings(
        IReadOnlyList<(string inputPath, string referencePath)> pairs,
        string inputRoot,
        string? outputFolder,
        int analysisMaxEdge,
        BatchLogHandler log,
        BatchRunController runControl,
        BatchProgressHandler? onProgress = null,
        string? zonaFolder = null,
        ColorCorrectionSettings? colorCorrection = null,
        bool applyColorCorrection = false,
        bool saveAsWebP = false)
    {
        if (pairs.Count == 0)
        {
            log("Нет строк для обработки.");
            return new BatchRunResult(false, 0, 0, 0, TimeSpan.Zero);
        }

        var refCache = new Dictionary<string, AutoCropComputation.ReferenceMetrics>(StringComparer.OrdinalIgnoreCase);
        var inputRootFull = Path.GetFullPath(inputRoot);
        var total = pairs.Count;
        var completed = 0;
        var succeeded = 0;
        var errors = 0;
        var cancelled = false;

        log($"Заданий: {total}");
        onProgress?.Invoke(0, total, 0, 0);

        foreach (var (inputPath, referencePath) in pairs)
        {
            try
            {
                runControl.WaitIfPausedOrCancelled();
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                break;
            }

            var rel = TryGetRelativeDisplayPath(inputRootFull, inputPath);

            try
            {
                var zonaPath = zonaFolder is not null
                    ? FindZonaFile(zonaFolder, inputRootFull, inputPath)
                    : null;

                var outDir = BatchOutputPathResolver.Resolve(inputRootFull, inputPath, outputFolder, saveAsWebP);

                if (zonaPath is not null)
                {
                    if (!refCache.TryGetValue(referencePath, out var zonaRefMetrics))
                    {
                        runControl.WaitIfPausedOrCancelled();
                        zonaRefMetrics = AutoCropComputation.AnalyzeReference(referencePath, analysisMaxEdge);
                        refCache[referencePath] = zonaRefMetrics;
                    }

                    log($"Технология Zona: {rel}");
                    if (ProcessWithZona(inputPath, outDir, zonaPath, zonaRefMetrics, runControl, log, colorCorrection, applyColorCorrection, saveAsWebP))
                        succeeded++;
                    else
                        errors++;
                }
                else
                {
                    var refName = Path.GetFileName(referencePath);

                    if (string.Equals(inputPath, referencePath, StringComparison.OrdinalIgnoreCase))
                    {
                        log($"Пропуск (файл совпадает с референсом): {rel}");
                    }
                    else
                    {
                        if (!refCache.TryGetValue(referencePath, out var metrics))
                        {
                            runControl.WaitIfPausedOrCancelled();
                            metrics = AutoCropComputation.AnalyzeReference(referencePath, analysisMaxEdge);
                            refCache[referencePath] = metrics;
                            log($"Референс (анализ): {refName}");
                        }

                        ProcessWithReference(inputPath, outDir, metrics, runControl, analysisMaxEdge, colorCorrection, applyColorCorrection, saveAsWebP);
                        log($"OK: {rel}  (референс: {refName})");
                        succeeded++;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                break;
            }
            catch (Exception ex)
            {
                errors++;
                log($"Ошибка «{rel}»: {ex.Message}", LogLineKind.Error);
            }
            finally
            {
                completed++;
                onProgress?.Invoke(completed, total, succeeded, errors);
            }
        }

        if (cancelled)
            log("Кадрирование отменено.", LogLineKind.Cancel);

        return new BatchRunResult(cancelled, total, succeeded, errors, runControl.ActiveElapsed);
    }

    private static string TryGetRelativeDisplayPath(string inputRootFull, string inputPath)
    {
        try
        {
            var rel = Path.GetRelativePath(inputRootFull, Path.GetFullPath(inputPath));
            return rel == "." ? Path.GetFileName(inputPath) : rel;
        }
        catch
        {
            return Path.GetFileName(inputPath);
        }
    }

    private static string? FindZonaFile(string zonaFolder, string inputRootFull, string inputPath)
    {
        var stem = Path.GetFileNameWithoutExtension(inputPath);
        var inputDir = Path.GetDirectoryName(Path.GetFullPath(inputPath));
        if (inputDir is not null)
        {
            var relDir = Path.GetRelativePath(inputRootFull, inputDir);
            if (relDir is not "." and not "")
            {
                var nestedZona = Path.Combine(zonaFolder, relDir);
                if (Directory.Exists(nestedZona))
                {
                    var inNested = FindZonaFileInDirectory(nestedZona, stem);
                    if (inNested is not null)
                        return inNested;
                }
            }
        }

        var inRoot = FindZonaFileInDirectory(zonaFolder, stem);
        if (inRoot is not null)
            return inRoot;

        foreach (var f in Directory.EnumerateFiles(zonaFolder, "*", SearchOption.AllDirectories))
        {
            if (string.Equals(Path.GetFileNameWithoutExtension(f), stem, StringComparison.OrdinalIgnoreCase)
                && ImageFileCatalog.IsImageFile(f))
                return f;
        }

        return null;
    }

    private static string? FindZonaFileInDirectory(string directory, string stem)
    {
        if (!Directory.Exists(directory))
            return null;

        foreach (var f in Directory.EnumerateFiles(directory))
        {
            if (string.Equals(Path.GetFileNameWithoutExtension(f), stem, StringComparison.OrdinalIgnoreCase)
                && ImageFileCatalog.IsImageFile(f))
                return f;
        }

        return null;
    }

    private static bool ProcessWithZona(
        string inputPath,
        string outputFolder,
        string zonaPath,
        AutoCropComputation.ReferenceMetrics reference,
        BatchRunController runControl,
        BatchLogHandler log,
        ColorCorrectionSettings? colorCorrection,
        bool applyColorCorrection,
        bool saveAsWebP)
    {
        runControl.WaitIfPausedOrCancelled();
        var zona = ZonaCropService.Detect(zonaPath);
        if (zona is null)
        {
            log($"  Маркёр Zona: красная зона не найдена, пропуск: {Path.GetFileName(zonaPath)}", LogLineKind.Error);
            return false;
        }

        runControl.WaitIfPausedOrCancelled();
        using var full = RasterImageLoader.Load(inputPath);
        runControl.WaitIfPausedOrCancelled();

        Directory.CreateDirectory(outputFolder);

        var outName = Path.GetFileNameWithoutExtension(inputPath) + (saveAsWebP ? ".webp" : ".jpg");
        var outPath = Path.Combine(outputFolder, outName);

        using var cropped = ZonaCropService.Crop(full, zona.Value);
        runControl.WaitIfPausedOrCancelled();
        AutoCropComputation.ResizeToReferenceOutputSize(cropped, reference);
        if (colorCorrection is not null)
            ColorCorrectionService.ApplyIfEnabled(cropped, colorCorrection, applyColorCorrection);
        runControl.WaitIfPausedOrCancelled();
        WriteCroppedFile(cropped, outPath, saveAsWebP);

        log($"  OK Zona: {Path.GetFileName(inputPath)} → угол={zona.Value.RectInZonaCoords.Angle:F1}°, rotated={zona.Value.IsRotated}, цвет={(applyColorCorrection ? "да" : "нет")}");
        return true;
    }

    private static void ProcessWithReference(
        string inputPath,
        string outputFolder,
        AutoCropComputation.ReferenceMetrics reference,
        BatchRunController runControl,
        int analysisMaxEdge,
        ColorCorrectionSettings? colorCorrection,
        bool applyColorCorrection,
        bool saveAsWebP)
    {
        runControl.WaitIfPausedOrCancelled();
        using var full = RasterImageLoader.Load(inputPath);
        runControl.WaitIfPausedOrCancelled();
        var target = AutoCropComputation.AnalyzeTarget(full, analysisMaxEdge);
        runControl.WaitIfPausedOrCancelled();
        var crop = AutoCropComputation.ComputeCropBox(reference, target);
        var (x, y, w, h) = CropGeometryService.ToIntegers(crop, (int)full.Width, (int)full.Height);

        using var cropped = (MagickImage)full.Clone();
        cropped.Crop(new MagickGeometry { X = x, Y = y, Width = (uint)w, Height = (uint)h });
        cropped.ResetPage();
        AutoCropComputation.ResizeToReferenceOutputSize(cropped, reference);
        runControl.WaitIfPausedOrCancelled();

        if (colorCorrection is not null)
            ColorCorrectionService.ApplyIfEnabled(cropped, colorCorrection, applyColorCorrection);

        Directory.CreateDirectory(outputFolder);

        var outName = Path.GetFileNameWithoutExtension(inputPath) + (saveAsWebP ? ".webp" : ".jpg");
        var outPath = Path.Combine(outputFolder, outName);
        runControl.WaitIfPausedOrCancelled();
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
