using AutoRAW.Models;

namespace AutoRAW.Services;

public static class CropGeometryService
{
    /// <summary>
    /// Строит кадр так, чтобы:
    ///   • объект занимал ту же долю кадра, что на референсе (определяет «зум»);
    ///   • центр объекта в кадре совпадал с положением на референсе (не только геометрический центр);
    ///   • соотношение сторон = как у референса.
    /// </summary>
    public static Box2d ComputeCrop(
        Box2d subjectRef,
        double refImageWidth,
        double refImageHeight,
        Box2d subjectTarget,
        double targetImageWidth,
        double targetImageHeight,
        bool centerSubjectInFrame = false)
    {
        var refW = refImageWidth;
        var refH = refImageHeight;
        var aspect = refW / Math.Max(1e-6, refH);

        // Доля, которую объект занимает на референсе
        var fracW = Math.Max(1e-6, subjectRef.Width) / refW;
        var fracH = Math.Max(1e-6, subjectRef.Height) / refH;

        // Размер выходного кропа: масштабируем из размера объекта на цели
        var cropW = subjectTarget.Width / fracW;
        var cropH = subjectTarget.Height / fracH;

        // Соблюдаем aspect ratio, увеличивая меньший размер
        if (cropW / cropH > aspect)
            cropH = cropW / aspect;
        else
            cropW = cropH * aspect;

        var relX = centerSubjectInFrame
            ? 0.5
            : subjectRef.CenterX / Math.Max(1e-6, refW);
        var relY = centerSubjectInFrame
            ? 0.5
            : subjectRef.CenterY / Math.Max(1e-6, refH);
        var x0 = subjectTarget.CenterX - relX * cropW;
        var y0 = subjectTarget.CenterY - relY * cropH;

        var crop = new Box2d(x0, y0, cropW, cropH);

        return FitInsideImage(crop, targetImageWidth, targetImageHeight, subjectTarget, preserveSubjectBottom: false);
    }

    /// <summary>
    /// Умещаем кроп в границы кадра. Сохраняем заданный прямоугольник, если он влезает.
    /// Если нужно уменьшить — масштабируем вокруг опорной точки объекта (центр XY или центр X + низ по Y).
    /// </summary>
    /// <summary>Уменьшает и сдвигает кроп так, чтобы он целиком лежал в кадре, сохраняя положение объекта в кадре.</summary>
    public static Box2d FitInsideImage(
        Box2d crop,
        double imgW,
        double imgH,
        Box2d anchor,
        bool preserveSubjectBottom)
    {
        if (crop.Width <= 1 || crop.Height <= 1)
            return Box2d.CenterFraction(imgW, imgH, 0.95, 0.95);

        var px = anchor.CenterX;
        var py = preserveSubjectBottom ? anchor.Bottom : anchor.CenterY;

        var fx = (px - crop.X) / crop.Width;
        var fy = (py - crop.Y) / crop.Height;

        var s = Math.Min(1.0, Math.Min(imgW / crop.Width, imgH / crop.Height));
        var w = crop.Width * s;
        var h = crop.Height * s;

        var x = px - fx * w;
        var y = py - fy * h;

        x = Math.Clamp(x, 0, Math.Max(0, imgW - w));
        y = Math.Clamp(y, 0, Math.Max(0, imgH - h));

        return new Box2d(x, y, w, h);
    }

    public static (int x, int y, int w, int h) ToIntegers(Box2d crop, int imgW, int imgH)
    {
        var x = (int)Math.Floor(crop.X);
        var y = (int)Math.Floor(crop.Y);
        var w = (int)Math.Ceiling(crop.Right) - x;
        var h = (int)Math.Ceiling(crop.Bottom) - y;

        x = Math.Clamp(x, 0, Math.Max(0, imgW - 1));
        y = Math.Clamp(y, 0, Math.Max(0, imgH - 1));
        w = Math.Clamp(w, 1, imgW - x);
        h = Math.Clamp(h, 1, imgH - y);
        return (x, y, w, h);
    }
}
