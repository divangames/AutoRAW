using ImageMagick;
using OpenCvSharp;

namespace AutoRAW.Services;

public static class ImageTransformHelper
{
    public static bool IsLandscape(MagickImage image) => image.Width > image.Height;

    /// <summary>Поворот на 90° против часовой стрелки.</summary>
    public static void RotateCounterClockwise90(MagickImage image)
    {
        image.Rotate(-90);
        image.ResetPage();
    }

    /// <summary>Сдвиг содержимого; пустые области — цвет фона с краёв.</summary>
    public static MagickImage Translate(MagickImage src, double dx, double dy)
    {
        var w = (int)src.Width;
        var h = (int)src.Height;
        var bg = ToMagickColor(ImageBorderFill.SampleBackground(src));
        using var canvas = new MagickImage(bg, (uint)w, (uint)h);
        var ix = (int)Math.Round(dx);
        var iy = (int)Math.Round(dy);
        var srcX = Math.Max(0, -ix);
        var srcY = Math.Max(0, -iy);
        var dstX = Math.Max(0, ix);
        var dstY = Math.Max(0, iy);
        var copyW = Math.Min(w - srcX, w - dstX);
        var copyH = Math.Min(h - srcY, h - dstY);
        if (copyW > 0 && copyH > 0)
        {
            using var patch = (MagickImage)src.Clone();
            patch.Crop(new MagickGeometry { X = srcX, Y = srcY, Width = (uint)copyW, Height = (uint)copyH });
            patch.ResetPage();
            canvas.Composite(patch, dstX, dstY, CompositeOperator.Copy);
        }
        var result = (MagickImage)canvas.Clone();
        result.ResetPage();
        return result;
    }

    /// <summary>Масштаб вокруг точки (аналог «приблизить объект» перед центрированием).</summary>
    public static MagickImage ScaleAround(MagickImage src, double scale, double centerX, double centerY)
    {
        using var mat = MagickMatConverter.ToMatBgr(src);
        var m = Cv2.GetRotationMatrix2D(new Point2f((float)centerX, (float)centerY), 0, scale);
        using var scaled = new Mat();
        Cv2.WarpAffine(
            mat,
            scaled,
            m,
            new OpenCvSharp.Size(mat.Cols, mat.Rows),
            InterpolationFlags.Lanczos4,
            BorderTypes.Replicate);

        var result = MagickMatConverter.ToMagickImage(scaled);
        result.ResetPage();
        return result;
    }

    private static MagickColor ToMagickColor(Scalar sc)
        => new(
            (byte)Math.Clamp(sc.Val2, 0, 255),
            (byte)Math.Clamp(sc.Val1, 0, 255),
            (byte)Math.Clamp(sc.Val0, 0, 255));
}

