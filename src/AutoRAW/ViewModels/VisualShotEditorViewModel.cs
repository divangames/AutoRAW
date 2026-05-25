using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AutoRAW.Models;
using AutoRAW.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageMagick;

namespace AutoRAW.ViewModels;

/// <summary>Режим списка файлов в браузере редактора.</summary>
public enum EditorBrowseFilterKind
{
    All,
    FilterByShotOrName
}

/// <summary>Окно: подпапки «Товар» → превью файлов → ручная подгонка кадра.</summary>
public partial class VisualShotEditorViewModel : ObservableObject
{
    /// <summary>Превью «Результат» в режиме правки: длинная сторона растра (при типичном референсе 4:3 ≈ 600×450 px).</summary>
    private const int EditorResultPreviewDisplayMaxEdge = 600;

    /// <summary>Миниатюра «Референс» в правой колонке — свой лимит загрузки (панель узкая, без лишних пикселей).</summary>
    private const int EditorReferencePreviewDisplayMaxEdge = 450;

    /// <summary>Декод исходника только для большого интерактивного превью (пакет и экспорт используют «Анализ» с главного окна).</summary>
    private const int EditorInteractiveSourceMaxEdge = 600;

    /// <summary>Параллельная сборка миниатюр списка (1 — щадящий режим для слабых ПК).</summary>
    private const int EditorBrowseThumbParallelism = 1;

    /// <summary>Кнопка автоподгонки и фон: не распаковываем 4K+ только ради OpenCV.</summary>
    private const int EditorAutoAlignLoadEdgeCap = 896;

    private readonly MainViewModel _main;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _previewDebounce;
    /// <summary>После паузы в правках — один проход превью с более высоким внутренним разрешением (только экран).</summary>
    private readonly DispatcherTimer _previewUpgradeTimer;

    /// <summary>Во время перетаскивания результата — не чаще одного дорогого compose за интервал (слабые ПК).</summary>
    private readonly DispatcherTimer _panComposeCoalesceTimer;

    private volatile bool _panComposeRefreshPending;

    /// <summary>Один lite-compose сразу при первом движении после зажатия ЛКМ, дальше — только коалесценция таймером.</summary>
    private bool _panComposeDidImmediateLiteThisDrag;

    /// <summary>Полный кадр (после AutoOrient и политики по стему) — дорогая загрузка, кэш по файлу.</summary>
    private readonly object _cropCacheLock = new();
    private MagickImage? _editorFullImageCache;
    private string? _editorFullImageCacheKey;

    /// <summary>Внутреннее разрешение compose для последнего запланированного превью (только UI).</summary>
    private volatile bool _previewDraftCompose;

    private bool _suppressPreviewUpgradeDuringPan;

    /// <summary>Не ставить debounce пока массово выставляем поля при открытии файла.</summary>
    private bool _suppressSchedulePreviewRefresh;

    /// <summary>Отбрасывать устаревшие результаты <see cref="Task.Run"/> при быстрых правках.</summary>
    private int _previewSerial;

    /// <summary>Число параллельных построений больших превью — чтобы не гасить спиннер до завершения последнего.</summary>
    private int _editorPreviewBusyDepth;

    /// <summary>Поколение фоновой авто-подгонки по списку папки — устаревшие проходы не пишут в json.</summary>
    private int _folderPrefetchGeneration;

    /// <summary>Кэш правок в сессии редактора (ключ = нормализованный путь файла).</summary>
    private readonly Dictionary<string, ManualShotAdjust> _sessionByFile =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ObservableCollection<FileThumbItemViewModel> _filesBacking = new();

    private readonly CollectionViewSource _filesViewSource;

    private readonly object _thumbCacheGate = new();

    private readonly Dictionary<string, (string Sig, BitmapSource Bmp)> _thumbBitmapCache =
        new(StringComparer.OrdinalIgnoreCase);

    private DispatcherTimer? _statusFlashClearTimer;

    private readonly EditorAdjustUndoStack _adjustUndo = new();
    private readonly DispatcherTimer _undoCommitTimer;
    private bool _applyingUndoRedo;

    public VisualShotEditorViewModel(MainViewModel main, Dispatcher dispatcher)
    {
        _main = main;
        _dispatcher = dispatcher;
        _filesViewSource = new CollectionViewSource { Source = _filesBacking };
        FilesView = _filesViewSource.View;
        FilesView.Filter = FilterFilesPredicate;
        _previewDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
        _previewDebounce.Tick += (_, _) =>
        {
            _previewDebounce.Stop();
            PersistCurrentEditorFile();
            if (!string.IsNullOrEmpty(EditorInputPath))
                RefreshThumbnailForPath(EditorInputPath);
            _main.RequestPreviewRefresh();
            var token = Interlocked.Increment(ref _previewSerial);
            _ = ExecuteRefreshPreviewsAsync(token, litePanCompose: false);
        };

        _previewUpgradeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        _previewUpgradeTimer.Tick += (_, _) =>
        {
            _previewUpgradeTimer.Stop();
            _previewDraftCompose = false;
            if (!IsBrowseMode && !string.IsNullOrEmpty(EditorInputPath))
            {
                PersistCurrentEditorFile();
                _main.RequestPreviewRefresh();
                _ = RefreshPreviewsAsync();
            }
        };

        _panComposeCoalesceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _panComposeCoalesceTimer.Tick += (_, _) =>
        {
            _panComposeCoalesceTimer.Stop();
            if (_suppressSchedulePreviewRefresh || IsBrowseMode || string.IsNullOrEmpty(EditorInputPath))
            {
                _panComposeRefreshPending = false;
                return;
            }

            if (!_panComposeRefreshPending)
                return;

            _panComposeRefreshPending = false;
            var token = Interlocked.Increment(ref _previewSerial);
            _ = ExecuteRefreshPreviewsAsync(token, litePanCompose: true);
        };

        _undoCommitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _undoCommitTimer.Tick += (_, _) =>
        {
            _undoCommitTimer.Stop();
            if (!_applyingUndoRedo && !IsBrowseMode)
                _adjustUndo.CommitSnapshot(BuildCurrentAdjust());
        };

        _folderSyncDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _folderSyncDebounce.Tick += async (_, _) =>
        {
            _folderSyncDebounce.Stop();
            var prefetch = _folderSyncWantsPrefetch;
            _folderSyncWantsPrefetch = false;
            var gen = Interlocked.Increment(ref _folderPrefetchGeneration);
            await SyncFilesFromFolderVisibilityAsync(gen, prefetch).ConfigureAwait(true);
        };
    }

    /// <summary>Список файлов с фильтром и группировкой по имени.</summary>
    public ICollectionView FilesView { get; }

    public MainViewModel Main => _main;

    public ObservableCollection<string> ShotStemChoices { get; } = new();

    /// <summary>Варианты сетки для ComboBox (не использовать Tag на ComboBoxItem — в WPF часто ломается привязка).</summary>
    public IReadOnlyList<VisualEditorGridOption> GridOverlayOptions { get; } =
    [
        new(ZonaGridOverlayKind.None, "Без сетки"),
        new(ZonaGridOverlayKind.LayoutRules, "Границы (правила макета)")
    ];

    [ObservableProperty] private bool _isBrowseMode = true;

    [ObservableProperty] private ObservableCollection<FolderTreeNodeViewModel> _folderTreeRoots = new();

    /// <summary>Пункты «Показать:» (без ComboBoxItem+Tag — стабильная привязка).</summary>
    public IReadOnlyList<EditorBrowseFilterOption> BrowseFilterOptions { get; } =
    [
        new(EditorBrowseFilterKind.All, "Все файлы"),
        new(EditorBrowseFilterKind.FilterByShotOrName, "По номеру / имени")
    ];

    private bool _filesScopeIsEntireGoods;

    /// <summary>Все файлы / фильтр по подстроке имени файла.</summary>
    [ObservableProperty] private EditorBrowseFilterKind _browseFilterKind = EditorBrowseFilterKind.All;

    /// <summary>Подстрока для фильтра по имени (без учёта регистра).</summary>
    [ObservableProperty] private string _browseFilterText = string.Empty;

    /// <summary>Группировать карточки по имени файла (разные папки с одинаковым именем — в одной группе).</summary>
    [ObservableProperty] private bool _browseGroupByFileName;

    /// <summary>Индикатор загрузки больших превью «Референс / Результат» в режиме правки.</summary>
    [ObservableProperty] private bool _isEditorLargePreviewBusy;

