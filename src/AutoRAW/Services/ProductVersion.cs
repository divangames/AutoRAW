namespace AutoRAW.Services;

/// <summary>
/// Версия продукта из <c>CHANGELOG.md</c>: пять неотрицательных компонентов.
/// 1 — тотальный релиз, 2 — крупные изменения, 3 — значительные, 4 — простые изменения, 5 — мелкие (в т.ч. фиксы).
/// </summary>
public readonly struct ProductVersion : IComparable<ProductVersion>, IEquatable<ProductVersion>
{
    public ProductVersion(int part1, int part2, int part3, int part4, int part5)
    {
        Validate(part1);
        Validate(part2);
        Validate(part3);
        Validate(part4);
        Validate(part5);
        Part1 = part1;
        Part2 = part2;
        Part3 = part3;
        Part4 = part4;
        Part5 = part5;
    }

    public int Part1 { get; }
    public int Part2 { get; }
    public int Part3 { get; }
    public int Part4 { get; }
    public int Part5 { get; }

    /// <summary>Первые четыре компонента для <see cref="System.Version"/> (CLR).</summary>
    public Version ToAssemblyVersion() => new(Part1, Part2, Part3, Part4);

    public static bool TryParse(string? s, out ProductVersion v)
    {
        v = default;
        if (string.IsNullOrWhiteSpace(s))
            return false;
        var t = s.Trim();
        var plus = t.IndexOf('+');
        if (plus >= 0)
            t = t[..plus].TrimEnd();

        var segments = t.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length is < 1 or > 5)
            return false;

        var nums = new int[5];
        for (var i = 0; i < segments.Length; i++)
        {
            if (!int.TryParse(segments[i], out var n) || n < 0 || n > ushort.MaxValue)
                return false;
            nums[i] = n;
        }

        v = new ProductVersion(nums[0], nums[1], nums[2], nums[3], nums[4]);
        return true;
    }

    public int CompareTo(ProductVersion other)
    {
        var c = Part1.CompareTo(other.Part1);
        if (c != 0) return c;
        c = Part2.CompareTo(other.Part2);
        if (c != 0) return c;
        c = Part3.CompareTo(other.Part3);
        if (c != 0) return c;
        c = Part4.CompareTo(other.Part4);
        if (c != 0) return c;
        return Part5.CompareTo(other.Part5);
    }

    public bool Equals(ProductVersion other) => CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is ProductVersion o && Equals(o);

    public override int GetHashCode() => HashCode.Combine(Part1, Part2, Part3, Part4, Part5);

    public override string ToString() => $"{Part1}.{Part2}.{Part3}.{Part4}.{Part5}";

    public static bool operator >(ProductVersion a, ProductVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(ProductVersion a, ProductVersion b) => a.CompareTo(b) < 0;
    public static bool operator >=(ProductVersion a, ProductVersion b) => a.CompareTo(b) >= 0;
    public static bool operator <=(ProductVersion a, ProductVersion b) => a.CompareTo(b) <= 0;

    private static void Validate(int n)
    {
        if (n < 0 || n > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(n), n, "Ожидается 0..65535.");
    }
}
