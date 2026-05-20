using OpenCvSharp;

namespace AutoRAW.Services;

/// <summary>Красные направляющие (как маркёр Zona).</summary>
internal static class RedGuideLineMask
{
    public static Mat Build(Mat bgr)
    {
        Mat[] ch = Cv2.Split(bgr);
        using var b = ch[0];
        using var g = ch[1];
        using var r = ch[2];

        using var rMask = new Mat();
        using var gMask = new Mat();
        using var bMask = new Mat();

        Cv2.Threshold(r, rMask, 130, 255, ThresholdTypes.Binary);
        Cv2.Threshold(g, gMask, 90, 255, ThresholdTypes.BinaryInv);
        Cv2.Threshold(b, bMask, 90, 255, ThresholdTypes.BinaryInv);

        var result = new Mat();
        Cv2.BitwiseAnd(rMask, gMask, result);
        Cv2.BitwiseAnd(result, bMask, result);
        return result;
    }
}
