using System.Text.RegularExpressions;

namespace AutoRAW.Services;

/// <summary>Номер кадра (1…8) из имени входного файла: <c>1.NEF</c>, <c>тип3</c>, <c>type_5</c>, <c>01</c>.</summary>
public static partial class InputShotNumberParser
{
    public const int MinShot = 1;
    public const int MaxShot = 8;

    public static bool TryParse(string inputPath, out int shotNumber)
    {
        shotNumber = 0;
        var stem = Path.GetFileNameWithoutExtension(inputPath);
        if (string.IsNullOrWhiteSpace(stem))
            return false;

        stem = stem.Trim();

        if (TryParseNumericStem(stem, out shotNumber))
            return true;

        var typeMatch = TypeKeywordRegex().Match(stem);
        if (typeMatch.Success && TryClamp(typeMatch.Groups[1].Value, out shotNumber))
            return true;

        var trailing = TrailingNumberRegex().Match(stem);
        if (trailing.Success && TryClamp(trailing.Groups[1].Value, out shotNumber))
            return true;

        var leading = LeadingNumberRegex().Match(stem);
        if (leading.Success && TryClamp(leading.Groups[1].Value, out shotNumber))
            return true;

        return false;
    }

    private static bool TryParseNumericStem(string stem, out int shotNumber)
    {
        shotNumber = 0;
        if (!int.TryParse(stem, out var n))
            return false;
        return TryClamp(n, out shotNumber);
    }

    private static bool TryClamp(int value, out int shotNumber)
    {
        if (value < MinShot || value > MaxShot)
        {
            shotNumber = 0;
            return false;
        }

        shotNumber = value;
        return true;
    }

    private static bool TryClamp(string digits, out int shotNumber)
    {
        shotNumber = 0;
        return int.TryParse(digits, out var n) && TryClamp(n, out shotNumber);
    }

    [GeneratedRegex(@"(?:тип|type)[\s_\-\.]*(\d{1,2})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TypeKeywordRegex();

    [GeneratedRegex(@"(?:^|[\s_\-])(\d{1,2})$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingNumberRegex();

    [GeneratedRegex(@"^(\d{1,2})(?:[\s_\-]|$)", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingNumberRegex();
}
