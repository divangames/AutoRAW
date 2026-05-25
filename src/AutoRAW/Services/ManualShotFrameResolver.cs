using AutoRAW.Models;
using ImageMagick;

namespace AutoRAW.Services;

/// <summary>
/// Сохранённые правки → иначе авто-подгонка → иначе пустые значения.
/// Отдельно <see cref="ResolveAutoOnly"/> для колонки «После (авто)».
/// </summary>
public static class ManualShotFrameResolver
{
    public static ResolvedManualShotFrame ResolveForExport(
        string inputPath,
        string referencePath,
        string? profileDisplayName,
        string? outputFileStem,
        string? zonaFolder,
        int analysisMaxEdge,
        bool rotateCounterClockwise90 = false) =>
        Resolve(inputPath, referencePath, profileDisplayName, outputFileStem, zonaFolder, analysisMaxEdge,
            rotateCounterClockwise90, autoOnly: false);

    public static ResolvedManualShotFrame ResolveAutoOnly(
        string inputPath,
        string referencePath,
        string? profileDisplayName,
        string? outputFileStem,
        string? zonaFolder,
        int analysisMaxEdge,
        bool rotateCounterClockwise90 = false) =>
        Resolve(inputPath, referencePath, profileDisplayName, outputFileStem, zonaFolder, analysisMaxEdge,
            rotateCounterClockwise90, autoOnly: true);

    public static ResolvedManualShotFrame Resolve(
        string inputPath,
        string referencePath,
        string? profileDisplayName,
        string? outputFileStem,
        string? zonaFolder,
        int analysisMaxEdge,
        bool rotateCounterClockwise90,
        bool autoOnly)
    {
        ManualShotAdjust? identityBasenameGridOnly = null;
        ManualShotAdjust? identityStemGridOnly = null;

        if (!autoOnly)
        {
            if (ManualShotAdjustStore.TryGetPerFile(inputPath, out var perFile))
            {
                return new ResolvedManualShotFrame
                {
                    Adjust = perFile,
                    Provenance = ManualShotFrameProvenance.PerFile
                };
            }

            if (ManualShotAdjustStore.TryGetProfileBasename(profileDisplayName, inputPath, out var basenameAdj))
            {
                if (!basenameAdj.IsIdentity)
                {
                    return new ResolvedManualShotFrame
                    {
                        Adjust = basenameAdj,
                        Provenance = ManualShotFrameProvenance.ProfileFileName
                    };
                }

                identityBasenameGridOnly = basenameAdj;
            }

            var stem = ZonaOperationGuideParser.NormalizeShotStem(outputFileStem, inputPath);
            if (stem is not null
                && ManualShotAdjustStore.TryGetProfileStem(profileDisplayName, stem, out var profileAdj))
            {
                if (!profileAdj.IsIdentity)
                {
                    return new ResolvedManualShotFrame
                    {
                        Adjust = profileAdj,
                        Provenance = ManualShotFrameProvenance.ProfileStem
                    };
                }

                identityStemGridOnly = profileAdj;
            }
        }

        if (!File.Exists(referencePath))
        {
            return new ResolvedManualShotFrame
            {
                Adjust = new ManualShotAdjust(),
                Provenance = ManualShotFrameProvenance.Default,
                Detail = "no reference"
            };
        }

        using var full = CropPreviewBitmapFactory.TryLoadPreparedFullForManualFrame(
            inputPath, outputFileStem, analysisMaxEdge, rotateCounterClockwise90);
        if (full is null)
        {
            return new ResolvedManualShotFrame
            {
                Adjust = new ManualShotAdjust(),
                Provenance = ManualShotFrameProvenance.Default,
                Detail = "load failed"
            };
        }

        var resolved =
            ResolveAutoFromFull(full, referencePath, inputPath, outputFileStem, zonaFolder, analysisMaxEdge);
        var gridHint = PreferGridOverlaySource(identityBasenameGridOnly, identityStemGridOnly);
        return MergeProfileGridOntoResolvedIfNeeded(resolved, gridHint);
    }

    private static ManualShotAdjust? PreferGridOverlaySource(
        ManualShotAdjust? basenameIdentity,
        ManualShotAdjust? stemIdentity)
    {
        if (basenameIdentity?.GridOverlay != ZonaGridOverlayKind.None)
            return basenameIdentity;
        return stemIdentity?.GridOverlay != ZonaGridOverlayKind.None ? stemIdentity : null;
    }

