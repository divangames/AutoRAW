using AutoRAW.Models;
using ImageMagick;

namespace AutoRAW.Services;

/// <summary>
/// Ручной кадр поверх <b>полного</b> исходника (после загрузки и политики ориентации):
/// режим «cover» под размер референса, затем пан / масштаб / поворот в координатах выхода refW×refH.
/// </summary>
public static class ManualShotAdjustApplier
{
    /// <summary>
    /// <paramref name="full"/> не изменяется и не захватывается — внутри делается <see cref="MagickImage.Clone"/>.
    /// </summary>
    public static MagickImage ComposeFromFullToReference(
        MagickImage full,
        ManualShotAdjust adjust,
        int refW,
        int refH)
    {
        var bg = SampleBg(full);
        var layer = (MagickImage)full.Clone();
        layer.BackgroundColor = bg;
        layer.VirtualPixelMethod = VirtualPixelMethod.Edge;

        if (Math.Abs(adjust.RotationDeg) > 0.01)
            layer.Rotate(adjust.RotationDeg);

        var lw0 = (double)layer.Width;
        var lh0 = (double)layer.Height;
        var cover = Math.Max(refW / lw0, refH / lh0);
        var userZ = Math.Max(0.05, adjust.ZoomPercent / 100.0);
        var scaleTotal = cover * userZ;

        var nw = Math.Max(1u, (uint)Math.Round(lw0 * scaleTotal));
        var nh = Math.Max(1u, (uint)Math.Round(lh0 * scaleTotal));
        layer.Resize(new MagickGeometry(nw, nh) { IgnoreAspectRatio = false });
        layer.ResetPage();

        return RenderLayerIntoReferenceFrame(layer, bg, adjust, refW, refH);
    }

    private static MagickImage RenderLayerIntoReferenceFrame(
        MagickImage layer,
        MagickColor bg,
        ManualShotAdjust a,
        int outW,
        int outH)
    {
        try
        {
            var lw = (int)layer.Width;
            var lh = (int)layer.Height;

            var x0 = (outW - (double)lw) * 0.5 + a.OffsetX;
            var y0 = (outH - (double)lh) * 0.5 + a.OffsetY;
            var margin = (int)Math.Ceiling(Math.Max(32,
                Math.Max(
                    Math.Max(Math.Max(0, -x0), Math.Max(0, x0 + lw - outW)),
                    Math.Max(Math.Max(0, -y0), Math.Max(0, y0 + lh - outH)))));

            var ws = outW + 2 * margin;
            var hs = outH + 2 * margin;

            using var work = new MagickImage(bg, (uint)ws, (uint)hs);
            var px = (int)Math.Round(margin + x0);
            var py = (int)Math.Round(margin + y0);
            work.Composite(layer, px, py, CompositeOperator.Over);

            work.Crop(new MagickGeometry((uint)outW, (uint)outH)
            {
                X = margin,
                Y = margin
            });
            work.ResetPage();
            return (MagickImage)work.Clone();
        }
        finally
        {
            layer.Dispose();
        }
    }

    private static MagickColor SampleBg(MagickImage img)
    {
        try
        {
            using var mat = MagickMatConverter.ToMatBgr(img);
            var sc = ImageBorderFill.SampleBackground(mat);
            return new MagickColor(
                (byte)Math.Clamp(sc.Val2, 0, 255),
                (byte)Math.Clamp(sc.Val1, 0, 255),
                (byte)Math.Clamp(sc.Val0, 0, 255));
        }
        catch
        {
            return new MagickColor(245, 245, 245);
        }
    }
}
