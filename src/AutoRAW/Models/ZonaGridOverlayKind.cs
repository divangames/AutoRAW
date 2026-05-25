namespace AutoRAW.Models;

/// <summary>Сетка-ориентир в редакторе (не в экспорт): линии по правилам макета.</summary>
public enum ZonaGridOverlayKind
{
    None = 0,
    /// <summary>Красные линии из <see cref="Services.SneakersLayoutSafeZone"/> по номеру кадра.</summary>
    LayoutRules = 1,
    /// <summary>Совместимость: старые сохранения «zona_tovara_02».</summary>
    LegacyRules020408 = 2
}
