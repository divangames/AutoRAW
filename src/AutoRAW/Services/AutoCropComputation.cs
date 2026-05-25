using AutoRAW.Models;
using AutoRAW.Services.SubjectDetection;
using ImageMagick;

namespace AutoRAW.Services;

/// <summary>Общий расчёт референса и цели — одинаково для экспорта и превью.</summary>
public static class AutoCropComputation
{
    public readonly record struct ReferenceMetrics(Box2d SubjectRef, double RefW, double RefH);

    public readonly record struct TargetMetrics(Box2d SubjectTarget, double ImgW, double ImgH);

    public static ReferenceMetrics AnalyzeReference(string referencePath, int analysisMaxEdge) =>
        ReferenceCompositionCatalog.GetOrBuild(referencePath, analysisMaxEdge).ToReferenceMetrics();

    /// <summary>EXIF/XMP ориентация и сброс виртуального холста — важно для .nef/.cr2 после AutoOrient.</summary>
    public static void AutoOrientAndNormalize(MagickImage image)
    {
        image.AutoOrient();
        image.ResetPage();
    }

    public static ReferenceMetrics AnalyzeReference(MagickImage refFullOriented, int analysisMaxEdge)
    {
        var refW = (double)refFullOriented.Width;
        var refH = (double)refFullOriented.Height;

        using var refAnalysis = CloneResizedLongEdge(refFullOriented, analysisMaxEdge);
        var refScale = refW / refAnalysis.Width;

        Box2d subjectRef;
        using (var refMat = MagickMatConverter.ToMatBgr(refAnalysis))
        {
            var det = SubjectDetectionService.DetectOnMat(refMat);
            subjectRef = det.IsValid ? det.Subject : SubjectBoundsEstimator.Estimate(refMat);
            subjectRef = SubjectBoundsEstimator.RefineHorizontalWidthByEdgeProjection(refMat, subjectRef);
        }

        subjectRef = subjectRef.Scale(refScale, refScale);
        return new ReferenceMetrics(subjectRef, refW, refH);
    }

    public static TargetMetrics AnalyzeTarget(MagickImage fullOriented, int analysisMaxEdge) =>
        SubjectDetectionService.AnalyzeTarget(fullOriented, analysisMaxEdge);

    /// <summary>Цель для сценария <c>operation</c> — <see cref="SubjectBoundsEstimator.EstimateForOperation"/>, без логики line-guide/стола.</summary>
    public static TargetMetrics AnalyzeTargetForOperation(MagickImage fullOriented, int analysisMaxEdge)
    {
        var imgW = (double)fullOriented.Width;
        var imgH = (double)fullOriented.Height;

        using var analysis = CloneResizedLongEdge(fullOriented, analysisMaxEdge);
        var scale = imgW / analysis.Width;

        Box2d subjectTarget;
        using (var mat = MagickMatConverter.ToMatBgr(analysis))
            subjectTarget = SubjectBoundsEstimator.EstimateForOperation(mat);

        subjectTarget = subjectTarget.Scale(scale, scale);
        return new TargetMetrics(subjectTarget, imgW, imgH);
    }

    public static Box2d ComputeCropBox(ReferenceMetrics reference, TargetMetrics target, bool centerSubjectInFrame = false)
    {
        return CropGeometryService.ComputeCrop(
            reference.SubjectRef,
            reference.RefW,
            reference.RefH,
            target.SubjectTarget,
            target.ImgW,
            target.ImgH,
            centerSubjectInFrame);
    }

    public static MagickImage CloneResizedLongEdge(MagickImage src, int maxEdge)
    {
        var clone = (MagickImage)src.Clone();
        var m = Math.Max(clone.Width, clone.Height);
        if (m <= maxEdge)
            return clone;

        var s = maxEdge / (double)m;
        var nw = Math.Max(1u, (uint)Math.Round(clone.Width * s));
        var nh = Math.Max(1u, (uint)Math.Round(clone.Height * s));
        clone.Resize(nw, nh);
        return clone;
    }

    /// <summary>Уменьшает изображение in-place так, чтобы max(ширина, высота) ≤ maxEdge (превью редактора после декода).</summary>
    public static void ResizeLongEdgeFitInPlace(MagickImage image, int maxEdge)
    {
        if (maxEdge < 64)
            return;

        var m = Math.Max(image.Width, image.Height);
        if (m <= maxEdge)
            return;

        var s = maxEdge / (double)m;
        var nw = Math.Max(1u, (uint)Math.Round(image.Width * s));
        var nh = Math.Max(1u, (uint)Math.Round(image.Height * s));
        image.FilterType = FilterType.Triangle;
        image.Resize(nw, nh);
        image.ResetPage();
    }

    /// <summary>Совмещает положение товара в кадре с референсом (после кропа и масштаба).</summary>
    public static void AlignCompositionToReference(
        MagickImage image,
        ReferenceMetrics reference,
        int analysisMaxEdge,
        string? outputFileStem = null,
        string? zonaFolder = null) =>
        ReferenceCompositionAlignService.Align(image, reference, analysisMaxEdge, outputFileStem, zonaFolder);

    /// <summary>Приводит кадр к пиксельному размеру референса (например 1400×1050 для «Кроссовки»).</summary>
    public static void ResizeToReferenceOutputSize(MagickImage image, ReferenceMetrics reference)
    {
        var w = (int)Math.Round(reference.RefW);
        var h = (int)Math.Round(reference.RefH);
        if (w < 1 || h < 1)
            return;

        if (image.Width == (uint)w && image.Height == (uint)h)
            return;

        image.FilterType = FilterType.Lanczos;
        image.Resize(new MagickGeometry((uint)w, (uint)h) { IgnoreAspectRatio = true });
        image.ResetPage();
    }
}
