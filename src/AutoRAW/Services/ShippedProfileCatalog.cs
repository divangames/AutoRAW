using System.Text.Json;
using System.Text.Json.Serialization;
using AutoRAW.Models;

namespace AutoRAW.Services;

/// <summary>
/// Профили из комплекта приложения: для каждого подкаталога <c>profiles\&lt;slug&gt;\profile.json</c>
/// данные берутся из <c>reference\&lt;slug&gt;</c> и <c>zona\&lt;slug&gt;</c> рядом с exe (имя папки = slug).
/// </summary>
public static class ShippedProfileCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class ManifestDto
    {
        public string DisplayName { get; set; } = string.Empty;
        public ColorRowDto? Color { get; set; }
    }

    public static IReadOnlyList<ProductProfile> LoadAll()
    {
        var root = AppPaths.ShippedProfilesCatalogRoot;
        if (!Directory.Exists(root))
            return [];

        var list = new List<ProductProfile>();
        foreach (var dir in Directory.GetDirectories(root))
        {
            var p = TryLoad(dir);
            if (p is not null)
                list.Add(p);
        }

        list.Sort(CompareShippedOrder);
        return list;
    }

    private static int CompareShippedOrder(ProductProfile a, ProductProfile b)
    {
        var sa = AppPaths.ReferencesBuiltInSneakersFolders(a);
        var sb = AppPaths.ReferencesBuiltInSneakersFolders(b);
        if (sa != sb)
            return sa ? -1 : 1;
        return string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase);
    }

    public static ProductProfile? TryLoad(string bundleDirectory)
    {
        try
        {
            var slug = Path.GetFileName(bundleDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(slug))
                return null;

            var manifestPath = Path.Combine(bundleDirectory, "profile.json");
            if (!File.Exists(manifestPath))
                return null;

            var json = File.ReadAllText(manifestPath);
            var m = JsonSerializer.Deserialize<ManifestDto>(json, JsonOptions);
            if (m is null || string.IsNullOrWhiteSpace(m.DisplayName))
                return null;

            var refDir = Path.Combine(AppPaths.AppRoot, "reference", slug);
            var zonaDir = Path.Combine(AppPaths.AppRoot, "zona", slug);
            if (!Directory.Exists(refDir) || !Directory.Exists(zonaDir))
                return null;

            var color = ColorCorrectionSettings.FromDto(m.Color);
            color = NormalizeShippedColorPaths(bundleDirectory, color);

            return new ProductProfile(m.DisplayName.Trim(), refDir, zonaDir, color);
        }
        catch
        {
            return null;
        }
    }

    private static ColorCorrectionSettings NormalizeShippedColorPaths(string bundleDirectory, ColorCorrectionSettings color)
    {
        var xmp = color.XmpFilePath;
        if (string.IsNullOrWhiteSpace(xmp))
            return color with { XmpFilePath = null };

        if (Path.IsPathFullyQualified(xmp))
            return color;

        var resolved = Path.GetFullPath(Path.Combine(bundleDirectory, xmp.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        return color with { XmpFilePath = File.Exists(resolved) ? resolved : color.XmpFilePath };
    }
}
