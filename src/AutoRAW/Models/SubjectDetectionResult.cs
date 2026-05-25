namespace AutoRAW.Models;

public sealed class SubjectDetectionResult
{
    public Box2d Subject { get; init; }

    public SubjectDetectSource Source { get; init; }

    public float Confidence { get; init; }

    public bool IsValid { get; init; }

    public string? Detail { get; init; }

    public static SubjectDetectionResult Invalid(string? detail = null) => new()
    {
        Subject = default,
        Source = SubjectDetectSource.None,
        Confidence = 0,
        IsValid = false,
        Detail = detail
    };
}
