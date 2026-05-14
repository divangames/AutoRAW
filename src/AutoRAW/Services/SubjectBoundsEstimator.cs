using AutoRAW.Models;
using OpenCvSharp;

namespace AutoRAW.Services;

/// <summary>
/// Ищет bbox основного объекта на предметной съёмке.
/// Стратегии (в порядке приоритета):
///   1. flood-fill от краёв → изолируем «висящие» объекты, не связные с фоном
///   2. relaxed — убираем только тонкие горизонтальные полосы снизу (стол/пол)
///   3. fallback — центральный прямоугольник
/// </summary>
public static class SubjectBoundsEstimator
{
    public const int DefaultAnalysisMaxEdge = 1600;

    public static Box2d Estimate(Mat bgr)
    {
        if (bgr.Empty())
            return new Box2d(0, 0, 1, 1);

        using var gray = new Mat();
        Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
        using var blur = new Mat();
        Cv2.GaussianBlur(gray, blur, new OpenCvSharp.Size(7, 7), 0);

        return TryFloodFillIsolation(blur, bgr.Cols, bgr.Rows)
            ?? TryRelaxedComponentFilter(blur, bgr.Cols, bgr.Rows)
            ?? Box2d.CenterFraction(bgr.Cols, bgr.Rows, 0.68, 0.68);
    }

    // ----------------------------------------------------------------
    // Стратегия 1: flood-fill от краёв кадра — фон «стекает», объект остаётся
    // ----------------------------------------------------------------
    private static Box2d? TryFloodFillIsolation(Mat blur, int cols, int rows)
    {
        Box2d? best = null;
        double bestArea = 0;

        foreach (var invert in new[] { false, true })
        {
            using var bin = new Mat();
            Cv2.Threshold(blur, bin, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
            if (invert)
                Cv2.BitwiseNot(bin, bin);

            // Заливаем из всех пикселей по периметру → помечаем «фон»
            using var filled = bin.Clone();
            FillFromBorder(filled, cols, rows);

            // subject = пиксели, которые были 255 до и не были залиты (остались 255)
            using var subject = new Mat();
            Cv2.InRange(filled, new Scalar(255), new Scalar(255), subject);

            // Морфология: закрываем мелкие дыры
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(9, 9));
            using var closed = new Mat();
            Cv2.MorphologyEx(subject, closed, MorphTypes.Close, kernel);

            var box = LargestConnectedBbox(closed, cols, rows, minFrac: 0.004, maxFrac: 0.92);
            if (box is { } b && b.Width * b.Height > bestArea)
            {
                bestArea = b.Width * b.Height;
                best = b;
            }
        }

        return best;
    }

    /// <summary>Flood-fill фоновым значением (128) от всех пикселей по периметру изображения.</summary>
    private static void FillFromBorder(Mat bin, int cols, int rows)
    {
        var seeds = new List<OpenCvSharp.Point>();
        for (int x = 0; x < cols; x++) { seeds.Add(new OpenCvSharp.Point(x, 0)); seeds.Add(new OpenCvSharp.Point(x, rows - 1)); }
        for (int y = 1; y < rows - 1; y++) { seeds.Add(new OpenCvSharp.Point(0, y)); seeds.Add(new OpenCvSharp.Point(cols - 1, y)); }

        var lo = new Scalar(0); var hi = new Scalar(0);
        foreach (var pt in seeds)
        {
            byte pv = bin.At<byte>(pt.Y, pt.X);
            if (pv == 255)
                Cv2.FloodFill(bin, pt, new Scalar(128), out _, lo, hi, FloodFillFlags.Link4);
        }
    }

    // ----------------------------------------------------------------
    // Стратегия 2: connected components + ослабленный фильтр краёв
    // Убираем ТОЛЬКО тонкие горизонтальные полосы, касающиеся нижнего края (стол/пол)
    // ----------------------------------------------------------------
    private static Box2d? TryRelaxedComponentFilter(Mat blur, int cols, int rows)
    {
        Box2d? best = null;
        double bestArea = 0;
        double totalArea = (double)cols * rows;

        foreach (var invert in new[] { false, true })
        {
            using var bin = new Mat();
            Cv2.Threshold(blur, bin, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
            if (invert)
                Cv2.BitwiseNot(bin, bin);

            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(7, 7));
            using var closed = new Mat();
            Cv2.MorphologyEx(bin, closed, MorphTypes.Close, kernel);

            using var labels = new Mat();
            using var stats = new Mat();
            using var centroids = new Mat();
            int n = Cv2.ConnectedComponentsWithStats(closed, labels, stats, centroids);

            for (int i = 1; i < n; i++)
            {
                int bx = stats.At<int>(i, (int)ConnectedComponentsTypes.Left);
                int by = stats.At<int>(i, (int)ConnectedComponentsTypes.Top);
                int bw = stats.At<int>(i, (int)ConnectedComponentsTypes.Width);
                int bh = stats.At<int>(i, (int)ConnectedComponentsTypes.Height);
                int area = stats.At<int>(i, (int)ConnectedComponentsTypes.Area);

                if (area < totalArea * 0.003 || area > totalArea * 0.92)
                    continue;

                // Только фильтруем тонкую горизонтальную полосу у нижнего края
                bool bottomStrip = (by + bh >= rows - 2)
                                && bh < rows * 0.12
                                && (double)bh / bw < 0.25;
                if (bottomStrip)
                    continue;

                if (area > bestArea)
                {
                    bestArea = area;
                    best = new Box2d(bx, by, bw, bh);
                }
            }
        }

        return best;
    }

    // ----------------------------------------------------------------
    // Вспомогательные
    // ----------------------------------------------------------------
    private static Box2d? LargestConnectedBbox(Mat mask, int cols, int rows, double minFrac, double maxFrac)
    {
        double total = (double)cols * rows;
        using var labels = new Mat();
        using var stats = new Mat();
        using var centroids = new Mat();
        int n = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids);

        Box2d? best = null;
        double bestArea = 0;
        for (int i = 1; i < n; i++)
        {
            int bx = stats.At<int>(i, (int)ConnectedComponentsTypes.Left);
            int by = stats.At<int>(i, (int)ConnectedComponentsTypes.Top);
            int bw = stats.At<int>(i, (int)ConnectedComponentsTypes.Width);
            int bh = stats.At<int>(i, (int)ConnectedComponentsTypes.Height);
            int area = stats.At<int>(i, (int)ConnectedComponentsTypes.Area);

            if (area < total * minFrac || area > total * maxFrac)
                continue;

            if (area > bestArea)
            {
                bestArea = area;
                best = new Box2d(bx, by, bw, bh);
            }
        }
        return best;
    }
}
