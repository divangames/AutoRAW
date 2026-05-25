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
    /// <param name="clampLoadedImageLongEdge">Если задано — после ориентации исходник ужимается только для этого пути загрузки (превью UI; экспорт и прочее передают null).</param>
    public static MagickImage? TryLoadPreparedFullForManualFrame(
        string inputPath,
        string? outputFileStem,
        int analysisMaxEdge,
        bool rotateCounterClockwise90 = false,
        int? clampLoadedImageLongEdge = null)
    {
        try
        {
            var full = RasterImageLoader.Load(inputPath);
            if (rotateCounterClockwise90)
                ImageTransformHelper.RotateCounterClockwise90(full);

            if (clampLoadedImageLongEdge is { } cap && cap >= 64 &&
                Math.Max(full.Width, full.Height) > (uint)cap)
                AutoCropComputation.ResizeLongEdgeFitInPlace(full, cap);

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

    /// <summary>Превью с уже разрешёнными параметрами кадра (<see cref="ManualShotFrameResolver"/>).</summary>
    public static BitmapSource? LoadCroppedPreviewResolved(
        string inputPath,
        string referencePath,
        int analysisMaxEdge,
        int displayMaxEdge,
        ResolvedManualShotFrame frame,
        ColorCorrectionSettings? colorCorrection = null,
        bool applyColorCorrection = false,
        bool rotateCounterClockwise90 = false) =>
        LoadCroppedPreviewWithSize(
                inputPath,
                referencePath,
                analysisMaxEdge,
                displayMaxEdge,
                colorCorrection,
                applyColorCorrection,
                rotateCounterClockwise90,
                outputFileStem: null,
                zonaFolder: null,
                profileDisplayName: null,
                manualAdjustOverride: frame.Adjust)
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

            var previewComposeLongEdge = Math.Clamp(displayMaxEdge * 2, 480, 680);
            var refLong = Math.Max(refW, refH);
            var composeScale = refLong > 0 ? Math.Min(1.0, previewComposeLongEdge / (double)refLong) : 1.0;

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
                    refH,
                    composeScale);

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

    /// <param name="composeScale">Доля разрешения превью (1 = как экспорт); меньше — быстрее счёт для экрана.</param>
    private static MagickImage BuildManualFramedOutput(
        MagickImage full,
        string inputPath,
        string? outputFileStem,
        string? zonaFolder,
        string? profileDisplayName,
        ManualShotAdjust? manualAdjustOverride,
        int refW,
        int refH,
        double composeScale = 1.0)
    {
        var adj = manualAdjustOverride ?? ManualShotAdjustStore.Resolve(profileDisplayName, inputPath, outputFileStem);

        ManualShotAdjust adjUse = adj;
        var rw = refW;
        var rh = refH;
        if (composeScale < 1.0 - 1e-9)
        {
            adjUse = adj.Clone();
            adjUse.OffsetX *= composeScale;
            adjUse.OffsetY *= composeScale;
            rw = Math.Max(1, (int)Math.Round(refW * composeScale));
            rh = Math.Max(1, (int)Math.Round(refH * composeScale));
        }

        var working = ManualShotAdjustApplier.ComposeFromFullToReference(full, adjUse, rw, rh);
        TryCompositeGrid(working, outputFileStem, adj.GridOverlay);
        return working;
    }

    /// <summary>Сетка только для превью; экспорт без сетки.</summary>
    public static void CompositeGridForEditorPreview(
        MagickImage working,
        string? outputStem,
        ZonaGridOverlayKind kind) =>
        TryCompositeGrid(working, outputStem, kind);

    private static void TryCompositeGrid(MagickImage working, string? outputStem, ZonaGridOverlayKind kind)
    {
        if (kind == ZonaGridOverlayKind.None)
            return;

        if (kind is ZonaGridOverlayKind.LayoutRules or ZonaGridOverlayKind.LegacyRules020408)
            SneakersLayoutSafeZone.DrawRulesOverlay(working, outputStem);
    }

    private static void FitLongEdge(MagickImage img, int maxEdge)
    {
        if (maxEdge < MinEdge)
            return;

        AutoCropComputation.ResizeLongEdgeFitInPlace(img, maxEdge);
    }

    /// <summary>Клон, уменьшение по длинной стороне и растровый поток для UI (редактор). BMP быстрее PNG при малых размерах.</summary>
    public static BitmapSource? ToBitmapSourceScaled(MagickImage source, int displayMaxEdge)
    {
        try
        {
            using var copy = (MagickImage)source.Clone();
            FitLongEdge(copy, displayMaxEdge);
            return ToBitmapSource(copy, MagickFormat.Bmp);
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource ToBitmapSource(MagickImage img, MagickFormat format)
    {
        using var ms = new MemoryStream();
        img.Format = format;
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

    private static BitmapSource ToBitmapSource(MagickImage img) =>
        ToBitmapSource(img, MagickFormat.Png);
}
