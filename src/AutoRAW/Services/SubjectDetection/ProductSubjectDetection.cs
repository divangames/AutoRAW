using AutoRAW.Models;
using OpenCvSharp;

namespace AutoRAW.Services.SubjectDetection;

/// <summary>Проверка bbox и выбор детекции для предметной съёмки (товар на белом/светлом фоне).</summary>
internal static class ProductSubjectDetection
{
    public const double MinAreaFrac = 0.055;
    public const double MaxAreaFrac = 0.88;

    public static bool IsPlausibleProductBox(Box2d box, int cols, int rows)
    {
        if (cols < 8 || rows < 8 || box.Width < 4 || box.Height < 4)
            return false;

        var areaFrac = box.Width * box.Height / ((double)cols * rows);
        if (areaFrac < MinAreaFrac || areaFrac > MaxAreaFrac)
            return false;

        var wFrac = box.Width / cols;
        var hFrac = box.Height / rows;
        if (wFrac < 0.12 || hFrac < 0.12 || wFrac > 0.98 || hFrac > 0.98)
            return false;

        var cx = box.CenterX / cols;
        var cy = box.CenterY / rows;
        return cx is >= 0.06 and <= 0.94 && cy is >= 0.05 and <= 0.94;
    }

    public static Box2d RefineBox(Mat bgr, Box2d box) =>
        SubjectBoundsEstimator.RefineHorizontalWidthByEdgeProjection(bgr, box);

    public static Box2d EstimateOpenCv(Mat bgr) =>
        RefineBox(bgr, SubjectBoundsEstimator.Estimate(bgr));

    public static SubjectDetectionResult OpenCvResult(Mat bgr, string detail = "opencv")
    {
        var box = EstimateOpenCv(bgr);
        return new SubjectDetectionResult
        {
            Subject = box,
            Source = SubjectDetectSource.OpenCv,
            Confidence = 1,
            IsValid = true,
            Detail = detail
        };
    }

    /// <summary>Seg → detect YOLO → OpenCV; U2Net уточняет bbox detect.</summary>
    public static SubjectDetectionResult DetectForProductPhoto(Mat bgr)
    {
        if (bgr.Empty())
            return SubjectDetectionResult.Invalid("empty");

        if (YoloV8SegOnnxDetector.TryCreateShared(out var seg) && seg is not null)
        {
            try
            {
                using var segOut = seg.DetectWithMask(bgr);
                if (segOut.Result.IsValid && IsPlausibleProductBox(segOut.Result.Subject, bgr.Cols, bgr.Rows))
                    return segOut.Result;
            }
            catch
            {
                // broken/partial ONNX — fall through to detect/OpenCV
            }
        }

        SubjectDetectionResult? yolo = null;
        if (YoloV8OnnxDetector.TryCreateShared(out var detector) && detector is not null)
        {
            yolo = detector.Detect(bgr);
            if (yolo.IsValid && IsPlausibleProductBox(yolo.Subject, bgr.Cols, bgr.Rows))
                return RefineYoloWithU2Net(bgr, yolo);
        }

        var cv = OpenCvResult(bgr, yolo is { IsValid: true } ? "opencv(yolo-reject)" : "opencv");
        if (yolo is { IsValid: true })
            return PickCloserToOpenCv(bgr, yolo, cv);

        return cv;
    }

    /// <summary>Эталонные JPEG: стабильный OpenCV без COCO-классов YOLO.</summary>
    public static SubjectDetectionResult DetectForReference(Mat bgr) =>
        bgr.Empty() ? SubjectDetectionResult.Invalid("empty") : OpenCvResult(bgr, "opencv-ref");

    private static SubjectDetectionResult RefineYoloWithU2Net(Mat bgr, SubjectDetectionResult yolo)
    {
        if (!U2NetpOnnxRefiner.TryCreateShared(out var u2) || u2 is null)
            return WithRefinedBox(bgr, yolo, yolo.Subject);

        var refined = u2.RefineBox(bgr, yolo.Subject);
        var box = refined ?? yolo.Subject;
        return WithRefinedBox(bgr, yolo, box, refined is not null ? "yolov8n+u2netp" : yolo.Detail);
    }

    private static SubjectDetectionResult PickCloserToOpenCv(Mat bgr, SubjectDetectionResult yolo, SubjectDetectionResult cv)
    {
        var y = yolo.Subject;
        var c = cv.Subject;
        var yArea = y.Width * y.Height;
        var cArea = c.Width * c.Height;
        if (cArea < 1)
            return WithRefinedBox(bgr, yolo, y);

        var ratio = yArea / cArea;
        if (ratio is >= 0.45 and <= 2.2)
            return WithRefinedBox(bgr, yolo, y);

        return cv;
    }

    private static SubjectDetectionResult WithRefinedBox(
        Mat bgr,
        SubjectDetectionResult source,
        Box2d box,
        string? detail = null)
    {
        box = RefineBox(bgr, box);
        return new SubjectDetectionResult
        {
            Subject = box,
            Source = source.Source,
            Confidence = source.Confidence,
            IsValid = true,
            Detail = detail ?? source.Detail
        };
    }
}
