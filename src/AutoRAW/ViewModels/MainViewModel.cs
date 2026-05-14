using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AutoRAW;
using AutoRAW.Models;
using AutoRAW.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forms = System.Windows.Forms;

namespace AutoRAW.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly Dispatcher _dispatcher;
    private readonly AutoCropBatchService _batch = new();
    private ProductProfile? _draftSourceProfile;

    public event EventHandler? ProfileMenuInvalidated;

    private void InvalidateProfileMenu() => ProfileMenuInvalidated?.Invoke(this, EventArgs.Empty);

    public MainViewModel(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        MappingRows.CollectionChanged += OnMappingRowsCollectionChanged;
        _previewDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
        _previewDebounce.Tick += (_, _) =>
        {
            _previewDebounce.Stop();
            _ = ExecutePreviewRefreshAsync();
        };

        AllProducts.Add(ProductProfile.BuiltInSneakers);
        UserProfileBundleService.EnsureDirectories();
        foreach (var c in ProductProfileStore.LoadCustom())
            AllProducts.Add(c);

        SelectedProduct = ProductProfile.BuiltInSneakers;
        ApplyProductFolders();
        _applyColorCorrection = false;
        OnPropertyChanged(nameof(ApplyColorCorrection));
        NotifyColorSummaryProperties();

        LogLines.Add("Простой режим: папка «Товар», опционально выход. Профиль — меню «Профиль». Вид → журнал / расширенный режим. Цветокоррекция: галочка «Применить» — только после подтверждения в альфа-режиме.");
    }

    private readonly DispatcherTimer _previewDebounce;

    public ObservableCollection<string> LogLines { get; } = new();

    public ObservableCollection<string> ReferenceFiles { get; } = new();

    public ObservableCollection<CropMappingRowViewModel> MappingRows { get; } = new();

    /// <summary>Все профили товара для меню (первый — «Кроссовки»).</summary>
    public ObservableCollection<ProductProfile> AllProducts { get; } = new();

    [ObservableProperty] private ProductProfile _selectedProduct = ProductProfile.BuiltInSneakers;

    /// <summary>Расширенный интерфейс (таблица, превью, ручные пути).</summary>
    [ObservableProperty] private bool _isAdvancedView;

    /// <summary>Панель журнала внизу окна (по умолчанию скрыта).</summary>
    [ObservableProperty] private bool _isLogPanelVisible;

    [ObservableProperty] private CropMappingRowViewModel? _selectedMappingRow;

    public bool IsSimpleView => !IsAdvancedView;

    [ObservableProperty] private BitmapSource? _previewReference;

    [ObservableProperty] private BitmapSource? _previewBefore;

    [ObservableProperty] private BitmapSource? _previewAfter;

    [ObservableProperty] private string _referenceFolder = string.Empty;

    /// <summary>Референс по умолчанию для новых строк и кнопки «ко всем».</summary>
    [ObservableProperty] private string _selectedReferenceFile = string.Empty;

    [ObservableProperty] private string _inputFolder = string.Empty;

    [ObservableProperty] private string _outputFolder = string.Empty;

    /// <summary>Папка с маркированными zona-изображениями (красный прямоугольник = зона кропа).</summary>
    [ObservableProperty] private string _zonaFolder = string.Empty;

    [ObservableProperty] private double _analysisMaxEdge = SubjectBoundsEstimator.DefaultAnalysisMaxEdge;

    [ObservableProperty] private bool _isBusy;

    /// <summary>Идёт пересчёт превью (показ полосы и текста, чтобы не казалось зависанием).</summary>
    [ObservableProperty] private bool _isPreviewLoading;

    [ObservableProperty] private string _previewStatusText = string.Empty;

    /// <summary>Сохранять результат кадрирования в WebP; иначе JPEG. Запоминается в %AppData%\AutoRAW\export_prefs.json.</summary>
    [ObservableProperty] private bool _saveAsWebP = ExportPreferenceStore.GetSaveAsWebP();

    private bool _applyColorCorrection;

    /// <summary>Включение цветокоррекции (альфа): при включении показывается предупреждение; при старте всегда выключено.</summary>
    public bool ApplyColorCorrection
    {
        get => _applyColorCorrection;
        set
        {
            if (value == _applyColorCorrection)
                return;

            if (value)
            {
                var dlg = new AlphaDisclaimerDialog
                {
                    Owner = System.Windows.Application.Current?.MainWindow
                };
                if (dlg.ShowDialog() != true)
                {
                    OnPropertyChanged(nameof(ApplyColorCorrection));
                    return;
                }
            }

            SetProperty(ref _applyColorCorrection, value);
            SchedulePreviewRefresh();
        }
    }

    public ColorCorrectionSettings SelectedColorSettings => EffectiveSelectedColor;

    public ColorCorrectionSettings EffectiveSelectedColor => GetEffectiveColorFor(SelectedProduct);

    public string ColorSpaceSummaryText =>
        EffectiveSelectedColor.UseStandardColorSpace
            ? "Стандарт (sRGB)"
            : "Как в файле (ICC)";

    public string ColorNumericSummary
    {
        get
        {
            var c = EffectiveSelectedColor;
            var xmpName = c.XmpFilePath is not null
                ? System.IO.Path.GetFileName(c.XmpFilePath)
                : null;
            var prefix = xmpName is not null ? $"XMP: {xmpName} — " : string.Empty;
            return prefix + c.ToSummaryString();
        }
    }

    public bool IsSneakersProfile => ReferenceEquals(SelectedProduct, ProductProfile.BuiltInSneakers);

    partial void OnReferenceFolderChanged(string value)
    {
        RefreshReferenceFiles();
        ConsiderDraftPromotion();
        CommitDraftApplyCommand.NotifyCanExecuteChanged();
        CommitDraftNewCommand.NotifyCanExecuteChanged();
    }

    partial void OnZonaFolderChanged(string value)
    {
        RunBatchCommand.NotifyCanExecuteChanged();
        SchedulePreviewRefresh();
        ConsiderDraftPromotion();
        CommitDraftApplyCommand.NotifyCanExecuteChanged();
        CommitDraftNewCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedReferenceFileChanged(string value)
    {
        RunBatchCommand.NotifyCanExecuteChanged();
        ApplyDefaultReferenceToAllCommand.NotifyCanExecuteChanged();
    }

    partial void OnInputFolderChanged(string value) => RebuildMappingRows();

    partial void OnAnalysisMaxEdgeChanged(double value) => SchedulePreviewRefresh();

    partial void OnSelectedMappingRowChanged(CropMappingRowViewModel? value) => SchedulePreviewRefresh();

    partial void OnOutputFolderChanged(string value) => RunBatchCommand.NotifyCanExecuteChanged();

    partial void OnSaveAsWebPChanged(bool value) => ExportPreferenceStore.SetSaveAsWebP(value);

    partial void OnIsBusyChanged(bool value) => RunBatchCommand.NotifyCanExecuteChanged();

    partial void OnIsAdvancedViewChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSimpleView));
        SchedulePreviewRefresh();
        CommitDraftApplyCommand.NotifyCanExecuteChanged();
        CommitDraftNewCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedProductChanged(ProductProfile value)
    {
        if (!value.IsDraft)
            _draftSourceProfile = null;

        ApplyProductFolders();
        if (_applyColorCorrection)
        {
            _applyColorCorrection = false;
            OnPropertyChanged(nameof(ApplyColorCorrection));
        }

        NotifyColorSummaryProperties();
        SchedulePreviewRefresh();
        CommitDraftApplyCommand.NotifyCanExecuteChanged();
        CommitDraftNewCommand.NotifyCanExecuteChanged();
    }

    private void NotifyColorSummaryProperties()
    {
        OnPropertyChanged(nameof(SelectedColorSettings));
        OnPropertyChanged(nameof(EffectiveSelectedColor));
        OnPropertyChanged(nameof(IsSneakersProfile));
        OnPropertyChanged(nameof(ColorSpaceSummaryText));
        OnPropertyChanged(nameof(ColorNumericSummary));
    }

    public static ColorCorrectionSettings GetEffectiveColorFor(ProductProfile profile)
    {
        if (profile.IsDraft)
            return profile.Color;

        if (ReferenceEquals(profile, ProductProfile.BuiltInSneakers))
        {
            var stored = ProfileColorOverrideStore.TryGet(profile.DisplayName);
            if (stored is not null)
                return stored;

            // Загружаем дефолтный XMP-пресет, поставляемый с приложением
            var xmpPath = AppPaths.DefaultSneakersXmp;
            if (File.Exists(xmpPath))
            {
                try { return XmpSettingsParser.Parse(xmpPath); }
                catch { /* fallback to hardcoded */ }
            }

            return profile.Color;
        }

        // Для пользовательских профилей: если указан XmpFilePath — перечитываем
        if (profile.Color.XmpFilePath is { } path && File.Exists(path))
        {
            try { return XmpSettingsParser.Parse(path); }
            catch { /* fallback */ }
        }

        return profile.Color;
    }

    public string? TryGetColorPreviewSourcePath()
    {
        var row = SelectedMappingRow ?? MappingRows.FirstOrDefault();
        if (row is not null && File.Exists(row.InputPath))
            return row.InputPath;
        return ImageFileCatalog.ListImagesInFolder(InputFolder).FirstOrDefault();
    }

    public void SaveColorProfileSettings(ColorCorrectionSettings settings)
    {
        if (IsAdvancedView && !SelectedProduct.IsDraft)
        {
            if (!FoldersDivergeFromSelectedProduct())
                PromoteToDraftForColor(settings);
            else if (!SelectedProduct.IsDraft)
                PromoteToDraft();
        }

        if (SelectedProduct.IsDraft)
        {
            ReplaceDraftColor(settings);
            NotifyColorSummaryProperties();
            SchedulePreviewRefresh();
            InvalidateProfileMenu();
            return;
        }

        if (ReferenceEquals(SelectedProduct, ProductProfile.BuiltInSneakers))
        {
            ProfileColorOverrideStore.Set(SelectedProduct.DisplayName, settings);
        }
        else
        {
            for (var i = 0; i < AllProducts.Count; i++)
            {
                if (!ReferenceEquals(AllProducts[i], SelectedProduct))
                    continue;
                var updated = SelectedProduct.WithColor(settings);
                AllProducts[i] = updated;
                SelectedProduct = updated;
                if (!string.IsNullOrWhiteSpace(SelectedProduct.ReferenceFolder)
                    && !string.IsNullOrWhiteSpace(SelectedProduct.ZonaFolder)
                    && Directory.Exists(SelectedProduct.ReferenceFolder)
                    && Directory.Exists(SelectedProduct.ZonaFolder))
                {
                    UserProfileBundleService.WriteBundle(
                        SelectedProduct.DisplayName,
                        SelectedProduct.ReferenceFolder!,
                        SelectedProduct.ZonaFolder!,
                        updated.Color);
                }

                break;
            }
        }

        NotifyColorSummaryProperties();
        SchedulePreviewRefresh();
    }

    [RelayCommand]
    private void EditColorProfile()
    {
        var path = TryGetColorPreviewSourcePath();
        var initial = GetEffectiveColorFor(SelectedProduct) with { };
        var dlg = new ColorProfileEditorDialog(initial, path)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        if (dlg.ShowDialog() != true || dlg.ResultSettings is null)
            return;
        SaveColorProfileSettings(dlg.ResultSettings);
    }

    private void ApplyProductFolders()
    {
        if (SelectedProduct.IsDraft)
            return;

        var r = AppPaths.ResolveReferenceFolder(SelectedProduct.ReferenceFolder);
        var z = AppPaths.ResolveZonaFolder(SelectedProduct.ZonaFolder);
        if (!string.Equals(ReferenceFolder, r, StringComparison.OrdinalIgnoreCase))
            ReferenceFolder = r;
        if (!string.Equals(ZonaFolder, z, StringComparison.OrdinalIgnoreCase))
            ZonaFolder = z;
    }

    private void OnMappingRowsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RunBatchCommand.NotifyCanExecuteChanged();
        ApplyDefaultReferenceToAllCommand.NotifyCanExecuteChanged();
    }

    private void OnMappingRowChanged()
    {
        RunBatchCommand.NotifyCanExecuteChanged();
        SchedulePreviewRefresh();
    }

    private void RefreshReferenceFiles()
    {
        ReferenceFiles.Clear();
        SelectedReferenceFile = string.Empty;

        if (!Directory.Exists(ReferenceFolder))
        {
            SyncMappingRowReferences();
            RunBatchCommand.NotifyCanExecuteChanged();
            ApplyDefaultReferenceToAllCommand.NotifyCanExecuteChanged();
            SchedulePreviewRefresh();
            return;
        }

        foreach (var name in ImageFileCatalog.ListImagesInFolder(ReferenceFolder).Select(Path.GetFileName))
        {
            if (name is not null)
                ReferenceFiles.Add(name);
        }

        if (ReferenceFiles.Count > 0)
            SelectedReferenceFile = ReferenceFiles[0]!;

        if (Directory.Exists(InputFolder))
            RebuildMappingRows();
        else
        {
            SyncMappingRowReferences();
            RunBatchCommand.NotifyCanExecuteChanged();
            ApplyDefaultReferenceToAllCommand.NotifyCanExecuteChanged();
            SchedulePreviewRefresh();
        }
    }

    private string DefaultReferenceForNewRow()
    {
        if (!string.IsNullOrWhiteSpace(SelectedReferenceFile) && ReferenceFiles.Contains(SelectedReferenceFile))
            return SelectedReferenceFile;
        return ReferenceFiles.FirstOrDefault() ?? string.Empty;
    }

    private void SyncMappingRowReferences()
    {
        var def = DefaultReferenceForNewRow();
        foreach (var row in MappingRows)
        {
            if (string.IsNullOrWhiteSpace(row.SelectedReferenceFile) || !ReferenceFiles.Contains(row.SelectedReferenceFile))
                row.SelectedReferenceFile = def;
        }
    }

    private void RebuildMappingRows()
    {
        SelectedMappingRow = null;
        MappingRows.Clear();
        if (!Directory.Exists(InputFolder))
        {
            ClearPreviewImages();
            RunBatchCommand.NotifyCanExecuteChanged();
            ApplyDefaultReferenceToAllCommand.NotifyCanExecuteChanged();
            return;
        }

        var def = DefaultReferenceForNewRow();
        foreach (var path in ImageFileCatalog.ListImagesInFolder(InputFolder))
        {
            var matched = ReferenceNameMatcher.TryMatch(path, ReferenceFiles) ?? def;
            MappingRows.Add(new CropMappingRowViewModel(path, matched, OnMappingRowChanged));
        }

        SelectedMappingRow = MappingRows.FirstOrDefault();

        RunBatchCommand.NotifyCanExecuteChanged();
        ApplyDefaultReferenceToAllCommand.NotifyCanExecuteChanged();
        SchedulePreviewRefresh();
    }

    [RelayCommand]
    private void BrowseReferenceFolder()
    {
        using var dlg = new Forms.FolderBrowserDialog();
        if (dlg.ShowDialog() != Forms.DialogResult.OK)
            return;

        ReferenceFolder = dlg.SelectedPath;
    }

    [RelayCommand]
    private void BrowseInputFolder()
    {
        using var dlg = new Forms.FolderBrowserDialog();
        if (dlg.ShowDialog() != Forms.DialogResult.OK)
            return;

        InputFolder = dlg.SelectedPath;
    }

    [RelayCommand]
    private void BrowseOutputFolder()
    {
        using var dlg = new Forms.FolderBrowserDialog();
        if (dlg.ShowDialog() != Forms.DialogResult.OK)
            return;

        OutputFolder = dlg.SelectedPath;
    }

    [RelayCommand]
    private void BrowseZonaFolder()
    {
        using var dlg = new Forms.FolderBrowserDialog();
        dlg.Description = "Выберите папку с zona-изображениями (красный прямоугольник = кроп)";
        if (dlg.ShowDialog() != Forms.DialogResult.OK)
            return;

        ZonaFolder = dlg.SelectedPath;
    }

    [RelayCommand]
    private void SelectProduct(ProductProfile? profile)
    {
        if (profile is null || ReferenceEquals(profile, SelectedProduct))
            return;

        if (SelectedProduct.IsDraft && !profile.IsDraft)
        {
            var r = System.Windows.MessageBox.Show(
                "Сохранить изменения перед переключением профиля?\n\n«Да» — записать на диск (в исходный профиль или новый, если был «Кроссовки»).\n«Нет» — отменить правки.\n«Отмена» — остаться в «Несохранённые изменения».",
                "AutoRAW",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel)
            {
                InvalidateProfileMenu();
                return;
            }

            if (r == MessageBoxResult.Yes)
            {
                if (!TrySaveDraftBeforeSwitch())
                {
                    InvalidateProfileMenu();
                    return;
                }
            }
            else
                RemoveDraftFromList();
        }

        SelectedProduct = profile;
        InvalidateProfileMenu();
    }

    [RelayCommand]
    private void AddNewProfile()
    {
        var dlg = new AddProfileDialog { Owner = System.Windows.Application.Current?.MainWindow };
        if (dlg.ShowDialog() != true || dlg.ResultProfile is null)
            return;

        RemoveDraftFromList();
        _draftSourceProfile = null;
        ReloadCustomProfilesFromDisk();
        var pick = AllProducts.FirstOrDefault(p =>
            !p.IsDraft && p.DisplayName.Equals(dlg.ResultProfile.DisplayName, StringComparison.OrdinalIgnoreCase));
        SelectedProduct = pick ?? dlg.ResultProfile;
        AppendLog($"Профиль «{dlg.ResultProfile.DisplayName}»: %LocalAppData%\\AutoRAW\\user files\\Profile\\{UserProfileBundleService.SanitizeFolderName(dlg.ResultProfile.DisplayName)}");
        InvalidateProfileMenu();
    }

    [RelayCommand]
    private void OpenUserProfilesFolder()
    {
        UserProfileBundleService.EnsureDirectories();
        var path = AppPaths.UserProfilesRoot;
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = '"' + path + '"',
            UseShellExecute = true,
        });
    }

    [RelayCommand]
    private void ShowInstruction()
    {
        var w = new TextDocumentWindow("Инструкция", AppPaths.InstructionFile);
        w.Owner = System.Windows.Application.Current?.MainWindow;
        w.Show();
    }

    [RelayCommand]
    private void ShowChangelog()
    {
        var w = new TextDocumentWindow("Чендж лог", AppPaths.ChangelogFile);
        w.Owner = System.Windows.Application.Current?.MainWindow;
        w.Show();
    }

    [RelayCommand]
    private void ShowAbout()
    {
        var w = new AboutWindow
        {
            Owner = System.Windows.Application.Current?.MainWindow,
        };
        w.ShowDialog();
    }

    private bool CanCommitDraft =>
        IsAdvancedView
        && SelectedProduct.IsDraft
        && Directory.Exists(ReferenceFolder)
        && Directory.Exists(ZonaFolder);

    [RelayCommand(CanExecute = nameof(CanCommitDraft))]
    private void CommitDraftApply()
    {
        var src = _draftSourceProfile;
        var color = GetEffectiveColorFor(SelectedProduct);
        if (src is null)
            return;

        if (ReferenceEquals(src, ProductProfile.BuiltInSneakers))
        {
            System.Windows.MessageBox.Show(
                "Профиль «Кроссовки» нельзя перезаписать на диске. Используйте «Сохранить как новый профиль…» — данные попадут в %LocalAppData%\\AutoRAW\\user files\\Profile.",
                "AutoRAW",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        UserProfileBundleService.WriteBundle(src.DisplayName, ReferenceFolder, ZonaFolder, color);
        var name = src.DisplayName;
        RemoveDraftFromList();
        _draftSourceProfile = null;
        ReloadCustomProfilesFromDisk();
        var pick = AllProducts.FirstOrDefault(p =>
            !p.IsDraft && p.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase));
        SelectedProduct = pick ?? ProductProfile.BuiltInSneakers;
        ApplyProductFolders();
        AppendLog($"Профиль «{name}» обновлён (%LocalAppData%\\AutoRAW\\user files\\Profile).");
        InvalidateProfileMenu();
    }

    [RelayCommand(CanExecute = nameof(CanCommitDraft))]
    private void CommitDraftNew()
    {
        var nameDlg = new PromptDialog(
                "Новый профиль",
                "Имя профиля (%LocalAppData%\\AutoRAW\\user files\\Profile\\… будут reference, zona, setting):",
                "Новый профиль")
            { Owner = System.Windows.Application.Current?.MainWindow };
        if (nameDlg.ShowDialog() != true || string.IsNullOrWhiteSpace(nameDlg.Result))
            return;

        var color = GetEffectiveColorFor(SelectedProduct);
        var name = nameDlg.Result.Trim();
        UserProfileBundleService.WriteBundle(name, ReferenceFolder, ZonaFolder, color);
        RemoveDraftFromList();
        _draftSourceProfile = null;
        ReloadCustomProfilesFromDisk();
        var pick = AllProducts.FirstOrDefault(p =>
            !p.IsDraft && p.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase));
        SelectedProduct = pick ?? ProductProfile.BuiltInSneakers;
        ApplyProductFolders();
        AppendLog($"Создан профиль «{name}» (%LocalAppData%\\AutoRAW\\user files\\Profile).");
        InvalidateProfileMenu();
    }

    private void ReloadCustomProfilesFromDisk()
    {
        RemoveDraftFromList();
        for (var i = AllProducts.Count - 1; i >= 1; i--)
            AllProducts.RemoveAt(i);
        foreach (var c in ProductProfileStore.LoadCustom())
            AllProducts.Add(c);
    }

    private void ConsiderDraftPromotion()
    {
        if (!IsAdvancedView || SelectedProduct.IsDraft)
            return;
        if (!FoldersDivergeFromSelectedProduct())
            return;
        PromoteToDraft();
    }

    private bool FoldersDivergeFromSelectedProduct()
    {
        var wantR = AppPaths.ResolveReferenceFolder(SelectedProduct.ReferenceFolder);
        var wantZ = AppPaths.ResolveZonaFolder(SelectedProduct.ZonaFolder);
        return !string.Equals(ReferenceFolder, wantR, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(ZonaFolder, wantZ, StringComparison.OrdinalIgnoreCase);
    }

    private void RemoveDraftFromList()
    {
        for (var i = AllProducts.Count - 1; i >= 0; i--)
        {
            if (AllProducts[i].IsDraft)
                AllProducts.RemoveAt(i);
        }
    }

    private void PromoteToDraft()
    {
        if (SelectedProduct.IsDraft)
            return;
        _draftSourceProfile = SelectedProduct;
        RemoveDraftFromList();
        var color = GetEffectiveColorFor(SelectedProduct);
        var draft = ProductProfile.CreateUnsavedDraft(color);
        AllProducts.Insert(1, draft);
        SelectedProduct = draft;
        InvalidateProfileMenu();
        AppendLog("Черновик: «Несохранённые изменения». Сохраните через кнопки ниже.");
    }

    private void PromoteToDraftForColor(ColorCorrectionSettings settings)
    {
        if (SelectedProduct.IsDraft)
            return;
        _draftSourceProfile = SelectedProduct;
        RemoveDraftFromList();
        var draft = ProductProfile.CreateUnsavedDraft(settings);
        AllProducts.Insert(1, draft);
        SelectedProduct = draft;
        InvalidateProfileMenu();
    }

    private void ReplaceDraftColor(ColorCorrectionSettings settings)
    {
        for (var i = 0; i < AllProducts.Count; i++)
        {
            if (!AllProducts[i].IsDraft)
                continue;
            var u = AllProducts[i].WithColor(settings);
            AllProducts[i] = u;
            SelectedProduct = u;
            break;
        }

        CommitDraftApplyCommand.NotifyCanExecuteChanged();
        CommitDraftNewCommand.NotifyCanExecuteChanged();
    }

    private bool TrySaveDraftBeforeSwitch()
    {
        var color = GetEffectiveColorFor(SelectedProduct);
        var src = _draftSourceProfile;
        if (src is null)
            return true;

        if (!ReferenceEquals(src, ProductProfile.BuiltInSneakers))
        {
            UserProfileBundleService.WriteBundle(src.DisplayName, ReferenceFolder, ZonaFolder, color);
            RemoveDraftFromList();
            _draftSourceProfile = null;
            ReloadCustomProfilesFromDisk();
            return true;
        }

        var nameDlg = new PromptDialog(
                "Новый профиль",
                "Введите имя нового профиля (%LocalAppData%\\AutoRAW\\user files\\Profile):",
                "Новый профиль")
            { Owner = System.Windows.Application.Current?.MainWindow };
        if (nameDlg.ShowDialog() != true || string.IsNullOrWhiteSpace(nameDlg.Result))
            return false;

        UserProfileBundleService.WriteBundle(nameDlg.Result.Trim(), ReferenceFolder, ZonaFolder, color);
        RemoveDraftFromList();
        _draftSourceProfile = null;
        ReloadCustomProfilesFromDisk();
        return true;
    }

    private bool CanRunBatch()
    {
        if (IsBusy)
            return false;

        if (!Directory.Exists(InputFolder))
            return false;

        if (!string.IsNullOrWhiteSpace(OutputFolder) && !Directory.Exists(OutputFolder))
            return false;

        if (MappingRows.Count == 0)
            return false;

        bool hasZona = Directory.Exists(ZonaFolder);

        foreach (var row in MappingRows)
        {
            if (!File.Exists(row.InputPath))
                return false;

            // Если zona задана — референс для строки необязателен
            if (!hasZona)
            {
                if (!Directory.Exists(ReferenceFolder))
                    return false;

                if (string.IsNullOrWhiteSpace(row.SelectedReferenceFile))
                    return false;

                var refPath = Path.Combine(ReferenceFolder, row.SelectedReferenceFile);
                if (!File.Exists(refPath))
                    return false;
            }
        }

        return true;
    }

    [RelayCommand(CanExecute = nameof(CanApplyDefaultReferenceToAll))]
    private void ApplyDefaultReferenceToAll()
    {
        if (string.IsNullOrWhiteSpace(SelectedReferenceFile) || !ReferenceFiles.Contains(SelectedReferenceFile))
            return;

        foreach (var row in MappingRows)
            row.SelectedReferenceFile = SelectedReferenceFile;
    }

    private bool CanApplyDefaultReferenceToAll()
        => MappingRows.Count > 0
           && !string.IsNullOrWhiteSpace(SelectedReferenceFile)
           && ReferenceFiles.Contains(SelectedReferenceFile);

    [RelayCommand(CanExecute = nameof(CanRunBatch))]
    private async Task RunBatch()
    {
        bool hasZona = Directory.Exists(ZonaFolder);
        bool hasRef = Directory.Exists(ReferenceFolder);

        var explicitOut = string.IsNullOrWhiteSpace(OutputFolder) ? null : OutputFolder.Trim();

        var pairs = MappingRows
            .Select(r =>
            {
                var refPath = hasRef && !string.IsNullOrWhiteSpace(r.SelectedReferenceFile)
                    ? Path.Combine(ReferenceFolder, r.SelectedReferenceFile)
                    : r.InputPath; // fallback — сервис проверит zona первым
                return (r.InputPath, refPath);
            })
            .ToList();

        IsBusy = true;
        try
        {
            AppendLog(hasZona
                ? $"Старт пакета (zona-кроп из {ZonaFolder})…"
                : "Старт пакета (reference-кроп)…");
            if (explicitOut is null)
            {
                AppendLog(SaveAsWebP
                    ? "Формат выхода: WebP (.webp) в подпапку «webp» рядом с каждым исходным файлом."
                    : "Формат выхода: JPEG (.jpg) в подпапку «jpg» рядом с каждым исходным файлом.");
                AppendLog("Папка выхода не задана — для каждого файла: <папка исходника>\\webp или \\jpg.");
            }
            else
            {
                AppendLog(SaveAsWebP ? "Формат выхода: WebP (.webp)." : "Формат выхода: JPEG (.jpg).");
                AppendLog($"Папка выхода: {explicitOut}");
            }
            var edge = (int)Math.Clamp(Math.Round(AnalysisMaxEdge), 256, 8192);
            var zona = hasZona ? ZonaFolder : null;

            var color = GetEffectiveColorFor(SelectedProduct);
            var applyColor = ApplyColorCorrection;
            var webp = SaveAsWebP;

            await Task.Run(() =>
            {
                _batch.RunMappings(pairs, explicitOut, edge, AppendLog, CancellationToken.None, zona, color, applyColor, webp);
            });
        }
        catch (Exception ex)
        {
            AppendLog($"Сбой: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AppendLog(string message)
    {
        if (_dispatcher.CheckAccess())
            LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        else
            _ = _dispatcher.BeginInvoke(() => LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}"));
    }

    private void SchedulePreviewRefresh()
    {
        _previewDebounce.Stop();
        _previewDebounce.Start();
    }

    private void ClearPreviewImages()
    {
        PreviewReference = null;
        PreviewBefore = null;
        PreviewAfter = null;
    }

    private async Task SetPreviewLoadingUiAsync(bool loading, string statusText)
    {
        await _dispatcher.InvokeAsync(() =>
        {
            IsPreviewLoading = loading;
            PreviewStatusText = statusText;
        });
    }

    private async Task ExecutePreviewRefreshAsync()
    {
        await SetPreviewLoadingUiAsync(true, "Обновление превью…");
        try
        {
            var row = SelectedMappingRow ?? MappingRows.FirstOrDefault();
            if (row is null || !File.Exists(row.InputPath))
            {
                ClearPreviewImages();
                return;
            }

            bool hasZona = Directory.Exists(ZonaFolder);
            var stem = Path.GetFileNameWithoutExtension(row.InputPath);

            string? zonaPath = hasZona
                ? Directory.EnumerateFiles(ZonaFolder)
                    .FirstOrDefault(f =>
                        string.Equals(Path.GetFileNameWithoutExtension(f), stem, StringComparison.OrdinalIgnoreCase)
                        && ImageFileCatalog.IsImageFile(f))
                : null;

            string? refPath = null;
            if (!string.IsNullOrWhiteSpace(ReferenceFolder) && !string.IsNullOrWhiteSpace(row.SelectedReferenceFile))
            {
                var rp = Path.Combine(ReferenceFolder, row.SelectedReferenceFile);
                if (File.Exists(rp))
                    refPath = rp;
            }

            if (zonaPath is null && refPath is null)
            {
                ClearPreviewImages();
                return;
            }

            await SetPreviewLoadingUiAsync(true, "Загрузка изображений и расчёт кропа…");

            var edge = (int)Math.Clamp(Math.Round(AnalysisMaxEdge), 256, 8192);
            const int thumb = 520;
            var color = GetEffectiveColorFor(SelectedProduct);
            var applyColor = ApplyColorCorrection;

            try
            {
                BitmapSource? r = null;
                BitmapSource? b = null;
                BitmapSource? a = null;

                await Task.Run(() =>
                {
                    r = zonaPath is not null
                        ? CropPreviewBitmapFactory.LoadThumbnail(zonaPath, thumb)
                        : (refPath is not null ? CropPreviewBitmapFactory.LoadThumbnail(refPath, thumb) : null);

                    b = CropPreviewBitmapFactory.LoadThumbnail(row.InputPath, thumb);

                    a = zonaPath is not null
                        ? CropPreviewBitmapFactory.LoadZonaCroppedPreview(row.InputPath, zonaPath, thumb, color, applyColor)
                        : (refPath is not null
                            ? CropPreviewBitmapFactory.LoadCroppedPreview(row.InputPath, refPath, edge, thumb, color, applyColor)
                            : null);
                });

                PreviewReference = r;
                PreviewBefore = b;
                PreviewAfter = a;
            }
            catch
            {
                ClearPreviewImages();
            }
        }
        finally
        {
            await SetPreviewLoadingUiAsync(false, string.Empty);
        }
    }
}
