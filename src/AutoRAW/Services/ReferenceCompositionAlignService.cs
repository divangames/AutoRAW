using AutoRAW.Models;
using AutoRAW.Services.SubjectDetection;
using ImageMagick;
using OpenCvSharp;

namespace AutoRAW.Services;

/// <summary>
/// Сдвигает уже обрезанный кадр: центрирование или композиция как на референсе.
/// </summary>
public static class ReferenceCompositionAlignService
{
    private const double MinShiftPx = 2.0;
    private const double MaxShiftMatchReference = 0.22;
    private const double MaxShiftCenterInFrame = 0.42;

    public static void Align(
        MagickImage image,
        AutoCropComputation.ReferenceMetrics reference,
        int analysisMaxEdge,
        string? outputFileStem = null,
        string? zonaFolder = null)
    {
        if (image.Width < 4 || image.Height < 4)
            return;

        if (ShotLineGuideAlignService.TryAlign(image, zonaFolder, outputFileStem, analysisMaxEdge))
            return;

        var policy = ShotCompositionPolicy.GetAlignPolicy(outputFileStem);
        if (policy == CompositionAlignPolicy.Skip)
            return;

        AlignByPolicy(image, reference, analysisMaxEdge, policy);
    }

    /// <summary>Выравнивание только по эталону (без line-guide и zona).</summary>
    public static void AlignByPolicy(
        MagickImage image,
        AutoCropComputation.ReferenceMetrics reference,
        int analysisMaxEdge,
        CompositionAlignPolicy policy)
    {
        if (image.Width < 4 || image.Height < 4 || policy == CompositionAlignPolicy.Skip)
            return;

        var imgW = (double)image.Width;
        var imgH = (double)image.Height;

        double desiredRelX;
        double desiredRelY;
        double maxShiftFraction;

        if (policy == CompositionAlignPolicy.CenterInFrame)
        {
            desiredRelX = 0.5;
            desiredRelY = 0.5;
            maxShiftFraction = MaxShiftCenterInFrame;
        }
        else
        {
            desiredRelX = reference.SubjectRef.CenterX / Math.Max(1e-6, reference.RefW);
            desiredRelY = reference.SubjectRef.CenterY / Math.Max(1e-6, reference.RefH);
            maxShiftFraction = MaxShiftMatchReference;
        }

        using var analysis = AutoCropComputation.CloneResizedLongEdge(image, analysisMaxEdge);
        var scale = imgW / analysis.Width;

        Box2d subject;
        using (var mat = MagickMatConverter.ToMatBgr(analysis))
        {
            var det = SubjectDetectionService.DetectOnMat(mat);
            subject = (det.IsValid ? det.Subject : SubjectBoundsEstimator.Estimate(mat)).Scale(scale, scale);
        }

        var dx = desiredRelX * imgW - subject.CenterX;
        var dy = desiredRelY * imgH - subject.CenterY;

        var maxDx = imgW * maxShiftFraction;
        var maxDy = imgH * maxShiftFraction;
        dx = Math.Clamp(dx, -maxDx, maxDx);
        dy = Math.Clamp(dy, -maxDy, maxDy);

        if (Math.Abs(dx) < MinShiftPx && Math.Abs(dy) < MinShiftPx)
            return;

        ApplyTranslation(image, dx, dy);

        if (policy != CompositionAlignPolicy.CenterInFrame)
            return;

        using var analysis2 = AutoCropComputation.CloneResizedLongEdge(image, analysisMaxEdge);
        var scale2 = imgW / analysis2.Width;
        Box2d subject2;
        using (var mat2 = MagickMatConverter.ToMatBgr(analysis2))
        {
            var det2 = SubjectDetectionService.DetectOnMat(mat2);
            subject2 = (det2.IsValid ? det2.Subject : SubjectBoundsEstimator.Estimate(mat2)).Scale(scale2, scale2);
        }

        var dx2 = 0.5 * imgW - subject2.CenterX;
        var dy2 = 0.5 * imgH - subject2.CenterY;
        dx2 = Math.Clamp(dx2, -maxDx, maxDx);
        dy2 = Math.Clamp(dy2, -maxDy, maxDy);

        if (Math.Abs(dx2) >= MinShiftPx || Math.Abs(dy2) >= MinShiftPx)
            ApplyTranslation(image, dx2, dy2);
    }

    internal static void ApplyTranslation(MagickImage image, double dx, double dy, OpenCvSharp.Scalar? borderFill = null) =>
        ApplyTranslationInPlace(image, dx, dy, borderFill);

    private static void ApplyTranslationInPlace(MagickImage image, double dx, double dy, OpenCvSharp.Scalar? borderFill)
    {
        using var mat = MagickMatConverter.ToMatBgr(image);
        var fill = borderFill ?? ImageBorderFill.SampleBackground(mat);
        using var shifted = new Mat();
        using var m = new Mat(2, 3, MatType.CV_64FC1);
        m.Set(0, 0, 1.0);
        m.Set(0, 1, 0.0);
        m.Set(0, 2, dx);
        m.Set(1, 0, 0.0);
        m.Set(1, 1, 1.0);
        m.Set(1, 2, dy);

        Cv2.WarpAffine(
            mat,
            shifted,
            m,
            new OpenCvSharp.Size((int)image.Width, (int)image.Height),
            InterpolationFlags.Lanczos4,
            BorderTypes.Constant,
            fill);

        using var aligned = MagickMatConverter.ToMagickImage(shifted);
        image.Composite(aligned, CompositeOperator.Copy);
    }
}
