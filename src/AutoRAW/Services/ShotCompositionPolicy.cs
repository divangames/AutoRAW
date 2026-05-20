using AutoRAW.Models;

namespace AutoRAW.Services;

/// <summary>Правила композиции по номеру выходного кадра (01…08).</summary>
public static class ShotCompositionPolicy
{
    public static CompositionAlignPolicy GetAlignPolicy(string? outputStem) =>
        NormalizeStem(outputStem) switch
        {
            "05" or "07" => CompositionAlignPolicy.Skip,
            "01" or "08" => CompositionAlignPolicy.CenterInFrame,
            "06" => CompositionAlignPolicy.Skip,
            _ => CompositionAlignPolicy.MatchReference
        };

    public static bool UseCenteredCropGeometry(string? outputStem) =>
        GetAlignPolicy(outputStem) == CompositionAlignPolicy.CenterInFrame;

    private static string? NormalizeStem(string? stem)
    {
        if (string.IsNullOrWhiteSpace(stem))
            return null;
        var s = stem.Trim();
        return s.Length >= 2 ? s : s.PadLeft(2, '0');
    }
}
