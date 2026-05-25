using ImageMagick;
using Sdcb.LibRaw;

namespace AutoRAW.Services;

/// <summary>Демозаик RAW через LibRaw (Sdcb.LibRaw), результат — BGR для Magick/OpenCV.</summary>
public static class LibRawImageLoader
{
    public static bool TryLoad(string path, out MagickImage? image, out string? error)
    {
        image = null;
        error = null;

        try
        {
            using var ctx = RawContext.OpenFile(path);
            ctx.Unpack();
            ctx.DcrawProcess(c =>
            {
                c.UseCameraWb = true;
                c.Interpolation = true;
                c.OutputBps = 8;
                c.HalfSize = false;
            });

            using var processed = ctx.MakeDcrawMemoryImage();
            if (processed.ImageType != ProcessedImageType.Bitmap)
            {
                error = $"неподдерживаемый тип {processed.ImageType}";
                return false;
            }

            processed.SwapRGB();
            var w = processed.Width;
            var h = processed.Height;
            if (w < 1 || h < 1)
            {
                error = "пустой кадр";
                return false;
            }

            var bytes = processed.AsSpan<byte>().ToArray();
            var settings = new PixelReadSettings((uint)w, (uint)h, StorageType.Char, PixelMapping.BGR);
            image = new MagickImage(bytes, settings);
            AutoCropComputation.AutoOrientAndNormalize(image);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
