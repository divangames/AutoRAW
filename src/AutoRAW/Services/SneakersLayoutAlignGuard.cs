using AutoRAW.Models;
using ImageMagick;

namespace AutoRAW.Services;

/// <summary>
/// Защита от «сломанной» авто-подгонки при ошибочном bbox (зум 200%, увод за край).
/// </summary>
internal static class SneakersLayoutAlignGuard
{
    public const double MaxLayoutZoomPercent = 200;
    public const double MinLayoutZoomPercent = 50;
    private const double MaxOffsetFracW = 0.28;
    private const double MaxOffsetFracH = 0.28;

    public static bool IsSubjectSaneForLayout(
        AutoCropComputation.ReferenceMetrics reference,
        Box2d subjectOnFull,
        double imgW,
        double imgH,
        string? outputStem)
    {
        if (imgW < 1 || imgH < 1 || subjectOnFull.Width < 8 || subjectOnFull.Height < 8)
            return false;

        var refBox = reference.SubjectRef;
        if (refBox.Width < 8 || refBox.Height < 8)
            return false;

        var subWFrac = subjectOnFull.Width / imgW;
        var subHFrac = subjectOnFull.Height / imgH;

        if (SneakersTopViewComposition.UsesHeightCenteredLayout(outputStem))
        {
            var refHFrac = refBox.Height / reference.RefH;
            var hRatio = subHFrac / Math.Max(1e-6, refHFrac);
            return hRatio is >= 0.36 and <= 1.65
                   && subWFrac >= 0.10
                   && subHFrac >= 0.16;
        }

        var refWFrac = refBox.Width / reference.RefW;
        var wRatio = subWFrac / Math.Max(1e-6, refWFrac);
        return wRatio is >= 0.38 and <= 1.72
               && subWFrac >= 0.14
               && subHFrac >= 0.10;
    }

    public static bool IsAdjustWithinLimits(
        ManualShotAdjust adjust,
        AutoCropComputation.ReferenceMetrics reference)
    {
        if (adjust.ZoomPercent > MaxLayoutZoomPercent || adjust.ZoomPercent < MinLayoutZoomPercent)
            return false;

        return Math.Abs(adjust.OffsetX) <= reference.RefW * MaxOffsetFracW
               && Math.Abs(adjust.OffsetY) <= reference.RefH * MaxOffsetFracH;
    }

    public static bool MeetsComposedQuality(
        MagickImage fullOriented,
        ManualShotAdjust adjust,
        AutoCropComputation.ReferenceMetrics reference,
        string? outputStem,
        int analysisMaxEdge,
        out FrameAlignQualityService.QualityResult quality)
    {
        quality = default;
        if (!IsAdjustWithinLimits(adjust, reference))
            return false;

        var refW = (int)Math.Round(reference.RefW);
        var refH = (int)Math.Round(reference.RefH);
        if (refW < 1 || refH < 1)
            return false;

        using var composed = ManualShotAdjustApplier.ComposeFromFullToReference(
            fullOriented, adjust, refW, refH);
        quality = FrameAlignQualityService.EvaluateComposed(composed, reference, analysisMaxEdge);
        if (quality.IsBelowThreshold)
            return false;

        if (SneakersTopViewComposition.UsesTopViewLayout(outputStem)
            && !SneakersTopViewAlignService.MeetsSafeZoneOnComposed(
                fullOriented, adjust, reference, outputStem, analysisMaxEdge))
            return false;

        return true;
    }

    /// <summary>Качество и лимиты зума/смещения; safe zone не обязателен (для layout-подгонки).</summary>
    public static bool MeetsComposedQualityRelaxed(
        MagickImage fullOriented,
        ManualShotAdjust adjust,
        AutoCropComputation.ReferenceMetrics reference,
        int analysisMaxEdge,
        out FrameAlignQualityService.QualityResult quality)
    {
        quality = default;
        if (!IsAdjustWithinLimits(adjust, reference))
            return false;

        var refW = (int)Math.Round(reference.RefW);
        var refH = (int)Math.Round(reference.RefH);
        if (refW < 1 || refH < 1)
            return false;

        using var composed = ManualShotAdjustApplier.ComposeFromFullToReference(
            fullOriented, adjust, refW, refH);
        quality = FrameAlignQualityService.EvaluateComposed(composed, reference, analysisMaxEdge);
        return !quality.IsBelowThreshold;
    }
}
