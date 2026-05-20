using AutoRAW.Models;
using OpenCvSharp;

namespace AutoRAW.Services;

/// <summary>Разбор цветных прямоугольников и перекрестия на PNG-макетах.</summary>
internal static class ColoredGuideRectParser
{
    public static Box2d? TryParseGreenSubjectBox(Mat bgr)
    {
        using var mask = GreenGuideMask.Build(bgr);
        var box = LargestMaskBbox(mask, minAreaFrac: 0.002, maxAreaFrac: 0.65, excludeBorder: false);
        if (box != null)
            return box;

        using var maskLoose = GreenGuideMask.BuildRelaxed(bgr);
        return LargestMaskBbox(maskLoose, minAreaFrac: 0.0015, maxAreaFrac: 0.72, excludeBorder: false);
    }

    /// <summary>Красная рамка кропа (не по периметру всего кадра).</summary>
    /// <param name="cropAspectHOverW">Высота/ширина выходного кадра (референс). Нужна для макетов «П-образной» красной линии без верхней границы.</param>
    public static Box2d? TryParseRedCropFrame(Mat bgr, double cropAspectHOverW = 1050.0 / 1400.0)
    {
        using var mask = RedGuideLineMask.Build(bgr);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(7, 7));
        using var thick = new Mat();
        Cv2.Dilate(mask, thick, kernel);

        var w = thick.Cols;
        var h = thick.Rows;
        var colSum = ProjectColumns(thick);
        var rowSum = ProjectRows(thick);

        if (TryPickFrameFromProjections(colSum, rowSum, w, h, out var frame))
            return frame;

        // Только три линии (лево / право / низ), без верхней — типичный zona_tovara / 02_crop.
        if (TryPickOpenTopCropFrame(colSum, rowSum, w, h, cropAspectHOverW, out frame))
            return frame;

        // Та же логика на маске без дилатации (тонкие линии не слипаются в «кляксу»).
        var col2 = ProjectColumns(mask);
        var row2 = ProjectRows(mask);
        if (TryPickFrameFromProjections(col2, row2, w, h, out frame))
            return frame;
        if (TryPickOpenTopCropFrame(col2, row2, w, h, cropAspectHOverW, out frame))
            return frame;

        // Без исключения касания края — нижняя направляющая часто у самого низа PNG.
        var box = LargestMaskBbox(thick, minAreaFrac: 0.008, maxAreaFrac: 0.95, excludeBorder: false);
        if (box != null)
            return box;

