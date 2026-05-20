namespace AutoRAW.Models;

/// <summary>
/// Красные линии и эталонное положение товара на <c>NN_line.png</c>.
/// </summary>
public readonly record struct ShotLineGuide(
    double LeftX,
    double RightX,
    double BottomY,
    double? TopY,
    double TemplateSubjectCenterX,
    double TemplateSubjectCenterY,
    double TemplateSubjectBottom,
    int GuideWidth,
    int GuideHeight)
{
    public bool HasTemplateSubject =>
        TemplateSubjectCenterX > 0 && TemplateSubjectBottom > TemplateSubjectCenterY;

    public double SafeLeft(double marginPx = 2) => LeftX + marginPx;

    public double SafeRight(double marginPx = 2) => RightX - marginPx;

    public double SafeBottom(double marginPx = 2) => BottomY - marginPx;

    public double? SafeTop(double marginPx = 2) => TopY.HasValue ? TopY.Value + marginPx : null;
}
