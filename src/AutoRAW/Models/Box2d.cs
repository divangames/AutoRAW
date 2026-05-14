namespace AutoRAW.Models;

/// <summary>Прямоугольник в координатах изображения (double, пиксели).</summary>
public readonly record struct Box2d(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double CenterX => X + Width * 0.5;
    public double CenterY => Y + Height * 0.5;

    public static Box2d CenterFraction(double imgW, double imgH, double fracW = 0.7, double fracH = 0.7)
    {
        var w = imgW * fracW;
        var h = imgH * fracH;
        return new Box2d((imgW - w) * 0.5, (imgH - h) * 0.5, w, h);
    }

    public Box2d Scale(double sx, double sy)
        => new(X * sx, Y * sy, Width * sx, Height * sy);

    public Box2d Union(Box2d other)
    {
        var x0 = Math.Min(X, other.X);
        var y0 = Math.Min(Y, other.Y);
        var x1 = Math.Max(Right, other.Right);
        var y1 = Math.Max(Bottom, other.Bottom);
        return new Box2d(x0, y0, x1 - x0, y1 - y0);
    }
}
