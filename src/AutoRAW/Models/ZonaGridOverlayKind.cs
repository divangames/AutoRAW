namespace AutoRAW.Models;

/// <summary>Сетка-ориентир из zona (только в редакторе / превью, не в файл результата).</summary>
public enum ZonaGridOverlayKind
{
    None = 0,
    /// <summary><c>zona_tovara_01.png</c> (фото 01).</summary>
    Photo01 = 1,
    /// <summary><c>zona_tovara_02.png</c> (остальные кадры).</summary>
    OtherPhotos = 2
}
