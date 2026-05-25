using AutoRAW.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace AutoRAW.Services.SubjectDetection;

/// <summary>YOLOv8n-seg ONNX: bbox + маска силуэта (32 прототипа, 160×160).</summary>
public sealed class YoloV8SegOnnxDetector : IDisposable
{
    public const int InputSize = 640;
    private const int MaskDim = 32;
    private const int ProtoSize = 160;
    private const int ClassCount = 80;
    private const int DetChannels = 4 + ClassCount + MaskDim;
    private const float ConfThreshold = 0.28f;
    private const float IouThreshold = 0.45f;
    private const float MaskThreshold = 0.5f;

    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string _detOutputName;
    private readonly string _protoOutputName;

    private static readonly object Gate = new();
    private static YoloV8SegOnnxDetector? _shared;

    public static bool TryCreateShared(out YoloV8SegOnnxDetector? detector)
    {
        lock (Gate)
        {
            if (_shared is not null)
            {
                detector = _shared;
                return true;
            }

            var path = AppPaths.ResolveSubjectOnnxPath("yolov8n-seg.onnx");
            if (path is null || !File.Exists(path))
            {
                detector = null;
                return false;
            }

            if (new FileInfo(path).Length < 5_000_000)
            {
                detector = null;
                return false;
            }

            try
            {
                _shared = new YoloV8SegOnnxDetector(path);
                detector = _shared;
                return true;
            }
            catch
            {
                detector = null;
                return false;
            }
        }
    }

    private YoloV8SegOnnxDetector(string modelPath)
    {
        _session = OnnxSessionFactory.CreateSession(modelPath);
        _inputName = _session.InputMetadata.Keys.First();
        _detOutputName = "";
        _protoOutputName = "";
        var outputNames = _session.OutputMetadata.Keys.ToList();
        foreach (var name in outputNames)
        {
            var dims = _session.OutputMetadata[name].Dimensions;
            if (dims.Length == 4)
                _protoOutputName = name;
            else if (dims.Length == 3)
                _detOutputName = name;
        }

        if (string.IsNullOrEmpty(_detOutputName) && outputNames.Count > 0)
            _detOutputName = outputNames[0];
        if (string.IsNullOrEmpty(_protoOutputName) && outputNames.Count > 1)
            _protoOutputName = outputNames[1];

        if (string.IsNullOrEmpty(_detOutputName) || string.IsNullOrEmpty(_protoOutputName))
            throw new InvalidOperationException("yolov8n-seg: det/proto outputs not found");
    }

    public YoloSegDetectOutput DetectWithMask(Mat bgr)
    {
        if (bgr.Empty())
            return new YoloSegDetectOutput(SubjectDetectionResult.Invalid("empty image"), null);

        try
        {
            var w = bgr.Cols;
            var h = bgr.Rows;
            var (tensor, padX, padY, scale) = LetterboxToTensor(bgr);

            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, tensor) };
            using var results = _session.Run(inputs);

            var detTensor = results.First(v => v.Name == _detOutputName).AsTensor<float>();
            var protoTensor = results.First(v => v.Name == _protoOutputName).AsTensor<float>();

            var best = ParseBestDetection(detTensor, padX, padY, scale, w, h);
            if (best is null)
                return new YoloSegDetectOutput(SubjectDetectionResult.Invalid("no detection"), null);

            using var mask = BuildMask(best.Value.Coeffs, protoTensor, padX, padY, scale, best.Value, w, h);
            var box = RefineBoxFromMask(mask, best.Value.ToBox2d(), w, h);
            box = ProductSubjectDetection.RefineBox(bgr, box);

            var result = new SubjectDetectionResult
            {
                Subject = box,
                Source = SubjectDetectSource.YoloV8Seg,
                Confidence = best.Value.Score,
                IsValid = true,
                Detail = "yolov8n-seg"
            };

