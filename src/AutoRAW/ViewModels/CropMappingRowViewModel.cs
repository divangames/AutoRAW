using AutoRAW.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AutoRAW.ViewModels;

public partial class CropMappingRowViewModel : ObservableObject
{
    private readonly Action<CropMappingRowViewModel>? _notifyParent;

    public CropMappingRowViewModel(
        string inputPath,
        string selectedReferenceFile,
        Action<CropMappingRowViewModel>? notifyParent = null)
    {
        _notifyParent = notifyParent;
        _inputPath = inputPath;
        _selectedReferenceFile = selectedReferenceFile;
    }

    /// <summary>Путь относительно папки «Товар» для списка очереди.</summary>
    [ObservableProperty]
    private string _relativeDisplayPath = string.Empty;

    [ObservableProperty]
    private string _alignStatusGlyph = "…";

    [ObservableProperty]
    private string _alignStatusToolTip = string.Empty;

    [ObservableProperty]
    private FrameAlignStatusKind _alignStatusKind = FrameAlignStatusKind.Pending;

    public string QueueLineText =>
        string.IsNullOrEmpty(RelativeDisplayPath)
            ? InputFileName
            : RelativeDisplayPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InputFileName))]
    private string _inputPath = string.Empty;

    [ObservableProperty]
    private string _selectedReferenceFile = string.Empty;

    /// <summary>Имя выходного файла без расширения (например 06); иначе — как у входа.</summary>
    [ObservableProperty]
    private string? _outputFileStem;

    /// <summary>Имя маркёра Zona без расширения (обычно совпадает с номером референса).</summary>
    [ObservableProperty]
    private string? _zonaMarkerStem;

    /// <summary>Повернуть исходник на 90° против часовой перед кропом.</summary>
    [ObservableProperty]
    private bool _rotateCounterClockwise90;

    public string InputFileName => Path.GetFileName(InputPath);

    public string DisplayReferenceLabel =>
        string.IsNullOrWhiteSpace(OutputFileStem)
            ? Path.GetFileNameWithoutExtension(SelectedReferenceFile)
            : OutputFileStem;

    partial void OnSelectedReferenceFileChanged(string value) => _notifyParent?.Invoke(this);
}

