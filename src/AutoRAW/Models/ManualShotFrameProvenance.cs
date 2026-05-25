namespace AutoRAW.Models;

/// <summary>Откуда взяты параметры кадра для превью и экспорта.</summary>
public enum ManualShotFrameProvenance
{
    /// <summary>Нет сохранённых правок и авто-подгонка не сработала.</summary>
    Default = 0,

    /// <summary>Per-file запись в manual_shot_adjust.json.</summary>
    PerFile,

    /// <summary>Умолчание профиля по номеру кадра.</summary>
    ProfileStem,

    /// <summary>Умолчание профиля по имени файла (<c>basename</c>, без учёта папки).</summary>
    ProfileFileName,

    /// <summary>Автоматически по шаблону эталона reference (YOLO / OpenCV).</summary>
    AutoAlign
}
