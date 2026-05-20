using AutoRAW.Models;
using ImageMagick;

namespace AutoRAW.Services;

/// <summary>
/// Кроп по референсу: тот же зум и положение товара в кадре, что на <c>reference\NN.jpg</c>.
/// При наличии <c>NN_line.png</c> зум и позиция определяются шириной зоны между красными линиями:
/// товар заполняет зону ровно — не меньше и не больше.
/// </summary>
public static class ReferenceAlignedCropService
{
    /// <summary>
    /// Геометрия кропа в координатах <paramref name="full"/> (как у <see cref="Crop"/>,
    /// но без вырезания) — для кропа <b>после</b> ручных правок на нативном патче.
    /// </summary>
    public static Box2d ComputeFittedCropBox(
        MagickImage full,
        string? zonaFolder,
        string? outputStem,
        AutoCropComputation.ReferenceMetrics reference,
        AutoCropComputation.TargetMetrics target)
    {
        double cropW, cropH, cropX, cropY;

        var guide = TryGetLineGuide(zonaFolder, outputStem);
        Box2d subjectForCrop = target.SubjectTarget;
        var preserveBottom = false;

        if (guide.HasValue)
        {
            preserveBottom = true;
            using (var mat = MagickMatConverter.ToMatBgr(full))
                subjectForCrop = SubjectBoundsEstimator.RefineHorizontalWidthByEdgeProjection(mat, subjectForCrop);

            // ZONE-BASED: зум из ширины зоны между вертикальными линиями.
            var g = guide.Value;
            var zoneWidthFrac   = (g.RightX - g.LeftX) / Math.Max(1.0, g.GuideWidth);
            var zoneCenterXFrac = (g.LeftX + g.RightX) * 0.5 / Math.Max(1.0, g.GuideWidth);
            var zoneBottomFrac  = g.BottomY / Math.Max(1.0, g.GuideHeight);

            cropW = subjectForCrop.Width  / Math.Max(1e-6, zoneWidthFrac);
            cropH = cropW * reference.RefH / Math.Max(1.0, reference.RefW);

            var lineWin = ShotCropPolicy.LineGuideCropWindowScale(outputStem);
            if (lineWin < 1.0 - 1e-9)
            {
                cropW *= lineWin;
                cropH *= lineWin;
            }

            cropX = subjectForCrop.CenterX - zoneCenterXFrac * cropW;
            cropY = subjectForCrop.Bottom   - zoneBottomFrac  * cropH;
        }
        else
        {
            // REFERENCE-BASED fallback (для кадров без line-guide: 05, 07 и т.п.)
            var centerInFrame = ShotCompositionPolicy.UseCenteredCropGeometry(outputStem);
            var refCrop = AutoCropComputation.ComputeCropBox(reference, target, centerInFrame);

            var scale = ShotCropPolicy.CropSizeScale(outputStem);
            cropW = refCrop.Width  * scale;
            cropH = refCrop.Height * scale;

            cropX = refCrop.X + (refCrop.Width  - cropW) * 0.5;
            cropY = refCrop.Y + (refCrop.Height - cropH) * 0.5;
        }

        return CropGeometryService.FitInsideImage(
            new Box2d(cropX, cropY, cropW, cropH),
            target.ImgW,
            target.ImgH,
            subjectForCrop,
            preserveSubjectBottom: preserveBottom);
    }

    public static MagickImage Crop(
        MagickImage full,
        string? zonaFolder,
        string? outputStem,
        AutoCropComputation.ReferenceMetrics reference,
        AutoCropComputation.TargetMetrics target)
    {
        var fitted = ComputeFittedCropBox(full, zonaFolder, outputStem, reference, target);
        return CropAndResize(
            full,
            fitted.X,
            fitted.Y,
            fitted.Width,
            fitted.Height,
            (int)reference.RefW,
            (int)reference.RefH);
    }

    /// <summary>Вырезать <paramref name="fitted"/> с исходного без масштабирования до референса (больше пикселей для правок).</summary>
    public static MagickImage CropFittedToNative(MagickImage full, Box2d fitted)
        => CropRectangleToNative(full, fitted.X, fitted.Y, fitted.Width, fitted.Height);

    private static ShotLineGuide? TryGetLineGuide(string? zonaFolder, string? outputStem)
    {
        var path = ShotLineGuideParser.ResolveLineGuidePath(zonaFolder ?? string.Empty, outputStem ?? string.Empty);
        if (path is null)
            return null;

        var guide = ShotLineGuideParser.TryParse(path);
        if (guide is null)
            return null;

        var g = guide.Value;
        return g.GuideWidth > 0 && g.GuideHeight > 0 ? guide : null;
    }

    /// <summary>Вырезать прямоугольник и привести к размеру референса (operation, line-guide).</summary>
    public static MagickImage CropRegionAndResize(
        MagickImage full,
        Box2d crop,
        int outW,
        int outH)
    {
        var native = CropRectangleToNative(full, crop.X, crop.Y, crop.Width, crop.Height);
        ResizeImageTo(native, outW, outH);
        return native;
    }

    /// <summary>Зона кропа из operation — только вырезание, без даунскейла до референса.</summary>
    public static MagickImage CropRegionToNative(MagickImage full, Box2d crop) =>
        CropRectangleToNative(full, crop.X, crop.Y, crop.Width, crop.Height);

    private static MagickImage CropAndResize(
        MagickImage full,
        double boxX,
        double boxY,
        double boxW,
        double boxH,
        int outW,
        int outH)
    {
        var native = CropRectangleToNative(full, boxX, boxY, boxW, boxH);
        ResizeImageTo(native, outW, outH);
        return native;
    }

    /// <summary>Вырезание прямоугольника в координатах исходника; размер результата ≈ w×h в пикселях сенсора.</summary>
    private static MagickImage CropRectangleToNative(
        MagickImage full,
        double boxX,
        double boxY,
        double boxW,
        double boxH)
    {
        var fullW = (int)full.Width;
        var fullH = (int)full.Height;

        var x = (int)Math.Round(boxX);
        var y = (int)Math.Round(boxY);
        var w = Math.Max(1, (int)Math.Round(boxW));
        var h = Math.Max(1, (int)Math.Round(boxH));

        if (x >= 0 && y >= 0 && x + w <= fullW && y + h <= fullH)
        {
            var result = (MagickImage)full.Clone();
            result.Crop(new MagickGeometry { X = x, Y = y, Width = (uint)w, Height = (uint)h });
            result.ResetPage();
            return result;
        }

        var bg = SampleBgColor(full);
        using var canvas = new MagickImage(bg, (uint)w, (uint)h);

        var srcX = Math.Max(0, x);
        var srcY = Math.Max(0, y);
        var srcX2 = Math.Min(fullW, x + w);
        var srcY2 = Math.Min(fullH, y + h);

        if (srcX2 > srcX && srcY2 > srcY)
        {
            using var patch = (MagickImage)full.Clone();
            patch.Crop(new MagickGeometry
            {
                X = srcX,
                Y = srcY,
                Width = (uint)(srcX2 - srcX),
                Height = (uint)(srcY2 - srcY)
            });
            patch.ResetPage();
            canvas.Composite(patch, srcX - x, srcY - y, CompositeOperator.Copy);
        }

        return (MagickImage)canvas.Clone();
    }

    /// <summary>Привести к размеру файла референса (после ручных правок или при отсутствии правок).</summary>
    public static void ResizeImageTo(MagickImage img, int w, int h)
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
