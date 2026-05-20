using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AutoRAW.Models;
using AutoRAW.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageMagick;

namespace AutoRAW.ViewModels;

/// <summary>Окно: подпапки «Товар» → превью файлов → ручная подгонка кадра.</summary>
public partial class VisualShotEditorViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _previewDebounce;

    /// <summary>Полный кадр (после AutoOrient и политики по стему) — дорогая загрузка, кэш по файлу.</summary>
    private readonly object _cropCacheLock = new();
    private MagickImage? _editorFullImageCache;
    private string? _editorFullImageCacheKey;

    /// <summary>Отбрасывать устаревшие результаты <see cref="Task.Run"/> при быстрых правках.</summary>
    private int _previewSerial;

    /// <summary>Кэш правок в сессии редактора (ключ = нормализованный путь файла).</summary>
    private readonly Dictionary<string, ManualShotAdjust> _sessionByFile =
        new(StringComparer.OrdinalIgnoreCase);

    public VisualShotEditorViewModel(MainViewModel main, Dispatcher dispatcher)
    {
        _main = main;
        _dispatcher = dispatcher;
        _previewDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _previewDebounce.Tick += (_, _) =>
        {
            _previewDebounce.Stop();
            Interlocked.Increment(ref _previewSerial);
            _ = ExecuteRefreshPreviewsAsync();
        };
    }

    public MainViewModel Main => _main;

    public ObservableCollection<string> ShotStemChoices { get; } = new();

    /// <summary>Варианты сетки для ComboBox (не использовать Tag на ComboBoxItem — в WPF часто ломается привязка).</summary>
    public IReadOnlyList<VisualEditorGridOption> GridOverlayOptions { get; } =
    [
        new(ZonaGridOverlayKind.None, "Без сетки"),
        new(ZonaGridOverlayKind.Photo01, "zona_tovara_01.png"),
        new(ZonaGridOverlayKind.OtherPhotos, "zona_tovara_02.png")
    ];

    [ObservableProperty] private bool _isBrowseMode = true;

    [ObservableProperty] private ObservableCollection<FolderListItemViewModel> _folders = new();

    [ObservableProperty] private FolderListItemViewModel? _selectedFolder;

    [ObservableProperty] private ObservableCollection<FileThumbItemViewModel> _filesInFolder = new();

    [ObservableProperty] private string? _editorInputPath;

    [ObservableProperty] private string _selectedShotStem = "01";

    [ObservableProperty] private double _offsetX;

    [ObservableProperty] private double _offsetY;

    [ObservableProperty] private double _zoomPercent = 100;

    [ObservableProperty] private double _rotationDeg;

    [ObservableProperty] private ZonaGridOverlayKind _gridOverlay = ZonaGridOverlayKind.None;

    [ObservableProperty] private BitmapSource? _referencePreview;

    [ObservableProperty] private BitmapSource? _resultPreview;

    /// <summary>Ширина выходного кадра в пикселах (для перетаскивания).</summary>
    [ObservableProperty] private int _resultOutputWidth;

    /// <summary>Высота выходного кадра в пикселах.</summary>
    [ObservableProperty] private int _resultOutputHeight;

    [ObservableProperty] private string _statusText = string.Empty;

    /// <summary>Смещение в пикселах выхода; <paramref name="scaleX"/>/<paramref name="scaleY"/> — из UI (например outW/ActualWidth).</summary>
    public void ApplyDragDelta(double dxScreenPx, double dyScreenPx, double scaleX, double scaleY)
    {
        OffsetX += dxScreenPx * scaleX;
        OffsetY += dyScreenPx * scaleY;
    }

    partial void OnSelectedFolderChanged(FolderListItemViewModel? value) => _ = ReloadFilesForFolderAsync(value);

    partial void OnIsBrowseModeChanged(bool value)
    {
        // Состояние файла сохраняется в BackToBrowse / OpenFile до смены режима
        if (value)
            EditorInputPath = null;
    }

    partial void OnOffsetXChanged(double value) => SchedulePreviewRefresh();

    partial void OnOffsetYChanged(double value) => SchedulePreviewRefresh();

    partial void OnZoomPercentChanged(double value) => SchedulePreviewRefresh();

    partial void OnRotationDegChanged(double value) => SchedulePreviewRefresh();

    partial void OnGridOverlayChanged(ZonaGridOverlayKind value) => SchedulePreviewRefresh();

    partial void OnSelectedShotStemChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(EditorInputPath))
            LoadAdjustFromStoreForCurrentStem();
        SchedulePreviewRefresh();
    }

    public void ReloadFolderList()
    {
        Folders.Clear();
        var root = _main.InputFolder?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            StatusText = "В главном окне укажите папку «Товар».";
            return;
        }

        var rootFull = Path.GetFullPath(root);
        var ignored = EditorIgnoredFoldersStore.GetIgnoredFolders(rootFull);
        var subs = Directory.GetDirectories(rootFull).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        if (subs.Count > 0)
        {
            foreach (var d in subs)
            {
                var ig = EditorIgnoredFoldersStore.IsUnderIgnoredFolder(rootFull, d, ignored);
                Folders.Add(new FolderListItemViewModel(Path.GetFileName(d) ?? d, d, ig));
            }
        }
        else
        {
            var ig = EditorIgnoredFoldersStore.IsUnderIgnoredFolder(rootFull, rootFull, ignored);
            Folders.Add(new FolderListItemViewModel("(эта папка)", rootFull, ig));
        }

        SelectedFolder = Folders.FirstOrDefault(f => !f.IsIgnored) ?? Folders.FirstOrDefault();
        var ignoredCount = Folders.Count(f => f.IsIgnored);
        StatusText = subs.Count > 0
            ? $"Подпапок: {subs.Count}" + (ignoredCount > 0 ? $", пропущено: {ignoredCount}." : ". Выберите папку.")
            : "В папке «Товар» нет подпапок — показаны файлы из корня.";
    }

    [RelayCommand]
    private void IgnoreSelectedFolder()
    {
        if (SelectedFolder is null)
            return;

        var root = _main.InputFolder?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            return;

        var rootFull = Path.GetFullPath(root);
        var ignored = EditorIgnoredFoldersStore.GetIgnoredFolders(rootFull);
        ignored.Add(SelectedFolder.FullPath);
        EditorIgnoredFoldersStore.SetIgnoredFolders(rootFull, ignored);
        ReloadFolderList();
        _main.RebuildMappingRowsFromEditor();
        StatusText = "Папка «" + SelectedFolder.DisplayName + "» исключена из очереди.";
    }

    [RelayCommand]
    private void RestoreSelectedFolder()
    {
        if (SelectedFolder is null || !SelectedFolder.IsIgnored)
            return;

        var root = _main.InputFolder?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            return;

        var rootFull = Path.GetFullPath(root);
        var ignored = EditorIgnoredFoldersStore.GetIgnoredFolders(rootFull);
        ignored.Remove(SelectedFolder.FullPath);
        EditorIgnoredFoldersStore.SetIgnoredFolders(rootFull, ignored);
        ReloadFolderList();
        _main.RebuildMappingRowsFromEditor();
        StatusText = "Папка «" + SelectedFolder.DisplayName + "» снова в очереди.";
    }

    [RelayCommand]
    private void SaveAsNewProfile()
    {
        var refF = _main.ReferenceFolder?.Trim() ?? string.Empty;
        var zonaF = _main.ZonaFolder?.Trim() ?? string.Empty;
        if (!Directory.Exists(refF) || !Directory.Exists(zonaF))
        {
            System.Windows.MessageBox.Show(
                "У текущего профиля нет папок reference и zona. Выберите профиль с данными или укажите папки в расширенном режиме.",
                "AutoRAW",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var nameDlg = new PromptDialog(
                "Новый профиль",
                "Имя профиля (%LocalAppData%\\AutoRAW\\user files\\Profile):",
                _main.SelectedProduct.DisplayName + " (копия)")
            { Owner = System.Windows.Application.Current?.MainWindow };
        if (nameDlg.ShowDialog() != true || string.IsNullOrWhiteSpace(nameDlg.Result))
            return;

        try
        {
            var color = MainViewModel.GetEffectiveColorFor(_main.SelectedProduct);
            var profile = UserProfileBundleService.WriteBundle(nameDlg.Result.Trim(), refF, zonaF, color);
            _main.NotifyUserProfileAdded(profile);
            StatusText = "Создан профиль «" + profile.DisplayName + "».";
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Не удалось сохранить профиль:\n{ex.Message}", "AutoRAW",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task ReloadFilesForFolderAsync(FolderListItemViewModel? folder)
    {
        FilesInFolder.Clear();
        if (folder is null)
            return;

        var list = ImageFileCatalog.ListImagesInFolder(folder.FullPath);
        foreach (var p in list)
            FilesInFolder.Add(new FileThumbItemViewModel(p));

        await LoadThumbnailsAsync();
    }

    private async Task LoadThumbnailsAsync()
    {
        const int t = 120;
        await Task.Run(() =>
        {
            foreach (var item in FilesInFolder)
            {
                try
                {
                    var bmp = CropPreviewBitmapFactory.LoadThumbnail(item.FilePath, t);
                    _dispatcher.Invoke(() => item.Thumbnail = bmp);
                }
                catch
                {
                    // миниатюра опциональна
                }
            }
        });
    }

    [RelayCommand]
    private void OpenFile(FileThumbItemViewModel? item)
    {
        if (item is null || !File.Exists(item.FilePath))
            return;

        PersistCurrentEditorFile();
        DisposeCroppedBase();

        RefreshShotStemChoices();

        EditorInputPath = item.FilePath;
        IsBrowseMode = false;

        var guess = ZonaOperationGuideParser.NormalizeShotStem(null, item.FilePath) ?? "01";
        SelectedShotStem = ShotStemChoices.Contains(guess) ? guess : ShotStemChoices.FirstOrDefault() ?? guess;

        LoadAdjustFromSessionOrStore();
        _ = RefreshPreviewsAsync();
    }

    private void RefreshShotStemChoices()
    {
        ShotStemChoices.Clear();
        foreach (var fn in _main.ReferenceFiles)
        {
            var s = Path.GetFileNameWithoutExtension(fn);
            if (!string.IsNullOrWhiteSpace(s))
                ShotStemChoices.Add(s);
        }

        if (ShotStemChoices.Count == 0)
        {
            for (var i = 1; i <= 8; i++)
                ShotStemChoices.Add(i.ToString("D2"));
        }
    }

    [RelayCommand]
    private void BackToBrowse()
    {
        PersistCurrentEditorFile();
        DisposeCroppedBase();
        IsBrowseMode = true;
        ResultPreview = null;
        ReferencePreview = null;
    }

    /// <summary>Освободить кэш Magick после выхода из редактора или смены источника.</summary>
    public void DisposeCropResources() => DisposeCroppedBase();

    private void DisposeCroppedBase()
    {
        lock (_cropCacheLock)
        {
            _editorFullImageCache?.Dispose();
            _editorFullImageCache = null;
            _editorFullImageCacheKey = null;
        }
    }

    /// <summary>Вызывается при закрытии окна — не терять правки текущего файла.</summary>
    public void PersistIfEditingForClose() => PersistCurrentEditorFile();

    /// <summary>Сохраняет правки текущего файла в память сессии и в manual_shot_adjust.json (как «только этот файл»).</summary>
    private void PersistCurrentEditorFile()
    {
        if (IsBrowseMode || string.IsNullOrEmpty(EditorInputPath))
            return;

        var key = NormEditorFileKey(EditorInputPath);
        var adj = BuildCurrentAdjust();
        _sessionByFile[key] = adj.Clone();
        ManualShotAdjustStore.SetForFile(EditorInputPath, adj);
    }

    private void LoadAdjustFromSessionOrStore()
    {
        if (string.IsNullOrEmpty(EditorInputPath))
            return;

        var key = NormEditorFileKey(EditorInputPath);
        if (_sessionByFile.TryGetValue(key, out var cached))
        {
            OffsetX = cached.OffsetX;
            OffsetY = cached.OffsetY;
            ZoomPercent = cached.ZoomPercent;
            RotationDeg = cached.RotationDeg;
            GridOverlay = cached.GridOverlay;
            return;
        }

        LoadAdjustFromStoreForCurrentStem();
    }

    private void LoadAdjustFromStoreForCurrentStem()
    {
        if (string.IsNullOrEmpty(EditorInputPath))
            return;

        var m = ManualShotAdjustStore.Resolve(
            _main.SelectedProduct.DisplayName,
            EditorInputPath,
            SelectedShotStem);

        OffsetX = m.OffsetX;
        OffsetY = m.OffsetY;
        ZoomPercent = m.ZoomPercent;
        RotationDeg = m.RotationDeg;
        GridOverlay = m.GridOverlay;
    }

    private static string NormEditorFileKey(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private ManualShotAdjust BuildCurrentAdjust() => new()
    {
        OffsetX = OffsetX,
        OffsetY = OffsetY,
        ZoomPercent = ZoomPercent,
        RotationDeg = RotationDeg,
        GridOverlay = GridOverlay
    };

    [RelayCommand]
    private void SaveForProfile()
    {
        if (string.IsNullOrEmpty(EditorInputPath))
            return;

        var stem = ZonaOperationGuideParser.NormalizeShotStem(SelectedShotStem, EditorInputPath)
            ?? SelectedShotStem.Trim();

        ManualShotAdjustStore.SetForProfile(
            _main.SelectedProduct.DisplayName,
            stem,
            BuildCurrentAdjust());

        StatusText = "Сохранено для профиля «" + _main.SelectedProduct.DisplayName + "», кадр " + stem + ".";
        if (!string.IsNullOrEmpty(EditorInputPath))
            _sessionByFile[NormEditorFileKey(EditorInputPath)] = BuildCurrentAdjust().Clone();
    }

    [RelayCommand]
    private void SaveForThisFileOnly()
    {
        if (string.IsNullOrEmpty(EditorInputPath))
            return;

        ManualShotAdjustStore.SetForFile(EditorInputPath, BuildCurrentAdjust());
        _sessionByFile[NormEditorFileKey(EditorInputPath)] = BuildCurrentAdjust().Clone();
        StatusText = "Сохранено только для этого файла.";
    }

    [RelayCommand]
    private void ResetAdjustments()
    {
        if (string.IsNullOrEmpty(EditorInputPath))
            return;

        var stem = ZonaOperationGuideParser.NormalizeShotStem(SelectedShotStem, EditorInputPath)
            ?? SelectedShotStem.Trim();

        ManualShotAdjustStore.ClearForFile(EditorInputPath);
        ManualShotAdjustStore.ClearForProfileStem(
            _main.SelectedProduct.DisplayName,
            stem);

        _sessionByFile.Remove(NormEditorFileKey(EditorInputPath));

        LoadAdjustFromStoreForCurrentStem();
        StatusText = "Сброшено (настройки профиля и файла для этого кадра).";
        SchedulePreviewRefresh();
    }

    private void SchedulePreviewRefresh()
    {
        if (!IsBrowseMode && !string.IsNullOrEmpty(EditorInputPath))
        {
            _previewDebounce.Stop();
            _previewDebounce.Start();
        }
    }

    public Task RefreshPreviewsAsync()
    {
        Interlocked.Increment(ref _previewSerial);
        return ExecuteRefreshPreviewsAsync();
    }

    private async Task ExecuteRefreshPreviewsAsync()
    {
        if (IsBrowseMode || string.IsNullOrEmpty(EditorInputPath))
            return;

        var serial = Volatile.Read(ref _previewSerial);
        var path = EditorInputPath;
        var stem = SelectedShotStem.Trim();
        var edge = (int)Math.Clamp(Math.Round(_main.AnalysisMaxEdge), 256, 8192);
        const int disp = 420;
        var color = MainViewModel.GetEffectiveColorFor(_main.SelectedProduct);
        var applyColor = _main.ApplyColorCorrection;
        var zonaDir = Directory.Exists(_main.ZonaFolder) ? _main.ZonaFolder : null;
        var refFolder = _main.ReferenceFolder;
        var manual = BuildCurrentAdjust();

        string? refPath = null;
        if (!string.IsNullOrWhiteSpace(refFolder))
        {
            var cand = Directory.EnumerateFiles(refFolder)
                .FirstOrDefault(f =>
                    ImageFileCatalog.IsImageFile(f)
                    && string.Equals(Path.GetFileNameWithoutExtension(f), stem, StringComparison.OrdinalIgnoreCase));
            refPath = cand;
        }

        var cacheKey = BuildEditorFullImageCacheKey(path, stem, edge, refPath);

        await Task.Run(() =>
        {
            BitmapSource? r = null;
            BitmapSource? a = null;
            var ow = 0;
            var oh = 0;
            var finished = false;

            var refW = 0;
            var refH = 0;
            if (refPath is not null && File.Exists(refPath))
            {
                var rm = AutoCropComputation.AnalyzeReference(refPath, edge);
                refW = (int)rm.RefW;
                refH = (int)rm.RefH;
            }

            try
            {
                if (refPath is not null && File.Exists(refPath))
                    r = CropPreviewBitmapFactory.LoadThumbnail(refPath, disp);

                MagickImage? fullRef;
                lock (_cropCacheLock)
                {
                    if (_editorFullImageCacheKey != cacheKey || _editorFullImageCache is null)
                    {
                        _editorFullImageCache?.Dispose();
                        _editorFullImageCache = CropPreviewBitmapFactory.TryLoadPreparedFullForManualFrame(
                            path, stem, edge, rotateCounterClockwise90: false);
                        _editorFullImageCacheKey = cacheKey;
                    }

                    fullRef = _editorFullImageCache;

                    if (fullRef is null || refW < 1 || refH < 1)
                    {
                        _ = _dispatcher.BeginInvoke(() =>
                        {
                            if (serial != Volatile.Read(ref _previewSerial))
                                return;
                            if (EditorInputPath != path)
                                return;
                            ReferencePreview = r;
                            ResultPreview = null;
                            ResultOutputWidth = 0;
                            ResultOutputHeight = 0;
                        });
                        finished = true;
                        return;
                    }
                }

                MagickImage? work = null;
                try
                {
                    work = ManualShotAdjustApplier.ComposeFromFullToReference(fullRef, manual, refW, refH);
                    CropPreviewBitmapFactory.CompositeGridForEditorPreview(work, zonaDir, manual.GridOverlay);
                    ColorCorrectionService.ApplyIfEnabled(work, color, applyColor);
                    a = CropPreviewBitmapFactory.ToBitmapSourceScaled(work, disp);
                    ow = refW;
                    oh = refH;
                }
                finally
                {
                    work?.Dispose();
                }
            }
            catch
            {
                // остаётся null
            }

            if (finished)
                return;

            _ = _dispatcher.BeginInvoke(() =>
            {
                if (serial != Volatile.Read(ref _previewSerial))
                    return;
                if (EditorInputPath != path)
                    return;

                ReferencePreview = r;
                ResultPreview = a;
                ResultOutputWidth = ow;
                ResultOutputHeight = oh;
            });
        });
    }

    private static string BuildEditorFullImageCacheKey(string editorPath, string stem, int edge, string? refPath)
    {
        static string Norm(string? p) =>
            string.IsNullOrEmpty(p) ? "" : Path.GetFullPath(p);

        return string.Join('|', Norm(editorPath), stem, edge.ToString(), Norm(refPath));
    }
}

/// <summary>Элемент списка сетки в редакторе (для привязки ComboBox).</summary>
public readonly record struct VisualEditorGridOption(ZonaGridOverlayKind Kind, string Display);

public partial class FolderListItemViewModel(string displayName, string fullPath, bool isIgnored = false) : ObservableObject
{
    public string DisplayName { get; } = displayName;
    public string FullPath { get; } = fullPath;

    [ObservableProperty] private bool _isIgnored = isIgnored;

    public string ListLabel => IsIgnored ? "⊘ " + DisplayName : DisplayName;
}

public partial class FileThumbItemViewModel(string filePath) : ObservableObject
{
    public string FilePath { get; } = filePath;

    [ObservableProperty] private BitmapSource? _thumbnail;

    public string FileLabel => Path.GetFileName(FilePath);
}
