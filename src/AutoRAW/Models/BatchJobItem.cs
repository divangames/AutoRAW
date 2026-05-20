namespace AutoRAW.Models;

/// <summary>Одно задание пакетного кадрирования.</summary>
public sealed record BatchJobItem(
    string InputPath,
    string ReferencePath,
    string? OutputFileStem = null,
    bool RotateCounterClockwise90 = false,
    string? ZonaMarkerStem = null,
    string? ProfileDisplayName = null);
