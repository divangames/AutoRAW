using AutoRAW.Models;
using AutoRAW.ViewModels;
using ImageMagick;

namespace AutoRAW.Services;

/// <summary>Оценка статуса ✓/⚠ для строки очереди на главном экране.</summary>
public static class MappingRowAlignStatusService
{
    public sealed class RowStatusUpdate
    {
        public FrameAlignStatusKind Kind { get; init; }

        public string Glyph { get; init; } = "…";

        public string ToolTip { get; init; } = string.Empty;

        public double QualityScore { get; init; } = double.NaN;
    }

    public static RowStatusUpdate Evaluate(
        CropMappingRowViewModel row,
        string inputRoot,
        string referenceFolder,
        string? profileDisplayName,
        string? zonaFolder,
        int analysisMaxEdge)
    {
        try
        {
            if (!File.Exists(row.InputPath))
                return Fail("файл не найден");

            var refName = row.SelectedReferenceFile;
            if (string.IsNullOrWhiteSpace(refName))
                return Fail("нет референса");

            var refPath = Path.Combine(referenceFolder, refName);
            if (!File.Exists(refPath))
                return Fail("референс не найден");

            var (export, _) = ManualShotFrameResolver.ResolveExportAndAuto(
                row.InputPath,
                refPath,
                profileDisplayName,
                row.OutputFileStem,
                zonaFolder,
                analysisMaxEdge,
                row.RotateCounterClockwise90);

            var reference = AutoCropComputation.AnalyzeReference(refPath, analysisMaxEdge);
            var refW = (int)reference.RefW;
            var refH = (int)reference.RefH;

            using var full = CropPreviewBitmapFactory.TryLoadPreparedFullForManualFrame(
                row.InputPath, row.OutputFileStem, analysisMaxEdge, row.RotateCounterClockwise90);
            if (full is null)
                return Fail("не загрузился кадр");

            using var composed = ManualShotAdjustApplier.ComposeFromFullToReference(full, export.Adjust, refW, refH);
            var enriched = FrameAlignQualityService.EnrichWithQuality(export, composed, reference, analysisMaxEdge);
            var kind = FrameAlignQualityService.ClassifyStatus(enriched);
            var rel = TryRel(inputRoot, row.InputPath);
            var tip = $"{rel}\n{enriched.ProvenanceLabel}";
            if (!double.IsNaN(enriched.AlignQualityScore))
                tip += $"\n{enriched.AlignQualitySummary}";
            if (kind == FrameAlignStatusKind.AutoReview)
                tip += "\nРекомендуется проверить в редакторе.";

            return new RowStatusUpdate
            {
                Kind = kind,
                Glyph = FrameAlignQualityService.GlyphFor(kind),
                ToolTip = tip,
                QualityScore = enriched.AlignQualityScore
            };
        }
        catch
        {
            return Fail("ошибка оценки");
        }
    }

    private static RowStatusUpdate Fail(string msg) => new()
    {
        Kind = FrameAlignStatusKind.Failed,
        Glyph = FrameAlignQualityService.GlyphFor(FrameAlignStatusKind.Failed),
        ToolTip = msg
    };

    private static string TryRel(string inputRoot, string path)
    {
        try
        {
            var rel = Path.GetRelativePath(Path.GetFullPath(inputRoot), Path.GetFullPath(path));
            return rel == "." ? Path.GetFileName(path) : rel;
        }
        catch
        {
            return Path.GetFileName(path);
        }
    }
}
