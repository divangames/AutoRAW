using AutoRAW.Models;
using AutoRAW.Services.SubjectDetection;
using ImageMagick;
using OpenCvSharp;

namespace AutoRAW.Services;

/// <summary>
/// Bbox для layout-кадров: OpenCV + U2Net-p в ROI; визуальный центр силуэта (PCA опционально).
/// </summary>
public static class RemoveBgSubjectDetector
{
    private const int MaxAnalysisLongEdge = 1280;
    private const double MinIoUWithOpenCv = 0.12;
    private const double MaxCenterShiftFrac = 0.22;

    public static bool TryDetectSubjectOnFullBox(
        MagickImage fullOriented,
        int analysisMaxEdge,
        out Box2d subjectOnFull,
        out SubjectDetectSource source,
        out string detail)
    {
        subjectOnFull = default;
        if (!TryDetectSubjectOnFull(fullOriented, analysisMaxEdge, out var subject, out source, out detail))
            return false;
        subjectOnFull = subject.Box;
        return subjectOnFull.Width >= 8;
    }

    public static bool TryDetectSubjectOnFull(
        MagickImage fullOriented,
        int analysisMaxEdge,
        out SubjectOnImage subject,
        out SubjectDetectSource source,
        out string detail)
    {
        subject = default;
        source = SubjectDetectSource.OpenCv;
        detail = "opencv";

        var uploadEdge = Math.Min(analysisMaxEdge, MaxAnalysisLongEdge);
        using var upload = AutoCropComputation.CloneResizedLongEdge(fullOriented, uploadEdge);
        var scaleUp = (double)fullOriented.Width / upload.Width;

        if (!TryDetectHybridOnUpload(upload, scaleUp, out subject, out source, out detail))
            return false;

        if (subject.PcaAngleDeg is { } pca && Math.Abs(pca) >= 0.5)
            detail += $" pca{pca:0.#}°";
        return true;
    }

    /// <summary>
    /// Вариант только для связки эталон + правила Sneakers layout: без YOLO/Seg/U2Net (скорость пакета и редактора).
    /// </summary>
    public static bool TryDetectSubjectOpenCvLayoutOnly(
        MagickImage fullOriented,
        int analysisMaxEdge,
        out SubjectOnImage subject,
        out SubjectDetectSource source,
        out string detail)
    {
        subject = default;
        source = SubjectDetectSource.OpenCv;
        detail = "opencv-layout";

        var uploadEdge = Math.Clamp(analysisMaxEdge, 256, 960);
        using var upload = AutoCropComputation.CloneResizedLongEdge(fullOriented, uploadEdge);
        var scaleUp = (double)fullOriented.Width / upload.Width;

        using var mat = MagickMatConverter.ToMatBgr(upload);
        var cols = mat.Cols;
        var rows = mat.Rows;
        var chosen = ProductSubjectDetection.EstimateOpenCv(mat);
        if (!ProductSubjectDetection.IsPlausibleProductBox(chosen, cols, rows))
        {
            detail = "opencv-implausible";
            return false;
        }

        var boxFull = chosen.Scale(scaleUp, scaleUp);
        if (boxFull.Width < 8 || boxFull.Height < 8)
            return false;

        try
        {
            if (SubjectSilhouetteGeometry.TryAnalyze(mat, chosen, out var shape))
            {
                subject = SubjectOnImage.FromBoxWithShape(
                    boxFull,
                    shape.VisualCenterX * scaleUp,
                    shape.VisualCenterY * scaleUp,
                    shape.PcaAngleDeg);
            }
            else
                subject = SubjectOnImage.FromBox(boxFull);
        }
        catch
        {
            subject = SubjectOnImage.FromBox(boxFull);
        }

        return true;
    }

