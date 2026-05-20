using OpenCvSharp;

namespace AutoRAW.Services;

/// <summary>Зелёная разметка на макетах <c>operation/NN/01_center.png</c>.</summary>
internal static class GreenGuideMask
{
    public static Mat Build(Mat bgr)
    {
        Mat[] ch = Cv2.Split(bgr);
        using var b = ch[0];
        using var g = ch[1];
        using var r = ch[2];

        using var gMask = new Mat();
        using var rMask = new Mat();
        using var bMask = new Mat();

        Cv2.Threshold(g, gMask, 120, 255, ThresholdTypes.Binary);
        Cv2.Threshold(r, rMask, 120, 255, ThresholdTypes.BinaryInv);
        Cv2.Threshold(b, bMask, 120, 255, ThresholdTypes.BinaryInv);

        var result = new Mat();
        Cv2.BitwiseAnd(gMask, rMask, result);
        Cv2.BitwiseAnd(result, bMask, result);
        return result;
    }

    /// <summary>Более мягкие пороги — если на макете зелень не чистая #00FF00.</summary>
    public static Mat BuildRelaxed(Mat bgr)
    {
        Mat[] ch = Cv2.Split(bgr);
        using var b = ch[0];
        using var g = ch[1];
        using var r = ch[2];

        using var gMask = new Mat();
        using var rMask = new Mat();
        using var bMask = new Mat();

        Cv2.Threshold(g, gMask, 80, 255, ThresholdTypes.Binary);
        Cv2.Threshold(r, rMask, 150, 255, ThresholdTypes.BinaryInv);
        Cv2.Threshold(b, bMask, 150, 255, ThresholdTypes.BinaryInv);

        var result = new Mat();
        Cv2.BitwiseAnd(gMask, rMask, result);
        Cv2.BitwiseAnd(result, bMask, result);
        return result;
    }
}
