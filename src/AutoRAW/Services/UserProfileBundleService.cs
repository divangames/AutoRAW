using System.Text.Json;
using System.Text.Json.Serialization;
using AutoRAW.Models;

namespace AutoRAW.Services;

/// <summary>
/// Профиль как папка под <see cref="AppPaths.UserProfilesRoot"/>:
/// reference\имя\, zona\имя\ (технология «Zona»), setting (XMP), profile.json.
/// </summary>
public static class UserProfileBundleService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public sealed class Manifest
    {
        public string DisplayName { get; set; } = string.Empty;
        public ColorRowDto? Color { get; set; }
    }

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(AppPaths.UserProfilesRoot);
    }

    public static string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var s = new string(chars);
        if (string.IsNullOrWhiteSpace(s))
            s = "Profile";
        return s.Length > 80 ? s[..80] : s;
    }

    public static string GetBundlePath(string displayName) =>
        Path.Combine(AppPaths.UserProfilesRoot, SanitizeFolderName(displayName));

    public static void CopyDirectory(string sourceDir, string destDir)
    {
        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException(sourceDir);
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var name = Path.GetFileName(file);
            File.Copy(file, Path.Combine(destDir, name), overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var name = Path.GetFileName(dir);
            CopyDirectory(dir, Path.Combine(destDir, name));
        }
    }

    /// <summary>
    /// Записывает профиль: копирует reference и zona (Zona) в подкаталоги с именем профиля,
    /// XMP в setting, манифест. Структура: reference\имя\, zona\имя\ (как reference\Sneakers у встроенного профиля).
    /// </summary>
    public static Task<ProductProfile> WriteBundleAsync(
        string displayName,
        string sourceReferenceDir,
        string sourceZonaDir,
        ColorCorrectionSettings color) =>
        Task.Run(() => WriteBundle(displayName, sourceReferenceDir, sourceZonaDir, color));

    public static ProductProfile WriteBundle(string displayName, string sourceReferenceDir, string sourceZonaDir, ColorCorrectionSettings color)
    {
        EnsureDirectories();
        var bundle = GetBundlePath(displayName);
        Directory.CreateDirectory(bundle);

        var slug = SanitizeFolderName(displayName);
        var refDest = Path.Combine(bundle, "reference", slug);
        var zonaDest = Path.Combine(bundle, "zona", slug);
        var settingDest = Path.Combine(bundle, "setting");

        if (Directory.Exists(refDest))
            Directory.Delete(refDest, recursive: true);
        if (Directory.Exists(zonaDest))
            Directory.Delete(zonaDest, recursive: true);
        Directory.CreateDirectory(settingDest);

        CopyDirectory(sourceReferenceDir, refDest);
        CopyDirectory(sourceZonaDir, zonaDest);

        string? xmpInBundle = null;
        if (!string.IsNullOrWhiteSpace(color.XmpFilePath) && File.Exists(color.XmpFilePath))
        {
            var xmpName = Path.GetFileName(color.XmpFilePath);
            if (!string.IsNullOrEmpty(xmpName))
            {
                var destXmp = Path.Combine(settingDest, xmpName);
                File.Copy(color.XmpFilePath, destXmp, overwrite: true);
                xmpInBundle = destXmp;
            }
        }

        var colorForProfile = string.IsNullOrEmpty(xmpInBundle)
            ? color with { XmpFilePath = null }
            : color with { XmpFilePath = xmpInBundle };

        var manifest = new Manifest
        {
            DisplayName = displayName.Trim(),
            Color = colorForProfile.ToDto()
        };
        File.WriteAllText(Path.Combine(bundle, "profile.json"), JsonSerializer.Serialize(manifest, JsonOptions));

        return new ProductProfile(
            manifest.DisplayName,
            refDest,
            zonaDest,
            ColorCorrectionSettings.FromDto(manifest.Color));
    }

    public static ProductProfile? TryLoadBundle(string bundleDirectory)
    {
        try
        {
            var manifestPath = Path.Combine(bundleDirectory, "profile.json");
            if (!File.Exists(manifestPath))
                return null;

            var json = File.ReadAllText(manifestPath);
            var m = JsonSerializer.Deserialize<Manifest>(json, JsonOptions);
            if (m is null || string.IsNullOrWhiteSpace(m.DisplayName))
                return null;

            var slug = SanitizeFolderName(m.DisplayName);
            var refDirNew = Path.Combine(bundleDirectory, "reference", slug);
            var zonaDirNew = Path.Combine(bundleDirectory, "zona", slug);
            var refDirLegacy = Path.Combine(bundleDirectory, "reference");
            var zonaDirLegacy = Path.Combine(bundleDirectory, "zona");

            string refDir;
            string zonaDir;
            if (Directory.Exists(refDirNew) && Directory.Exists(zonaDirNew))
            {
                refDir = refDirNew;
                zonaDir = zonaDirNew;
            }
            else if (Directory.Exists(refDirLegacy) && Directory.Exists(zonaDirLegacy))
            {
                refDir = refDirLegacy;
                zonaDir = zonaDirLegacy;
            }
            else
                return null;

            return new ProductProfile(
                m.DisplayName.Trim(),
                refDir,
                zonaDir,
                ColorCorrectionSettings.FromDto(m.Color));
        }
        catch
        {
            return null;
        }
    }

    public static IReadOnlyList<ProductProfile> LoadAllBundles()
    {
        EnsureDirectories();
        if (!Directory.Exists(AppPaths.UserProfilesRoot))
            return [];

        var list = new List<ProductProfile>();
        foreach (var dir in Directory.GetDirectories(AppPaths.UserProfilesRoot))
        {
            var p = TryLoadBundle(dir);
            if (p is not null)
                list.Add(p);
        }

        return list;
    }

    public static void DeleteBundle(string displayName)
    {
        var path = GetBundlePath(displayName);
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    /// <summary>
    /// Импорт из папки профиля: полный bundle в AppData, манифест из <c>profiles\slug</c> с данными рядом с exe,
    /// или каталоги <c>reference</c> / <c>zona</c> (с подпапкой по имени или без).
    /// </summary>
    public static ProductProfile ImportFromDirectory(string sourceDir, string displayName)
    {
        EnsureDirectories();
        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException(sourceDir);

        sourceDir = Path.GetFullPath(sourceDir.Trim());
        displayName = displayName.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Укажите имя профиля.");

        var color = ReadColorFromManifest(sourceDir);

        if (File.Exists(Path.Combine(sourceDir, "profile.json"))
            && Directory.Exists(Path.Combine(sourceDir, "reference"))
            && Directory.Exists(Path.Combine(sourceDir, "zona")))
        {
            if (TryLoadBundle(sourceDir) is { } bundle)
                return WriteBundle(displayName, bundle.ReferenceFolder, bundle.ZonaFolder, color);
        }

        if (File.Exists(Path.Combine(sourceDir, "profile.json"))
            && TryResolveShippedAssetDirs(sourceDir, out var shippedRef, out var shippedZona))
            return WriteBundle(displayName, shippedRef, shippedZona, color);

        if (TryFindReferenceAndZonaDirs(sourceDir, displayName, out var refDir, out var zonaDir)
            && !string.IsNullOrEmpty(refDir)
            && !string.IsNullOrEmpty(zonaDir))
            return WriteBundle(displayName, refDir, zonaDir, color);

        throw new InvalidOperationException(
            "Не удалось распознать профиль: нужны папки reference и zona (или profile.json с данными рядом с программой).");
    }

    private static ColorCorrectionSettings ReadColorFromManifest(string sourceDir)
    {
        try
        {
            var manifestPath = Path.Combine(sourceDir, "profile.json");
            if (!File.Exists(manifestPath))
                return ColorCorrectionSettings.Neutral;

            var json = File.ReadAllText(manifestPath);
            var m = JsonSerializer.Deserialize<Manifest>(json, JsonOptions);
            return m?.Color is null
                ? ColorCorrectionSettings.Neutral
                : ColorCorrectionSettings.FromDto(m.Color);
        }
        catch
        {
            return ColorCorrectionSettings.Neutral;
        }
    }

    private static bool TryResolveShippedAssetDirs(string manifestDir, out string refDir, out string zonaDir)
    {
        var slug = Path.GetFileName(manifestDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        refDir = Path.Combine(AppPaths.AppRoot, "reference", slug);
        zonaDir = Path.Combine(AppPaths.AppRoot, "zona", slug);
        return Directory.Exists(refDir) && Directory.Exists(zonaDir);
    }

    private static bool TryFindReferenceAndZonaDirs(string sourceDir, string displayName, out string refDir, out string zonaDir)
    {
        refDir = string.Empty;
        zonaDir = string.Empty;
        var slug = SanitizeFolderName(displayName);

        var nestedRef = Path.Combine(sourceDir, "reference", slug);
        var nestedZona = Path.Combine(sourceDir, "zona", slug);
        if (Directory.Exists(nestedRef) && Directory.Exists(nestedZona))
        {
            refDir = nestedRef;
            zonaDir = nestedZona;
            return true;
        }

        var flatRef = Path.Combine(sourceDir, "reference");
        var flatZona = Path.Combine(sourceDir, "zona");
        if (Directory.Exists(flatRef) && Directory.Exists(flatZona))
        {
            refDir = flatRef;
            zonaDir = flatZona;
            return true;
        }

        if (Directory.EnumerateFiles(sourceDir).Any(ImageFileCatalog.IsImageFile))
        {
            var parent = Path.GetDirectoryName(sourceDir);
            if (parent is null)
                return false;

            if (string.Equals(Path.GetFileName(sourceDir), "reference", StringComparison.OrdinalIgnoreCase))
            {
                var zonaSlug = Path.Combine(parent, "zona", slug);
                if (Directory.Exists(zonaSlug))
                {
                    refDir = sourceDir;
                    zonaDir = zonaSlug;
                    return true;
                }

                var zonaFlat = Path.Combine(parent, "zona");
                if (Directory.Exists(zonaFlat))
                {
                    refDir = sourceDir;
                    zonaDir = zonaFlat;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Старый products.json в AppData — импорт в user files\Profile (один раз).</summary>
    public static void MigrateLegacyAppDataProductsJsonIfNeeded()
    {
        try
        {
            var legacy = AppPaths.CustomProductsFile;
            if (!File.Exists(legacy))
                return;

            if (LoadAllBundles().Count > 0)
                return;

            var json = File.ReadAllText(legacy);
            var rows = JsonSerializer.Deserialize<List<ProductProfileStore.Row>>(json, ProductProfileStore.JsonSerializerOptions);
            if (rows is null || rows.Count == 0)
                return;

            foreach (var r in rows)
            {
                if (string.IsNullOrWhiteSpace(r.Name)
                    || !Directory.Exists(r.ReferenceFolder)
                    || !Directory.Exists(r.ZonaFolder))
                    continue;

                var color = ColorCorrectionSettings.FromDto(r.Color);
                WriteBundle(r.Name.Trim(), r.ReferenceFolder, r.ZonaFolder, color);
            }

            File.Move(legacy, legacy + ".migrated.bak", overwrite: true);
        }
        catch
        {
            /* ignore migration failure */
        }
    }
}
