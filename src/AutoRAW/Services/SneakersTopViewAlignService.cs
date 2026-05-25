using AutoRAW.Models;
using AutoRAW.Services.SubjectDetection;
using ImageMagick;

namespace AutoRAW.Services;

/// <summary>
/// Подгонка по макету 1400×1050: центр между вертикалями, низ у нижней линии, товар в safe zone.
/// </summary>
public static class SneakersTopViewAlignService
{
    private const int MaxRefinePasses = 1;
    private const double PositionDonePx = 3.0;

    public static bool TryComputeAdjust(
        MagickImage fullOriented,
        AutoCropComputation.ReferenceMetrics reference,
        SubjectOnImage subject,
        string? outputStem,
        out ManualShotAdjust adjust)
    {
        if (!SneakersLayoutSafeZone.TryGet(outputStem, reference.RefW, reference.RefH, out var zone))
        {
            adjust = default;
            return false;
        }

        return SneakersTopViewComposition.UsesHeightCenteredLayout(outputStem)
            ? TryComputeHeight(fullOriented, reference, subject, zone, out adjust)
            : TryComputeWidth(fullOriented, reference, subject, zone, out adjust);
    }

    public static ManualShotAdjust RefineOnComposed(
        MagickImage fullOriented,
        AutoCropComputation.ReferenceMetrics reference,
        ManualShotAdjust adjust,
        string? outputStem,
        int analysisMaxEdge)
    {
        if (!SneakersLayoutSafeZone.TryGet(outputStem, reference.RefW, reference.RefH, out var zone))
            return adjust;

        return SneakersTopViewComposition.UsesHeightCenteredLayout(outputStem)
            ? RefineHeight(fullOriented, reference, adjust, zone, outputStem, analysisMaxEdge)
            : RefineWidth(fullOriented, reference, adjust, zone, analysisMaxEdge);
    }

    private static bool TryComputeWidth(
        MagickImage fullOriented,
        AutoCropComputation.ReferenceMetrics reference,
        SubjectOnImage subject,
        SneakersLayoutSafeZone zone,
        out ManualShotAdjust adjust)
    {
        adjust = new ManualShotAdjust();
        var refW = reference.RefW;
        var refH = reference.RefH;
        var imgW = (double)fullOriented.Width;
        var imgH = (double)fullOriented.Height;
        var box = subject.Box;

        if (box.Width < 8 || box.Height < 8)
            return false;

        var targetW = zone.ZoneWidth;
        var bx = box.X;
        var by = box.Y;
        var bw = box.Width;
        var bh = box.Height;
        var cx = subject.CenterX;

        var cover = Math.Max(refW / imgW, refH / imgH);
        var zoomRaw = targetW / bw / cover * 100.0;
        if (!TryClampZoom(zoomRaw, out var zoom))
            return false;

        var scaleTotal = cover * (zoom / 100.0);
        var lw = imgW * scaleTotal;
        var lh = imgH * scaleTotal;

        var offX = zone.TargetCenterX - cx * scaleTotal - (refW - lw) * 0.5;
        var offY = zone.TargetBottom - (by + bh) * scaleTotal - (refH - lh) * 0.5;

        if (!TryFinalizeOffsets(refW, refH, offX, offY, out offX, out offY))
            return false;

        adjust = new ManualShotAdjust
        {
            OffsetX = offX,
            OffsetY = offY,
            ZoomPercent = zoom,
            RotationDeg = 0,
            GridOverlay = ZonaGridOverlayKind.LayoutRules
        };
        return true;
    }

