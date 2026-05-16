using System.Text.Json;
using System.Text.Json.Serialization;
using AutoRAW.Models;

namespace AutoRAW.Services;

public static class ProductProfileStore
{
    /// <summary>Для миграции из старого products.json.</summary>
    public static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public sealed class Row
    {
        public string Name { get; set; } = string.Empty;
        public string ReferenceFolder { get; set; } = string.Empty;
        public string ZonaFolder { get; set; } = string.Empty;
        public ColorRowDto? Color { get; set; }
    }

    public static IReadOnlyList<ProductProfile> LoadCustom()
    {
        UserProfileBundleService.MigrateLegacyAppDataProductsJsonIfNeeded();
        return UserProfileBundleService.LoadAllBundles();
    }

    /// <summary>
    /// Профили из комплекта приложения (<c>profiles\*\profile.json</c>) и при необходимости запасной экземпляр «Кроссовки» из кода.
    /// </summary>
    public static IReadOnlyList<ProductProfile> LoadBuiltInMenuProfiles()
    {
        var shipped = ShippedProfileCatalog.LoadAll();
        var hasSneakersSlot = shipped.Any(AppPaths.ReferencesBuiltInSneakersFolders);
        var sneakersDirsExist =
            Directory.Exists(AppPaths.BuiltInSneakersReferenceFolder)
            && Directory.Exists(AppPaths.BuiltInSneakersZonaFolder);

        if (hasSneakersSlot || !sneakersDirsExist)
            return shipped;

        var withFallback = new List<ProductProfile>(shipped.Count + 1) { ProductProfile.BuiltInSneakers };
        foreach (var p in shipped)
            withFallback.Add(p);
        return withFallback;
    }
}
