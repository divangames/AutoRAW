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

    /// <summary>Последний успешный загрузчик RAW: libraw или magick.</summary>
    public static string? LastRawLoaderDetail { get; private set; }

    public static MagickImage Load(string path)
    {
        var ext = Path.GetExtension(path);
        if (RawExtensions.Contains(ext))
            return LoadRaw(path);

        var jpg = new MagickImage(path);
        AutoCropComputation.AutoOrientAndNormalize(jpg);
        return jpg;
    }

    private static MagickImage LoadRaw(string path)
    {
        var mode = RawLoaderPreferenceStore.GetMode();
        if (mode != RawLoaderMode.Magick)
        {
            if (LibRawImageLoader.TryLoad(path, out var libRaw, out _) && libRaw is not null)
            {
                LastRawLoaderDetail = "libraw";
                return libRaw;
            }

            if (mode == RawLoaderMode.LibRawOnly)
                throw new InvalidOperationException(
                    $"LibRaw не смог открыть файл: {Path.GetFileName(path)}. Проверьте формат или переключите загрузчик на ImageMagick.");
        }

        LastRawLoaderDetail = "magick";
        var settings = new MagickReadSettings
        {
            Defines = new DngReadDefines { ReadThumbnail = false }
        };
        var img = new MagickImage(path, settings);
        AutoCropComputation.AutoOrientAndNormalize(img);
        return img;
    }
}
