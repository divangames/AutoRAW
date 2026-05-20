using AutoRAW.Models;
using ImageMagick;
using OpenCvSharp;

namespace AutoRAW.Services;

/// <summary>
/// Технология «Zona» для AutoRAW: чтение зоны кропа с маркёрного изображения (красный контур),
/// расчёт MinAreaRect с учётом наклона и применение к полноразмерному исходнику. Собственная логика продукта, не внешний SDK.
/// </summary>
public static class ZonaCropService
{
    public readonly record struct ZonaCropResult(
        RotatedRect RectInZonaCoords,
        int ZonaWidth,
        int ZonaHeight,
        bool IsRotated);

    // ----------------------------------------------------------------
    // Маркёрное изображение папки zona (технология Zona)
    // ----------------------------------------------------------------
    public static ZonaCropResult? Detect(string zonaPath)
    {
        using var zonaImg = RasterImageLoader.Load(zonaPath);
        return Detect(zonaImg);
    }

    public static ZonaCropResult? Detect(MagickImage zonaImg)
    {
        var zw = (int)zonaImg.Width;
        var zh = (int)zonaImg.Height;

        using var small = AutoCropComputation.CloneResizedLongEdge(zonaImg, 1400);
        using var mat = MagickMatConverter.ToMatBgr(small);

        using var redMask = BuildRedMask(mat);

        // Морфология: сшить пунктир / артефакты
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(5, 5));
        using var closed = new Mat();
        Cv2.MorphologyEx(redMask, closed, MorphTypes.Close, kernel);

        Cv2.FindContours(closed, out OpenCvSharp.Point[][] contours, out HierarchyIndex[] _,
            RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        if (contours.Length == 0)
            return null;

        var allPts = contours.SelectMany(c => c).ToArray();
        if (allPts.Length < 4)
            return null;

        var minRect = Cv2.MinAreaRect(allPts);

        // Нормализуем угол к диапазону [-45 .. +45)
        var angle = (double)minRect.Angle;
        if (angle < -45) angle += 90;
        if (minRect.Size.Width < minRect.Size.Height)
        {
            (minRect.Size.Width, minRect.Size.Height) = (minRect.Size.Height, minRect.Size.Width);
            angle = angle < 0 ? angle + 90 : angle - 90;
        }

        bool isRotated = Math.Abs(angle) > 3.0;

        // Масштабируем из «small» → zona coords
        double sx = (double)zw / small.Width;
        double sy = (double)zh / small.Height;

        var scaledRect = new RotatedRect(
            new Point2f(minRect.Center.X * (float)sx, minRect.Center.Y * (float)sy),
            new Size2f(minRect.Size.Width * (float)sx, minRect.Size.Height * (float)sy),
            (float)angle);

        return new ZonaCropResult(scaledRect, zw, zh, isRotated);
    }

    // ----------------------------------------------------------------
    // Вырезать область по результату детектирования (возвращает new MagickImage)
    // Caller is responsible for disposing the result.
    // ----------------------------------------------------------------
    public static MagickImage Crop(MagickImage full, ZonaCropResult zona)
    {
        var tw = (int)full.Width;
        var th = (int)full.Height;

        double sx = (double)tw / zona.ZonaWidth;
        double sy = (double)th / zona.ZonaHeight;

        var cx = zona.RectInZonaCoords.Center.X * sx;
        var cy = zona.RectInZonaCoords.Center.Y * sy;
        var rw = zona.RectInZonaCoords.Size.Width * sx;
        var rh = zona.RectInZonaCoords.Size.Height * sy;
        var angle = (double)zona.RectInZonaCoords.Angle;

        if (zona.IsRotated)
        {
            using var mat = MagickMatConverter.ToMatBgr(full);

            var M = Cv2.GetRotationMatrix2D(new Point2f((float)cx, (float)cy), -angle, 1.0);
            using var rotated = new Mat();
            Cv2.WarpAffine(mat, rotated, M, new OpenCvSharp.Size(tw, th),
                InterpolationFlags.Lanczos4, BorderTypes.Replicate);

            int x = Math.Clamp((int)Math.Round(cx - rw / 2), 0, tw - 1);
            int y = Math.Clamp((int)Math.Round(cy - rh / 2), 0, th - 1);
            int w = Math.Clamp((int)Math.Round(rw), 1, tw - x);
            int h = Math.Clamp((int)Math.Round(rh), 1, th - y);

            using var croppedMat = new Mat(rotated, new Rect(x, y, w, h));
            return MagickMatConverter.ToMagickImage(croppedMat);
        }
        else
        {
            int x = Math.Clamp((int)Math.Round(cx - rw / 2), 0, tw - 1);
            int y = Math.Clamp((int)Math.Round(cy - rh / 2), 0, th - 1);
            int w = Math.Clamp((int)Math.Round(rw), 1, tw - x);
            int h = Math.Clamp((int)Math.Round(rh), 1, th - y);

            var cropped = (MagickImage)full.Clone();
            cropped.Crop(new MagickGeometry { X = x, Y = y, Width = (uint)w, Height = (uint)h });
            cropped.ResetPage();
            return cropped;
        }
    }

    /// <summary>Размер Zona-области в пикселях полноразмерного изображения (задаёт масштаб кропа).</summary>
    public static (double CropW, double CropH) GetCropDimensions(MagickImage full, ZonaCropResult zona)
    {
        double sx = (double)full.Width / zona.ZonaWidth;
        double sy = (double)full.Height / zona.ZonaHeight;
        return (zona.RectInZonaCoords.Size.Width * sx, zona.RectInZonaCoords.Size.Height * sy);
    }

    // Convenience: crop and save to file
    public static void ApplyCrop(MagickImage full, ZonaCropResult zona, string outputPath)
    {
        using var result = Crop(full, zona);
        result.Format = MagickFormat.Jpeg;
        result.Quality = 92;
        result.Write(outputPath);
    }

    private static Mat BuildRedMask(Mat bgr)
    {
        Mat[] ch = Cv2.Split(bgr);
        using var b = ch[0];
        using var g = ch[1];
        using var r = ch[2];

        using var rMask = new Mat();
        using var gMask = new Mat();
        using var bMask = new Mat();

        Cv2.Threshold(r, rMask, 130, 255, ThresholdTypes.Binary);
        Cv2.Threshold(g, gMask, 90, 255, ThresholdTypes.BinaryInv);
        Cv2.Threshold(b, bMask, 90, 255, ThresholdTypes.BinaryInv);

        var result = new Mat();
        Cv2.BitwiseAnd(rMask, gMask, result);
        Cv2.BitwiseAnd(result, bMask, result);
        return result;
    }
}