        return LargestMaskBbox(thick, minAreaFrac: 0.002, maxAreaFrac: 0.97, excludeBorder: false);
    }

    /// <summary>
    /// Рамка по двум вертикалям и нижней горизонтали; верхняя граница выводится из соотношения сторон референса.
    /// </summary>
    private static bool TryPickOpenTopCropFrame(
        int[] colSum,
        int[] rowSum,
        int width,
        int height,
        double aspectHOverW,
        out Box2d frame)
    {
        frame = default;
        aspectHOverW = Math.Clamp(aspectHOverW, 0.35, 2.5);

        var midX = width / 2;
        var leftPeak = FindBestPeak(colSum, (int)(width * 0.05), Math.Max(midX - 1, (int)(width * 0.05) + 2));
        var rightPeak = FindBestPeak(colSum, midX, (int)(width * 0.95));
        var lowerStart = (int)(height * 0.28);
        var bottomPeak = FindBestPeak(rowSum, lowerStart, height - 2);

        if (leftPeak < 0 || rightPeak < 0 || rightPeak <= leftPeak + 8 || bottomPeak < 0)
            return false;

        var left = WeightedCenter(colSum, leftPeak, 6);
        var right = WeightedCenter(colSum, rightPeak, 6);
        var bottom = WeightedCenter(rowSum, bottomPeak, 6);

        var boxW = right - left;
        if (boxW < width * 0.06 || boxW > width * 0.96)
            return false;

        var boxH = boxW * aspectHOverW;
        if (boxH < height * 0.06)
            return false;

        var top = bottom - boxH;
        if (top < 0)
        {
            top = 0;
            boxH = bottom - top;
            if (boxH < height * 0.06)
                return false;
        }

        frame = new Box2d(left, top, boxW, boxH);
        return true;
    }

    private static bool TryPickFrameFromProjections(
        int[] colSum,
        int[] rowSum,
        int width,
        int height,
        out Box2d frame)
    {
        frame = default;
        var midX = width / 2;
        var midY = height / 2;

        var leftPeak = FindBestPeak(colSum, (int)(width * 0.05), midX);
        var rightPeak = FindBestPeak(colSum, midX, (int)(width * 0.95));
        var topPeak = FindBestPeak(rowSum, (int)(height * 0.05), midY);
        var bottomPeak = FindBestPeak(rowSum, midY, (int)(height * 0.95));

        if (leftPeak < 0 || rightPeak < 0 || topPeak < 0 || bottomPeak < 0)
            return false;
        if (rightPeak <= leftPeak + 12 || bottomPeak <= topPeak + 12)
            return false;

        var left = WeightedCenter(colSum, leftPeak, 6);
        var right = WeightedCenter(colSum, rightPeak, 6);
        var top = WeightedCenter(rowSum, topPeak, 6);
        var bottom = WeightedCenter(rowSum, bottomPeak, 6);

        var boxW = right - left;
        var boxH = bottom - top;
        if (boxW < width * 0.08 || boxH < height * 0.08)
            return false;
        if (boxW > width * 0.95 || boxH > height * 0.95)
            return false;

        frame = new Box2d(left, top, boxW, boxH);
        return true;
    }

    public static (double Cx, double Cy) ParseTargetCenter(Mat bgr, int width, int height)
    {
        using var redMask = RedGuideLineMask.Build(bgr);
        var colSum = ProjectColumns(redMask);
        var rowSum = ProjectRows(redMask);

        var midX = width / 2;
        var midY = height / 2;

        var vPeak = FindBestPeak(colSum, midX - width / 4, midX + width / 4);
        var hPeak = FindBestPeak(rowSum, midY - height / 4, midY + height / 4);

        var cx = vPeak >= 0 ? WeightedCenter(colSum, vPeak, 8) : width * 0.5;
        var cy = hPeak >= 0 ? WeightedCenter(rowSum, hPeak, 8) : height * 0.5;
        return (cx, cy);
    }

    private static Box2d? LargestMaskBbox(Mat mask, double minAreaFrac, double maxAreaFrac, bool excludeBorder)
    {
        var cols = mask.Cols;
        var rows = mask.Rows;
        if (cols < 8 || rows < 8)
            return null;

        var total = (double)cols * rows;
        var margin = Math.Max(3, Math.Min(cols, rows) / 80);

        using var labels = new Mat();
        using var stats = new Mat();
        using var centroids = new Mat();
        var n = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids);

        Box2d? best = null;
        var bestArea = 0.0;

        for (var i = 1; i < n; i++)
        {
            var bx = stats.At<int>(i, (int)ConnectedComponentsTypes.Left);
            var by = stats.At<int>(i, (int)ConnectedComponentsTypes.Top);
            var bw = stats.At<int>(i, (int)ConnectedComponentsTypes.Width);
            var bh = stats.At<int>(i, (int)ConnectedComponentsTypes.Height);
            var area = stats.At<int>(i, (int)ConnectedComponentsTypes.Area);
            var areaD = (double)area;

            if (areaD < total * minAreaFrac || areaD > total * maxAreaFrac)
                continue;

            if (excludeBorder)
            {
                if (bx <= margin || by <= margin
                    || bx + bw >= cols - margin
                    || by + bh >= rows - margin)
                    continue;
            }

            if (areaD > bestArea)
            {
                bestArea = areaD;
                best = new Box2d(bx, by, bw, bh);
            }
        }

        return best;
    }

    private static int[] ProjectColumns(Mat mask)
    {
        var sums = new int[mask.Cols];
        for (var x = 0; x < mask.Cols; x++)
        {
            var n = 0;
            for (var y = 0; y < mask.Rows; y++)
            {
                if (mask.At<byte>(y, x) > 0)
                    n++;
            }

            sums[x] = n;
        }

        return sums;
    }

    private static int[] ProjectRows(Mat mask)
    {
        var sums = new int[mask.Rows];
        for (var y = 0; y < mask.Rows; y++)
        {
            var n = 0;
            for (var x = 0; x < mask.Cols; x++)
            {
                if (mask.At<byte>(y, x) > 0)
                    n++;
            }

            sums[y] = n;
        }

        return sums;
    }

    private static int FindBestPeak(int[] projection, int from, int to, int minCount = 6)
    {
        from = Math.Clamp(from, 1, projection.Length - 2);
        to = Math.Clamp(to, from + 1, projection.Length - 1);

        var bestIdx = -1;
        var bestVal = minCount - 1;
        for (var i = from; i <= to; i++)
        {
            var v = projection[i];
            if (v < minCount)
                continue;
            if (v >= projection[i - 1] && v >= projection[i + 1] && v > bestVal)
            {
                bestVal = v;
                bestIdx = i;
            }
        }

        if (bestIdx >= 0)
            return bestIdx;

        for (var i = from; i <= to; i++)
        {
            var v = projection[i];
            if (v > bestVal)
            {
                bestVal = v;
                bestIdx = i;
            }
        }

        return bestVal >= minCount ? bestIdx : -1;
    }

    private static double WeightedCenter(int[] projection, int center, int radius)
    {
        var from = Math.Max(0, center - radius);
        var to = Math.Min(projection.Length - 1, center + radius);
        double sum = 0;
        double wsum = 0;
        for (var i = from; i <= to; i++)
        {
            var w = projection[i];
            if (w <= 0)
                continue;
            sum += i * w;
            wsum += w;
        }

        return wsum > 0 ? sum / wsum : center;
    }
}
