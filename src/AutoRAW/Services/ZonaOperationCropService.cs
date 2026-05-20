using AutoRAW.Models;
using ImageMagick;

namespace AutoRAW.Services;

/// <summary>
/// Сценарий <b>operation</b> строго по папке <c>zona\…\operation\NN\</c> (если её нет — этот сервис не вызывается, кадр идёт по обычному Zona/референсу).
/// Порядок: <c>01_center.png</c> — масштаб и сдвиг к перекрестию; <c>02_crop.png</c> — прямоугольник кропа в долях кадра.
/// <see cref="BuildNativePatch"/> возвращает вырезанную область без масштабирования к референсу (для ручных правок);
/// <see cref="ProcessToReferenceSize"/> — как итог после автокропа, сразу размер референса.
/// Никаких <c>NN_line.png</c> и иных алгоритмов поверх — только эти макеты.
/// </summary>
public static class ZonaOperationCropService
{
    public static bool TryProcess(
        MagickImage full,
        string? zonaFolder,
        string? outputStem,
        AutoCropComputation.ReferenceMetrics reference,
        int analysisMaxEdge,
        out MagickImage result,
        string? inputPath = null)
    {
        result = null!;
        var opFolder = ZonaOperationGuideParser.ResolveOperationFolder(
            zonaFolder ?? string.Empty,
            outputStem ?? string.Empty,
            inputPath);
        var cropAspect = reference.RefH / Math.Max(1.0, reference.RefW);
        if (opFolder is null || !ZonaOperationGuideParser.TryLoad(opFolder, out var guide, cropAspect, zonaFolder))
            return false;

        result = ProcessToReferenceSize(full, guide, reference, analysisMaxEdge, outputStem);
        return true;
    }

    /// <summary>Кроп по макету operation в полном разрешении вырезанной области (до даунскейла к референсу).</summary>
    public static MagickImage BuildNativePatch(
        MagickImage full,
        ZonaOperationGuide guide,
        AutoCropComputation.ReferenceMetrics reference,
        int analysisMaxEdge,
        string? outputStem = null)
    {
        var imgW = (double)full.Width;
        var imgH = (double)full.Height;

        // 01_center — зелёный bbox и перекрестие из макета
        var target = AutoCropComputation.AnalyzeTargetForOperation(full, analysisMaxEdge);
        var subject = target.SubjectTarget;

        var guideW = guide.SubjectWidthFrac * imgW;
        var guideH = guide.SubjectHeightFrac * imgH;
        var scale = Math.Clamp(
            Math.Min(guideW / Math.Max(1.0, subject.Width), guideH / Math.Max(1.0, subject.Height)),
            0.82,
            1.28);

        using var scaled = Math.Abs(scale - 1.0) > 0.015
            ? ImageTransformHelper.ScaleAround(full, scale, subject.CenterX, subject.CenterY)
            : (MagickImage)full.Clone();

        var scaledSubject = ScaleBoxAround(subject, scale);
        var targetCx = guide.TargetCenterXFrac * imgW;
        var targetCy = guide.TargetCenterYFrac * imgH;
        var dx = targetCx - scaledSubject.CenterX;
        var dy = targetCy - scaledSubject.CenterY;

        GetOperationComposeNudge(outputStem, imgW, imgH, out var addDx, out var addDy);

        using var centered = ImageTransformHelper.Translate(scaled, dx + addDx, dy + addDy);

        var canvasW = (double)centered.Width;
        var canvasH = (double)centered.Height;

        // 02_crop — зона кропа строго по макету (доли от того же размера кадра, что и при разборе PNG)
        var cropBox = new Box2d(
            guide.CropLeftFrac * canvasW,
            guide.CropTopFrac * canvasH,
            guide.CropWidthFrac * canvasW,
            guide.CropHeightFrac * canvasH);

        if (string.Equals(NormalizeStem(outputStem), "06", StringComparison.Ordinal))
            cropBox = ShrinkBoxAboutCenter(cropBox, 0.90);
        else if (NormalizeStem(outputStem) is "01" or "02" or "03")
            cropBox = ShrinkBoxAboutCenter(cropBox, 0.90);

        return ReferenceAlignedCropService.CropRegionToNative(centered, cropBox);
    }

    /// <summary>Как раньше: вырезание + сразу размер референса (без промежуточных ручных правок).</summary>
    public static MagickImage ProcessToReferenceSize(
        MagickImage full,
        ZonaOperationGuide guide,
        AutoCropComputation.ReferenceMetrics reference,
        int analysisMaxEdge,
        string? outputStem = null)
    {
        var n = BuildNativePatch(full, guide, reference, analysisMaxEdge, outputStem);
        ReferenceAlignedCropService.ResizeImageTo(n, (int)reference.RefW, (int)reference.RefH);
        return n;
    }

    private static Box2d ScaleBoxAround(Box2d box, double scale)
    {
        var w = box.Width * scale;
        var h = box.Height * scale;
        return new Box2d(box.CenterX - w * 0.5, box.CenterY - h * 0.5, w, h);
    }

    /// <summary>Подстройка после «перекрестие → центр объекта» (доли от размера кадра).</summary>
    private static void GetOperationComposeNudge(string? outputStem, double imgW, double imgH, out double addDx, out double addDy)
    {
        addDx = 0;
        addDy = 0;
        switch (NormalizeStem(outputStem))
        {
            case "01" or "02" or "03":
                // Поднять к центру (Translate: отрицательный dy); ранее положительный dy опускал кадр.
                addDy = -0.078 * imgH;
                return;
            case "08":
                // Поднять к центру (сильнее, чем 01–03).
                addDy = -0.088 * imgH;
                return;
            case "04":
                // Чуть ниже и влево (ушли вправо и слишком высоко).
                addDx = -0.036 * imgW;
                addDy = 0.032 * imgH;
                return;
            default:
                return;
        }
    }

    private static Box2d ShrinkBoxAboutCenter(Box2d box, double factor)
    {
        if (factor >= 1.0 - 1e-9)
            return box;
        var w = box.Width * factor;
        var h = box.Height * factor;
        var cx = box.CenterX;
        var cy = box.CenterY;
        return new Box2d(cx - w * 0.5, cy - h * 0.5, w, h);
    }

    private static string? NormalizeStem(string? stem)
    {
        if (string.IsNullOrWhiteSpace(stem))
            return null;
        var t = stem.Trim();
        return t.Length >= 2 ? t : t.PadLeft(2, '0');
    }
}