    /// <summary>Если профиль по кадру хранил только «нулевую» трансформацию — подставить сетку превью с профиля.</summary>
    private static ResolvedManualShotFrame MergeProfileGridOntoResolvedIfNeeded(
        ResolvedManualShotFrame resolved,
        ManualShotAdjust? identityProfileGridSource)
    {
        if (identityProfileGridSource is null || identityProfileGridSource.GridOverlay == ZonaGridOverlayKind.None)
            return resolved;

        var adj = resolved.Adjust.Clone();
        adj.GridOverlay = identityProfileGridSource.GridOverlay;
        return new ResolvedManualShotFrame
        {
            Adjust = adj,
            Provenance = resolved.Provenance,
            DetectSource = resolved.DetectSource,
            Detail = resolved.Detail,
            ReferenceStem = resolved.ReferenceStem,
            AlignQualityScore = resolved.AlignQualityScore,
            IsLowAlignQuality = resolved.IsLowAlignQuality,
            AlignQualitySummary = resolved.AlignQualitySummary
        };
    }

    private static ResolvedManualShotFrame ResolveAutoFromFull(
        MagickImage full,
        string referencePath,
        string inputPath,
        string? outputFileStem,
        string? zonaFolder,
        int analysisMaxEdge)
    {
        var stem = ZonaOperationGuideParser.NormalizeShotStem(outputFileStem, inputPath);
        if (ManualShotAutoAlignService.IsSkippedStem(stem))
        {
            return new ResolvedManualShotFrame
            {
                Adjust = new ManualShotAdjust(),
                Provenance = ManualShotFrameProvenance.Default,
                Detail = "кадры 05 и 07 — только вручную"
            };
        }

        if (ManualShotAutoAlignService.TryCompute(full, referencePath, stem, zonaFolder: null, analysisMaxEdge, out var outcome))
        {
            return new ResolvedManualShotFrame
            {
                Adjust = outcome.Adjust,
                Provenance = ManualShotFrameProvenance.AutoAlign,
                DetectSource = outcome.DetectSource,
                Detail = outcome.Detail,
                ReferenceStem = outcome.Template.Stem
            };
        }

        return new ResolvedManualShotFrame
        {
            Adjust = new ManualShotAdjust(),
            Provenance = ManualShotFrameProvenance.Default,
            Detail = "align failed"
        };
    }

    /// <summary>Два разрешения за одну загрузку RAW (превью на главном экране).</summary>
    public static (ResolvedManualShotFrame Export, ResolvedManualShotFrame AutoOnly) ResolveExportAndAuto(
        string inputPath,
        string referencePath,
        string? profileDisplayName,
        string? outputFileStem,
        string? zonaFolder,
        int analysisMaxEdge,
        bool rotateCounterClockwise90 = false)
    {
        ResolvedManualShotFrame? exportSaved = null;
        ManualShotAdjust? identityBasenameGridOnly = null;
        ManualShotAdjust? identityStemGridOnly = null;

        if (ManualShotAdjustStore.TryGetPerFile(inputPath, out var perFile))
        {
            exportSaved = new ResolvedManualShotFrame
            {
                Adjust = perFile,
                Provenance = ManualShotFrameProvenance.PerFile
            };
        }
        else
        {
            if (ManualShotAdjustStore.TryGetProfileBasename(profileDisplayName, inputPath, out var basenameAdj))
            {
                if (!basenameAdj.IsIdentity)
                {
                    exportSaved = new ResolvedManualShotFrame
                    {
                        Adjust = basenameAdj,
                        Provenance = ManualShotFrameProvenance.ProfileFileName
                    };
                }
                else
                    identityBasenameGridOnly = basenameAdj;
            }

            if (exportSaved is null)
            {
                var stem = ZonaOperationGuideParser.NormalizeShotStem(outputFileStem, inputPath);
                if (stem is not null
                    && ManualShotAdjustStore.TryGetProfileStem(profileDisplayName, stem, out var profileAdj))
                {
                    if (!profileAdj.IsIdentity)
                    {
                        exportSaved = new ResolvedManualShotFrame
                        {
                            Adjust = profileAdj,
                            Provenance = ManualShotFrameProvenance.ProfileStem
                        };
                    }
                    else
                        identityStemGridOnly = profileAdj;
                }
            }
        }

        using var full = CropPreviewBitmapFactory.TryLoadPreparedFullForManualFrame(
            inputPath, outputFileStem, analysisMaxEdge, rotateCounterClockwise90);
        if (full is null)
        {
            var fail = new ResolvedManualShotFrame
            {
                Adjust = new ManualShotAdjust(),
                Provenance = ManualShotFrameProvenance.Default,
                Detail = "load failed"
            };
            return (exportSaved ?? fail, fail);
        }

        var auto = ResolveAutoFromFull(full, referencePath, inputPath, outputFileStem, zonaFolder, analysisMaxEdge);

        ResolvedManualShotFrame export;
        if (exportSaved is not null)
            export = exportSaved;
        else
        {
            var gridHint = PreferGridOverlaySource(identityBasenameGridOnly, identityStemGridOnly);
            export = MergeProfileGridOntoResolvedIfNeeded(auto, gridHint);
        }

        return (export, auto);
    }
}
