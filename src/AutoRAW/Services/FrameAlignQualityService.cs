using AutoRAW.Models;
using AutoRAW.Services.SubjectDetection;
using ImageMagick;
using OpenCvSharp;

namespace AutoRAW.Services;

/// <summary>Оценка совпадения положения товара на выходном кадре с референсом.</summary>
public static class FrameAlignQualityService
{
    public readonly record struct QualityResult(double ScorePercent, bool IsBelowThreshold, string Summary);

    public static QualityResult EvaluateComposed(
        MagickImage composedAtReferenceSize,
        AutoCropComputation.ReferenceMetrics reference,
        int analysisMaxEdge)
    {
        if (composedAtReferenceSize.Width < 4 || composedAtReferenceSize.Height < 4)
            return new QualityResult(0, true, "пустой кадр");

        var refW = reference.RefW;
        var refH = reference.RefH;
        var subjectRef = reference.SubjectRef;

        using var analysis = AutoCropComputation.CloneResizedLongEdge(composedAtReferenceSize, analysisMaxEdge);
        var scale = (double)composedAtReferenceSize.Width / analysis.Width;

        Box2d subject;
        using (var mat = MagickMatConverter.ToMatBgr(analysis))
        {
            var cv = ProductSubjectDetection.EstimateOpenCv(mat);
            subject = cv.Scale(scale, scale);
        }

        var dx = subject.CenterX / refW - subjectRef.CenterX / refW;
        var dy = subject.CenterY / refH - subjectRef.CenterY / refH;
        var posErr = Math.Sqrt(dx * dx + dy * dy);
        var refFracW = subjectRef.Width / Math.Max(1.0, refW);
        var outFracW = subject.Width / Math.Max(1.0, refW);
        var sizeErr = Math.Abs(Math.Log(outFracW / Math.Max(1e-6, refFracW)));

        var penalty = posErr * 115.0 + sizeErr * 22.0;
        var score = Math.Clamp(100.0 - penalty, 0, 100);
        var min = AlignQualityPreferenceStore.GetMinAlignQualityPercent();
        var low = score < min;
        var summary = low
            ? $"качество {score:0}% (&lt; {min}%)"
            : $"качество {score:0}%";
        return new QualityResult(score, low, summary);
    }

    public static FrameAlignStatusKind ClassifyStatus(ResolvedManualShotFrame frame)
    {
        if (frame.Provenance == ManualShotFrameProvenance.Default)
            return FrameAlignStatusKind.Failed;

        if (frame.IsLowAlignQuality)
            return FrameAlignStatusKind.LowQuality;

        if (frame.Provenance is ManualShotFrameProvenance.PerFile
            or ManualShotFrameProvenance.ProfileStem
            or ManualShotFrameProvenance.ProfileFileName)
            return FrameAlignStatusKind.Ok;

        return FrameAlignStatusKind.AutoReview;
    }

    public static string GlyphFor(FrameAlignStatusKind kind) => kind switch
    {
        FrameAlignStatusKind.Ok => "✓",
        FrameAlignStatusKind.AutoReview => "⚠",
        FrameAlignStatusKind.LowQuality => "⚠",
        FrameAlignStatusKind.Failed => "✗",
        _ => "…"
    };

    public static ResolvedManualShotFrame EnrichWithQuality(
        ResolvedManualShotFrame frame,
        MagickImage composedAtReferenceSize,
        AutoCropComputation.ReferenceMetrics reference,
        int analysisMaxEdge)
    {
        var q = EvaluateComposed(composedAtReferenceSize, reference, analysisMaxEdge);
        return frame.WithQuality(q.ScorePercent, q.IsBelowThreshold, q.Summary);
    }
}
