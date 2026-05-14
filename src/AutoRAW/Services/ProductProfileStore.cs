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
}
