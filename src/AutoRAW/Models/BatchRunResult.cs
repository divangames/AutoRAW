namespace AutoRAW.Models;

public readonly record struct BatchRunResult(
    bool Cancelled,
    int Total,
    int Succeeded,
    int Errors,
    TimeSpan ActiveElapsed);
