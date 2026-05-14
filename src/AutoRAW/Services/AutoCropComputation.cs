using AutoRAW.Models;
using ImageMagick;

namespace AutoRAW.Services;

/// <summary>Общий расчёт референса и цели — одинаково для экспорта и превью.</summary>
public static class AutoCropComputation
{
    public readonly record struct ReferenceMetrics(Box2d SubjectRef, double RefW, double RefH);

    public readonly record struct TargetMetrics(Box2d SubjectTarget, double ImgW, double ImgH);

    public static ReferenceMetrics AnalyzeReference(string referencePath, int analysisMaxEdge)
    {
        using var refFull = RasterImageLoader.Load(referencePath);
        return AnalyzeReference(refFull, analysisMaxEdge);
    }

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
            subjectRef = SubjectBoundsEstimator.Estimate(refMat);
        }

        subjectRef = subjectRef.Scale(refScale, refScale);
        return new ReferenceMetrics(subjectRef, refW, refH);
    }

    public static TargetMetrics AnalyzeTarget(MagickImage fullOriented, int analysisMaxEdge)
    {
        var imgW = (double)fullOriented.Width;
        var imgH = (double)fullOriented.Height;

        using var analysis = CloneResizedLongEdge(fullOriented, analysisMaxEdge);
        var scale = imgW / analysis.Width;

        Box2d subjectTarget;
        using (var mat = MagickMatConverter.ToMatBgr(analysis))
        {
            subjectTarget = SubjectBoundsEstimator.Estimate(mat);
        }

        subjectTarget = subjectTarget.Scale(scale, scale);
        return new TargetMetrics(subjectTarget, imgW, imgH);
    }

    public static Box2d ComputeCropBox(ReferenceMetrics reference, TargetMetrics target)
    {
        return CropGeometryService.ComputeCrop(
            reference.SubjectRef,
            reference.RefW,
            reference.RefH,
            target.SubjectTarget,
            target.ImgW,
            target.ImgH);
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
}
