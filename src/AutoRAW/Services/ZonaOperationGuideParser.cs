using AutoRAW.Models;
using ImageMagick;

namespace AutoRAW.Services;

/// <summary>Читает <c>01_center.png</c> и <c>02_crop.png</c> из <c>zona/…/operation/NN/</c>.</summary>
public static class ZonaOperationGuideParser
{
    private const string CenterFile = "01_center.png";
    private const string CropFrameFile = "02_crop.png";

    public static string? ResolveOperationFolder(string zonaFolder, string outputStem, string? inputPath = null)
    {
        if (string.IsNullOrWhiteSpace(zonaFolder))
            return null;

        var stem = NormalizeShotStem(outputStem, inputPath);
        if (stem is null)
            return null;

        var opRoot = Path.Combine(zonaFolder, "operation");
        if (!Directory.Exists(opRoot))
            return null;

        if (int.TryParse(stem, out var n) && n >= 1)
        {
            var paddedPath = Path.Combine(opRoot, n.ToString("D2"));
            if (Directory.Exists(paddedPath))
                return paddedPath;

            var plainPath = Path.Combine(opRoot, n.ToString());
            if (Directory.Exists(plainPath))
                return plainPath;
        }

        var direct = Path.Combine(opRoot, stem);
        return Directory.Exists(direct) ? direct : null;
    }

    public static string? NormalizeShotStem(string? outputStem, string? inputPath = null)
    {
        var stem = (outputStem ?? string.Empty).Trim();
        if (stem.Length == 1 && char.IsDigit(stem[0]))
            stem = "0" + stem;

        if (int.TryParse(stem, out var n) && n is >= 1 and <= 99)
            return n.ToString("D2");

        if (!string.IsNullOrWhiteSpace(inputPath)
            && InputShotNumberParser.TryParse(inputPath, out var shot))
            return shot.ToString("D2");

        return null;
    }

    /// <param name="zonaProfileFolder">Корень <c>zona\ИмяПрофиля</c> — для резервного <c>zona_tovara.png</c>, если рамку из <c>02_crop.png</c> разобрать не удалось.</param>
    public static bool TryLoad(
        string operationFolder,
        out ZonaOperationGuide guide,
        double cropAspectHOverW = 1050.0 / 1400.0,
        string? zonaProfileFolder = null)
    {
        guide = default;
        var centerPath = Path.Combine(operationFolder, CenterFile);
        var cropPath = Path.Combine(operationFolder, CropFrameFile);
        if (!File.Exists(centerPath) || !File.Exists(cropPath))
            return false;

        try
        {
            using var centerImg = RasterImageLoader.Load(centerPath);
            using var cropImg = RasterImageLoader.Load(cropPath);
            using var centerSmall = AutoCropComputation.CloneResizedLongEdge(centerImg, 1600);
            using var cropSmall = AutoCropComputation.CloneResizedLongEdge(cropImg, 1600);

            using var centerMat = MagickMatConverter.ToMatBgr(centerSmall);
            using var cropMat = MagickMatConverter.ToMatBgr(cropSmall);

            var subject = ColoredGuideRectParser.TryParseGreenSubjectBox(centerMat);
            if (subject is null)
                return false;

            var cropFrame = ColoredGuideRectParser.TryParseRedCropFrame(cropMat, cropAspectHOverW);

            double cropSrcFullW;
            double cropSrcFullH;
            double cropSrcSmallW;
            double cropSrcSmallH;
            Box2d cropInSrcSmall;

            if (cropFrame is not null)
            {
                cropSrcFullW = cropImg.Width;
                cropSrcFullH = cropImg.Height;
                cropSrcSmallW = cropSmall.Width;
                cropSrcSmallH = cropSmall.Height;
                cropInSrcSmall = cropFrame.Value;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(zonaProfileFolder))
                    return false;

                var ztPath = Path.Combine(zonaProfileFolder, "zona_tovara.png");
                if (!File.Exists(ztPath))
                    return false;

                using var ztImg = RasterImageLoader.Load(ztPath);
                using var ztSmall = AutoCropComputation.CloneResizedLongEdge(ztImg, 1600);
                using var ztMat = MagickMatConverter.ToMatBgr(ztSmall);
                var ztFrame = ColoredGuideRectParser.TryParseRedCropFrame(ztMat, cropAspectHOverW);
                if (ztFrame is null)
                    return false;

                cropSrcFullW = ztImg.Width;
                cropSrcFullH = ztImg.Height;
                cropSrcSmallW = ztSmall.Width;
                cropSrcSmallH = ztSmall.Height;
                cropInSrcSmall = ztFrame.Value;
            }

            // Все доли — в системе координат полноразмерного 01_center (а не «centerSmall» × «cropSmall» по отдельности).
            var cScX = centerImg.Width / (double)centerSmall.Width;
            var cScY = centerImg.Height / (double)centerSmall.Height;
            var subFull = subject.Value.Scale(cScX, cScY);

            var (tcx, tcy) = ColoredGuideRectParser.ParseTargetCenter(
                centerMat, centerMat.Cols, centerMat.Rows);
            var tcxFull = tcx * cScX;
            var tcyFull = tcy * cScY;

            var cropInSrcFull = cropInSrcSmall.Scale(
                cropSrcFullW / cropSrcSmallW,
                cropSrcFullH / cropSrcSmallH);

            var toCenterX = centerImg.Width / cropSrcFullW;
            var toCenterY = centerImg.Height / cropSrcFullH;
            var cropInCenter = cropInSrcFull.Scale(toCenterX, toCenterY);

            var cW = (double)centerImg.Width;
            var cH = (double)centerImg.Height;

            guide = new ZonaOperationGuide(
                subFull.CenterX / cW,
                subFull.CenterY / cH,
                subFull.Width / cW,
                subFull.Height / cH,
                tcxFull / cW,
                tcyFull / cH,
                cropInCenter.X / cW,
                cropInCenter.Y / cH,
                cropInCenter.Width / cW,
                cropInCenter.Height / cH);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