    /// <summary>Синхронизировать разворот дерева после пересборки (без перезагрузки файлов).</summary>
    public event Action<IReadOnlyList<FolderTreeNodeViewModel>>? SyncFolderSelectionAfterReload;

    [ObservableProperty] private bool _allFoldersShown = true;

    private bool _suppressFolderShownSync;
    private bool _suppressAllFoldersShown;
    private bool _folderSyncWantsPrefetch;
    private readonly DispatcherTimer _folderSyncDebounce;

    [ObservableProperty] private string? _editorInputPath;

    [ObservableProperty] private string _selectedShotStem = "01";

    [ObservableProperty] private double _offsetX;

    [ObservableProperty] private double _offsetY;

    [ObservableProperty] private double _zoomPercent = 100;

    [ObservableProperty] private double _rotationDeg;

    [ObservableProperty] private ZonaGridOverlayKind _gridOverlay = ZonaGridOverlayKind.LayoutRules;

    [ObservableProperty] private BitmapSource? _referencePreview;

    [ObservableProperty] private BitmapSource? _resultPreview;

    /// <summary>Ширина выходного кадра в пикселах (для перетаскивания).</summary>
    [ObservableProperty] private int _resultOutputWidth;

    /// <summary>Высота выходного кадра в пикселах.</summary>
    [ObservableProperty] private int _resultOutputHeight;

    [ObservableProperty] private string _statusText = string.Empty;

    /// <summary>Пользователь тащит превью результата мышью — не запускать HQ-таймер до отпускания.</summary>
    public void NotifyEditorResultPanStarted()
    {
        _suppressPreviewUpgradeDuringPan = true;
        _previewDraftCompose = true;
        _previewUpgradeTimer.Stop();
        _panComposeCoalesceTimer.Stop();
        _panComposeRefreshPending = false;
        _panComposeDidImmediateLiteThisDrag = false;
    }

    /// <summary>Закончили перетаскивание — после короткой паузы одно HQ-превью.</summary>
    public void NotifyEditorResultPanEnded()
    {
        _suppressPreviewUpgradeDuringPan = false;
        _panComposeCoalesceTimer.Stop();
        _panComposeRefreshPending = false;
        _panComposeDidImmediateLiteThisDrag = false;
        _previewDraftCompose = true;
        _previewUpgradeTimer.Stop();
        PersistCurrentEditorFile();
        if (!string.IsNullOrEmpty(EditorInputPath))
            RefreshThumbnailForPath(EditorInputPath);
        _main.RequestPreviewRefresh();
        _previewUpgradeTimer.Start();
    }

    /// <summary>Смещение в пикселах выхода; <paramref name="scaleX"/>/<paramref name="scaleY"/> — из UI (например outW/ActualWidth).</summary>
    public void ApplyDragDelta(double dxScreenPx, double dyScreenPx, double scaleX, double scaleY)
    {
        if (!_applyingUndoRedo)
            _adjustUndo.CommitSnapshot(BuildCurrentAdjust());
        // Иначе OnOffset* вызовет SchedulePreviewRefresh — второй compose на каждое движение мыши.
        _suppressSchedulePreviewRefresh = true;
        try
        {
            OffsetX += dxScreenPx * scaleX;
            OffsetY += dyScreenPx * scaleY;
        }
        finally
        {
            _suppressSchedulePreviewRefresh = false;
        }

        RefreshPreviewImmediateDraft();
    }

    /// <summary>Превью без задержки debounce (перетаскивание мышью по результату).</summary>
    private void RefreshPreviewImmediateDraft()
    {
        if (_suppressSchedulePreviewRefresh || IsBrowseMode || string.IsNullOrEmpty(EditorInputPath))
            return;

        _previewDraftCompose = true;
        _previewUpgradeTimer.Stop();
        if (!_suppressPreviewUpgradeDuringPan)
            _previewUpgradeTimer.Start();

        if (_suppressPreviewUpgradeDuringPan)
        {
            _panComposeRefreshPending = true;
            if (!_panComposeDidImmediateLiteThisDrag)
            {
                _panComposeDidImmediateLiteThisDrag = true;
                var burst = Interlocked.Increment(ref _previewSerial);
                _ = ExecuteRefreshPreviewsAsync(burst, litePanCompose: true);
            }

            _panComposeCoalesceTimer.Stop();
            _panComposeCoalesceTimer.Start();
            return;
        }

        var token = Interlocked.Increment(ref _previewSerial);
        _ = ExecuteRefreshPreviewsAsync(token, litePanCompose: false);
    }

    partial void OnIsBrowseModeChanged(bool value)
    {
        NotifyEditorProfileCommandsCanExecuteChanged();
        // Состояние файла сохраняется в BackToBrowse / OpenFile до смены режима
        if (value)
        {
            StopPreviewUiTimers();
            _suppressPreviewUpgradeDuringPan = false;
            EditorInputPath = null;
            IsEditorLargePreviewBusy = false;
        }
    }

    partial void OnBrowseFilterKindChanged(EditorBrowseFilterKind value)
    {
        RefreshFilesView();
    }

    partial void OnAllFoldersShownChanged(bool value)
    {
        if (_suppressAllFoldersShown)
            return;

        var root = FolderTreeRoots.FirstOrDefault();
        if (root is null)
            return;

        _suppressFolderShownSync = true;
        CascadeFolderShown(root, value);
        _suppressFolderShownSync = false;
        PersistIgnoredFromTree();
        ScheduleSyncFilesFromFolderVisibility();
        StatusText = value ? "Все папки показаны." : "Все папки скрыты — файлы не отображаются.";
    }

    partial void OnBrowseFilterTextChanged(string value) => RefreshFilesView();

    partial void OnBrowseGroupByFileNameChanged(bool value) => RefreshFilesView();

    partial void OnEditorInputPathChanged(string? value)
    {
        OnPropertyChanged(nameof(SameFileNameMenuHeader));
        NotifyEditorProfileCommandsCanExecuteChanged();
    }

    private void NotifyEditorProfileCommandsCanExecuteChanged()
    {
        PersistAdjustmentsForCurrentFileCommand.NotifyCanExecuteChanged();
        ApplyAdjustmentsToSameFileNameCommand.NotifyCanExecuteChanged();
        SaveStemForProfileCommand.NotifyCanExecuteChanged();
        ResetAdjustmentsCommand.NotifyCanExecuteChanged();
        ClearProfileBasenameRuleCommand.NotifyCanExecuteChanged();
        UndoAdjustCommand.NotifyCanExecuteChanged();
        RedoAdjustCommand.NotifyCanExecuteChanged();
    }

    private void QueueUndoCommit()
    {
        if (_applyingUndoRedo || IsBrowseMode)
            return;
        _undoCommitTimer.Stop();
        _undoCommitTimer.Start();
    }

    private void ApplyAdjustSnapshot(ManualShotAdjust m)
    {
        _applyingUndoRedo = true;
        _suppressSchedulePreviewRefresh = true;
        try
        {
            OffsetX = m.OffsetX;
            OffsetY = m.OffsetY;
            ZoomPercent = m.ZoomPercent;
            RotationDeg = m.RotationDeg;
            GridOverlay = NormalizeGridOverlay(m.GridOverlay);
        }
        finally
        {
            _suppressSchedulePreviewRefresh = false;
            _applyingUndoRedo = false;
        }

        SchedulePreviewRefresh();
        NotifyEditorProfileCommandsCanExecuteChanged();
    }

    /// <summary>Подпись пункта меню «применить ко всем с тем же именем файла».</summary>
    public string SameFileNameMenuHeader =>
        string.IsNullOrEmpty(EditorInputPath)
            ? "Только к файлам с тем же именем"
            : $"Только к «{Path.GetFileName(EditorInputPath)}» фотографиям";

