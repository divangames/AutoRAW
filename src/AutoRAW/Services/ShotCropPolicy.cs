using ImageMagick;

namespace AutoRAW.Services;

/// <summary>Правила ориентации и тонкой подстройки зума по номеру кадра.</summary>
public static class ShotCropPolicy
{
    /// <summary>Множитель размера кропа (&lt;1 — крупнее товар в кадре).</summary>
    public static double CropSizeScale(string? outputStem) =>
        NormalizeStem(outputStem) switch
        {
            "05" => 0.9,
            "07" => 0.608,
            "04" or "06" => 0.92,
            _ => 1.0
        };

    /// <summary>
    /// Доп. ужатие окна кропа в ветке line-guide (множитель к вычисленным cropW/cropH до позиционирования).
    /// </summary>
    public static double LineGuideCropWindowScale(string? outputStem) =>
        NormalizeStem(outputStem) is "07" ? 0.608 : 1.0;

    public static void ApplyPreCropOrientation(MagickImage full, string? outputStem, int analysisMaxEdge)
    {
        if (NormalizeStem(outputStem) is not "03")
            return;

        var target = AutoCropComputation.AnalyzeTarget(full, analysisMaxEdge);
        var subj = target.SubjectTarget;
        if (subj.Height <= subj.Width * 1.08)
            return;

        ImageTransformHelper.RotateCounterClockwise90(full);
    }

    private static string? NormalizeStem(string? stem)
    {
        if (string.IsNullOrWhiteSpace(stem))
            return null;
        var s = stem.Trim();
        return s.Length >= 2 ? s : s.PadLeft(2, '0');
    }
}
