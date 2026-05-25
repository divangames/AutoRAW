namespace AutoRAW.Models;

/// <summary>Статус строки очереди / результата авто-подгонки.</summary>
public enum FrameAlignStatusKind
{
    Pending = 0,
    Ok,
    AutoReview,
    LowQuality,
    Failed
}