    private void RefreshFilesView()
    {
        FilesView.GroupDescriptions.Clear();
        if (BrowseGroupByFileName)
            FilesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FileThumbItemViewModel.FileNameOnly)));
        else if (ShouldGroupFilesByFolder())
            FilesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FileThumbItemViewModel.FolderGroupHeader)));
        FilesView.Refresh();
    }

    private bool ShouldGroupFilesByFolder()
    {
        var root = FolderTreeRoots.FirstOrDefault();
        return _filesScopeIsEntireGoods
               || (root is not null && CountShownFolderNodes(root) > 1);
    }

    private bool FilterFilesPredicate(object obj)
    {
        if (obj is not FileThumbItemViewModel item)
            return false;
        if (BrowseFilterKind != EditorBrowseFilterKind.FilterByShotOrName)
            return true;

        var q = BrowseFilterText.Trim();
        if (string.IsNullOrEmpty(q))
            return true;

        if (int.TryParse(q, out var wantShot)
            && InputShotNumberParser.TryParse(item.FilePath, out var fileShot))
            return fileShot == wantShot;

        var fileName = Path.GetFileName(item.FilePath);
        return fileName.Contains(q, StringComparison.OrdinalIgnoreCase)
               || Path.GetFileNameWithoutExtension(fileName).Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void FlashStatus(string message, int clearAfterMs = 4500)
    {
        StatusText = message;
        _statusFlashClearTimer?.Stop();
        _statusFlashClearTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(clearAfterMs) };
        _statusFlashClearTimer.Tick += (_, _) =>
        {
            _statusFlashClearTimer.Stop();
            if (StatusText == message)
                StatusText = string.Empty;
        };
        _statusFlashClearTimer.Start();
    }

    partial void OnOffsetXChanged(double value)
    {
        QueueUndoCommit();
        SchedulePreviewRefresh();
    }

    partial void OnOffsetYChanged(double value)
    {
        QueueUndoCommit();
        SchedulePreviewRefresh();
    }

    partial void OnZoomPercentChanged(double value)
    {
        QueueUndoCommit();
        SchedulePreviewRefresh();
    }

    partial void OnRotationDegChanged(double value)
    {
        QueueUndoCommit();
        SchedulePreviewRefresh();
    }

    partial void OnGridOverlayChanged(ZonaGridOverlayKind value)
    {
        QueueUndoCommit();
        SchedulePreviewRefresh();
    }

    partial void OnSelectedShotStemChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(EditorInputPath))
        {
            LoadAdjustFromStoreForCurrentStem();
            if (IsCurrentAdjustIdentity())
                _ = TryAutoAlignAsync();

            // Выбор номера кадра в комбобоксе — то же семантическое действие, что и для имени файла после кропа:
            // сразу фиксируем эталон, иначе в режиме «Стандартный» очередь пересчитает выход как 01,02,03 по порядку.
            if (!IsBrowseMode && !string.IsNullOrWhiteSpace(value))
                RecordCropFrameChoiceHint(EditorInputPath, value);
        }

        SchedulePreviewRefresh();
        ClearProfileBasenameRuleCommand.NotifyCanExecuteChanged();
    }

    private static int CountTreeNodes(FolderTreeNodeViewModel node)
    {
        var n = 1;
        foreach (var c in node.Children)
            n += CountTreeNodes(c);
        return n;
    }

    private static int CountHiddenInTree(FolderTreeNodeViewModel node)
    {
        var n = node.IsShown ? 0 : 1;
        foreach (var c in node.Children)
            n += CountHiddenInTree(c);
        return n;
    }

    private static int CountShownFolderNodes(FolderTreeNodeViewModel node)
    {
        var n = node.IsShown ? 1 : 0;
        foreach (var c in node.Children)
            n += CountShownFolderNodes(c);
        return n;
    }

    private static bool HasAnyHiddenFolder(FolderTreeNodeViewModel node)
    {
        if (!node.IsShown)
            return true;
        foreach (var c in node.Children)
        {
            if (HasAnyHiddenFolder(c))
                return true;
        }

        return false;
    }

    private static void CollectHiddenFolderPaths(FolderTreeNodeViewModel node, HashSet<string> into)
    {
        if (!node.IsShown)
            into.Add(node.FullPath);
        foreach (var c in node.Children)
            CollectHiddenFolderPaths(c, into);
    }

    private static void CascadeFolderShown(FolderTreeNodeViewModel node, bool shown)
    {
        node.IsShown = shown;
        foreach (var c in node.Children)
            CascadeFolderShown(c, shown);
    }

    private static void WireFolderTreeHandlers(
        FolderTreeNodeViewModel node,
        Action<FolderTreeNodeViewModel> onShownChanged)
    {
        node.SetShownChangedHandler(onShownChanged);
        foreach (var c in node.Children)
            WireFolderTreeHandlers(c, onShownChanged);
    }

    private void PopulateFolderTreeChildren(
        FolderTreeNodeViewModel parent,
        string directory,
        string goodsRootFull,
        HashSet<string> ignored)
    {
        foreach (var sub in Directory.GetDirectories(directory).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var hidden = EditorIgnoredFoldersStore.IsUnderIgnoredFolder(goodsRootFull, sub, ignored);
            var name = Path.GetFileName(sub) ?? sub;
            var child = new FolderTreeNodeViewModel(name, sub, isGoodsRoot: false, isShown: !hidden, parent);
            parent.Children.Add(child);
            PopulateFolderTreeChildren(child, sub, goodsRootFull, ignored);
        }
    }

    public void ReloadFolderList()
    {
        FolderTreeRoots.Clear();
        var root = _main.InputFolder?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            StatusText = "В главном окне укажите папку «Товар».";
            return;
        }

        var rootFull = Path.GetFullPath(root);
        var ignored = EditorIgnoredFoldersStore.GetIgnoredFolders(rootFull);
        var rootLabel = Path.GetFileName(rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(rootLabel))
            rootLabel = rootFull;

        var rootHidden = EditorIgnoredFoldersStore.IsUnderIgnoredFolder(rootFull, rootFull, ignored);
        var rootNode = new FolderTreeNodeViewModel(rootLabel, rootFull, isGoodsRoot: true, isShown: !rootHidden);
        PopulateFolderTreeChildren(rootNode, rootFull, rootFull, ignored);
        FolderTreeRoots.Add(rootNode);
        WireFolderTreeHandlers(rootNode, OnFolderShownChanged);
        UpdateAllFoldersShownFromTree();

        SyncFolderSelectionAfterReload?.Invoke([rootNode]);
        _filesBacking.Clear();
        ScheduleSyncFilesFromFolderVisibility(wantsPrefetch: true);

        var nodeCount = CountTreeNodes(rootNode) - 1;
        var hiddenCount = CountHiddenInTree(rootNode);
        StatusText = nodeCount > 0
            ? $"Дерево: корень + {nodeCount} подпапок"
              + (hiddenCount > 0 ? $", скрыто: {hiddenCount}." : ".")
            : "Только корень «Товар».";
    }

    private void OnFolderShownChanged(FolderTreeNodeViewModel node)
    {
        if (_suppressFolderShownSync)
            return;

        if (!node.IsShown)
        {
            _suppressFolderShownSync = true;
            foreach (var c in node.Children)
                CascadeFolderShown(c, false);
            _suppressFolderShownSync = false;
        }

        PersistIgnoredFromTree();
        UpdateAllFoldersShownFromTree();
        ScheduleSyncFilesFromFolderVisibility();

        var root = FolderTreeRoots.FirstOrDefault();
        var hidden = root is null ? 0 : CountHiddenInTree(root);
        StatusText = hidden > 0
            ? $"Скрыто папок: {hidden} (файлы этих веток не показываются)."
            : "Все папки показаны.";
    }

    private void UpdateAllFoldersShownFromTree()
    {
        var root = FolderTreeRoots.FirstOrDefault();
        if (root is null)
            return;

        _suppressAllFoldersShown = true;
        AllFoldersShown = !HasAnyHiddenFolder(root);
        _suppressAllFoldersShown = false;
    }

    private void PersistIgnoredFromTree()
    {
        var root = FolderTreeRoots.FirstOrDefault();
        var goodsRoot = _main.InputFolder?.Trim() ?? string.Empty;
        if (root is null || string.IsNullOrEmpty(goodsRoot) || !Directory.Exists(goodsRoot))
            return;

        var rootFull = Path.GetFullPath(goodsRoot);
        var hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectHiddenFolderPaths(root, hidden);
        EditorIgnoredFoldersStore.SetIgnoredFolders(rootFull, hidden);
        _main.RebuildMappingRowsFromEditor();
    }

    private void ScheduleSyncFilesFromFolderVisibility(bool wantsPrefetch = false)
    {
        if (wantsPrefetch)
            _folderSyncWantsPrefetch = true;
        _folderSyncDebounce.Stop();
        _folderSyncDebounce.Start();
    }

    private async Task SyncFilesFromFolderVisibilityAsync(int syncGeneration, bool runPrefetch)
    {
        var root = FolderTreeRoots.FirstOrDefault();
        if (root is null)
            return;

        var goodsRootRaw = _main.InputFolder?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(goodsRootRaw) || !Directory.Exists(goodsRootRaw))
            return;

        var goodsRootFull = Path.GetFullPath(goodsRootRaw);
        var hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectHiddenFolderPaths(root, hidden);

        var newPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in ImageFileCatalog.ListImagesRecursiveUnderSubtree(goodsRootFull, goodsRootFull))
        {
            if (!EditorIgnoredFoldersStore.IsUnderIgnoredFolder(goodsRootFull, p, hidden))
                newPaths.Add(p);
        }

        var removed = _filesBacking.Where(i => !newPaths.Contains(i.FilePath)).ToList();
        foreach (var item in removed)
            _filesBacking.Remove(item);

        var addedItems = new List<FileThumbItemViewModel>();
        foreach (var path in newPaths)
        {
            if (_filesBacking.Any(i => i.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase)))
                continue;
            var item = CreateFileThumbItem(path, goodsRootFull);
            _filesBacking.Add(item);
            addedItems.Add(item);
        }

        _filesScopeIsEntireGoods = BrowseFilterKind == EditorBrowseFilterKind.All && !HasAnyHiddenFolder(root);
        RefreshFilesView();

        if (syncGeneration != Volatile.Read(ref _folderPrefetchGeneration))
            return;

        if (addedItems.Count > 0)
            await LoadThumbnailsForItemsAsync(addedItems).ConfigureAwait(true);

        if (!runPrefetch || syncGeneration != Volatile.Read(ref _folderPrefetchGeneration))
            return;

        var paths = _filesBacking.Select(i => i.FilePath).ToList();
        _ = RunFolderAutoAlignPrefetchAsync(syncGeneration, paths);
    }

    [RelayCommand]
    private async Task SaveAsNewProfile()
    {
        var refF = _main.ReferenceFolder?.Trim() ?? string.Empty;
        var zonaF = _main.ZonaFolder?.Trim() ?? string.Empty;
        if (!Directory.Exists(refF) || !Directory.Exists(zonaF))
        {
            System.Windows.MessageBox.Show(
                "У текущего профиля нет папок reference и zona. Выберите профиль с данными или добавьте свой профиль через меню «Профиль».",
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
            var profile = await UserProfileBundleService.WriteBundleAsync(
                nameDlg.Result.Trim(),
                refF,
                zonaF,
                color).ConfigureAwait(true);
            _main.NotifyUserProfileAdded(profile);
            FlashStatus("Профиль «" + profile.DisplayName + "» создан и добавлен в меню.");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Не удалось сохранить профиль:\n{ex.Message}", "AutoRAW",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Фоном: для файлов без сохранённых правок считает авто-подгонку (как кнопка в редакторе) и пишет per-file одним save.
    /// Пока идёт пакетное кадрирование (<see cref="MainViewModel.IsBusy"/>), ждёт — чтобы не спорить с очередью за LibRaw/CPU.
    /// </summary>
    private async Task RunFolderAutoAlignPrefetchAsync(int generation, List<string> paths)
    {
        if (paths.Count == 0 || generation != Volatile.Read(ref _folderPrefetchGeneration))
            return;

        var profile = _main.SelectedProduct.DisplayName?.Trim();
        if (string.IsNullOrEmpty(profile))
            return;

        var refRoot = _main.ReferenceFolder?.Trim();
        if (string.IsNullOrWhiteSpace(refRoot) || !Directory.Exists(refRoot))
            return;

        var zonaDir = Directory.Exists(_main.ZonaFolder) ? _main.ZonaFolder : null;
        var edgeConfigured = (int)Math.Clamp(Math.Round(_main.AnalysisMaxEdge), 256, 8192);
        var alignEdge = Math.Min(edgeConfigured, EditorAutoAlignLoadEdgeCap);

        var refByStem = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Directory.EnumerateFiles(refRoot))
        {
            if (!ImageFileCatalog.IsImageFile(f))
                continue;
            var stem = Path.GetFileNameWithoutExtension(f);
            if (!string.IsNullOrWhiteSpace(stem))
                refByStem[stem] = f;
        }

        if (refByStem.Count == 0)
            return;

        string? activePathFull = null;
        var isBrowse = true;
        await _dispatcher.InvokeAsync(() =>
        {
            isBrowse = IsBrowseMode;
            if (!isBrowse && !string.IsNullOrEmpty(EditorInputPath))
                activePathFull = NormEditorFileKey(EditorInputPath);
        });

        var activeNorm = activePathFull;

        AppPipelineHeavyGate.Enter();
        try
        {
            var bag = new ConcurrentBag<(string Path, ManualShotAdjust Adjust)>();

            try
            {
                await Parallel.ForEachAsync(
                    paths,
                    new ParallelOptions { MaxDegreeOfParallelism = 1 },
                    async (path, ct) =>
                    {
                        if (generation != Volatile.Read(ref _folderPrefetchGeneration))
                            return;

                        while (_main.IsBusy)
                        {
                            if (generation != Volatile.Read(ref _folderPrefetchGeneration))
                                return;
                            await Task.Delay(120).ConfigureAwait(false);
                        }

                        if (generation != Volatile.Read(ref _folderPrefetchGeneration))
                            return;

                        if (!File.Exists(path))
                            return;

                        var pathKey = NormEditorFileKey(path);
                        if (!isBrowse && activeNorm is not null
                            && string.Equals(pathKey, activeNorm, StringComparison.OrdinalIgnoreCase))
                            return;

                        if (ManualShotAdjustStore.TryGetPerFile(path, out _))
                            return;

                        if (ManualShotAdjustStore.TryGetProfileBasename(profile, path, out _))
                            return;

                        var stem = ZonaOperationGuideParser.NormalizeShotStem(null, path);
                        stem ??= "01";
                        if (ManualShotAutoAlignService.IsSkippedStem(stem))
                            return;

                        if (ManualShotAdjustStore.TryGetProfileStem(profile, stem, out var profStem)
                            && !profStem.IsIdentity)
                            return;

                        if (!refByStem.TryGetValue(stem, out var refPath) || string.IsNullOrEmpty(refPath))
                            return;

                        ManualShotAdjust? toStore = null;
                        try
                        {
                            await Task.Run(() =>
                            {
                                using var full = CropPreviewBitmapFactory.TryLoadPreparedFullForManualFrame(
                                    path, stem, alignEdge, rotateCounterClockwise90: false);
                                if (full is null)
                                    return;

                                if (!ManualShotAutoAlignService.TryCompute(
                                        full,
                                        refPath,
                                        stem,
                                        zonaDir,
                                        alignEdge,
                                        out var outcome))
                                    return;

                                toStore = outcome.Adjust.Clone();
                                if (ManualShotAdjustStore.TryGetProfileStem(profile, stem, out var pf))
                                    toStore.GridOverlay = pf.GridOverlay;
                            }).ConfigureAwait(false);
                        }
                        catch
                        {
                            /* пропускаем один файл */
                        }

                        var adj = toStore;
                        if (adj is null || !adj.HasPersistableState)
                            return;

                        bag.Add((path, adj));
                    }).ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            if (generation != Volatile.Read(ref _folderPrefetchGeneration))
                return;

            var merged = bag.ToArray();
            if (merged.Length == 0)
                return;

            var written = ManualShotAdjustStore.UpsertPerFileBatchSkipExisting(merged);

            await _dispatcher.InvokeAsync(() =>
            {
                if (generation != Volatile.Read(ref _folderPrefetchGeneration))
                    return;

                if (written > 0)
                {
                    foreach (var (path, _) in merged)
                        InvalidateThumbCache(path);
                    _ = RefreshThumbnailsForPathsAsync(merged.Select(m => m.Path).ToList());
                    _main.RequestMappingStatusRefresh();
                    FlashStatus(
                        $"Авто-подгонка по эталону (фон): записано правок для {written} файл(ов). Кадрирование может идти без ожидания расчёта.",
                        clearAfterMs: 6500);
                }
            }, DispatcherPriority.Background);
        }
        finally
        {
            AppPipelineHeavyGate.Leave();
        }
    }

    private FileThumbItemViewModel CreateFileThumbItem(string path, string goodsRootFull)
    {
        var item = new FileThumbItemViewModel(path, this);
        item.FolderGroupHeader = BuildFolderGroupHeader(path, goodsRootFull);
        return item;
    }

    private static string BuildFolderGroupHeader(string filePath, string goodsRootFull)
    {
        if (string.IsNullOrEmpty(goodsRootFull))
            return Path.GetDirectoryName(filePath) ?? "(файлы)";

        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (string.IsNullOrEmpty(dir))
            return "(файлы)";

        var rel = Path.GetRelativePath(goodsRootFull, dir);
        if (string.IsNullOrEmpty(rel) || rel == ".")
            return "(корень)";

        return rel.Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private async Task LoadThumbnailsForItemsAsync(IReadOnlyList<FileThumbItemViewModel> items)
    {
        if (items.Count == 0)
            return;

        var needLoad = new List<FileThumbItemViewModel>();
        await _dispatcher.InvokeAsync(() =>
        {
            foreach (var item in items)
            {
                if (TryApplyCachedThumbnailToItem(item))
                    continue;
                if (item.Thumbnail is not null && !item.IsThumbLoading)
                    continue;
                item.IsThumbLoading = true;
                needLoad.Add(item);
            }
        }, DispatcherPriority.Normal);

        if (needLoad.Count == 0)
            return;

        await Parallel.ForEachAsync(
            needLoad,
            new ParallelOptions { MaxDegreeOfParallelism = EditorBrowseThumbParallelism },
            async (item, ct) =>
            {
                BitmapSource? bmp = null;
                try
                {
                    bmp = await Task.Run(() => TryBuildCompositeThumbnailBitmap(item.FilePath), ct)
                        .ConfigureAwait(false);
                }
                catch
                {
                    /* ignore */
                }

                var assigned = bmp;
                await _dispatcher.InvokeAsync(() =>
                {
                    item.IsThumbLoading = false;
                    item.Thumbnail = assigned;
                }, DispatcherPriority.Background, ct);
            }).ConfigureAwait(true);
    }

    private bool TryApplyCachedThumbnailToItem(FileThumbItemViewModel item)
    {
        var stem = ZonaOperationGuideParser.NormalizeShotStem(null, item.FilePath) ?? "01";
        var manual = ManualShotAdjustStore.Resolve(_main.SelectedProduct.DisplayName, item.FilePath, stem);
        var sig = BuildThumbSignature(stem, manual);

        lock (_thumbCacheGate)
        {
            if (_thumbBitmapCache.TryGetValue(item.FilePath, out var hit) && hit.Sig == sig)
            {
                item.Thumbnail = hit.Bmp;
                item.IsThumbLoading = false;
                return true;
            }
        }

        return false;
    }

    private async Task RefreshThumbnailsForPathsAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            return;

        await Parallel.ForEachAsync(
            paths,
            new ParallelOptions { MaxDegreeOfParallelism = EditorBrowseThumbParallelism },
            async (path, ct) =>
            {
                var item = _filesBacking.FirstOrDefault(i =>
                    i.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase));
                if (item is null)
                    return;

                item.IsThumbLoading = true;
                BitmapSource? bmp = null;
                try
                {
                    bmp = await Task.Run(() => TryBuildCompositeThumbnailBitmap(path), ct).ConfigureAwait(false);
                }
                catch
                {
                    /* ignore */
                }

                var assigned = bmp;
                await _dispatcher.InvokeAsync(() =>
                {
                    item.IsThumbLoading = false;
                    item.Thumbnail = assigned;
                }, DispatcherPriority.Background, ct);
            }).ConfigureAwait(true);
    }

    private BitmapSource? TryBuildCompositeThumbnailBitmap(string path)
    {
        var stem = ZonaOperationGuideParser.NormalizeShotStem(null, path) ?? "01";
        var manual = ManualShotAdjustStore.Resolve(_main.SelectedProduct.DisplayName, path, stem);
        var sig = BuildThumbSignature(stem, manual);

        lock (_thumbCacheGate)
        {
            if (_thumbBitmapCache.TryGetValue(path, out var hit) && hit.Sig == sig)
                return hit.Bmp;
        }

        var bmp = BuildCompositeThumbnailUncached(path, stem, manual);
        if (bmp is not null)
        {
            lock (_thumbCacheGate)
                _thumbBitmapCache[path] = (sig, bmp);
        }

        return bmp;
    }

    private string BuildThumbSignature(string stem, ManualShotAdjust manual)
    {
        static string AdjSig(ManualShotAdjust m) =>
            $"{m.OffsetX:R}|{m.OffsetY:R}|{m.ZoomPercent:R}|{m.RotationDeg:R}";

        var rf = _main.ReferenceFolder?.Trim() ?? "";
        return
            $"{stem}|browse|{CropPreviewBitmapFactory.EditorBrowseThumbLoadEdge}|{_main.SelectedProduct.DisplayName}|{AdjSig(manual)}|grid|{rf}";
    }

    private BitmapSource? BuildCompositeThumbnailUncached(string path, string stem, ManualShotAdjust manual)
    {
        var refPath = ResolveReferencePathForStem(stem);
        if (refPath is null)
            return CropPreviewBitmapFactory.LoadThumbnail(path, CropPreviewBitmapFactory.EditorBrowseThumbDisplayEdge);

        return CropPreviewBitmapFactory.LoadEditorBrowseThumbnail(
            path,
            refPath,
            stem,
            _main.SelectedProduct.DisplayName,
            manual);
    }

    private string? ResolveReferencePathForStem(string stem)
    {
        var refFolder = _main.ReferenceFolder?.Trim();
        if (string.IsNullOrEmpty(refFolder) || !Directory.Exists(refFolder))
            return null;

        return Directory.EnumerateFiles(refFolder)
            .FirstOrDefault(f =>
                ImageFileCatalog.IsImageFile(f)
                && string.Equals(Path.GetFileNameWithoutExtension(f), stem, StringComparison.OrdinalIgnoreCase));
    }

    private void InvalidateThumbCache(string path)
    {
        lock (_thumbCacheGate)
            _thumbBitmapCache.Remove(path);
    }

    private void RefreshThumbnailForPath(string path)
    {
        var full = NormEditorFileKey(path);
        var item = _filesBacking.FirstOrDefault(i =>
            string.Equals(NormEditorFileKey(i.FilePath), full, StringComparison.OrdinalIgnoreCase));
        if (item is null)
            return;

        InvalidateThumbCache(item.FilePath);
        item.IsThumbLoading = true;
        _ = Task.Run(() =>
        {
            var bmp = TryBuildCompositeThumbnailBitmap(item.FilePath);
            _dispatcher.Invoke(() =>
            {
                item.IsThumbLoading = false;
                item.Thumbnail = bmp;
            }, DispatcherPriority.Background);
        });
    }

    /// <summary>Переименовать файл на диске и обновить списки.</summary>
    public bool TryRenameThumbFile(FileThumbItemViewModel item, string newNameRaw)
    {
        if (string.IsNullOrWhiteSpace(newNameRaw))
            return false;

        var dir = Path.GetDirectoryName(item.FilePath);
        if (string.IsNullOrEmpty(dir))
            return false;

        var trimmed = newNameRaw.Trim();
        var targetFileName = Path.HasExtension(trimmed)
            ? trimmed
            : Path.GetFileNameWithoutExtension(trimmed) + Path.GetExtension(item.FilePath);

        if (targetFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;

        var dest = Path.Combine(dir, targetFileName);
        var oldPath = item.FilePath;
        var oldFull = NormEditorFileKey(oldPath);
        var newFull = NormEditorFileKey(dest);
        if (string.Equals(oldFull, newFull, StringComparison.OrdinalIgnoreCase))
            return true;

        if (File.Exists(dest))
            return false;

        File.Move(oldPath, dest);

        ManualShotAdjustStore.RenamePerFileKey(oldPath, dest);
        PersistedCropFrameChoiceStore.RenamePathKey(oldPath, dest);

        var root = _main.InputFolder?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
            EditorSkippedFilesStore.RenameSkippedPath(Path.GetFullPath(root), oldPath, dest);

        if (_sessionByFile.TryGetValue(oldFull, out var cachedAdj))
        {
            _sessionByFile.Remove(oldFull);
            _sessionByFile[newFull] = cachedAdj.Clone();
        }

        InvalidateThumbCache(oldPath);
        lock (_thumbCacheGate)
            _thumbBitmapCache.Remove(NormEditorFileKey(dest));

        item.FilePath = dest;

        if (string.Equals(EditorInputPath, oldPath, StringComparison.OrdinalIgnoreCase))
            EditorInputPath = dest;

        _main.RebuildMappingRowsFromEditor();
        RefreshFilesView();
        FlashStatus($"Файл переименован в «{Path.GetFileName(dest)}».");
        return true;
    }

    public void BeginRenameFile(FileThumbItemViewModel item)
    {
        foreach (var x in _filesBacking)
        {
            if (!ReferenceEquals(x, item))
            {
                x.IsRenaming = false;
                x.RenameDraft = string.Empty;
            }
        }

        item.RenameDraft = item.FileLabel;
        item.IsRenaming = true;
    }

    public void CancelRenameFile(FileThumbItemViewModel item)
    {
        item.IsRenaming = false;
        item.RenameDraft = string.Empty;
    }

    public void CommitRenameFile(FileThumbItemViewModel item)
    {
        var draft = item.RenameDraft.Trim();
        item.IsRenaming = false;
        item.RenameDraft = string.Empty;
        if (string.IsNullOrEmpty(draft) || draft.Equals(item.FileLabel, StringComparison.OrdinalIgnoreCase))
            return;

        if (!TryRenameThumbFile(item, draft))
            FlashStatus("Не удалось переименовать (имя занято или недопустимо).");
    }

    [RelayCommand]
    private void OpenFile(FileThumbItemViewModel? item)
    {
        if (item is null || !File.Exists(item.FilePath))
            return;

        PersistCurrentEditorFile();
        DisposeCroppedBase();

        RefreshShotStemChoices();

        _suppressSchedulePreviewRefresh = true;
        try
        {
            EditorInputPath = item.FilePath;
            IsBrowseMode = false;

            var guess = ZonaOperationGuideParser.NormalizeShotStem(null, item.FilePath) ?? "01";
            SelectedShotStem = ShotStemChoices.Contains(guess) ? guess : ShotStemChoices.FirstOrDefault() ?? guess;

            LoadAdjustFromSessionOrStore();
            _adjustUndo.Reset(BuildCurrentAdjust());
            if (IsCurrentAdjustIdentity())
                _ = TryAutoAlignAsync();
        }
        finally
        {
            _suppressSchedulePreviewRefresh = false;
        }

        NotifyEditorProfileCommandsCanExecuteChanged();

        StopPreviewUiTimers();
        _previewDraftCompose = false;
        _suppressPreviewUpgradeDuringPan = false;
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

    private void StopPreviewUiTimers()
    {
        _previewDebounce.Stop();
        _previewUpgradeTimer.Stop();
        _panComposeCoalesceTimer.Stop();
        _panComposeRefreshPending = false;
    }

    private void MarkDraftPreviewNeededAndMaybeScheduleUpgrade()
    {
        _previewDraftCompose = true;
        if (!_suppressPreviewUpgradeDuringPan)
        {
            _previewUpgradeTimer.Stop();
            _previewUpgradeTimer.Start();
        }
    }

    private void DisposeCroppedBase()
    {
        StopPreviewUiTimers();
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
        RecordCropFrameChoiceForCurrentFile();
    }

    private void RecordCropFrameChoiceForCurrentFile()
    {
        if (IsBrowseMode || string.IsNullOrEmpty(EditorInputPath))
            return;

        RecordCropFrameChoiceHint(EditorInputPath!, SelectedShotStem);
    }

    /// <summary>Чтобы после пропуска части файлов пакет не переименовывал первый оставшийся как 01: фиксируем эталон по имени входа.</summary>
    private void RecordCropFrameChoiceHint(string inputPath, string stemRaw)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            return;

        var trimmed = stemRaw.Trim();
        var stem = (ZonaOperationGuideParser.NormalizeShotStem(trimmed, inputPath) ?? trimmed).Trim();
        if (string.IsNullOrEmpty(stem))
            return;

        var refFolder = _main.ReferenceFolder?.Trim();
        if (string.IsNullOrEmpty(refFolder) || !Directory.Exists(refFolder))
            return;

        foreach (var f in Directory.EnumerateFiles(refFolder))
        {
            if (!ImageFileCatalog.IsImageFile(f))
                continue;
            if (!string.Equals(Path.GetFileNameWithoutExtension(f), stem, StringComparison.OrdinalIgnoreCase))
                continue;

            PersistedCropFrameChoiceStore.Set(NormEditorFileKey(inputPath), Path.GetFileName(f)!);
            return;
        }
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
            GridOverlay = NormalizeGridOverlay(cached.GridOverlay);
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
        GridOverlay = NormalizeGridOverlay(m.GridOverlay);
    }

    private static ZonaGridOverlayKind NormalizeGridOverlay(ZonaGridOverlayKind kind) =>
        kind == ZonaGridOverlayKind.LegacyRules020408 ? ZonaGridOverlayKind.LayoutRules : kind;

    private bool IsCurrentAdjustIdentity() => BuildCurrentAdjust().IsIdentity;

    /// <summary>Авто-подгонка: без привязки CanExecute у RelayCommand — иначе клик может молча игнорироваться, если не переоценили доступность команды.</summary>
    [RelayCommand]
    private void RunAutoAlign()
    {
        if (IsBrowseMode)
        {
            FlashStatus("Сначала откройте фотографию из списка — авто-подгонка только в режиме правки.");
            return;
        }

        if (string.IsNullOrEmpty(EditorInputPath))
        {
            FlashStatus("Не выбран файл — откройте фотографию из списка.");
            return;
        }

        if (!File.Exists(EditorInputPath))
        {
            FlashStatus("Файл на диске не найден. Обновите список или выберите другой файл.");
            return;
        }

        _ = TryAutoAlignAsync(force: true);
    }

    private async Task TryAutoAlignAsync(bool force = false)
    {
        if (IsBrowseMode || string.IsNullOrEmpty(EditorInputPath))
            return;

        if (!force && !IsCurrentAdjustIdentity())
            return;

        IsEditorLargePreviewBusy = true;
        FlashStatus("Авто-подгонка…");

        var path = EditorInputPath!;
        var stem = SelectedShotStem.Trim();
        if (ManualShotAutoAlignService.IsSkippedStem(stem))
        {
            IsEditorLargePreviewBusy = false;
            FlashStatus("Авто-подгонка для кадров 05 и 07 пока не поддерживается — настройте вручную.");
            return;
        }

        var edgeConfigured = (int)Math.Clamp(Math.Round(_main.AnalysisMaxEdge), 256, 8192);
        var alignEdge = Math.Min(edgeConfigured, EditorAutoAlignLoadEdgeCap);
        var refFolder = _main.ReferenceFolder;

        string? refPath = null;
        if (!string.IsNullOrWhiteSpace(refFolder))
        {
            refPath = Directory.EnumerateFiles(refFolder)
                .FirstOrDefault(f =>
                    ImageFileCatalog.IsImageFile(f)
                    && string.Equals(Path.GetFileNameWithoutExtension(f), stem, StringComparison.OrdinalIgnoreCase));
        }

        if (refPath is null || !File.Exists(refPath))
        {
            IsEditorLargePreviewBusy = false;
            FlashStatus("Авто-подгонка: нет референса для выбранного кадра.");
            return;
        }

        ManualShotAutoAlignService.AutoAlignOutcome? outcome = null;
        try
        {
            await Task.Run(() =>
            {
                using var full = CropPreviewBitmapFactory.TryLoadPreparedFullForManualFrame(
                    path, stem, alignEdge, rotateCounterClockwise90: false);
                if (full is null)
                    return;

                var zonaDir = Directory.Exists(_main.ZonaFolder) ? _main.ZonaFolder : null;
                if (ManualShotAutoAlignService.TryCompute(
                        full,
                        refPath,
                        stem,
                        zonaDir,
                        alignEdge,
                        out var o))
                    outcome = o;
            }).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            IsEditorLargePreviewBusy = false;
            var hint = ex.Message.Length > 120 ? ex.Message[..120] + "…" : ex.Message;
            FlashStatus($"Авто-подгонка: сбой — {hint}");
            return;
        }

        if (outcome is null)
        {
            IsEditorLargePreviewBusy = false;
            FlashStatus(
                "Авто-подгонка: OpenCV не нашёл убедительный силуэт товара на уменьшенной копии — подвиньте вручную.");
            return;
        }

        if (!string.Equals(path, EditorInputPath, StringComparison.OrdinalIgnoreCase))
        {
            IsEditorLargePreviewBusy = false;
            return;
        }

        var o2 = outcome.Value;
        _adjustUndo.CommitSnapshot(BuildCurrentAdjust());
        _suppressSchedulePreviewRefresh = true;
        try
        {
            OffsetX = o2.Adjust.OffsetX;
            OffsetY = o2.Adjust.OffsetY;
            ZoomPercent = o2.Adjust.ZoomPercent;
            RotationDeg = o2.Adjust.RotationDeg;
        }
        finally
        {
            _suppressSchedulePreviewRefresh = false;
        }

        var extra = string.IsNullOrWhiteSpace(o2.Detail) ? "" : $" · {o2.Detail}";
        FlashStatus($"Авто-подгонка: эталон {o2.Template.Stem} (OpenCV + правила){extra}");
        _adjustUndo.Reset(BuildCurrentAdjust());
        IsEditorLargePreviewBusy = false;
        NotifyEditorProfileCommandsCanExecuteChanged();
        SchedulePreviewRefresh();
    }

    private bool CanUndoAdjust() => CanRunEditorFileCommands() && _adjustUndo.CanUndo;

    private bool CanRedoAdjust() => CanRunEditorFileCommands() && _adjustUndo.CanRedo;

    [RelayCommand(CanExecute = nameof(CanUndoAdjust))]
    private void UndoAdjust()
    {
        var prev = _adjustUndo.TryUndo(BuildCurrentAdjust());
        if (prev is null)
            return;
        ApplyAdjustSnapshot(prev);
        FlashStatus("Отмена правки.");
    }

    [RelayCommand(CanExecute = nameof(CanRedoAdjust))]
    private void RedoAdjust()
    {
        var next = _adjustUndo.TryRedo(BuildCurrentAdjust());
        if (next is null)
            return;
        ApplyAdjustSnapshot(next);
        FlashStatus("Повтор правки.");
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

    private bool CanRunEditorFileCommands() =>
        !IsBrowseMode && !string.IsNullOrEmpty(EditorInputPath) && File.Exists(EditorInputPath!);

    /// <summary>Сохранить текущие значения как умолчание профиля для выбранного номера кадра.</summary>
    [RelayCommand(CanExecute = nameof(CanRunEditorFileCommands))]
    private void SaveStemForProfile()
    {
        if (string.IsNullOrEmpty(EditorInputPath))
            return;

        var stem = ZonaOperationGuideParser.NormalizeShotStem(SelectedShotStem, EditorInputPath)
            ?? SelectedShotStem.Trim();

        var adj = BuildCurrentAdjust();

        ManualShotAdjustStore.SetForProfile(
            _main.SelectedProduct.DisplayName,
            stem,
            adj);

        var basename = Path.GetFileName(EditorInputPath);
        ManualShotAdjustStore.SetForProfileBasename(_main.SelectedProduct.DisplayName, basename, adj);

        FlashStatus(
            $"Сохранено для профиля «{_main.SelectedProduct.DisplayName}», кадр {stem}; имя файла «{basename}» — для любой папки, пока не уберёте через меню.");
        _sessionByFile[NormEditorFileKey(EditorInputPath)] = adj.Clone();
        RecordCropFrameChoiceForCurrentFile();
        RefreshThumbnailForPath(EditorInputPath);
        _main.RequestPreviewRefresh();
        ClearProfileBasenameRuleCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Применить текущие правки только к этому файлу (json).</summary>
    [RelayCommand(CanExecute = nameof(CanRunEditorFileCommands))]
    private void PersistAdjustmentsForCurrentFile()
    {
        if (string.IsNullOrEmpty(EditorInputPath))
            return;

        ManualShotAdjustStore.SetForFile(EditorInputPath, BuildCurrentAdjust());
        _sessionByFile[NormEditorFileKey(EditorInputPath)] = BuildCurrentAdjust().Clone();
        RecordCropFrameChoiceForCurrentFile();
        RefreshThumbnailForPath(EditorInputPath);
        _main.RequestPreviewRefresh();
        FlashStatus("Изменения применены к этой фотографии.");
    }

    /// <summary>Текущие правки записать всем файлам с тем же именем в списке браузера.</summary>
    [RelayCommand(CanExecute = nameof(CanRunEditorFileCommands))]
    private void ApplyAdjustmentsToSameFileName()
    {
        if (string.IsNullOrEmpty(EditorInputPath))
            return;

        var name = Path.GetFileName(EditorInputPath);
        var adj = BuildCurrentAdjust().Clone();
        var targets = _filesBacking
            .Where(i => string.Equals(Path.GetFileName(i.FilePath), name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var t in targets)
        {
            ManualShotAdjustStore.SetForFile(t.FilePath, adj.Clone());
            _sessionByFile[NormEditorFileKey(t.FilePath)] = adj.Clone();
            RecordCropFrameChoiceHint(t.FilePath, SelectedShotStem);
            RefreshThumbnailForPath(t.FilePath);
        }

        ManualShotAdjustStore.SetForProfileBasename(_main.SelectedProduct.DisplayName, name, adj.Clone());

        _main.RebuildMappingRowsFromEditor();
        _main.RequestPreviewRefresh();
        FlashStatus($"Настройки применены к {targets.Count} файл(ам) «{name}» и зафиксированы для профиля по имени (любые пути с тем же именем).");
        ClearProfileBasenameRuleCommand.NotifyCanExecuteChanged();
    }

    private bool CanClearProfileBasenameRule() =>
        CanRunEditorFileCommands()
        && ManualShotAdjustStore.TryGetProfileBasename(_main.SelectedProduct.DisplayName, EditorInputPath!, out _);

    /// <summary>Удалить в json сохранённые для текущего имени файла настройки профиля (все такие же имена).</summary>
    [RelayCommand(CanExecute = nameof(CanClearProfileBasenameRule))]
    private void ClearProfileBasenameRule()
    {
        if (string.IsNullOrEmpty(EditorInputPath))
            return;

        ManualShotAdjustStore.ClearForProfileBasename(
            _main.SelectedProduct.DisplayName,
            EditorInputPath);

        var key = NormEditorFileKey(EditorInputPath);
        _sessionByFile.Remove(key);
        LoadAdjustFromStoreForCurrentStem();

        RefreshThumbnailForPath(EditorInputPath);
        _main.RebuildMappingRowsFromEditor();
        _main.RequestMappingStatusRefresh();
        SchedulePreviewRefresh();
        NotifyEditorProfileCommandsCanExecuteChanged();

        FlashStatus(
            $"Убрано правило профиля по имени «{Path.GetFileName(EditorInputPath)}». Дальше действуют per-file → кадр → авто-подгонка.");
    }

    /// <summary>Сбросить сохранённые правки для этого файла (вернуться к настройкам профиля по кадру).</summary>
    [RelayCommand(CanExecute = nameof(CanRunEditorFileCommands))]
    private void ResetAdjustments()
    {
        if (string.IsNullOrEmpty(EditorInputPath))
            return;

        ManualShotAdjustStore.ClearForFile(EditorInputPath);
        _sessionByFile.Remove(NormEditorFileKey(EditorInputPath));

        LoadAdjustFromStoreForCurrentStem();
        PersistCurrentEditorFile();
        RefreshThumbnailForPath(EditorInputPath);
        _main.RequestPreviewRefresh();
        SchedulePreviewRefresh();
        FlashStatus("Для этой фотографии сброшены сохранённые правки (используются настройки профиля по кадру).");
    }

    private void SchedulePreviewRefresh()
    {
        if (_suppressSchedulePreviewRefresh || IsBrowseMode || string.IsNullOrEmpty(EditorInputPath))
            return;

        MarkDraftPreviewNeededAndMaybeScheduleUpgrade();
        _previewDebounce.Stop();
        _previewDebounce.Start();
    }

    public Task RefreshPreviewsAsync()
    {
        var token = Interlocked.Increment(ref _previewSerial);
        return ExecuteRefreshPreviewsAsync(token, litePanCompose: false);
    }

    /// <param name="litePanCompose">
    /// Облегчённый режим во время перетаскивания: без спиннера, без цветокоррекции, реже запусков (коалесценция UI);
    /// compose чуть выше базового черновика, чтобы сразу читалась сетка правил макета.
    /// </param>
    private async Task ExecuteRefreshPreviewsAsync(int generationToken, bool litePanCompose)
    {
        if (IsBrowseMode || string.IsNullOrEmpty(EditorInputPath))
            return;

        await _dispatcher.InvokeAsync(() =>
        {
            if (!litePanCompose)
            {
                Interlocked.Increment(ref _editorPreviewBusyDepth);
                IsEditorLargePreviewBusy = true;
            }
        });
        var path = EditorInputPath!;
        var stem = SelectedShotStem.Trim();
        var edgeConfigured = (int)Math.Clamp(Math.Round(_main.AnalysisMaxEdge), 256, 8192);
        var edge = Math.Min(edgeConfigured, EditorInteractiveSourceMaxEdge);
        var resultDisplayEdge = EditorResultPreviewDisplayMaxEdge;
        var refDisplayEdge = EditorReferencePreviewDisplayMaxEdge;
        var color = MainViewModel.GetEffectiveColorFor(_main.SelectedProduct);
        var applyColor = _main.ApplyColorCorrection;
        var zonaDir = Directory.Exists(_main.ZonaFolder) ? _main.ZonaFolder : null;
        var refFolder = _main.ReferenceFolder;
        var manual = BuildCurrentAdjust();

        BitmapSource? r = null;
        BitmapSource? a = null;
        var ow = 0;
        var oh = 0;
        var previewAbort = false;

        try
        {
            await Task.Run(() =>
            {
                var composeDraftTier = _previewDraftCompose;

                string? refPath = null;
                if (!string.IsNullOrWhiteSpace(refFolder))
                {
                    refPath = Directory.EnumerateFiles(refFolder)
                        .FirstOrDefault(f =>
                            ImageFileCatalog.IsImageFile(f)
                            && string.Equals(Path.GetFileNameWithoutExtension(f), stem, StringComparison.OrdinalIgnoreCase));
                }

                var cacheKey = BuildEditorFullImageCacheKey(path, stem, edge, refPath);

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
                        r = CropPreviewBitmapFactory.LoadThumbnail(refPath, refDisplayEdge);

                    MagickImage? fullRef;
                    lock (_cropCacheLock)
                    {
                        if (_editorFullImageCacheKey != cacheKey || _editorFullImageCache is null)
                        {
                            _editorFullImageCache?.Dispose();
                            _editorFullImageCache = CropPreviewBitmapFactory.TryLoadPreparedFullForManualFrame(
                                path, stem, edge, rotateCounterClockwise90: false,
                                clampLoadedImageLongEdge: EditorInteractiveSourceMaxEdge);
                            _editorFullImageCacheKey = cacheKey;
                        }

                        fullRef = _editorFullImageCache;

                        if (fullRef is null || refW < 1 || refH < 1)
                        {
                            previewAbort = true;
                            return;
                        }
                    }

                    MagickImage? work = null;
                    try
                    {
                        // Черновик: компактнее для ползунков; при lite (таскание мышью) чуть выше разрешение, чтобы красная сетка макета читалась сразу.
                        var previewComposeLongEdge = composeDraftTier
                            ? (litePanCompose
                                ? Math.Clamp((int)Math.Round(resultDisplayEdge * 0.75), 340,
                                    Math.Min(resultDisplayEdge, 480))
                                : Math.Clamp(resultDisplayEdge / 2, 240, 360))
                            : resultDisplayEdge;
                        var refLong = Math.Max(refW, refH);
                        var composeScale = refLong > 0 ? Math.Min(1.0, previewComposeLongEdge / (double)refLong) : 1.0;

                        var previewRefW = Math.Max(1, (int)Math.Round(refW * composeScale));
                        var previewRefH = Math.Max(1, (int)Math.Round(refH * composeScale));

                        var manualPreview = manual.Clone();
                        manualPreview.OffsetX *= composeScale;
                        manualPreview.OffsetY *= composeScale;

                        work = ManualShotAdjustApplier.ComposeFromFullToReference(fullRef, manualPreview, previewRefW, previewRefH);
                        CropPreviewBitmapFactory.CompositeGridForEditorPreview(
                            work, stem, ZonaGridOverlayKind.LayoutRules);
                        if (!litePanCompose)
                            ColorCorrectionService.ApplyIfEnabled(work, color, applyColor);
                        a = CropPreviewBitmapFactory.ToBitmapSourceScaled(work, resultDisplayEdge);
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
            }).ConfigureAwait(true);

            await _dispatcher.InvokeAsync(() =>
            {
                if (generationToken != Volatile.Read(ref _previewSerial))
                    return;
                if (EditorInputPath != path)
                    return;

                ReferencePreview = r;
                if (previewAbort)
                {
                    ResultPreview = null;
                    ResultOutputWidth = 0;
                    ResultOutputHeight = 0;
                }
                else
                {
                    ResultPreview = a;
                    ResultOutputWidth = ow;
                    ResultOutputHeight = oh;
                }
            }, DispatcherPriority.Normal);
        }
        finally
        {
            await _dispatcher.InvokeAsync(() =>
            {
                if (litePanCompose)
                    return;
                var left = Interlocked.Decrement(ref _editorPreviewBusyDepth);
                IsEditorLargePreviewBusy = left > 0;
            });
        }
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

public readonly record struct EditorBrowseFilterOption(EditorBrowseFilterKind Kind, string Display);

public partial class FolderTreeNodeViewModel : ObservableObject
{
    private Action<FolderTreeNodeViewModel>? _shownChanged;

    public FolderTreeNodeViewModel(
        string displayName,
        string fullPath,
        bool isGoodsRoot,
        bool isShown = true,
        FolderTreeNodeViewModel? parent = null)
    {
        DisplayName = displayName;
        FullPath = fullPath;
        IsGoodsRoot = isGoodsRoot;
        Parent = parent;
        _isShown = isShown;
    }

    public string DisplayName { get; }
    public string FullPath { get; }
    public bool IsGoodsRoot { get; }
    public FolderTreeNodeViewModel? Parent { get; }

    public ObservableCollection<FolderTreeNodeViewModel> Children { get; } = new();

    [ObservableProperty] private bool _isShown = true;

    public void SetShownChangedHandler(Action<FolderTreeNodeViewModel>? handler) => _shownChanged = handler;

    partial void OnIsShownChanged(bool value) => _shownChanged?.Invoke(this);
}

public partial class FileThumbItemViewModel : ObservableObject
{
    private readonly VisualShotEditorViewModel _owner;
    private bool _suppressIncludedPersistence;

    public FileThumbItemViewModel(string filePath, VisualShotEditorViewModel owner)
    {
        _owner = owner;

        var root = owner.Main.InputFolder?.Trim() ?? string.Empty;
        _suppressIncludedPersistence = true;
        FilePath = filePath;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            IsIncluded = true;
        else
            IsIncluded = !EditorSkippedFilesStore.IsSkipped(Path.GetFullPath(root), FilePath);
        _suppressIncludedPersistence = false;
    }

    [ObservableProperty] private string _filePath;

    partial void OnFilePathChanged(string value)
    {
        OnPropertyChanged(nameof(FileLabel));
        OnPropertyChanged(nameof(FileNameOnly));
    }

    [ObservableProperty] private BitmapSource? _thumbnail;

    /// <summary>Построение превью результата для списка файлов.</summary>
    [ObservableProperty] private bool _isThumbLoading;

    /// <summary>Включить файл в очередь пакетной обработки (снимок сохраняется в %AppData%).</summary>
    [ObservableProperty] private bool _isIncluded = true;

    /// <summary>Режим переименования по двойному щелчку по имени файла.</summary>
    [ObservableProperty] private bool _isRenaming;

    /// <summary>Черновик имени при переименовании.</summary>
    [ObservableProperty] private string _renameDraft = string.Empty;

    public string FileLabel => Path.GetFileName(FilePath);

    /// <summary>Стабильное свойство для группировки по имени файла.</summary>
    public string FileNameOnly => Path.GetFileName(FilePath);

    /// <summary>Заголовок группы по подпапке относительно «Товар».</summary>
    public string FolderGroupHeader { get; set; } = string.Empty;

    partial void OnIsIncludedChanged(bool value)
    {
        if (_suppressIncludedPersistence)
            return;

        var root = _owner.Main.InputFolder?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            return;

        var rootFull = Path.GetFullPath(root);
        if (!value)
            EditorSkippedFilesStore.AddSkipped(rootFull, FilePath);
        else
            EditorSkippedFilesStore.RemoveSkipped(rootFull, FilePath);

        _owner.Main.RebuildMappingRowsFromEditor();
    }
}
