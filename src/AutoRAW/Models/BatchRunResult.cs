namespace AutoRAW.Models;

public readonly record struct BatchRunResult(
    bool Cancelled,
    int Total,
    int Succeeded,
    int Errors,
    TimeSpan ActiveElapsed,
    int ManualSaved = 0,
    int AutoAligned = 0,
    int NeedsReview = 0,
    int LowQuality = 0);