            return new YoloSegDetectOutput(result, mask.Clone());
        }
        catch
        {
            return new YoloSegDetectOutput(SubjectDetectionResult.Invalid("yolov8n-seg error"), null);
        }
    }

    public SubjectDetectionResult Detect(Mat bgr)
    {
        using var output = DetectWithMask(bgr);
        output.Mask?.Dispose();
        return output.Result;
    }

    private static Box2d RefineBoxFromMask(Mat mask, Box2d fallback, int cols, int rows)
    {
        if (mask.Empty() || Cv2.CountNonZero(mask) < 48)
            return fallback;

        Cv2.FindContours(mask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        if (contours.Length == 0)
            return fallback;

        var rect = Cv2.BoundingRect(contours.OrderByDescending(c => Cv2.ContourArea(c)).First());
        if (rect.Width < 8 || rect.Height < 8)
            return fallback;

        var tight = new Box2d(rect.X, rect.Y, rect.Width, rect.Height);
        return ProductSubjectDetection.IsPlausibleProductBox(tight, cols, rows) ? tight : fallback;
    }

    private static Mat BuildMask(
        float[] coeffs,
        Tensor<float> protos,
        float padX,
        float padY,
        float scale,
        DetectedBox box,
        int origW,
        int origH)
    {
        var pDims = protos.Dimensions.ToArray();
        var protoPlanes = pDims.Length == 4 ? pDims[1] : MaskDim;
        var protoH = pDims.Length == 4 ? pDims[2] : ProtoSize;
        var protoW = pDims.Length == 4 ? pDims[3] : ProtoSize;
        if (protoH < 8 || protoW < 8)
            throw new InvalidOperationException("invalid proto tensor");

        using var logits = new Mat(protoH, protoW, MatType.CV_32FC1, Scalar.All(0));
        var coeffCount = Math.Min(MaskDim, coeffs.Length);
        var planes = Math.Min(protoPlanes, coeffCount);
        for (var y = 0; y < protoH; y++)
        {
            for (var x = 0; x < protoW; x++)
            {
                var sum = 0f;
                for (var i = 0; i < planes; i++)
                    sum += coeffs[i] * protos[0, i, y, x];
                logits.Set(y, x, sum);
            }
        }

        using var protoProb = new Mat(logits.Size(), MatType.CV_32FC1);
        ApplySigmoid(logits, protoProb);

        using var protoBin = new Mat();
        Cv2.Compare(protoProb, new Scalar(MaskThreshold), protoBin, CmpType.GT);

        using var mask640 = new Mat();
        Cv2.Resize(protoBin, mask640, new OpenCvSharp.Size(InputSize, InputSize), 0, 0, InterpolationFlags.Nearest);

        var bx1 = (int)Math.Floor(box.X1 * scale + padX);
        var by1 = (int)Math.Floor(box.Y1 * scale + padY);
        var bx2 = (int)Math.Ceiling(box.X2 * scale + padX);
        var by2 = (int)Math.Ceiling(box.Y2 * scale + padY);
        bx1 = Math.Clamp(bx1, 0, InputSize - 1);
        by1 = Math.Clamp(by1, 0, InputSize - 1);
        bx2 = Math.Clamp(bx2, bx1 + 1, InputSize);
        by2 = Math.Clamp(by2, by1 + 1, InputSize);

        using (var outside = new Mat(mask640.Size(), MatType.CV_8UC1, Scalar.All(255)))
        {
            Cv2.Rectangle(outside, new Rect(bx1, by1, bx2 - bx1, by2 - by1), Scalar.All(0), -1);
            Cv2.BitwiseAnd(mask640, outside, mask640);
        }

        var nw = (int)Math.Round(origW * scale);
        var nh = (int)Math.Round(origH * scale);
        var px = (int)Math.Round(padX);
        var py = (int)Math.Round(padY);
        var cw = Math.Min(nw, InputSize - px);
        var ch = Math.Min(nh, InputSize - py);
        if (cw < 1 || ch < 1)
            return new Mat(origH, origW, MatType.CV_8UC1, Scalar.All(0));

        using var crop640 = new Mat(mask640, new Rect(px, py, cw, ch));
        var letterboxed = new Mat(origH, origW, MatType.CV_8UC1, Scalar.All(0));
        using var resized = new Mat();
        Cv2.Resize(crop640, resized, new OpenCvSharp.Size(origW, origH), 0, 0, InterpolationFlags.Nearest);
        resized.CopyTo(letterboxed);
        return letterboxed;
    }

    private static void ApplySigmoid(Mat logits, Mat dst)
    {
        for (var y = 0; y < logits.Rows; y++)
        {
            for (var x = 0; x < logits.Cols; x++)
            {
                var v = logits.At<float>(y, x);
                dst.Set(y, x, 1f / (1f + MathF.Exp(-v)));
            }
        }
    }

    private static (DenseTensor<float> Tensor, float PadX, float PadY, float Scale) LetterboxToTensor(Mat bgr)
    {
        var w = bgr.Cols;
        var h = bgr.Rows;
        var scale = Math.Min(InputSize / (float)w, InputSize / (float)h);
        var nw = (int)Math.Round(w * scale);
        var nh = (int)Math.Round(h * scale);
        var padX = (InputSize - nw) / 2f;
        var padY = (InputSize - nh) / 2f;

        using var resized = new Mat();
        Cv2.Resize(bgr, resized, new OpenCvSharp.Size(nw, nh));
        using var padded = new Mat(InputSize, InputSize, MatType.CV_8UC3, new Scalar(114, 114, 114));
        resized.CopyTo(new Mat(padded, new Rect((int)padX, (int)padY, nw, nh)));

        using var rgb = new Mat();
        Cv2.CvtColor(padded, rgb, ColorConversionCodes.BGR2RGB);
        var tensor = new DenseTensor<float>([1, 3, InputSize, InputSize]);
        for (var y = 0; y < InputSize; y++)
        {
            for (var x = 0; x < InputSize; x++)
            {
                var p = rgb.At<Vec3b>(y, x);
                tensor[0, 0, y, x] = p.Item2 / 255f;
                tensor[0, 1, y, x] = p.Item1 / 255f;
                tensor[0, 2, y, x] = p.Item0 / 255f;
            }
        }

        return (tensor, padX, padY, scale);
    }

    private static DetectedBox? ParseBestDetection(
        Tensor<float> output,
        float padX,
        float padY,
        float scale,
        int origW,
        int origH)
    {
        var dims = output.Dimensions.ToArray();
        if (dims.Length != 3)
            return null;

        var channelLast = dims[2] >= DetChannels && dims[2] <= 256 && dims[1] > dims[2];
        var channels = channelLast ? dims[2] : dims[1];
        var count = channelLast ? dims[1] : dims[2];
        if (channels < DetChannels || count < 1)
            return null;

        var classStart = 4;
        var maskStart = 4 + ClassCount;
        var maskCoeffs = Math.Min(MaskDim, channels - maskStart);
        if (maskCoeffs < 8)
            return null;

        var candidates = new List<DetectedBox>();
        for (var i = 0; i < count; i++)
        {
            float At(int ch) => channelLast ? output[0, i, ch] : output[0, ch, i];

            var cx = At(0);
            var cy = At(1);
            var bw = At(2);
            var bh = At(3);

            var maxScore = 0f;
            for (var c = classStart; c < maskStart; c++)
            {
                var s = At(c);
                if (s > maxScore)
                    maxScore = s;
            }

            if (maxScore < ConfThreshold)
                continue;

            var x1 = (cx - bw * 0.5f - padX) / scale;
            var y1 = (cy - bh * 0.5f - padY) / scale;
            var x2 = (cx + bw * 0.5f - padX) / scale;
            var y2 = (cy + bh * 0.5f - padY) / scale;

            x1 = Math.Clamp(x1, 0, origW - 1);
            y1 = Math.Clamp(y1, 0, origH - 1);
            x2 = Math.Clamp(x2, x1 + 1, origW);
            y2 = Math.Clamp(y2, y1 + 1, origH);

            var area = (x2 - x1) * (y2 - y1);
            if (area < 64)
                continue;

            var coeffs = new float[MaskDim];
            for (var m = 0; m < maskCoeffs; m++)
                coeffs[m] = At(maskStart + m);

            candidates.Add(new DetectedBox(x1, y1, x2, y2, maxScore, area, coeffs));
        }

        if (candidates.Count == 0)
            return null;

        var sorted = candidates.OrderByDescending(c => c.Score).ToList();
        var kept = new List<DetectedBox> { sorted[0] };
        for (var i = 1; i < sorted.Count; i++)
        {
            var a = sorted[i];
            if (kept.All(k => IoU(a, k) < IouThreshold))
                kept.Add(a);
        }

        return kept.OrderByDescending(b => ScoreForProductPhoto(b, origW, origH)).First();
    }

    private static float ScoreForProductPhoto(DetectedBox b, int origW, int origH)
    {
        var areaFrac = b.Area / Math.Max(1, origW * origH);
        if (areaFrac < (float)ProductSubjectDetection.MinAreaFrac || areaFrac > (float)ProductSubjectDetection.MaxAreaFrac)
            return 0;

        var cx = (b.X1 + b.X2) * 0.5f / origW;
        var cy = (b.Y1 + b.Y2) * 0.5f / origH;
        var centerPenalty = (float)(Math.Pow((cx - 0.5) / 0.48, 2) + Math.Pow((cy - 0.5) / 0.48, 2));
        var centerW = Math.Max(0.15f, 1f - centerPenalty);
        return b.Score * MathF.Sqrt((float)areaFrac) * (0.35f + 0.65f * centerW);
    }

    private static float IoU(DetectedBox a, DetectedBox b)
    {
        var ix1 = Math.Max(a.X1, b.X1);
        var iy1 = Math.Max(a.Y1, b.Y1);
        var ix2 = Math.Min(a.X2, b.X2);
        var iy2 = Math.Min(a.Y2, b.Y2);
        var inter = Math.Max(0, ix2 - ix1) * Math.Max(0, iy2 - iy1);
        var union = a.Area + b.Area - inter;
        return union <= 0 ? 0 : inter / union;
    }

    private readonly record struct DetectedBox(
        float X1,
        float Y1,
        float X2,
        float Y2,
        float Score,
        float Area,
        float[] Coeffs)
    {
        public Box2d ToBox2d() => new(X1, Y1, X2 - X1, Y2 - Y1);
    }

    public void Dispose() => _session.Dispose();
}

public sealed class YoloSegDetectOutput : IDisposable
{
    public SubjectDetectionResult Result { get; }
    public Mat? Mask { get; }

    public YoloSegDetectOutput(SubjectDetectionResult result, Mat? mask)
    {
        Result = result;
        Mask = mask;
    }

    public void Dispose() => Mask?.Dispose();
}
