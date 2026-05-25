namespace AutoRAW.Models;

/// <summary>Итоговые ручные параметры кадра и источник для UI и пакета.</summary>
public sealed class ResolvedManualShotFrame
{
    public ManualShotAdjust Adjust { get; init; } = new();

    public ManualShotFrameProvenance Provenance { get; init; }

    public SubjectDetectSource DetectSource { get; init; }

    public string? Detail { get; init; }

    /// <summary>Номер эталонного кадра (01…08), если подгонка по шаблону референса.</summary>
    public string? ReferenceStem { get; init; }

    public double AlignQualityScore { get; init; } = double.NaN;

    public bool IsLowAlignQuality { get; init; }

    public string? AlignQualitySummary { get; init; }

    public bool NeedsReview =>
        Provenance is ManualShotFrameProvenance.AutoAlign or ManualShotFrameProvenance.Default
        || IsLowAlignQuality;

    public string ProvenanceLabel => Provenance switch
    {
        ManualShotFrameProvenance.PerFile => "сохранено (файл)",
        ManualShotFrameProvenance.ProfileStem => "сохранено (профиль, номер кадра)",
        ManualShotFrameProvenance.ProfileFileName => "сохранено (профиль, имя файла)",
        ManualShotFrameProvenance.AutoAlign when DetectSource == SubjectDetectSource.YoloV8 => "шаблон референса (YOLOv8)",
        ManualShotFrameProvenance.AutoAlign when DetectSource == SubjectDetectSource.OpenCv => "шаблон референса (OpenCV)",
        ManualShotFrameProvenance.AutoAlign => $"шаблон референса ({Detail ?? "детекция"})",
        _ => "по умолчанию"
    };

    public string BatchLogSuffix
    {
        get
        {
            if (IsLowAlignQuality && !double.IsNaN(AlignQualityScore))
                return $" ⚠ {AlignQualityScore:0}%";

            return Provenance switch
            {
                ManualShotFrameProvenance.PerFile or ManualShotFrameProvenance.ProfileStem
                    or ManualShotFrameProvenance.ProfileFileName => " ✓",
                ManualShotFrameProvenance.AutoAlign => " ⚠ авто",
                _ => " ⚠"
            };
        }
    }

    public ResolvedManualShotFrame WithQuality(double scorePercent, bool isLow, string summary) => new()
    {
        Adjust = Adjust,
        Provenance = Provenance,
        DetectSource = DetectSource,
        Detail = Detail,
        ReferenceStem = ReferenceStem,
        AlignQualityScore = scorePercent,
        IsLowAlignQuality = isLow,
        AlignQualitySummary = summary
    };
}
