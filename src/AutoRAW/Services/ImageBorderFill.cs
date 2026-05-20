using ImageMagick;
using OpenCvSharp;

namespace AutoRAW.Services;

internal static class ImageBorderFill
{
    public static Scalar SampleBackground(Mat bgr)
    {
        if (bgr.Empty())
            return new Scalar(245, 245, 245);

        var h = bgr.Rows;
        var w = bgr.Cols;
        var samples = new List<Vec3b>();

        void Sample(int x, int y)
        {
            if (x >= 0 && x < w && y >= 0 && y < h)
                samples.Add(bgr.At<Vec3b>(y, x));
        }

        var step = Math.Max(1, Math.Min(w, h) / 40);
        for (var x = 0; x < w; x += step)
        {
            Sample(x, 0);
            Sample(x, h - 1);
        }

        for (var y = 0; y < h; y += step)
        {
            Sample(0, y);
            Sample(w - 1, y);
        }

        if (samples.Count == 0)
            return new Scalar(245, 245, 245);

        double b = 0, g = 0, r = 0;
        foreach (var p in samples)
        {
            b += p.Item0;
            g += p.Item1;
            r += p.Item2;
        }

        var n = samples.Count;
        return new Scalar(b / n, g / n, r / n);
    }

    public static Scalar SampleBackground(MagickImage image)
    {
        using var mat = MagickMatConverter.ToMatBgr(image);
        return SampleBackground(mat);
    }
}
