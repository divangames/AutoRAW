using ImageMagick;
using OpenCvSharp;

namespace AutoRAW.Services;

public static class MagickMatConverter
{
    /// <summary>Кодирует кадр в BMP и декодирует в <see cref="Mat"/> BGR (удобно для OpenCV).</summary>
    public static Mat ToMatBgr(MagickImage image)
    {
        using var ms = new MemoryStream();
        image.Format = MagickFormat.Bmp3;
        image.Write(ms);
        ms.Position = 0;
        var bytes = ms.ToArray();
        var mat = Cv2.ImDecode(bytes, ImreadModes.Color);
        if (mat.Empty())
            throw new InvalidOperationException("Не удалось преобразовать изображение в Mat.");
        return mat;
    }
}
