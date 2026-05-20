using AutoRAW.Models;
using ImageMagick;
using OpenCvSharp;

namespace AutoRAW.Services;

/// <summary>Читает <c>01_line.png</c> … — линии и эталон товара на макете.</summary>
public static class ShotLineGuideParser
{
    private const int PeakMinCount = 40;
    private const double TopLineMaxFraction = 0.22;

    public static string? ResolveLineGuidePath(string zonaFolder, string outputStem)
    {
        if (string.IsNullOrWhiteSpace(zonaFolder) || string.IsNullOrWhiteSpace(outputStem))
            return null;

        var stem = outputStem.Trim();
        if (stem.Length == 1)
            stem = "0" + stem;

        var path = Path.Combine(zonaFolder, $"{stem}_line.png");
        return File.Exists(path) ? path : null;
    }

    public static bool IsLineGuideFile(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.EndsWith("_line", StringComparison.OrdinalIgnoreCase);
    }

    public static ShotLineGuide? TryParse(string lineGuidePath)
    {
        if (!File.Exists(lineGuidePath))
            return null;

        try
        {
            using var img = RasterImageLoader.Load(lineGuidePath);
            using var small = AutoCropComputation.CloneResizedLongEdge(img, 1400);
            using var mat = MagickMatConverter.ToMatBgr(small);
            using var redMask = RedGuideLineMask.Build(mat);

            var w = redMask.Cols;
            var h = redMask.Rows;
            if (w < 8 || h < 8)
                return null;

            var colSum = ProjectColumns(redMask);
            var rowSum = ProjectRows(redMask);

            if (!TryPickVerticalLines(colSum, w, out var leftX, out var rightX))
                return null;

            if (!TryPickHorizontalLines(rowSum, h, out var bottomY, out var topY))
                return null;

            var sx = (double)img.Width / w;
            var sy = (double)img.Height / h;

            var template = TryParseTemplateSubject(mat, redMask);
            template = template.Scale(sx, sy);

            return new ShotLineGuide(
                leftX * sx,
                rightX * sx,
                bottomY * sy,
                topY.HasValue ? topY.Value * sy : null,
                template.CenterX,
                template.CenterY,
                template.Bottom,
                (int)img.Width,
                (int)img.Height);
        }
        catch
        {
            return null;
        }
    }

    private static Box2d TryParseTemplateSubject(Mat bgr, Mat redMask)
    {
        using var work = bgr.Clone();
        work.SetTo(new Scalar(245, 245, 245), redMask);
        return SubjectBoundsEstimator.Estimate(work);
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

    private static bool TryPickVerticalLines(int[] colSum, int width, out double leftX, out double rightX)
    {
        leftX = rightX = 0;
        var mid = width / 2;
        var leftPeak = FindBestPeak(colSum, 0, mid - 1);
        var rightPeak = FindBestPeak(colSum, mid, width - 1);
        if (leftPeak < 0 || rightPeak < 0 || rightPeak <= leftPeak + 8)
            return false;

        leftX = WeightedCenter(colSum, leftPeak, 6);
        rightX = WeightedCenter(colSum, rightPeak, 6);
        return true;
    }

    private static bool TryPickHorizontalLines(int[] rowSum, int height, out double bottomY, out double? topY)
    {
        bottomY = 0;
        topY = null;

        var lowerStart = (int)(height * 0.45);
        var bottomPeak = FindBestPeak(rowSum, lowerStart, height - 1);
        if (bottomPeak < 0)
            return false;

        bottomY = WeightedCenter(rowSum, bottomPeak, 6);

        var topEnd = (int)(height * TopLineMaxFraction);
        var topPeak = FindBestPeak(rowSum, 0, topEnd);
        if (topPeak >= 0 && topPeak < bottomPeak - height * 0.25)
            topY = WeightedCenter(rowSum, topPeak, 4);

        return true;
    }

    private static int FindBestPeak(int[] projection, int from, int to)
    {
        if (to <= from)
            return -1;

        var bestIdx = -1;
        var bestVal = PeakMinCount - 1;
        for (var i = from + 1; i < to; i++)
        {
            var v = projection[i];
            if (v < PeakMinCount)
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
            if (projection[i] > bestVal)
            {
                bestVal = projection[i];
                bestIdx = i;
            }
        }

        return bestVal >= PeakMinCount ? bestIdx : -1;
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
