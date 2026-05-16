namespace AutoRAW.Models;

/// <summary>Категория строки журнала (цвет в UI).</summary>
public enum LogLineKind
{
    Normal,
    Error,
    Pause,
    Cancel,
    Done,
    /// <summary>Реплика от имени ZONA — выделяется жирным и фиолетовым.</summary>
    Zona
}
