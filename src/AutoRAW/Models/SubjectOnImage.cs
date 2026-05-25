namespace AutoRAW.Models;

/// <summary>Товар на полном кадре: bbox + визуальный центр (силуэт) и опциональный угол PCA.</summary>
public readonly record struct SubjectOnImage(
    Box2d Box,
    double CenterX,
    double CenterY,
    double? PcaAngleDeg)
{
    public static SubjectOnImage FromBox(Box2d box) =>
        new(box, box.CenterX, box.CenterY, null);

    public static SubjectOnImage FromBoxWithShape(
        Box2d box,
        double visualCenterX,
        double visualCenterY,
        double? pcaAngleDeg) =>
        new(box, visualCenterX, visualCenterY, pcaAngleDeg);
}
