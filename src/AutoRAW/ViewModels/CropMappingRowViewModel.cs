using CommunityToolkit.Mvvm.ComponentModel;

namespace AutoRAW.ViewModels;

public partial class CropMappingRowViewModel : ObservableObject
{
    private readonly Action? _notifyParent;

    public CropMappingRowViewModel(string inputPath, string selectedReferenceFile, Action? notifyParent = null)
    {
        _notifyParent = notifyParent;
        _inputPath = inputPath;
        _selectedReferenceFile = selectedReferenceFile;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InputFileName))]
    private string _inputPath = string.Empty;

    [ObservableProperty]
    private string _selectedReferenceFile = string.Empty;

    public string InputFileName => Path.GetFileName(InputPath);

    partial void OnSelectedReferenceFileChanged(string value) => _notifyParent?.Invoke();
}
