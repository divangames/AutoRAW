namespace AutoRAW.Models;

public enum SubjectDetectSource
{
    None = 0,
    YoloV8,
    /// <summary>YOLOv8n-seg: bbox и маска силуэта (ONNX).</summary>
    YoloV8Seg,
    OpenCv,
    /// <summary>Локальная маска U2Net-p (аналог remove.bg, без облака).</summary>
    U2Net
}
