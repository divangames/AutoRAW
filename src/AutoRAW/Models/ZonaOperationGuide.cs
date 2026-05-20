namespace AutoRAW.Models;

/// <summary>
/// Макеты <c>operation/NN/</c> (доли кадра 0…1):
/// <c>01_center</c> — зелёный объект и перекрестие; <c>02_crop</c> — красная рамка кропа.
/// <c>03_crop</c> / <c>04_final</c> — эталон результата, в расчёт не входят.
/// </summary>
public readonly record struct ZonaOperationGuide(
    double SubjectCenterXFrac,
    double SubjectCenterYFrac,
    double SubjectWidthFrac,
    double SubjectHeightFrac,
    double TargetCenterXFrac,
    double TargetCenterYFrac,
    double CropLeftFrac,
    double CropTopFrac,
    double CropWidthFrac,
    double CropHeightFrac);
