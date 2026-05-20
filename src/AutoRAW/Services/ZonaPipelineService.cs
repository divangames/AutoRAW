using ImageMagick;

namespace AutoRAW.Services;

    /// <summary>Конвейер Zona: ориентация по кадру; возвращает <b>нативный</b> патч кропа (до размера референса).</summary>
public static class ZonaPipelineService
{
    public static MagickImage BuildCroppedImage(
        MagickImage full,
        ZonaCropService.ZonaCropResult zona,
        string? zonaFolder,
        string? outputStem,
        AutoCropComputation.ReferenceMetrics reference,
        int analysisMaxEdge,
        bool rotateCounterClockwise90,
        out bool usedLineGuide)
    {
        if (rotateCounterClockwise90)
            ImageTransformHelper.RotateCounterClockwise90(full);
        ShotCropPolicy.ApplyPreCropOrientation(full, outputStem, analysisMaxEdge);

        usedLineGuide = ShotLineGuideParser.ResolveLineGuidePath(zonaFolder ?? string.Empty, outputStem ?? string.Empty) is not null;

        // Наклонённый маркёр Zona: сначала выравниваем по красной рамке, затем — кроп по референсу на патче
        if (zona.IsRotated)
        {
            using var patch = ZonaCropService.Crop(full, zona);
            var patchTarget = AutoCropComputation.AnalyzeTarget(patch, analysisMaxEdge);
            var fitted = ReferenceAlignedCropService.ComputeFittedCropBox(
                patch, zonaFolder, outputStem, reference, patchTarget);
            return ReferenceAlignedCropService.CropFittedToNative(patch, fitted);
        }

        var target = AutoCropComputation.AnalyzeTarget(full, analysisMaxEdge);
        var fittedFull = ReferenceAlignedCropService.ComputeFittedCropBox(
            full, zonaFolder, outputStem, reference, target);
        return ReferenceAlignedCropService.CropFittedToNative(full, fittedFull);
    }
}
