using AutoRAW.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace AutoRAW.Services.SubjectDetection;

/// <summary>Детекция bbox через YOLOv8n ONNX (COCO); для товара берётся крупнейший уверенный bbox.</summary>
public sealed class YoloV8OnnxDetector : IDisposable
{
    public const int InputSize = 640;
    private const float ConfThreshold = 0.28f;
    private const float IouThreshold = 0.45f;

    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string _outputName;

    private static readonly object Gate = new();
    private static YoloV8OnnxDetector? _shared;

    public static bool TryCreateShared(out YoloV8OnnxDetector? detector)
    {
        lock (Gate)
        {
            if (_shared is not null)
            {
                detector = _shared;
                return true;
            }

            var path = AppPaths.ResolveSubjectOnnxPath("yolov8n.onnx");
            if (path is null || !File.Exists(path))
            {
                detector = null;
                return false;
            }

            try
            {
                _shared = new YoloV8OnnxDetector(path);
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

    private YoloV8OnnxDetector(string modelPath)
    {
        _session = OnnxSessionFactory.CreateSession(modelPath);
        _inputName = _session.InputMetadata.Keys.First();
        _outputName = _session.OutputMetadata.Keys.First();
    }

    public SubjectDetectionResult Detect(Mat bgr)
    {
        if (bgr.Empty())
            return SubjectDetectionResult.Invalid("empty image");

        var w = bgr.Cols;
        var h = bgr.Rows;
        var (tensor, padX, padY, scale) = LetterboxToTensor(bgr);

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(_inputName, tensor)
        };

        using var results = _session.Run(inputs);
        var output = results.First(v => v.Name == _outputName).AsTensor<float>();
        var best = ParseBestBox(output, padX, padY, scale, w, h);
        if (best is null)
            return SubjectDetectionResult.Invalid("no detection");

        return new SubjectDetectionResult
        {
            Subject = best.Value.ToBox2d(),
            Source = SubjectDetectSource.YoloV8,
            Confidence = best.Value.Score,
            IsValid = true,
            Detail = "yolov8n"
        };
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
        resized.CopyTo(new Mat(padded, new OpenCvSharp.Rect((int)padX, (int)padY, nw, nh)));

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

    private static DetectedBox? ParseBestBox(Tensor<float> output, float padX, float padY, float scale, int origW, int origH)
    {
        var dims = output.Dimensions.ToArray();
        if (dims.Length != 3)
            return null;

        var channels = dims[1];
        var count = dims[2];
        if (channels < 5 || count < 1)
            return null;

        var candidates = new List<DetectedBox>();
        for (var i = 0; i < count; i++)
        {
            var cx = output[0, 0, i];
            var cy = output[0, 1, i];
            var bw = output[0, 2, i];
            var bh = output[0, 3, i];

            var maxScore = 0f;
            for (var c = 4; c < channels; c++)
            {
                var s = output[0, c, i];
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

            candidates.Add(new DetectedBox(x1, y1, x2, y2, maxScore, area));
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
        if (areaFrac is < 0.055f or > 0.88f)
            return 0;

        var cx = (b.X1 + b.X2) * 0.5f / origW;
        var cy = (b.Y1 + b.Y2) * 0.5f / origH;
        var centerPenalty = (float)(Math.Pow((cx - 0.5) / 0.48, 2) + Math.Pow((cy - 0.5) / 0.48, 2));
        var centerW = Math.Max(0.15f, 1f - centerPenalty);
        return b.Score * MathF.Sqrt(areaFrac) * (0.35f + 0.65f * centerW);
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

    private readonly record struct DetectedBox(float X1, float Y1, float X2, float Y2, float Score, float Area)
    {
        public Box2d ToBox2d() => new(X1, Y1, X2 - X1, Y2 - Y1);
    }

    public void Dispose() => _session.Dispose();
}
