using AutoRAW.Models;
using AutoRAW.Services.SubjectDetection;
using ImageMagick;

namespace AutoRAW.Services;

/// <summary>
/// Авто-подгонка к размеру эталона из <c>reference\</c>: силуэт **OpenCV** + правила ракурсов (Sneakers layout).
/// Итерационный ONNX-пайплайн убран для скорости пакета; уточняющие проходы опциональны (<see cref="DefaultMaxRefinePasses"/> = 0).
/// </summary>
public static class ManualShotAutoAlignService
{
    /// <summary>Итерации «превью → поправка». По умолчанию 0 — одна сборка crop по эталону + OpenCV (пакетно).</summary>
    public const int DefaultMaxRefinePasses = 0;

    /// <summary>Если <c>maxRefinePasses</c> &gt; 0 — детект на composed ограничиваем этим ребром (только OpenCV).</summary>
    private const int RefineComposeDetectMaxEdge = 768;

    /// <summary>Единое разрешение анализа внутри TryCompute — меньший хвост ONNX/VRAM, один кэш шаблона эталона.</summary>
    private const int FastAlignCanonicalEdge = 896;

    private const double PositionDonePx = 4.0;
    private const double SizeDoneRatio = 0.025;

    public readonly record struct AutoAlignOutcome(
        ManualShotAdjust Adjust,
        ReferenceCompositionTemplate Template,
        SubjectDetectSource DetectSource,
        string Detail);

    public static bool IsSkippedStem(string? outputStem)
    {
        var stem = NormalizeStem(outputStem);
        return stem is "05" or "07";
    }

    public static bool TryCompute(
        MagickImage fullOriented,
        string referencePath,
        int analysisMaxEdge,
        out AutoAlignOutcome outcome) =>
        TryCompute(fullOriented, referencePath, outputStem: null, zonaFolder: null, analysisMaxEdge, out outcome);

    public static bool TryCompute(
        MagickImage fullOriented,
        string referencePath,
        string? outputStem,
        string? zonaFolder,
        int analysisMaxEdge,
        out AutoAlignOutcome outcome) =>
        TryCompute(
            fullOriented,
            referencePath,
            outputStem,
            zonaFolder,
            analysisMaxEdge,
            DefaultMaxRefinePasses,
            out outcome);

    public static bool TryCompute(
        MagickImage fullOriented,
        string referencePath,
        string? outputStem,
        string? zonaFolder,
        int analysisMaxEdge,
        int maxRefinePasses,
        out AutoAlignOutcome outcome)
    {
        outcome = default;

        if (IsSkippedStem(outputStem))
            return false;

        if (!File.Exists(referencePath))
            return false;

        maxRefinePasses = Math.Clamp(maxRefinePasses, 0, 8);

        var stem = NormalizeStem(outputStem);
        var alignedEdge = Math.Clamp(Math.Min(analysisMaxEdge, FastAlignCanonicalEdge), 256, FastAlignCanonicalEdge);

        var template = ReferenceCompositionCatalog.GetOrBuild(referencePath, alignedEdge);
        var reference = template.ToReferenceMetrics();
        _ = zonaFolder;

        if (SneakersTopViewComposition.UsesTopViewLayout(stem)
            && RemoveBgSubjectDetector.TryDetectSubjectOpenCvLayoutOnly(
                fullOriented, alignedEdge, out var subjectFull, out var detectSource, out var detectDetail))
        {
            var poseOk = SneakersTopViewComposition.IsCompatibleTopView(
                    reference, subjectFull.Box, fullOriented.Width, fullOriented.Height, stem);
            var bboxOk = SneakersLayoutAlignGuard.IsSubjectSaneForLayout(
                reference, subjectFull.Box, fullOriented.Width, fullOriented.Height, stem);

            if (poseOk && bboxOk
                && SneakersTopViewAlignService.TryComputeAdjust(fullOriented, reference, subjectFull, stem, out var topAdjust))
            {
                topAdjust = SneakersTopViewAlignService.RefineOnComposed(
                    fullOriented, reference, topAdjust, stem, alignedEdge);

                if (SneakersLayoutAlignGuard.MeetsComposedQualityRelaxed(
                        fullOriented, topAdjust, reference, alignedEdge, out var q))
                {
                    var zoneOk = SneakersTopViewAlignService.MeetsSafeZoneOnComposed(
                        fullOriented, topAdjust, reference, stem, alignedEdge);
                    var zoneNote = zoneOk ? "" : " zone-warn";
                    outcome = new AutoAlignOutcome(
                        topAdjust,
                        template,
                        detectSource,
                        $"layout {detectDetail} {SneakersTopViewComposition.DescribeLayout(stem)}{zoneNote} {q.Summary}");
                    return true;
                }

                detectDetail = $"{detectDetail}; topview-low-quality ({q.ScorePercent:0}%)";
            }
            else if (!bboxOk)
                detectDetail = $"{detectDetail}; bbox-мал/велик";
            else if (!poseOk)
                detectDetail = $"{detectDetail}; ракурс ≠ эталон";
            else
                detectDetail = $"{detectDetail}; topview-geom-fail";
        }

        if (!SubjectDetectionService.TryAnalyzeTargetOpenCvOnly(fullOriented, alignedEdge, out var target))
            return false;

        var policy = ResolveEditorAlignPolicy(stem);
        var centerCrop = policy == CompositionAlignPolicy.CenterInFrame;

        ManualShotAdjust adjust;
        var crop = AutoCropComputation.ComputeCropBox(reference, target, centerCrop);
        if (!TryAdjustFromCropBox(fullOriented, reference, crop, out adjust)
            && !TryComputeAdjust(reference, target.SubjectTarget, target.ImgW, target.ImgH, out adjust))
            return false;

        adjust = RefineIterativeToReferenceTemplate(
            fullOriented, reference, adjust, policy, alignedEdge, maxRefinePasses);

        if (!SneakersLayoutAlignGuard.MeetsComposedQuality(
                fullOriented, adjust, reference, stem, alignedEdge, out var fallbackQ))
            return false;

        outcome = new AutoAlignOutcome(
            adjust,
            template,
            SubjectDetectSource.OpenCv,
            $"{fallbackQ.Summary}");
        return true;
    }

