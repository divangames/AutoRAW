using AutoRAW.Services;

namespace AutoRAW.Models;

/// <summary>Пресет товара: reference, каталог zona (маркёры технологии «Zona») и цветокоррекция.</summary>
public sealed class ProductProfile
{
    public ProductProfile(string displayName, string? referenceFolder, string? zonaFolder, ColorCorrectionSettings color, bool isDraft = false)
    {
        DisplayName = displayName;
        ReferenceFolder = referenceFolder;
        ZonaFolder = zonaFolder;
        Color = color;
        IsDraft = isDraft;
    }

    public string DisplayName { get; }

    /// <summary>Черновик в меню: пути в UI не подставляются из профиля.</summary>
    public bool IsDraft { get; }

    /// <summary>Null или пусто — корень <c>reference\</c> рядом с программой (см. <see cref="AppPaths.DefaultReferenceFolder"/>).</summary>
    public string? ReferenceFolder { get; }

    /// <summary>Null или пусто — корень <c>zona\</c> рядом с программой (см. <see cref="AppPaths.DefaultZonaFolder"/>).</summary>
    public string? ZonaFolder { get; }

    public ColorCorrectionSettings Color { get; }

    public static string UnsavedDraftDisplayName => "Несохранённые изменения";

    /// <summary>Встроенный профиль: файлы в <c>reference\Sneakers</c> и <c>zona\Sneakers</c> рядом с exe.</summary>
    public static ProductProfile BuiltInSneakers { get; } = new(
        "Кроссовки",
        AppPaths.BuiltInSneakersReferenceFolder,
        AppPaths.BuiltInSneakersZonaFolder,
        ColorCorrectionSettings.SneakersDefaults);

    public static ProductProfile CreateUnsavedDraft(ColorCorrectionSettings color) =>
        new(UnsavedDraftDisplayName, null, null, color, isDraft: true);

    public ProductProfile WithColor(ColorCorrectionSettings color) =>
        new(DisplayName, ReferenceFolder, ZonaFolder, color, IsDraft);

    public ProductProfile WithFolders(string? referenceFolder, string? zonaFolder, ColorCorrectionSettings color, bool isDraft) =>
        new(DisplayName, referenceFolder, zonaFolder, color, isDraft);
}
