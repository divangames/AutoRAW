using AutoRAW.Models;
using ImageMagick;

namespace AutoRAW.Services;

/// <summary>
/// Кроп по шаблону без дорисовки фона.
///
/// ПРИНЦИП: масштаб кадра берётся от Zona-маркёра (или от ComputeCropBox для пути без Zona).
/// Шаблон _line.png используется ТОЛЬКО для определения ПОЗИЦИИ товара в кадре:
///   — центр товара по X → середина между вертикальными линиями
///   — низ товара → горизонтальная нижняя линия
/// Из этого вычисляется сдвиг прямоугольника кропа в оригинале (без изменения размера кропа).
/// </summary>
public static class LineGuideCropService
{
    /// <summary>
    /// Пробует выполнить позиционированный кроп по шаблону.
    /// Возвращает null если шаблон отсутствует.
    /// </summary>
    /// <param name="full">Полноразмерный входной файл (уже повёрнут если нужно).</param>
    /// <param name="zonaFolder">Папка zona (там ищем NN_line.png).</param>
    /// <param name="outputStem">Имя выходного файла (01, 02, ...08).</param>
    /// <param name="subjectInFull">Реальные границы товара в full-пикселях (из SubjectBoundsEstimator).</param>
    /// <param name="cropW">Ширина кропа в пикселях оригинала — задаёт масштаб (от Zona или ComputeCropBox).</param>
    /// <param name="cropH">Высота кропа в пикселях оригинала — задаёт масштаб.</param>
    /// <param name="outW">Ширина выходного изображения.</param>
    /// <param name="outH">Высота выходного изображения.</param>
    public static MagickImage? TryCrop(
        MagickImage full,
        string? zonaFolder,
        string? outputStem,
        Box2d subjectInFull,
        double cropW,
        double cropH,
        double outW,
        double outH)
    {
        if (cropW <= 0 || cropH <= 0) return null;

        var guidePath = ShotLineGuideParser.ResolveLineGuidePath(zonaFolder ?? string.Empty, outputStem ?? string.Empty);
        if (guidePath is null) return null;

        var guideOpt = ShotLineGuideParser.TryParse(guidePath);
        if (guideOpt is null) return null;

        var guide = guideOpt.Value;

        // Целевая позиция товара в выходном кадре (фракции от 0 до 1)
        var targetCenterXFrac = (guide.LeftX + guide.RightX) / 2.0 / guide.GuideWidth;
        var targetBottomFrac = guide.BottomY / guide.GuideHeight;

        // Позиция кропа в оригинале: двигаем прямоугольник так, чтобы
        // товар оказался на нужной позиции ПОСЛЕ ресайза до outW x outH
        //   (subjectCenterX - cropLeft) / cropW = targetCenterXFrac
        //   (subjectBottom  - cropTop)  / cropH = targetBottomFrac
        var cropX = subjectInFull.CenterX - targetCenterXFrac * cropW;
        var cropY = subjectInFull.Bottom - targetBottomFrac * cropH;

        return CropAndResize(full, cropX, cropY, cropW, cropH, (int)outW, (int)outH);
    }

    private static MagickImage CropAndResize(MagickImage full, double boxX, double boxY, double boxW, double boxH, int outW, int outH)
    {
        var fullW = (int)full.Width;
        var fullH = (int)full.Height;

        var x = (int)Math.Round(boxX);
        var y = (int)Math.Round(boxY);
        var w = Math.Max(1, (int)Math.Round(boxW));
        var h = Math.Max(1, (int)Math.Round(boxH));

        if (x >= 0 && y >= 0 && x + w <= fullW && y + h <= fullH)
        {
            // Кроп целиком внутри оригинала — никаких дорисовок
            var result = (MagickImage)full.Clone();
            result.Crop(new MagickGeometry { X = x, Y = y, Width = (uint)w, Height = (uint)h });
            result.ResetPage();
            ResizeTo(result, outW, outH);
            return result;
        }

        // Кроп выходит за границы — рисуем canvas с фоном и вставляем ту часть, что есть
        var bg = SampleBgColor(full);
        using var canvas = new MagickImage(bg, (uint)w, (uint)h);

        var srcX = Math.Max(0, x);
        var srcY = Math.Max(0, y);
        var srcX2 = Math.Min(fullW, x + w);
        var srcY2 = Math.Min(fullH, y + h);

        if (srcX2 > srcX && srcY2 > srcY)
        {
            var dstX = srcX - x;
            var dstY = srcY - y;
            var copyW = srcX2 - srcX;
            var copyH = srcY2 - srcY;

            using var patch = (MagickImage)full.Clone();
            patch.Crop(new MagickGeometry { X = srcX, Y = srcY, Width = (uint)copyW, Height = (uint)copyH });
            patch.ResetPage();
            canvas.Composite(patch, dstX, dstY, CompositeOperator.Copy);
        }

        var res = (MagickImage)canvas.Clone();
        ResizeTo(res, outW, outH);
        return res;
    }

    private static void ResizeTo(MagickImage img, int w, int h)
    {
        if (img.Width == (uint)w && img.Height == (uint)h)
            return;
        img.FilterType = FilterType.Lanczos;
        img.Resize(new MagickGeometry((uint)w, (uint)h) { IgnoreAspectRatio = true });
        img.ResetPage();
    }

    private static MagickColor SampleBgColor(MagickImage img)
    {
        try
        {
            using var mat = MagickMatConverter.ToMatBgr(img);
            var sc = ImageBorderFill.SampleBackground(mat);
            return new MagickColor(
                (byte)Math.Clamp(sc.Val2, 0, 255),
                (byte)Math.Clamp(sc.Val1, 0, 255),
                (byte)Math.Clamp(sc.Val0, 0, 255));
        }
        catch
        {
            return new MagickColor(245, 245, 245);
        }
    }
}
