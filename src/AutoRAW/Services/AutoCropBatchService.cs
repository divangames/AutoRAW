using AutoRAW.Models;
using ImageMagick;

namespace AutoRAW.Services;

public sealed class AutoCropBatchService
{
    public delegate void BatchLogHandler(string message, LogLineKind kind = LogLineKind.Normal);

    public delegate void BatchProgressHandler(int completed, int total, int succeeded, int errors);

    public BatchRunResult RunMappings(
        IReadOnlyList<BatchJobItem> jobs,
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
        if (jobs.Count == 0)
        {
            log("Нет строк для обработки.");
            return new BatchRunResult(false, 0, 0, 0, TimeSpan.Zero);
        }

        var refCache = new Dictionary<string, AutoCropComputation.ReferenceMetrics>(StringComparer.OrdinalIgnoreCase);
        var inputRootFull = Path.GetFullPath(inputRoot);
        var total = jobs.Count;
        var completed = 0;
        var succeeded = 0;
        var errors = 0;
        var cancelled = false;

        log($"Заданий: {total}");
        onProgress?.Invoke(0, total, 0, 0);

        foreach (var job in jobs)
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

            var rel = TryGetRelativeDisplayPath(inputRootFull, job.InputPath);

            try
            {
                var outDir = BatchOutputPathResolver.Resolve(inputRootFull, job.InputPath, outputFolder, saveAsWebP);
                var outStem = job.OutputFileStem ?? Path.GetFileNameWithoutExtension(job.InputPath);
                var refName = Path.GetFileName(job.ReferencePath);

                if (string.Equals(job.InputPath, job.ReferencePath, StringComparison.OrdinalIgnoreCase))
                {
                    log($"Пропуск (файл совпадает с референсом): {rel}");
                }
                else
                {
                    if (!refCache.TryGetValue(job.ReferencePath, out var metrics))
                    {
                        runControl.WaitIfPausedOrCancelled();
                        metrics = AutoCropComputation.AnalyzeReference(job.ReferencePath, analysisMaxEdge);
                        refCache[job.ReferencePath] = metrics;
                        log($"Референс (анализ): {refName}");
                    }

                    ProcessWithManualFrameFromFull(job, outDir, outStem, metrics, runControl, analysisMaxEdge, colorCorrection, applyColorCorrection, saveAsWebP);
                    log($"OK: {rel} → {outStem}  (референс: {refName})");
                    succeeded++;
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

    /// <summary>Полный кадр (после ориентации по стему) + ручные правки из json → файл размера референса.</summary>
    private static void ProcessWithManualFrameFromFull(
        BatchJobItem job,
        string outputFolder,
        string outStem,
        AutoCropComputation.ReferenceMetrics reference,
        BatchRunController runControl,
        int analysisMaxEdge,
        ColorCorrectionSettings? colorCorrection,
        bool applyColorCorrection,
        bool saveAsWebP)
    {
        runControl.WaitIfPausedOrCancelled();
        using var full = RasterImageLoader.Load(job.InputPath);
        if (job.RotateCounterClockwise90)
            ImageTransformHelper.RotateCounterClockwise90(full);
        ShotCropPolicy.ApplyPreCropOrientation(full, outStem, analysisMaxEdge);
        runControl.WaitIfPausedOrCancelled();

        var refW = (int)reference.RefW;
        var refH = (int)reference.RefH;
        var adj = ManualShotAdjustStore.Resolve(job.ProfileDisplayName, job.InputPath, outStem);
        var working = ManualShotAdjustApplier.ComposeFromFullToReference(full, adj, refW, refH);

        try
        {
            runControl.WaitIfPausedOrCancelled();
            if (colorCorrection is not null)
                ColorCorrectionService.ApplyIfEnabled(working, colorCorrection, applyColorCorrection);

            Directory.CreateDirectory(outputFolder);
            var outName = outStem + (saveAsWebP ? ".webp" : ".jpg");
            var outPath = Path.Combine(outputFolder, outName);
            runControl.WaitIfPausedOrCancelled();
            WriteCroppedFile(working, outPath, saveAsWebP);
        }
        finally
        {
            working.Dispose();
        }
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
