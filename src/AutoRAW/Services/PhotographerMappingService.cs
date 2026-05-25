using AutoRAW.Models;
using AutoRAW.ViewModels;

namespace AutoRAW.Services;

/// <summary>Сопоставление входных файлов с референсами в зависимости от выбранного фотографа.</summary>
public static class PhotographerMappingService
{
    /// <summary>Явный выбор файла эталона: задаёт строку очереди, стем выхода и zona по имени референса.</summary>
    public static bool ApplyStemFromChosenReference(CropMappingRowViewModel row, string referenceFileName)
    {
        row.SelectedReferenceFile = referenceFileName;
        var stem = Path.GetFileNameWithoutExtension(referenceFileName);
        row.OutputFileStem = string.IsNullOrEmpty(stem) ? null : stem;
        row.ZonaMarkerStem = row.OutputFileStem;
        row.RotateCounterClockwise90 = false;
        return true;
    }

    /// <summary>Сопоставление позиции/номера кадра с референсом 01…08.</summary>
    public static void ApplyShotNumberRow(
        CropMappingRowViewModel row,
        int shotNumber,
        IReadOnlyList<string> referenceFileNames,
        string defaultReference)
    {
        var label = RuslanShotMapping.GetReferenceLabelForShotNumber(shotNumber);
        if (label is null)
            return;

        row.SelectedReferenceFile =
            RuslanShotMapping.ResolveReferenceFileName(referenceFileNames, label) ?? defaultReference;
        row.OutputFileStem = label;
        row.ZonaMarkerStem = label;
        row.RotateCounterClockwise90 = false;
    }

    public static void ApplyStandardRow(
        CropMappingRowViewModel row,
        int oneBasedPositionInFolder,
        IReadOnlyList<string> referenceFileNames,
        string defaultReference) =>
        ApplyShotNumberRow(row, oneBasedPositionInFolder, referenceFileNames, defaultReference);

    public static void ApplyRuslanRow(
        CropMappingRowViewModel row,
        int shotNumber,
        IReadOnlyList<string> referenceFileNames,
        string defaultReference) =>
        ApplyShotNumberRow(row, shotNumber, referenceFileNames, defaultReference);

    public static IReadOnlyList<BatchJobItem> ToBatchJobs(
        IEnumerable<CropMappingRowViewModel> rows,
        string referenceFolder,
        bool hasReferenceFolder,
        string? profileDisplayName = null)
    {
        return rows.Select(r =>
        {
            var refPath = hasReferenceFolder && !string.IsNullOrWhiteSpace(r.SelectedReferenceFile)
                ? Path.Combine(referenceFolder, r.SelectedReferenceFile)
                : r.InputPath;
            return new BatchJobItem(
                r.InputPath,
                refPath,
                r.OutputFileStem,
                r.RotateCounterClockwise90,
                r.ZonaMarkerStem,
                profileDisplayName);
        }).ToList();
    }
}
