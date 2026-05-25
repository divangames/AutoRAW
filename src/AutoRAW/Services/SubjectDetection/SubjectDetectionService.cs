using AutoRAW.Models;
using ImageMagick;
using OpenCvSharp;

namespace AutoRAW.Services.SubjectDetection;

/// <summary>ONNX YOLOv8n при наличии модели, иначе <see cref="SubjectBoundsEstimator"/>.</summary>
public static class SubjectDetectionService
{
    public static SubjectDetectionResult DetectOnMat(Mat bgr) =>
        ProductSubjectDetection.DetectForProductPhoto(bgr);

    public static SubjectDetectionResult DetectOnReferenceMat(Mat bgr) =>
        ProductSubjectDetection.DetectForReference(bgr);

    public static Box2d DetectOnFullImage(MagickImage fullOriented, int analysisMaxEdge)
    {
        using var analysis = AutoCropComputation.CloneResizedLongEdge(fullOriented, analysisMaxEdge);
        var scale = (double)fullOriented.Width / analysis.Width;

        using var mat = MagickMatConverter.ToMatBgr(analysis);
        var r = DetectOnMat(mat);
        var box = r.IsValid ? r.Subject : SubjectBoundsEstimator.Estimate(mat);
        return box.Scale(scale, scale);
    }

    /// <summary>
    /// Пакет и редактор автоподгонки: только OpenCV по уменьшенному кадру — без ONNX (эталон + правила покрывают задачу).
    /// </summary>
    public static bool TryAnalyzeTargetOpenCvOnly(
        MagickImage fullOriented,
        int analysisMaxEdge,
        out AutoCropComputation.TargetMetrics target)
    {
        var imgW = (double)fullOriented.Width;
        var imgH = (double)fullOriented.Height;
        target = default;
        if (analysisMaxEdge < 48)
            return false;

        using var analysis = AutoCropComputation.CloneResizedLongEdge(fullOriented, analysisMaxEdge);
        var scale = imgW / analysis.Width;
        using var mat = MagickMatConverter.ToMatBgr(analysis);
        var cv = ProductSubjectDetection.EstimateOpenCv(mat);
        if (!ProductSubjectDetection.IsPlausibleProductBox(cv, mat.Cols, mat.Rows))
            return false;

        var scaled = cv.Scale(scale, scale);
        target = new AutoCropComputation.TargetMetrics(scaled, imgW, imgH);
        return true;
    }

    public static AutoCropComputation.TargetMetrics AnalyzeTarget(MagickImage fullOriented, int analysisMaxEdge)
    {
        var imgW = (double)fullOriented.Width;
        var imgH = (double)fullOriented.Height;
        var subject = DetectOnFullImage(fullOriented, analysisMaxEdge);
        return new AutoCropComputation.TargetMetrics(subject, imgW, imgH);
    }

    /// <summary>
    /// Как <see cref="AnalyzeTarget"/>, но возвращает сырой результат детекции на уменьшенной копии —
    /// чтобы не гонять тяжёлый ONNX повторно для метаданных итога.
    /// </summary>
    public static (AutoCropComputation.TargetMetrics Target, SubjectDetectionResult Detection) AnalyzeTargetWithDetection(
        MagickImage fullOriented,
        int analysisMaxEdge)
    {
        var imgW = (double)fullOriented.Width;
        var imgH = (double)fullOriented.Height;
        using var analysis = AutoCropComputation.CloneResizedLongEdge(fullOriented, analysisMaxEdge);
        var scale = imgW / analysis.Width;
        using var mat = MagickMatConverter.ToMatBgr(analysis);
        var r = DetectOnMat(mat);
        var box = r.IsValid ? r.Subject : SubjectBoundsEstimator.Estimate(mat);
        var scaled = box.Scale(scale, scale);
        return (new AutoCropComputation.TargetMetrics(scaled, imgW, imgH), r);
    }

}
