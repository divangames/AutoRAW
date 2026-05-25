using AutoRAW.Services;

namespace AutoRAW.Models;

/// <summary>
/// Запомненная композиция эталонного кадра <c>reference\NN.jpg</c>:
/// размер и положение товара в выходном кадре (пиксели и доли 0…1).
/// </summary>
public sealed class ReferenceCompositionTemplate
{
    public required string Stem { get; init; }

    public required string FilePath { get; init; }

    public double RefW { get; init; }

    public double RefH { get; init; }

    public Box2d SubjectBox { get; init; }

    public SubjectDetectSource DetectSource { get; init; }

    public double CenterXFrac => SubjectBox.CenterX / Math.Max(1.0, RefW);

    public double CenterYFrac => SubjectBox.CenterY / Math.Max(1.0, RefH);

    public double WidthFrac => SubjectBox.Width / Math.Max(1.0, RefW);

    public double HeightFrac => SubjectBox.Height / Math.Max(1.0, RefH);

    public double MarginLeftFrac => SubjectBox.X / Math.Max(1.0, RefW);

    public double MarginTopFrac => SubjectBox.Y / Math.Max(1.0, RefH);

    public double MarginRightFrac => (RefW - SubjectBox.Right) / Math.Max(1.0, RefW);

    public double MarginBottomFrac => (RefH - SubjectBox.Bottom) / Math.Max(1.0, RefH);

    public AutoCropComputation.ReferenceMetrics ToReferenceMetrics() =>
        new(SubjectBox, RefW, RefH);

    public string SummaryLine =>
        $"кадр {Stem}: центр {CenterXFrac * 100:0.#}%×{CenterYFrac * 100:0.#}%, "
        + $"товар {WidthFrac * 100:0.#}%×{HeightFrac * 100:0.#}%, "
        + $"отступы L{MarginLeftFrac * 100:0.#}% R{MarginRightFrac * 100:0.#}% "
        + $"T{MarginTopFrac * 100:0.#}% B{MarginBottomFrac * 100:0.#}%";
}
