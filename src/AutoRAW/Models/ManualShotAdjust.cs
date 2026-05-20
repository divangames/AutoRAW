namespace AutoRAW.Models;

/// <summary>Ручная доработка кадра после автокропа: смещение в пикселах выхода, масштаб %, поворот °.</summary>
public sealed class ManualShotAdjust
{
    public double OffsetX { get; set; }

    public double OffsetY { get; set; }

    /// <summary>100 — базовое заполнение окна выхода (cover под референс); &lt;100 — дальше, &gt;100 — крупнее.</summary>
    public double ZoomPercent { get; set; } = 100;

    public double RotationDeg { get; set; }

    /// <summary>Сетка из zona — только для визуала в редакторе и при превью; на экспорт не влияет.</summary>
    public ZonaGridOverlayKind GridOverlay { get; set; } = ZonaGridOverlayKind.None;

    /// <summary>Только трансформации выхода (без сетки) — для пайплайна экспорта.</summary>
    public bool IsIdentity =>
        Math.Abs(OffsetX) < 0.01
        && Math.Abs(OffsetY) < 0.01
        && Math.Abs(ZoomPercent - 100) < 0.01
        && Math.Abs(RotationDeg) < 0.01;

    /// <summary>Есть что сохранить в json (трансформации или выбранная сетка).</summary>
    public bool HasPersistableState =>
        !IsIdentity || GridOverlay != ZonaGridOverlayKind.None;

    public ManualShotAdjust Clone() => new()
    {
        OffsetX = OffsetX,
        OffsetY = OffsetY,
        ZoomPercent = ZoomPercent,
        RotationDeg = RotationDeg,
        GridOverlay = GridOverlay
    };
}
