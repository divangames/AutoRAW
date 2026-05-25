using AutoRAW.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace AutoRAW.Services.SubjectDetection;

/// <summary>Уточнение bbox по маске U2Net-p внутри ROI (опционально, если есть ONNX).</summary>
public sealed class U2NetpOnnxRefiner : IDisposable
{
    public const int InputSize = 320;
    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string _outputName;

    private static readonly object Gate = new();
    private static U2NetpOnnxRefiner? _shared;

    public static string? LastLoadError { get; private set; }

    public static bool TryCreateShared(out U2NetpOnnxRefiner? refiner)
    {
        lock (Gate)
        {
            if (_shared is not null)
            {
                refiner = _shared;
                return true;
            }

            var path = AppPaths.ResolveSubjectOnnxPath("u2netp.onnx");
            if (path is null || !File.Exists(path))
            {
                LastLoadError = $"файл не найден (ожидается models\\subject\\u2netp.onnx рядом с exe или в корне репозитория; exe={AppPaths.AppRoot})";
                refiner = null;
                return false;
            }

            try
            {
                _shared = new U2NetpOnnxRefiner(path);
                LastLoadError = null;
                refiner = _shared;
                return true;
            }
            catch (Exception ex)
            {
                LastLoadError = ex.Message;
                refiner = null;
                return false;
            }
        }
    }

    private U2NetpOnnxRefiner(string modelPath)
    {
        _session = OnnxSessionFactory.CreateSession(modelPath);
        _inputName = _session.InputMetadata.Keys.First();
        _outputName = _session.OutputMetadata.Keys.First();
    }

    /// <summary>Маска салиентности на всём кадре → bbox товара.</summary>
    public Box2d? DetectBox(Mat bgr)
    {
        if (bgr.Empty())
            return null;
        return RefineBox(bgr, new Box2d(0, 0, bgr.Cols, bgr.Rows));
    }

    public Box2d? RefineBox(Mat bgr, Box2d roi)
    {
        if (bgr.Empty() || roi.Width < 8 || roi.Height < 8)
            return null;

        var pad = Math.Max(8.0, Math.Max(roi.Width, roi.Height) * 0.06);
        var x0 = (int)Math.Floor(Math.Max(0, roi.X - pad));
        var y0 = (int)Math.Floor(Math.Max(0, roi.Y - pad));
        var x1 = (int)Math.Ceiling(Math.Min(bgr.Cols, roi.Right + pad));
        var y1 = (int)Math.Ceiling(Math.Min(bgr.Rows, roi.Bottom + pad));
        if (x1 - x0 < 12 || y1 - y0 < 12)
            return null;

        using var crop = new Mat(bgr, new OpenCvSharp.Rect(x0, y0, x1 - x0, y1 - y0));
        var tensor = Preprocess(crop);
        using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_inputName, tensor)]);
        var mask = results.First(v => v.Name == _outputName).AsTensor<float>();
        if (!TryMaskToBox(mask, crop.Cols, crop.Rows, out var lx, out var ly, out var rx, out var ry))
            return null;

        return new Box2d(x0 + lx, y0 + ly, rx - lx, ry - ly);
    }

    private static DenseTensor<float> Preprocess(Mat bgr)
    {
        using var resized = new Mat();
        Cv2.Resize(bgr, resized, new OpenCvSharp.Size(InputSize, InputSize));
        using var rgb = new Mat();
        Cv2.CvtColor(resized, rgb, ColorConversionCodes.BGR2RGB);
        var t = new DenseTensor<float>([1, 3, InputSize, InputSize]);
        for (var y = 0; y < InputSize; y++)
        {
            for (var x = 0; x < InputSize; x++)
            {
                var p = rgb.At<Vec3b>(y, x);
                t[0, 0, y, x] = (p.Item2 - 0.485f) / 0.229f;
                t[0, 1, y, x] = (p.Item1 - 0.456f) / 0.224f;
                t[0, 2, y, x] = (p.Item0 - 0.406f) / 0.225f;
            }
        }

        return t;
    }

    private static bool TryMaskToBox(Tensor<float> mask, int origW, int origH, out double x1, out double y1, out double x2, out double y2)
    {
        x1 = y1 = x2 = y2 = 0;
        var dims = mask.Dimensions.ToArray();
        if (dims.Length < 3)
            return false;

        var h = dims[^2];
        var w = dims[^1];
        const float thresh = 0.35f;
        var minX = w;
        var minY = h;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var v = ReadMask(mask, dims, y, x);
                if (v < thresh)
                    continue;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        if (maxX < 0)
            return false;

        var sx = origW / (double)w;
        var sy = origH / (double)h;
        x1 = minX * sx;
        y1 = minY * sy;
        x2 = (maxX + 1) * sx;
        y2 = (maxY + 1) * sy;
        return x2 - x1 >= 6 && y2 - y1 >= 6;
    }

    private static float ReadMask(Tensor<float> mask, int[] dims, int y, int x)
    {
        if (dims.Length == 4)
        {
            var channels = dims[1];
            var max = 0f;
            for (var c = 0; c < channels; c++)
                max = Math.Max(max, mask[0, c, y, x]);
            return max;
        }

        return dims.Length == 3 ? mask[0, y, x] : 0f;
    }

    public void Dispose() => _session.Dispose();
}