    private static bool TryDetectHybridOnUpload(
        MagickImage upload,
        double scaleUp,
        out SubjectOnImage subject,
        out SubjectDetectSource source,
        out string detail)
    {
        subject = default;
        source = SubjectDetectSource.OpenCv;
        detail = "opencv";

        using var mat = MagickMatConverter.ToMatBgr(upload);
        var cols = mat.Cols;
        var rows = mat.Rows;
        var cvBox = ProductSubjectDetection.EstimateOpenCv(mat);
        if (!ProductSubjectDetection.IsPlausibleProductBox(cvBox, cols, rows))
        {
            detail = "opencv-implausible";
            return false;
        }

        var chosen = cvBox;
        detail = "opencv";
        Mat? segMask = null;

        if (YoloV8SegOnnxDetector.TryCreateShared(out var yoloSeg) && yoloSeg is not null)
        {
            try
            {
                using var segOut = yoloSeg.DetectWithMask(mat);
                if (segOut.Result.IsValid
                    && ProductSubjectDetection.IsPlausibleProductBox(segOut.Result.Subject, cols, rows))
                {
                    chosen = segOut.Result.Subject;
                    detail = segOut.Result.Detail ?? "yolov8n-seg";
                    source = SubjectDetectSource.YoloV8Seg;
                    segMask = segOut.Mask?.Clone();
                }
                else
                    segOut.Mask?.Dispose();
            }
            catch
            {
                detail = "opencv(seg-error)";
            }
        }

        if (segMask is null && U2NetpOnnxRefiner.TryCreateShared(out var u2) && u2 is not null)
        {
            var refined = u2.RefineBox(mat, cvBox);
            if (refined is not null
                && ProductSubjectDetection.IsPlausibleProductBox(refined.Value, cols, rows)
                && BoxesAgree(cvBox, refined.Value, cols, rows))
            {
                chosen = refined.Value;
                detail = "u2netp+opencv";
                source = SubjectDetectSource.U2Net;
            }
            else if (refined is null)
                detail = U2NetpOnnxRefiner.LastLoadError is not null ? "opencv(u2-empty)" : "opencv(u2-empty)";
            else
                detail = "opencv(u2-reject)";
        }
        else
            detail = U2NetpOnnxRefiner.LastLoadError is not null
                ? $"opencv({U2NetpOnnxRefiner.LastLoadError})"
                : "opencv(u2-missing)";

        var boxFull = chosen.Scale(scaleUp, scaleUp);
        if (boxFull.Width < 8 || boxFull.Height < 8)
            return false;

        try
        {
            if (segMask is not null
                && SubjectSilhouetteGeometry.TryAnalyzeFromMask(mat, chosen, segMask!, out var fromSeg))
            {
                subject = SubjectOnImage.FromBoxWithShape(
                    boxFull,
                    fromSeg.VisualCenterX * scaleUp,
                    fromSeg.VisualCenterY * scaleUp,
                    fromSeg.PcaAngleDeg);
            }
            else if (SubjectSilhouetteGeometry.TryAnalyze(mat, chosen, out var shape))
            {
                subject = SubjectOnImage.FromBoxWithShape(
                    boxFull,
                    shape.VisualCenterX * scaleUp,
                    shape.VisualCenterY * scaleUp,
                    shape.PcaAngleDeg);
            }
            else
                subject = SubjectOnImage.FromBox(boxFull);
        }
        finally
        {
            segMask?.Dispose();
        }

        return true;
    }

    private static bool AssignBox(SubjectOnImage subject, out Box2d box)
    {
        box = subject.Box;
        return box.Width >= 8;
    }

    private static bool BoxesAgree(Box2d openCv, Box2d candidate, int cols, int rows)
    {
        if (BoxIoU(openCv, candidate) < MinIoUWithOpenCv)
            return false;

        var dx = Math.Abs(candidate.CenterX - openCv.CenterX) / cols;
        var dy = Math.Abs(candidate.CenterY - openCv.CenterY) / rows;
        return dx <= MaxCenterShiftFrac && dy <= MaxCenterShiftFrac;
    }

    private static double BoxIoU(Box2d a, Box2d b)
    {
        var ix1 = Math.Max(a.X, b.X);
        var iy1 = Math.Max(a.Y, b.Y);
        var ix2 = Math.Min(a.Right, b.Right);
        var iy2 = Math.Min(a.Bottom, b.Bottom);
        var inter = Math.Max(0, ix2 - ix1) * Math.Max(0, iy2 - iy1);
        var union = a.Width * a.Height + b.Width * b.Height - inter;
        return union <= 0 ? 0 : inter / union;
    }
}
