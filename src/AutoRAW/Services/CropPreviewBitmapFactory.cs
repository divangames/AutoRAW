using System.IO;
using System.Windows.Media.Imaging;
using AutoRAW.Models;
using ImageMagick;

namespace AutoRAW.Services;

/// <summary>Превью для UI — тот же пайплайн, что пакетный экспорт: полный кадр + ручные правки → размер референса.</summary>
public static class CropPreviewBitmapFactory
{
    private const int MinEdge = 64;

    public static BitmapSource? LoadThumbnail(string path, int maxEdge)
    {
        try
        {
            using var img = RasterImageLoader.Load(path);
            FitLongEdge(img, maxEdge);
            return ToBitmapSource(img);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Загрузка полного кадра как в редакторе/экспорте (ориентация по стему). Вызывающий обязан Dispose.</summary>
    public static MagickImage? TryLoadPreparedFullForManualFrame(
        string inputPath,
        string? outputFileStem,
        int analysisMaxEdge,
        bool rotateCounterClockwise90 = false)
    {
        try
        {
            var full = RasterImageLoader.Load(inputPath);
            if (rotateCounterClockwise90)
                ImageTransformHelper.RotateCounterClockwise90(full);
            ShotCropPolicy.ApplyPreCropOrientation(full, outputFileStem, analysisMaxEdge);
            return full;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Построить превью результата (масштаб только для экрана).</summary>
    public static BitmapSource? LoadCroppedPreview(
        string inputPath,
        string referencePath,
        int analysisMaxEdge,
        int displayMaxEdge,
        ColorCorrectionSettings? colorCorrection = null,
        bool applyColorCorrection = false,
        bool rotateCounterClockwise90 = false,
        string? outputFileStem = null,
        string? zonaFolder = null,
        string? profileDisplayName = null,
        ManualShotAdjust? manualAdjustOverride = null) =>
        LoadCroppedPreviewWithSize(
                inputPath,
                referencePath,
                analysisMaxEdge,
                displayMaxEdge,
                colorCorrection,
                applyColorCorrection,
                rotateCounterClockwise90,
                outputFileStem,
                zonaFolder,
                profileDisplayName,
                manualAdjustOverride)
            .Bitmap;

    /// <summary>Превью + размер кадра (до уменьшения для экрана).</summary>
    public static (BitmapSource? Bitmap, int OutW, int OutH) LoadCroppedPreviewWithSize(
        string inputPath,
        string referencePath,
        int analysisMaxEdge,
        int displayMaxEdge,
        ColorCorrectionSettings? colorCorrection = null,
        bool applyColorCorrection = false,
        bool rotateCounterClockwise90 = false,
        string? outputFileStem = null,
        string? zonaFolder = null,
        string? profileDisplayName = null,
        ManualShotAdjust? manualAdjustOverride = null)
    {
        try
        {
            var reference = AutoCropComputation.AnalyzeReference(referencePath, analysisMaxEdge);
            var refW = (int)reference.RefW;
            var refH = (int)reference.RefH;

            using var full = RasterImageLoader.Load(inputPath);
            if (rotateCounterClockwise90)
                ImageTransformHelper.RotateCounterClockwise90(full);
            ShotCropPolicy.ApplyPreCropOrientation(full, outputFileStem, analysisMaxEdge);

            MagickImage? working = null;
            try
            {
                working = BuildManualFramedOutput(
                    full,
                    inputPath,
                    outputFileStem,
                    zonaFolder,
                    profileDisplayName,
                    manualAdjustOverride,
                    refW,
                    refH);

                if (colorCorrection is not null)
                    ColorCorrectionService.ApplyIfEnabled(working, colorCorrection, applyColorCorrection);
                FitLongEdge(working, displayMaxEdge);
                return (ToBitmapSource(working), refW, refH);
            }
            finally
            {
                working?.Dispose();
            }
        }
        catch
        {
            return (null, 0, 0);
        }
    }

    /// <inheritdoc cref="LoadCroppedPreviewWithSize" />
    /// <remarks>Параметры Zona оставлены для совместимости вызовов и игнорируются.</remarks>
    public static BitmapSource? LoadZonaCroppedPreview(
        string inputPath,
        string zonaPath,
        string referencePath,
        int analysisMaxEdge,
        int displayMaxEdge,
        ColorCorrectionSettings? colorCorrection = null,
        bool applyColorCorrection = false,
        bool rotateCounterClockwise90 = false,
        string? outputFileStem = null,
        string? zonaFolder = null,
        string? profileDisplayName = null,
        ManualShotAdjust? manualAdjustOverride = null) =>
        LoadCroppedPreview(
            inputPath,
            referencePath,
            analysisMaxEdge,
            displayMaxEdge,
            colorCorrection,
            applyColorCorrection,
            rotateCounterClockwise90,
            outputFileStem,
            zonaFolder,
            profileDisplayName,
            manualAdjustOverride);

    /// <inheritdoc cref="LoadCroppedPreviewWithSize" />
    public static (BitmapSource? Bitmap, int OutW, int OutH) LoadZonaCroppedPreviewWithSize(
        string inputPath,
        string zonaPath,
        string referencePath,
        int analysisMaxEdge,
        int displayMaxEdge,
        ColorCorrectionSettings? colorCorrection = null,
        bool applyColorCorrection = false,
        bool rotateCounterClockwise90 = false,
        string? outputFileStem = null,
        string? zonaFolder = null,
        string? profileDisplayName = null,
        ManualShotAdjust? manualAdjustOverride = null) =>
        LoadCroppedPreviewWithSize(
            inputPath,
            referencePath,
            analysisMaxEdge,
            displayMaxEdge,
            colorCorrection,
            applyColorCorrection,
            rotateCounterClockwise90,
            outputFileStem,
            zonaFolder,
            profileDisplayName,
            manualAdjustOverride);

    private static MagickImage BuildManualFramedOutput(
        MagickImage full,
        string inputPath,
        string? outputFileStem,
        string? zonaFolder,
        string? profileDisplayName,
        ManualShotAdjust? manualAdjustOverride,
        int refW,
        int refH)
    {
        var adj = manualAdjustOverride ?? ManualShotAdjustStore.Resolve(profileDisplayName, inputPath, outputFileStem);
        var working = ManualShotAdjustApplier.ComposeFromFullToReference(full, adj, refW, refH);
        TryCompositeGrid(working, zonaFolder, adj.GridOverlay);
        return working;
    }

    /// <summary>Сетка только для превью; экспорт без сетки.</summary>
    public static void CompositeGridForEditorPreview(MagickImage working, string? zonaFolder, ZonaGridOverlayKind kind) =>
        TryCompositeGrid(working, zonaFolder, kind);

    private static void TryCompositeGrid(MagickImage working, string? zonaFolder, ZonaGridOverlayKind kind)
    {
        if (kind == ZonaGridOverlayKind.None)
            return;

        var path = ZonaGridOverlayPaths.TryResolve(zonaFolder, kind);
        if (path is null)
            return;

        try
        {
            using var grid = new MagickImage(path);
            grid.FilterType = FilterType.Box;
            grid.Resize(new MagickGeometry((uint)working.Width, (uint)working.Height));
            if (!grid.HasAlpha)
            {
                grid.Alpha(AlphaOption.Set);
                grid.Evaluate(Channels.Alpha, EvaluateOperator.Multiply, 0.45);
            }
            else
            {
                grid.Evaluate(Channels.Alpha, EvaluateOperator.Multiply, 0.45);
            }

            working.Composite(grid, CompositeOperator.Over);
        }
        catch
        {
            // превью без сетки
        }
    }

    private static void FitLongEdge(MagickImage img, int maxEdge)
    {
        if (maxEdge < MinEdge)
            return;

        var m = Math.Max(img.Width, img.Height);
        if (m <= maxEdge)
            return;

        var s = maxEdge / (double)m;
        var nw = Math.Max(1u, (uint)Math.Round(img.Width * s));
        var nh = Math.Max(1u, (uint)Math.Round(img.Height * s));
        img.Resize(nw, nh);
    }

    private static BitmapSource ToBitmapSource(MagickImage img)
    {
        using var ms = new MemoryStream();
        img.Format = MagickFormat.Png;
        img.Write(ms);
        ms.Position = 0;

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Клон, уменьшение по длинной стороне и PNG для UI (редактор).</summary>
    public static BitmapSource? ToBitmapSourceScaled(MagickImage source, int displayMaxEdge)
    {
        try
        {
            using var copy = (MagickImage)source.Clone();
            FitLongEdge(copy, displayMaxEdge);
            return ToBitmapSource(copy);
        }
        catch
        {
            return null;
        }
    }
}
