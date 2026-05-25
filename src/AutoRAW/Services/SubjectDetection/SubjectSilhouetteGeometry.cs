using AutoRAW.Models;
using OpenCvSharp;

namespace AutoRAW.Services.SubjectDetection;

/// <summary>Центр силуэта и угол PCA по маске/порогу внутри bbox.</summary>
public static class SubjectSilhouetteGeometry
{
    public readonly record struct ShapeMetrics(
        double VisualCenterX,
        double VisualCenterY,
        double? PcaAngleDeg);

    public static bool TryAnalyze(Mat bgr, Box2d subject, out ShapeMetrics metrics) =>
        TryAnalyzeMask(bgr, subject, BuildForegroundMask, out metrics);

    /// <summary>Центр и PCA по бинарной маске (например YOLOv8-seg), размер как у <paramref name="bgr"/>.</summary>
    public static bool TryAnalyzeFromMask(Mat bgr, Box2d subject, Mat binaryMask, out ShapeMetrics metrics)
    {
        metrics = default;
        if (bgr.Empty() || binaryMask.Empty() || subject.Width < 8 || subject.Height < 8)
            return false;
        if (binaryMask.Rows != bgr.Rows || binaryMask.Cols != bgr.Cols)
            return false;

        var pad = Math.Max(4.0, Math.Min(subject.Width, subject.Height) * 0.04);
        var x0 = (int)Math.Floor(Math.Max(0, subject.X - pad));
        var y0 = (int)Math.Floor(Math.Max(0, subject.Y - pad));
        var x1 = (int)Math.Ceiling(Math.Min(bgr.Cols, subject.Right + pad));
        var y1 = (int)Math.Ceiling(Math.Min(bgr.Rows, subject.Bottom + pad));
        if (x1 - x0 < 12 || y1 - y0 < 12)
            return false;

        using var roiMask = new Mat(binaryMask, new Rect(x0, y0, x1 - x0, y1 - y0));
        if (Cv2.CountNonZero(roiMask) < 48)
            return false;

        var m = Cv2.Moments(roiMask, binaryImage: true);
        if (Math.Abs(m.M00) < 1e-6)
            return false;

        metrics = new ShapeMetrics(x0 + m.M10 / m.M00, y0 + m.M01 / m.M00, TryPcaAngleDeg(roiMask));
        return true;
    }

    private static bool TryAnalyzeMask(
        Mat bgr,
        Box2d subject,
        Func<Mat, Mat> maskFactory,
        out ShapeMetrics metrics)
    {
        metrics = default;
        if (bgr.Empty() || subject.Width < 8 || subject.Height < 8)
            return false;

        var pad = Math.Max(4.0, Math.Min(subject.Width, subject.Height) * 0.04);
        var x0 = (int)Math.Floor(Math.Max(0, subject.X - pad));
        var y0 = (int)Math.Floor(Math.Max(0, subject.Y - pad));
        var x1 = (int)Math.Ceiling(Math.Min(bgr.Cols, subject.Right + pad));
        var y1 = (int)Math.Ceiling(Math.Min(bgr.Rows, subject.Bottom + pad));
        if (x1 - x0 < 12 || y1 - y0 < 12)
            return false;

        using var crop = new Mat(bgr, new Rect(x0, y0, x1 - x0, y1 - y0));
        using var mask = maskFactory(crop);
        if (mask.Empty() || Cv2.CountNonZero(mask) < 48)
            return false;

        var m = Cv2.Moments(mask, binaryImage: true);
        if (Math.Abs(m.M00) < 1e-6)
            return false;

        var lx = m.M10 / m.M00;
        var ly = m.M01 / m.M00;
        var visualCx = x0 + lx;
        var visualCy = y0 + ly;

        double? pcaDeg = TryPcaAngleDeg(mask);

        metrics = new ShapeMetrics(visualCx, visualCy, pcaDeg);
        return true;
    }

    private static Mat BuildForegroundMask(Mat bgr)
    {
        using var work = bgr.Channels() == 3 ? bgr.Clone() : CloneAsBgr(bgr);
        using var gray = new Mat();
        Cv2.CvtColor(work, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.GaussianBlur(gray, gray, new OpenCvSharp.Size(5, 5), 0);

        using var otsu = new Mat();
        Cv2.Threshold(gray, otsu, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

        var mean = Cv2.Mean(gray, otsu);
        if (mean.Val0 > 127)
            Cv2.BitwiseNot(otsu, otsu);

        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(5, 5));
        var mask = otsu.Clone();
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel);
        Cv2.MorphologyEx(mask, mask, MorphTypes.Open, kernel);
        return mask;
    }

    private static Mat CloneAsBgr(Mat src)
    {
        var dst = new Mat();
        if (src.Channels() == 1)
            Cv2.CvtColor(src, dst, ColorConversionCodes.GRAY2BGR);
        else if (src.Channels() == 4)
            Cv2.CvtColor(src, dst, ColorConversionCodes.BGRA2BGR);
        else
            throw new InvalidOperationException($"Unsupported Mat channels: {src.Channels()}");
        return dst;
    }

    private static double? TryPcaAngleDeg(Mat binaryMask)
    {
        Cv2.FindContours(binaryMask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        if (contours.Length == 0)
            return null;

        var best = contours.OrderByDescending(c => Cv2.ContourArea(c)).First();
        if (best.Length < 5)
            return null;

        using var pts = new Mat(best.Length, 1, MatType.CV_32FC2);
        for (var i = 0; i < best.Length; i++)
            pts.Set(i, 0, new Point2f(best[i].X, best[i].Y));

        var mean = new Mat();
        var eigenvectors = new Mat();
        var eigenvalues = new Mat();
        Cv2.PCACompute(pts, mean, eigenvectors, eigenvalues);
        if (eigenvectors.Rows < 1)
            return null;

        var vx = eigenvectors.At<float>(0, 0);
        var vy = eigenvectors.At<float>(0, 1);
        var angle = Math.Atan2(vy, vx) * 180.0 / Math.PI;
        while (angle > 90) angle -= 180;
        while (angle < -90) angle += 180;
        return Math.Abs(angle) < 0.4 ? null : angle;
    }
}