    /// <summary>Редкие доп. проходы: превью → OpenCV-силуэт на composed → коррекция offset/zoom.</summary>
    private static ManualShotAdjust RefineIterativeToReferenceTemplate(
        MagickImage fullOriented,
        AutoCropComputation.ReferenceMetrics reference,
        ManualShotAdjust adjust,
        CompositionAlignPolicy policy,
        int analysisMaxEdge,
        int maxPasses)
    {
        if (policy == CompositionAlignPolicy.Skip || maxPasses < 1)
            return adjust;

        var refW = (int)Math.Round(reference.RefW);
        var refH = (int)Math.Round(reference.RefH);
        if (refW < 1 || refH < 1)
            return adjust;

        var detEdge = Math.Clamp(Math.Min(analysisMaxEdge, RefineComposeDetectMaxEdge), 256, RefineComposeDetectMaxEdge);

        var subjectRef = reference.SubjectRef;
        var desiredCx = policy == CompositionAlignPolicy.CenterInFrame ? reference.RefW * 0.5 : subjectRef.CenterX;
        var desiredCy = policy == CompositionAlignPolicy.CenterInFrame ? reference.RefH * 0.5 : subjectRef.CenterY;
        var desiredW = subjectRef.Width;
        var desiredH = subjectRef.Height;

        for (var pass = 0; pass < maxPasses; pass++)
        {
            using var composed = ManualShotAdjustApplier.ComposeFromFullToReference(
                fullOriented, adjust, refW, refH);
            var sub = EstimateSubjectOnComposed(composed, detEdge);

            var dx = desiredCx - sub.CenterX;
            var dy = desiredCy - sub.CenterY;
            var posOk = Math.Abs(dx) < PositionDonePx && Math.Abs(dy) < PositionDonePx;

            var widthRatio = sub.Width / Math.Max(1.0, desiredW);
            var heightRatio = sub.Height / Math.Max(1.0, desiredH);
            var sizeOk = Math.Abs(widthRatio - 1.0) < SizeDoneRatio
                         && Math.Abs(heightRatio - 1.0) < SizeDoneRatio;

            if (posOk && sizeOk)
                break;

            if (!posOk)
            {
                adjust.OffsetX += dx;
                adjust.OffsetY += dy;
            }

            if (!sizeOk)
            {
                var fix = Math.Sqrt(
                    (desiredW / Math.Max(1.0, sub.Width)) *
                    (desiredH / Math.Max(1.0, sub.Height)));
                if (fix is > 0.02 and < 50
                    && adjust.ZoomPercent < SneakersLayoutAlignGuard.MaxLayoutZoomPercent - 2)
                    adjust.ZoomPercent = Math.Clamp(
                        adjust.ZoomPercent * fix,
                        SneakersLayoutAlignGuard.MinLayoutZoomPercent,
                        SneakersLayoutAlignGuard.MaxLayoutZoomPercent);
            }

            adjust.OffsetX = Math.Clamp(adjust.OffsetX, -reference.RefW * 0.48, reference.RefW * 0.48);
            adjust.OffsetY = Math.Clamp(adjust.OffsetY, -reference.RefH * 0.48, reference.RefH * 0.48);
        }

        return adjust;
    }

