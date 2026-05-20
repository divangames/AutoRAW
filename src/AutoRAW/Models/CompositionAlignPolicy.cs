namespace AutoRAW.Models;

/// <summary>Политика выравнивания товара в готовом кадре (после Zona / reference-кропа).</summary>
public enum CompositionAlignPolicy
{
    /// <summary>Не сдвигать — оставить как после Zona (как раньше).</summary>
    Skip,

    /// <summary>Центр товара в геометрический центр кадра.</summary>
    CenterInFrame,

    /// <summary>Смещение как на референсе.</summary>
    MatchReference
}
