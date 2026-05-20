namespace AutoRAW.Services;

/// <summary>
/// Номер кадра 1…8 → метка референса и выхода <c>01</c>…<c>08</c> (Стандартный, Руслан).
/// </summary>
public static class RuslanShotMapping
{
    public const int ShotCount = InputShotNumberParser.MaxShot;

    public static string? GetReferenceLabelForShotNumber(int shotNumber)
    {
        if (shotNumber < InputShotNumberParser.MinShot || shotNumber > ShotCount)
            return null;
        return shotNumber.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
    }

    public static string? ResolveReferenceFileName(IEnumerable<string> referenceFileNames, string label)
    {
        foreach (var name in referenceFileNames)
        {
            if (string.Equals(Path.GetFileNameWithoutExtension(name), label, StringComparison.OrdinalIgnoreCase))
                return name;
        }

        return null;
    }
}
