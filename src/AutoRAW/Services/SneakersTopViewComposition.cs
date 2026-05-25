using AutoRAW.Models;

namespace AutoRAW.Services;

/// <summary>
/// Фиксированная композиция по макету 1400×1050 для кадров 01–04, 06, 08 (см. Instruction.md: смысл ракурсов 01–08).
/// </summary>
public static class SneakersTopViewComposition
{
    public const double DesignRefW = 1400;
    public const double DesignRefH = 1050;

    private enum LayoutKind
    {
        WidthBottomLeft,
        HeightCenteredMargins
    }

    private readonly record struct LayoutSpec(
        LayoutKind Kind,
        double SizePx,
        double MarginLeftOrTopPx,
        double MarginBottomPx);

    private static readonly LayoutSpec Layout01 = new(LayoutKind.WidthBottomLeft, 965, 223, 185);
    private static readonly LayoutSpec Layout020408 = new(LayoutKind.WidthBottomLeft, 965, 223, 265);
    private static readonly LayoutSpec Layout06 = new(LayoutKind.HeightCenteredMargins, 897, 76, 76);

    private static readonly Dictionary<string, LayoutSpec> ByStem = new(StringComparer.OrdinalIgnoreCase)
    {
        ["01"] = Layout01,
        ["02"] = Layout020408,
        ["03"] = Layout020408,
        ["04"] = Layout020408,
        ["06"] = Layout06,
        ["08"] = Layout020408,
    };

    public static string DescribeLayout(string? outputStem)
    {
        var stem = NormalizeStem(outputStem) ?? "?";
        var layout = GetLayout(stem);
        return layout.Kind switch
        {
            LayoutKind.HeightCenteredMargins =>
                $"H{layout.SizePx:0} T{layout.MarginLeftOrTopPx:0} B{layout.MarginBottomPx:0} center",
            _ => $"W{layout.SizePx:0} L{layout.MarginLeftOrTopPx:0} B{layout.MarginBottomPx:0}"
        };
    }

    public static bool UsesTopViewLayout(string? outputStem)
    {
        var stem = NormalizeStem(outputStem);
        return stem is "01" or "02" or "03" or "04" or "06" or "08";
    }

    public static bool UsesHeightCenteredLayout(string? outputStem) =>
        NormalizeStem(outputStem) == "06";

    public static bool IsCompatibleTopView(
        AutoCropComputation.ReferenceMetrics reference,
        Box2d subjectOnFull,
        double imgW,
        double imgH,
        string? outputStem = null)
    {
        if (UsesHeightCenteredLayout(outputStem))
            return IsCompatibleTopView06(reference, subjectOnFull, imgW, imgH);

        return IsCompatibleTopViewWide(reference, subjectOnFull, imgW, imgH);
    }

    /// <summary>01–04, 08: товар шире, чем выше.</summary>
    private static bool IsCompatibleTopViewWide(
        AutoCropComputation.ReferenceMetrics reference,
        Box2d subjectOnFull,
        double imgW,
        double imgH)
    {
        var refBox = reference.SubjectRef;
        if (refBox.Width < 8 || refBox.Height < 8 || subjectOnFull.Width < 8 || subjectOnFull.Height < 8)
            return false;

        if (imgW < 1 || imgH < 1)
            return false;

        if (refBox.Width < refBox.Height * 0.92)
            return false;

        if (subjectOnFull.Height > subjectOnFull.Width * 1.03)
            return false;

        var refWFrac = refBox.Width / reference.RefW;
        var subWFrac = subjectOnFull.Width / imgW;
        var fracRatio = subWFrac / Math.Max(1e-6, refWFrac);
        if (fracRatio is < 0.38 or > 2.6)
            return false;

        var refAr = refBox.Width / refBox.Height;
        var subAr = subjectOnFull.Width / subjectOnFull.Height;
        var arRatio = subAr / Math.Max(1e-6, refAr);
        return arRatio is >= 0.42 and <= 2.35;
    }

    /// <summary>06: вид сверху, пара кроссовок — обычно выше, чем шире.</summary>
    private static bool IsCompatibleTopView06(
        AutoCropComputation.ReferenceMetrics reference,
        Box2d subjectOnFull,
        double imgW,
        double imgH)
    {
        var refBox = reference.SubjectRef;
        if (refBox.Width < 8 || refBox.Height < 8 || subjectOnFull.Width < 8 || subjectOnFull.Height < 8)
            return false;

        if (imgW < 1 || imgH < 1)
            return false;

        if (refBox.Height < refBox.Width * 0.88)
            return false;

        if (subjectOnFull.Width > subjectOnFull.Height * 1.08)
            return false;

        var refHFrac = refBox.Height / reference.RefH;
        var subHFrac = subjectOnFull.Height / imgH;
        var fracRatio = subHFrac / Math.Max(1e-6, refHFrac);
        if (fracRatio is < 0.32 or > 2.8)
            return false;

        var refAr = refBox.Height / refBox.Width;
        var subAr = subjectOnFull.Height / subjectOnFull.Width;
        var arRatio = subAr / Math.Max(1e-6, refAr);
        return arRatio is >= 0.4 and <= 2.5;
    }

    /// <summary>Кадры 01–04, 08: ширина товара, отступ слева и снизу.</summary>
    public static (double TargetWidth, double TargetLeft, double TargetBottom) GetWidthTargets(
        string? outputStem,
        double refW,
        double refH)
    {
        var layout = GetLayout(outputStem);
        if (layout.Kind != LayoutKind.WidthBottomLeft)
            layout = Layout020408;

        var sx = refW / DesignRefW;
        var sy = refH / DesignRefH;
        var targetW = layout.SizePx * sx;
        var targetLeft = layout.MarginLeftOrTopPx * sx;
        var targetBottom = refH - layout.MarginBottomPx * sy;
        return (targetW, targetLeft, targetBottom);
    }

    /// <summary>Кадр 06: высота 897, отступы 76 сверху и снизу, по X — центр.</summary>
    public static (double TargetHeight, double MarginTop, double MarginBottom) GetHeightCenteredTargets(
        double refH)
    {
        var layout = Layout06;
        var sy = refH / DesignRefH;
        return (layout.SizePx * sy, layout.MarginLeftOrTopPx * sy, layout.MarginBottomPx * sy);
    }

    private static LayoutSpec GetLayout(string? outputStem)
    {
        var stem = NormalizeStem(outputStem);
        return stem is not null && ByStem.TryGetValue(stem, out var spec) ? spec : Layout020408;
    }

    private static string? NormalizeStem(string? stem)
    {
        if (string.IsNullOrWhiteSpace(stem))
            return null;
        var s = stem.Trim();
        if (s.Length == 1 && char.IsDigit(s[0]))
            s = "0" + s;
        return int.TryParse(s, out var n) && n is >= 1 and <= 99 ? n.ToString("D2") : s;
    }
}