    private static Box2d EstimateSubjectOnComposed(MagickImage composed, int composedDetectMaxEdge)
    {
        var edge = Math.Clamp(composedDetectMaxEdge, 256, RefineComposeDetectMaxEdge);
        using var analysis = AutoCropComputation.CloneResizedLongEdge(composed, edge);
        var scale = (double)composed.Width / analysis.Width;
        using var mat = MagickMatConverter.ToMatBgr(analysis);
        var box = ProductSubjectDetection.EstimateOpenCv(mat);
        return box.Scale(scale, scale);
    }

    public static bool TryAdjustFromCropBox(
        MagickImage fullOriented,
        AutoCropComputation.ReferenceMetrics reference,
        Box2d crop,
        out ManualShotAdjust adjust)
    {
        adjust = new ManualShotAdjust();
        var refW = reference.RefW;
        var refH = reference.RefH;
        var lw0 = (double)fullOriented.Width;
        var lh0 = (double)fullOriented.Height;

        if (refW < 1 || refH < 1 || lw0 < 1 || lh0 < 1 || crop.Width < 8 || crop.Height < 8)
            return false;

        var cover = Math.Max(refW / lw0, refH / lh0);
        var scaleTotal = Math.Min(refW / crop.Width, refH / crop.Height);
        if (scaleTotal < 1e-6)
            return false;

        var zoom = Math.Clamp(
            scaleTotal / cover * 100.0,
            SneakersLayoutAlignGuard.MinLayoutZoomPercent,
            SneakersLayoutAlignGuard.MaxLayoutZoomPercent);
        scaleTotal = cover * (zoom / 100.0);

        var lw = lw0 * scaleTotal;
        var lh = lh0 * scaleTotal;

        var offX = -crop.X * scaleTotal - (refW - lw) * 0.5;
        var offY = -crop.Y * scaleTotal - (refH - lh) * 0.5;

        offX = Math.Clamp(offX, -refW * 0.45, refW * 0.45);
        offY = Math.Clamp(offY, -refH * 0.45, refH * 0.45);

        adjust = new ManualShotAdjust
        {
            OffsetX = offX,
            OffsetY = offY,
            ZoomPercent = zoom,
            RotationDeg = 0,
            GridOverlay = ZonaGridOverlayKind.None
        };
        return true;
    }

    private static CompositionAlignPolicy ResolveEditorAlignPolicy(string? stem) =>
        NormalizeStem(stem) switch
        {
            "01" or "08" => CompositionAlignPolicy.CenterInFrame,
            "06" or "02" or "03" or "04" => CompositionAlignPolicy.MatchReference,
            _ => CompositionAlignPolicy.MatchReference
        };

    private static string? NormalizeStem(string? stem)
    {
        if (string.IsNullOrWhiteSpace(stem))
            return null;
        var s = stem.Trim();
        if (s.Length == 1 && char.IsDigit(s[0]))
            s = "0" + s;
        return int.TryParse(s, out var n) && n is >= 1 and <= 99 ? n.ToString("D2") : s.Length >= 2 ? s : s.PadLeft(2, '0');
    }

    public static bool TryComputeAdjust(
        AutoCropComputation.ReferenceMetrics reference,
        Box2d subjectTarget,
        double imgW,
        double imgH,
        out ManualShotAdjust adjust)
    {
        adjust = new ManualShotAdjust();
        var refW = reference.RefW;
        var refH = reference.RefH;
        if (refW < 1 || refH < 1 || imgW < 1 || imgH < 1)
            return false;

        var subjectRef = reference.SubjectRef;
        if (subjectTarget.Width < 4 || subjectTarget.Height < 4 || subjectRef.Width < 4 || subjectRef.Height < 4)
            return false;

        var cover = Math.Max(refW / imgW, refH / imgH);
        var zoomW = 100.0 * (subjectRef.Width / Math.Max(1.0, subjectTarget.Width)) / cover;
        var zoomH = 100.0 * (subjectRef.Height / Math.Max(1.0, subjectTarget.Height)) / cover;
        var zoom = Math.Clamp(
            Math.Sqrt(zoomW * zoomH),
            SneakersLayoutAlignGuard.MinLayoutZoomPercent,
            SneakersLayoutAlignGuard.MaxLayoutZoomPercent);

        var scaleTotal = cover * (zoom / 100.0);
        var lw = imgW * scaleTotal;
        var lh = imgH * scaleTotal;

        var offX = subjectRef.CenterX - subjectTarget.CenterX * scaleTotal - (refW - lw) * 0.5;
        var offY = subjectRef.CenterY - subjectTarget.CenterY * scaleTotal - (refH - lh) * 0.5;

        offX = Math.Clamp(offX, -refW * 0.45, refW * 0.45);
        offY = Math.Clamp(offY, -refH * 0.45, refH * 0.45);

        adjust = new ManualShotAdjust
        {
            OffsetX = offX,
            OffsetY = offY,
            ZoomPercent = zoom,
            RotationDeg = 0,
            GridOverlay = ZonaGridOverlayKind.None
        };
        return true;
    }
}
