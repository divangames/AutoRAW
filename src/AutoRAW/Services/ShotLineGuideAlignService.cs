using AutoRAW.Models;
using ImageMagick;

namespace AutoRAW.Services;

/// <summary>Выравнивание товара по макету <c>NN_line.png</c>.</summary>
public static class ShotLineGuideAlignService
{
    private const double MinShiftPx = 1.5;
    private const double LineMarginPx = 3.0;

    public static bool TryAlign(
        MagickImage image,
        string? zonaFolder,
        string? outputFileStem,
        int analysisMaxEdge)
    {
        var path = ShotLineGuideParser.ResolveLineGuidePath(zonaFolder ?? string.Empty, outputFileStem ?? string.Empty);
        if (path is null)
            return false;

        var guide = ShotLineGuideParser.TryParse(path);
        if (guide is null)
            return false;

        AlignToGuide(image, guide.Value, analysisMaxEdge, outputFileStem);
        return true;
    }

    public static void AlignToGuide(
        MagickImage image,
        ShotLineGuide guide,
        int analysisMaxEdge,
        string? outputFileStem = null)
    {
        if (image.Width < 4 || image.Height < 4)
            return;

        var stem = NormalizeStem(outputFileStem);
        var imgW = (double)image.Width;
        var imgH = (double)image.Height;
        var sx = imgW / Math.Max(1, guide.GuideWidth);
        var sy = imgH / Math.Max(1, guide.GuideHeight);

        using var analysis = AutoCropComputation.CloneResizedLongEdge(image, analysisMaxEdge);
        var scale = imgW / analysis.Width;

        Box2d subject;
        using (var mat = MagickMatConverter.ToMatBgr(analysis))
            subject = SubjectBoundsEstimator.Estimate(mat).Scale(scale, scale);

        // Для всех кадров с линиями: центр между вертикальными линиями — целевой X;
        // нижняя линия — целевой Y для низа товара.
        AlignByLines(image, guide, subject, sx, sy, imgW, imgH);
    }

    private static void AlignByLines(
        MagickImage image,
        ShotLineGuide guide,
        Box2d subject,
        double sx,
        double sy,
        double imgW,
        double imgH)
    {
        // Цель X: середина между вертикальными линиями
        var targetCenterX = (guide.LeftX + guide.RightX) / 2.0;
        var dx = targetCenterX - subject.CenterX;

        // Цель Y: низ товара у нижней линии (с небольшим отступом)
        var targetBottom = guide.SafeBottom(LineMarginPx * sy);
        var dy = targetBottom - subject.Bottom;

        var maxDx = imgW * 0.40;
        var maxDy = imgH * 0.40;
        dx = Math.Clamp(dx, -maxDx, maxDx);
        dy = Math.Clamp(dy, -maxDy, maxDy);

        var bg = ImageBorderFill.SampleBackground(image);

        if (Math.Abs(dx) >= MinShiftPx || Math.Abs(dy) >= MinShiftPx)
            ReferenceCompositionAlignService.ApplyTranslation(image, dx, dy, bg);

        // Второй проход
        using var analysis2 = AutoCropComputation.CloneResizedLongEdge(image, 1400);
        var scale2 = imgW / analysis2.Width;
        Box2d subject2;
        using (var mat2 = MagickMatConverter.ToMatBgr(analysis2))
            subject2 = SubjectBoundsEstimator.Estimate(mat2).Scale(scale2, scale2);

        var dx2 = targetCenterX - subject2.CenterX;
        var dy2 = targetBottom - subject2.Bottom;
        dx2 = Math.Clamp(dx2, -maxDx, maxDx);
        dy2 = Math.Clamp(dy2, -maxDy, maxDy);

        if (Math.Abs(dx2) >= MinShiftPx || Math.Abs(dy2) >= MinShiftPx)
            ReferenceCompositionAlignService.ApplyTranslation(image, dx2, dy2, bg);
    }


    private static void AlignByBounds(
        MagickImage image,
        ShotLineGuide guide,
        Box2d subject,
        double sx,
        double sy,
        double maxShiftX,
        double maxShiftY)
    {
        var safeLeft = guide.SafeLeft(LineMarginPx * sx);
        var safeRight = guide.SafeRight(LineMarginPx * sx);
        var safeBottom = guide.SafeBottom(LineMarginPx * sy);
        var safeTop = guide.SafeTop(LineMarginPx * sy);

        var dx = 0.0;
        var dy = 0.0;

        if (subject.X < safeLeft)
            dx += safeLeft - subject.X;
        if (subject.Right > safeRight)
            dx -= subject.Right - safeRight;
        if (subject.Bottom > safeBottom)
            dy -= subject.Bottom - safeBottom;
        if (safeTop is { } top && subject.Y < top)
            dy += top - subject.Y;

        var imgW = (double)image.Width;
        var imgH = (double)image.Height;
        dx = Math.Clamp(dx, -imgW * maxShiftX, imgW * maxShiftX);
        dy = Math.Clamp(dy, -imgH * maxShiftY, imgH * maxShiftY);

        if (Math.Abs(dx) < MinShiftPx && Math.Abs(dy) < MinShiftPx)
            return;

        ReferenceCompositionAlignService.ApplyTranslation(image, dx, dy, ImageBorderFill.SampleBackground(image));
    }

    private static string? NormalizeStem(string? stem)
    {
        if (string.IsNullOrWhiteSpace(stem))
            return null;
        var s = stem.Trim();
        return s.Length >= 2 ? s : s.PadLeft(2, '0');
    }
}