    private static bool TryComputeHeight(
        MagickImage fullOriented,
        AutoCropComputation.ReferenceMetrics reference,
        SubjectOnImage subject,
        SneakersLayoutSafeZone zone,
        out ManualShotAdjust adjust)
    {
        adjust = new ManualShotAdjust();
        var refW = reference.RefW;
        var refH = reference.RefH;
        var imgW = (double)fullOriented.Width;
        var imgH = (double)fullOriented.Height;
        var box = subject.Box;

        var (layoutH, _, _) = SneakersTopViewComposition.GetHeightCenteredTargets(refH);
        var targetH = zone.TopLine.HasValue && zone.SafeTop.HasValue
            ? zone.SafeBottom - zone.SafeTop.Value
            : layoutH;

        var bx = box.X;
        var by = box.Y;
        var bw = box.Width;
        var bh = box.Height;
        var cx = subject.CenterX;

        var cover = Math.Max(refW / imgW, refH / imgH);
        var zoomRaw = targetH / bh / cover * 100.0;
        if (!TryClampZoom(zoomRaw, out var zoom))
            return false;

        var scaleTotal = cover * (zoom / 100.0);
        var lw = imgW * scaleTotal;
        var lh = imgH * scaleTotal;

        var offX = zone.TargetCenterX - cx * scaleTotal - (refW - lw) * 0.5;
        var offY = zone.TargetBottom - (by + bh) * scaleTotal - (refH - lh) * 0.5;

        if (!TryFinalizeOffsets(refW, refH, offX, offY, out offX, out offY))
            return false;

        adjust = new ManualShotAdjust
        {
            OffsetX = offX,
            OffsetY = offY,
            ZoomPercent = zoom,
            RotationDeg = 0,
            GridOverlay = ZonaGridOverlayKind.LayoutRules
        };
        return true;
    }

    private static ManualShotAdjust RefineWidth(
        MagickImage fullOriented,
        AutoCropComputation.ReferenceMetrics reference,
        ManualShotAdjust adjust,
        SneakersLayoutSafeZone zone,
        int analysisMaxEdge)
    {
        var refW = (int)Math.Round(reference.RefW);
        var refH = (int)Math.Round(reference.RefH);

        for (var pass = 0; pass < MaxRefinePasses; pass++)
        {
            using var composed = ManualShotAdjustApplier.ComposeFromFullToReference(
                fullOriented, adjust, refW, refH);
            var sub = EstimateSubject(composed, analysisMaxEdge);

            var centerErr = zone.TargetCenterX - sub.CenterX;
            var bottomErr = zone.TargetBottom - sub.Bottom;
            var zoneDx = zone.ZoneCorrectionX(sub);

            if (Math.Abs(centerErr) < PositionDonePx
                && Math.Abs(bottomErr) < PositionDonePx
                && Math.Abs(zoneDx) < PositionDonePx)
                break;

            adjust = adjust.Clone();
            adjust.OffsetX += centerErr + zoneDx;
            adjust.OffsetY += bottomErr;

            if (sub.Width > 1 && Math.Abs(sub.Width - zone.ZoneWidth) > zone.ZoneWidth * 0.03
                && adjust.ZoomPercent < SneakersLayoutAlignGuard.MaxLayoutZoomPercent - 2)
            {
                var fix = zone.ZoneWidth / sub.Width;
                adjust.ZoomPercent = Math.Clamp(
                    adjust.ZoomPercent * fix,
                    SneakersLayoutAlignGuard.MinLayoutZoomPercent,
                    SneakersLayoutAlignGuard.MaxLayoutZoomPercent);
            }

            ClampAdjust(ref adjust, reference);
        }

        return adjust;
    }

