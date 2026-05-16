using System.IO;
using System.Windows.Media.Imaging;
using AutoRAW.Models;
using ImageMagick;

namespace AutoRAW.Services;

/// <summary>Превью для UI — те же декодирование и кроп, что и при экспорте.</summary>
public static class CropPreviewBitmapFactory
{
    private const int MinEdge = 64;

    public static BitmapSource? LoadThumbnail(string path, int maxEdge)
    {
        try
        {
            using var img = RasterImageLoader.Load(path);
            FitLongEdge(img, maxEdge);
            return ToBitmapSource(img);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Построить превью результата кадрирования (масштаб только для экрана).</summary>
    public static BitmapSource? LoadCroppedPreview(
        string inputPath,
        string referencePath,
        int analysisMaxEdge,
        int displayMaxEdge,
        ColorCorrectionSettings? colorCorrection = null,
        bool applyColorCorrection = false)
    {
        try
        {
            var reference = AutoCropComputation.AnalyzeReference(referencePath, analysisMaxEdge);

            using var full = RasterImageLoader.Load(inputPath);

            var target = AutoCropComputation.AnalyzeTarget(full, analysisMaxEdge);
            var crop = AutoCropComputation.ComputeCropBox(reference, target);
            var (x, y, w, h) = CropGeometryService.ToIntegers(crop, (int)full.Width, (int)full.Height);

            using var cropped = (MagickImage)full.Clone();
            cropped.Crop(new MagickGeometry { X = x, Y = y, Width = (uint)w, Height = (uint)h });
            cropped.ResetPage();
            AutoCropComputation.ResizeToReferenceOutputSize(cropped, reference);
            if (colorCorrection is not null)
                ColorCorrectionService.ApplyIfEnabled(cropped, colorCorrection, applyColorCorrection);
            FitLongEdge(cropped, displayMaxEdge);
            return ToBitmapSource(cropped);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Превью кропа по технологии «Zona» (красный маркёр на парном изображении).</summary>
    public static BitmapSource? LoadZonaCroppedPreview(
        string inputPath,
        string zonaPath,
        string referencePath,
        int analysisMaxEdge,
        int displayMaxEdge,
        ColorCorrectionSettings? colorCorrection = null,
        bool applyColorCorrection = false)
    {
        try
        {
            var zona = ZonaCropService.Detect(zonaPath);
            if (zona is null)
                return null;

            var reference = AutoCropComputation.AnalyzeReference(referencePath, analysisMaxEdge);

            using var full = RasterImageLoader.Load(inputPath);
            using var cropped = ZonaCropService.Crop(full, zona.Value);
            AutoCropComputation.ResizeToReferenceOutputSize(cropped, reference);
            if (colorCorrection is not null)
                ColorCorrectionService.ApplyIfEnabled(cropped, colorCorrection, applyColorCorrection);
            FitLongEdge(cropped, displayMaxEdge);
            return ToBitmapSource(cropped);
        }
        catch
        {
            return null;
        }
    }

    private static void FitLongEdge(MagickImage img, int maxEdge)
    {
        if (maxEdge < MinEdge)
            return;

        var m = Math.Max(img.Width, img.Height);
        if (m <= maxEdge)
            return;

        var s = maxEdge / (double)m;
        var nw = Math.Max(1u, (uint)Math.Round(img.Width * s));
        var nh = Math.Max(1u, (uint)Math.Round(img.Height * s));
        img.Resize(nw, nh);
    }

    private static BitmapSource ToBitmapSource(MagickImage img)
    {
        using var ms = new MemoryStream();
        img.Format = MagickFormat.Png;
        img.Write(ms);
        ms.Position = 0;

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}
