using AutoRAW.Models;
using ImageMagick;
using ImageMagick.Drawing;

namespace AutoRAW.Services;

/// <summary>
/// «Железные» границы кадра из макета 1400×1050 (без <c>NN_line.png</c>).
/// </summary>
public readonly record struct SneakersLayoutSafeZone(
    double LeftLine,
    double RightLine,
    double BottomLine,
    double? TopLine,
    double SafeLeft,
    double SafeRight,
    double SafeBottom,
    double? SafeTop,
    double TargetCenterX,
    double TargetBottom,
    double ZoneWidth)
{
    private const double InnerMarginPx = 3.0;

    public static bool TryGet(string? outputStem, double refW, double refH, out SneakersLayoutSafeZone zone)
    {
        zone = default;
        if (refW < 1 || refH < 1 || !SneakersTopViewComposition.UsesTopViewLayout(outputStem))
            return false;

        if (SneakersTopViewComposition.UsesHeightCenteredLayout(outputStem))
        {
            var (targetH, marginTop, marginBottom) =
                SneakersTopViewComposition.GetHeightCenteredTargets(refH);
            var topLine = marginTop;
            var bottomLine = refH - marginBottom;
            var centerX = refW * 0.5;
            zone = new SneakersLayoutSafeZone(
                LeftLine: 0,
                RightLine: refW,
                BottomLine: bottomLine,
                TopLine: topLine,
                SafeLeft: InnerMarginPx,
                SafeRight: refW - InnerMarginPx,
                SafeBottom: bottomLine - InnerMarginPx,
                SafeTop: topLine + InnerMarginPx,
                TargetCenterX: centerX,
                TargetBottom: bottomLine - InnerMarginPx,
                ZoneWidth: refW - 2 * InnerMarginPx);
            return true;
        }

        var (targetW, targetLeft, targetBottom) =
            SneakersTopViewComposition.GetWidthTargets(outputStem, refW, refH);
        var leftLine = targetLeft;
        var rightLine = targetLeft + targetW;
        var centerX2 = (leftLine + rightLine) * 0.5;

        zone = new SneakersLayoutSafeZone(
            LeftLine: leftLine,
            RightLine: rightLine,
            BottomLine: targetBottom,
            TopLine: null,
            SafeLeft: leftLine + InnerMarginPx,
            SafeRight: rightLine - InnerMarginPx,
            SafeBottom: targetBottom - InnerMarginPx,
            SafeTop: null,
            TargetCenterX: centerX2,
            TargetBottom: targetBottom - InnerMarginPx,
            ZoneWidth: targetW - 2 * InnerMarginPx);
        return zone.ZoneWidth > 16;
    }

    public static void DrawRulesOverlay(MagickImage frame, string? outputStem)
    {
        if (!TryGet(outputStem, frame.Width, frame.Height, out var zone))
            return;

        var w = (int)frame.Width;
        var h = (int)frame.Height;
        var red = new MagickColor("#E53935");
        const int thickness = 2;

        var draw = new Drawables()
            .StrokeColor(red)
            .FillColor(MagickColors.Transparent)
            .StrokeWidth(thickness)
            .StrokeLineCap(LineCap.Round);

        if (zone.LeftLine > 1 && zone.RightLine < w - 1)
        {
            var lx = zone.LeftLine;
            var rx = zone.RightLine;
            draw.Line(lx, 0, lx, h).Line(rx, 0, rx, h);
        }

        if (zone.TopLine is { } top)
            draw.Line(0, top, w, top);

        draw.Line(0, zone.BottomLine, w, zone.BottomLine);
        frame.Draw(draw);
    }

    public bool SubjectInside(Box2d sub, double tolerancePx = 4)
    {
        if (sub.Width < 4 || sub.Height < 4)
            return false;

        if (LeftLine > 1 && sub.X < SafeLeft - tolerancePx)
            return false;
        if (RightLine < 1e6 && sub.Right > SafeRight + tolerancePx)
            return false;
        if (sub.Bottom > SafeBottom + tolerancePx)
            return false;
        if (SafeTop is { } top && sub.Y < top - tolerancePx)
            return false;

        return true;
    }

    public double ZoneCorrectionX(Box2d sub)
    {
        var dx = 0.0;
        if (LeftLine > 1 && sub.X < SafeLeft)
            dx += SafeLeft - sub.X;
        if (RightLine < 1e6 && sub.Right > SafeRight)
            dx -= sub.Right - SafeRight;
        return dx;
    }
}