    private static ManualShotAdjust RefineHeight(
        MagickImage fullOriented,
        AutoCropComputation.ReferenceMetrics reference,
        ManualShotAdjust adjust,
        SneakersLayoutSafeZone zone,
        string? outputStem,
        int analysisMaxEdge)
    {
        var refW = (int)Math.Round(reference.RefW);
        var refH = (int)Math.Round(reference.RefH);
        var (layoutH, _, _) = SneakersTopViewComposition.GetHeightCenteredTargets(reference.RefH);
        var targetH = zone.TopLine.HasValue && zone.SafeTop.HasValue
            ? zone.SafeBottom - zone.SafeTop.Value
            : layoutH;

        for (var pass = 0; pass < MaxRefinePasses; pass++)
        {
            using var composed = ManualShotAdjustApplier.ComposeFromFullToReference(
                fullOriented, adjust, refW, refH);
            var sub = EstimateSubject(composed, analysisMaxEdge);

            var centerErr = zone.TargetCenterX - sub.CenterX;
            var bottomErr = zone.TargetBottom - sub.Bottom;
            var zoneDx = zone.ZoneCorrectionX(sub);
            var topErr = zone.SafeTop.HasValue ? zone.SafeTop.Value - sub.Y : 0;

            if (Math.Abs(centerErr) < PositionDonePx
                && Math.Abs(bottomErr) < PositionDonePx
                && Math.Abs(zoneDx) < PositionDonePx
                && Math.Abs(topErr) < PositionDonePx)
                break;

            adjust = adjust.Clone();
            adjust.OffsetX += centerErr + zoneDx;
            adjust.OffsetY += bottomErr;

            if (zone.SafeTop.HasValue && Math.Abs(topErr) > PositionDonePx)
                adjust.OffsetY += topErr * 0.5;

            if (sub.Height > 1 && Math.Abs(sub.Height - targetH) > targetH * 0.03
                && adjust.ZoomPercent < SneakersLayoutAlignGuard.MaxLayoutZoomPercent - 2)
            {
                var fix = targetH / sub.Height;
                adjust.ZoomPercent = Math.Clamp(
                    adjust.ZoomPercent * fix,
                    SneakersLayoutAlignGuard.MinLayoutZoomPercent,
                    SneakersLayoutAlignGuard.MaxLayoutZoomPercent);
            }

            ClampAdjust(ref adjust, reference);
        }

        return adjust;
    }

    public static bool MeetsSafeZoneOnComposed(
        MagickImage fullOriented,
        ManualShotAdjust adjust,
        AutoCropComputation.ReferenceMetrics reference,
        string? outputStem,
        int analysisMaxEdge)
    {
        if (!SneakersLayoutSafeZone.TryGet(outputStem, reference.RefW, reference.RefH, out var zone))
            return false;

        var refW = (int)Math.Round(reference.RefW);
        var refH = (int)Math.Round(reference.RefH);
        using var composed = ManualShotAdjustApplier.ComposeFromFullToReference(
            fullOriented, adjust, refW, refH);
        var sub = EstimateSubject(composed, analysisMaxEdge);
        return zone.SubjectInside(sub);
    }

    private static bool TryClampZoom(double zoomRaw, out double zoom)
    {
        if (zoomRaw < SneakersLayoutAlignGuard.MinLayoutZoomPercent)
        {
            zoom = 0;
            return false;
        }

        zoom = Math.Clamp(zoomRaw, SneakersLayoutAlignGuard.MinLayoutZoomPercent, SneakersLayoutAlignGuard.MaxLayoutZoomPercent);
        return true;
    }

    private static bool TryFinalizeOffsets(
        double refW, double refH, double offX, double offY, out double x, out double y)
    {
        if (Math.Abs(offX) > refW * 0.32 || Math.Abs(offY) > refH * 0.32)
        {
            x = y = 0;
            return false;
        }

        x = Math.Clamp(offX, -refW * 0.48, refW * 0.48);
        y = Math.Clamp(offY, -refH * 0.48, refH * 0.48);
        return true;
    }

    private static void ClampAdjust(ref ManualShotAdjust adjust, AutoCropComputation.ReferenceMetrics reference)
    {
        adjust.OffsetX = Math.Clamp(adjust.OffsetX, -reference.RefW * 0.48, reference.RefW * 0.48);
        adjust.OffsetY = Math.Clamp(adjust.OffsetY, -reference.RefH * 0.48, reference.RefH * 0.48);
    }

    private static Box2d EstimateSubject(MagickImage composed, int analysisMaxEdge)
    {
        var edge = Math.Clamp(Math.Min(analysisMaxEdge, 1024), 256, 1024);
        using var small = AutoCropComputation.CloneResizedLongEdge(composed, edge);
        var sc = (double)composed.Width / small.Width;
        using var mat = MagickMatConverter.ToMatBgr(small);
        var box = ProductSubjectDetection.EstimateOpenCv(mat);
        return box.Scale(sc, sc);
    }
}
