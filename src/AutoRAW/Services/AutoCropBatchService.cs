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
        bool saveAsWebP = false,
        bool runThroughPhotoshopDroplets = false)
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
        var manualSaved = 0;
        var autoAligned = 0;
        var needsReview = 0;
        var lowQuality = 0;
        var cancelled = false;

        log($"Заданий: {total}");
        if (runThroughPhotoshopDroplets)
            log(PhotoshopDropletLauncher.DescribeInstallationForLog());

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

                    var frame = ProcessWithManualFrameFromFull(
                        job, outDir, outStem, metrics, runControl, analysisMaxEdge, zonaFolder,
                        colorCorrection, applyColorCorrection, saveAsWebP,
                        runThroughPhotoshopDroplets, log);
                    switch (frame.Provenance)
                    {
                        case ManualShotFrameProvenance.PerFile:
                        case ManualShotFrameProvenance.ProfileStem:
                        case ManualShotFrameProvenance.ProfileFileName:
                            manualSaved++;
                            break;
                        case ManualShotFrameProvenance.AutoAlign:
                            autoAligned++;
                            break;
                    }

                    if (frame.NeedsReview)
                        needsReview++;
                    if (frame.IsLowAlignQuality)
                        lowQuality++;

                    var qualityNote = frame.IsLowAlignQuality && !double.IsNaN(frame.AlignQualityScore)
                        ? $", {frame.AlignQualitySummary}"
                        : string.Empty;
                    log($"OK{frame.BatchLogSuffix}: {rel} → {outStem}  ({frame.ProvenanceLabel}{qualityNote}, референс: {refName})");
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

        return new BatchRunResult(cancelled, total, succeeded, errors, runControl.ActiveElapsed, manualSaved, autoAligned, needsReview, lowQuality);
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
    private static ResolvedManualShotFrame ProcessWithManualFrameFromFull(
        BatchJobItem job,
        string outputFolder,
        string outStem,
        AutoCropComputation.ReferenceMetrics reference,
        BatchRunController runControl,
        int analysisMaxEdge,
        string? zonaFolder,
        ColorCorrectionSettings? colorCorrection,
        bool applyColorCorrection,
        bool saveAsWebP,
        bool runThroughPhotoshopDroplets,
        BatchLogHandler log)
    {
        runControl.WaitIfPausedOrCancelled();
        using var full = RasterImageLoader.Load(job.InputPath);
        if (job.RotateCounterClockwise90)
            ImageTransformHelper.RotateCounterClockwise90(full);
        ShotCropPolicy.ApplyPreCropOrientation(full, outStem, analysisMaxEdge);
        runControl.WaitIfPausedOrCancelled();

        var refW = (int)reference.RefW;
        var refH = (int)reference.RefH;
        var frame = ManualShotFrameResolver.ResolveForExport(
            job.InputPath,
            job.ReferencePath,
            job.ProfileDisplayName,
            outStem,
            zonaFolder,
            analysisMaxEdge,
            job.RotateCounterClockwise90);
        using var working = ManualShotAdjustApplier.ComposeFromFullToReference(full, frame.Adjust, refW, refH);
        frame = FrameAlignQualityService.EnrichWithQuality(frame, working, reference, analysisMaxEdge);

        runControl.WaitIfPausedOrCancelled();
        if (colorCorrection is not null)
            ColorCorrectionService.ApplyIfEnabled(working, colorCorrection, applyColorCorrection);

        Directory.CreateDirectory(outputFolder);
        var outName = outStem + (saveAsWebP ? ".webp" : ".jpg");
        var outPath = Path.Combine(outputFolder, outName);
        runControl.WaitIfPausedOrCancelled();
        WriteCroppedFile(working, outPath, saveAsWebP);

        if (runThroughPhotoshopDroplets)
            PhotoshopDropletLauncher.TryLaunch(outStem, outPath, job.InputPath, (m, kind) => log(m, kind));

        return frame;
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
