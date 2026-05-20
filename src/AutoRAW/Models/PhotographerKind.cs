namespace AutoRAW.Models;

/// <summary>Режим сопоставления входных снимков с референсами (меню «Профиль → Фотограф»).</summary>
public enum PhotographerKind
{
  /// <summary>По порядку файлов в папке: 1→01, 2→02, … 8→08.</summary>
  Standard,

  /// <summary>По имени файла и референса.</summary>
  Masha,

  /// <summary>Номер кадра из имени файла (тип3, 3.NEF …) → 01…08.</summary>
  Ruslan
}
