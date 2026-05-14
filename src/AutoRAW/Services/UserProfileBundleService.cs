using System.Text.Json;
using System.Text.Json.Serialization;
using AutoRAW.Models;

namespace AutoRAW.Services;

/// <summary>
/// Профиль как папка под <see cref="AppPaths.UserProfilesRoot"/>:
/// <c>reference</c>, <c>zona</c>, <c>setting</c> (XMP), <c>profile.json</c>.
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

    /// <summary>Записывает профиль: копирует reference/zona, XMP в setting, манифест.</summary>
    public static ProductProfile WriteBundle(string displayName, string sourceReferenceDir, string sourceZonaDir, ColorCorrectionSettings color)
    {
        EnsureDirectories();
        var bundle = GetBundlePath(displayName);
        Directory.CreateDirectory(bundle);

        var refDest = Path.Combine(bundle, "reference");
        var zonaDest = Path.Combine(bundle, "zona");
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
            var refDir = Path.Combine(bundleDirectory, "reference");
            var zonaDir = Path.Combine(bundleDirectory, "zona");
            var manifestPath = Path.Combine(bundleDirectory, "profile.json");
            if (!Directory.Exists(refDir) || !Directory.Exists(zonaDir) || !File.Exists(manifestPath))
                return null;

            var json = File.ReadAllText(manifestPath);
            var m = JsonSerializer.Deserialize<Manifest>(json, JsonOptions);
            if (m is null || string.IsNullOrWhiteSpace(m.DisplayName))
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
