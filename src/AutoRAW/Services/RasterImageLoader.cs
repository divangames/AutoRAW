using ImageMagick;
using ImageMagick.Formats;

namespace AutoRAW.Services;

/// <summary>
/// Загрузка растров: для RAW отключаем встроенное превью — иначе кроп и ориентация считаются по другому кадру.
/// </summary>
public static class RasterImageLoader
{
    private static readonly HashSet<string> RawExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".nef", ".nrw", ".arw", ".dng", ".cr2", ".cr3", ".orf", ".pef", ".raf", ".rw2", ".raw"
    };

    public static MagickImage Load(string path)
    {
        var ext = Path.GetExtension(path);
        if (RawExtensions.Contains(ext))
        {
            var settings = new MagickReadSettings
            {
                Defines = new DngReadDefines
                {
                    ReadThumbnail = false
                }
            };
            var img = new MagickImage(path, settings);
            AutoCropComputation.AutoOrientAndNormalize(img);
            return img;
        }

        var jpg = new MagickImage(path);
        AutoCropComputation.AutoOrientAndNormalize(jpg);
        return jpg;
    }
}
