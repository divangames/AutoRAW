using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
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
    private readonly BatchRunController _batchRun = new();
    private ProductProfile? _draftSourceProfile;

    /// <summary>После «Пропустить» в авто-проверке не показывать снова до следующего запуска приложения.</summary>
    private bool _skipGithubUpdatePromptThisSession;

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

        foreach (var b in ProductProfileStore.LoadBuiltInMenuProfiles())
            AllProducts.Add(b);
        UserProfileBundleService.EnsureDirectories();
        foreach (var c in ProductProfileStore.LoadCustom())
            AllProducts.Add(c);

        SelectedProduct = PreferredProfileFallback;
        ApplyProductFolders();
        _applyColorCorrection = false;
        OnPropertyChanged(nameof(ApplyColorCorrection));
        NotifyColorSummaryProperties();

        var wp = WindowPanelPreferenceStore.GetSnapshot();
        IsLogPanelVisible = wp.LogPanelVisible;
        IsColorProfilePanelVisible = wp.ColorProfilePanelVisible;
        IsPreviewPanelVisible = wp.PreviewPanelVisible;

        LogLines.Add(new LogLineViewModel(ZonaMessages.NextGreeting(), LogLineKind.Zona, fromZona: true));
        LogLines.Add(new LogLineViewModel(
            "Простой режим: папка «Товар» (включая вложенные папки), опционально выход. Профиль — меню «Профиль». Вид → Окна (чат Zona, цветовой профиль, превью) и расширенный режим."));
    }

    /// <summary>Первый профиль «Кроссовки» в меню или первый не-черновик.</summary>
    private ProductProfile PreferredProfileFallback =>
        AllProducts.FirstOrDefault(AppPaths.ReferencesBuiltInSneakersFolders)
        ?? AllProducts.FirstOrDefault(p => !p.IsDraft)
        ?? ProductProfile.BuiltInSneakers;

    /// <summary>Число элементов в начале списка: встроенные/из комплекта, до черновика или первого пользовательского профиля.</summary>
    private int CountLeadingNonDraftCatalogProfiles()
    {
        var n = 0;
        foreach (var p in AllProducts)
        {
            if (p.IsDraft)
                break;
            if (AppPaths.IsUserInstallProfile(p))
                break;
            n++;
        }

        return n;
    }

    private readonly DispatcherTimer _previewDebounce;

    public ObservableCollection<LogLineViewModel> LogLines { get; } = new();

    public ObservableCollection<string> ReferenceFiles { get; } = new();

    public ObservableCollection<CropMappingRowViewModel> MappingRows { get; } = new();

    /// <summary>Все профили товара для меню (первый — «Кроссовки»).</summary>
    public ObservableCollection<ProductProfile> AllProducts { get; } = new();

    [ObservableProperty] private ProductProfile _selectedProduct = ProductProfile.BuiltInSneakers;

    /// <summary>Расширенный интерфейс (таблица, превью, ручные пути).</summary>
    [ObservableProperty] private bool _isAdvancedView;

    /// <summary>Панель журнала внизу окна (по умолчанию скрыта).</summary>
    [ObservableProperty] private bool _isLogPanelVisible;

    /// <summary>Блок «Цветовой профиль» в простом и расширенном режиме.</summary>
    [ObservableProperty] private bool _isColorProfilePanelVisible = false;

    /// <summary>Блок «Превью» в простом и расширенном режиме.</summary>
    [ObservableProperty] private bool _isPreviewPanelVisible = true;

    /// <summary>Тема интерфейса (меню «Вид → Тема»), сохраняется в %AppData%\AutoRAW\theme_prefs.json.</summary>
    [ObservableProperty] private AppUiTheme _uiTheme = ThemePreferenceStore.Get();

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

    /// <summary>Папка с маркёрными изображениями технологии «Zona» (красная зона = кроп на парном исходнике).</summary>
    [ObservableProperty] private string _zonaFolder = string.Empty;

    [ObservableProperty] private double _analysisMaxEdge = SubjectBoundsEstimator.DefaultAnalysisMaxEdge;

    [ObservableProperty] private bool _isBusy;

    [ObservableProperty] private bool _isBatchPaused;

    [ObservableProperty] private bool _isBatchComplete;

    [ObservableProperty] private bool _isLogDetached;

    [ObservableProperty] private double _batchProgressValue;

    [ObservableProperty] private double _batchProgressMaximum = 100;

    [ObservableProperty] private string _batchStatusText = string.Empty;

    public bool IsLogDockedVisible => IsLogPanelVisible && !IsLogDetached;

    public bool IsBatchStatusVisible => IsBusy || IsBatchComplete;

    public string BatchPrimaryButtonText =>
        !IsBusy ? "Запустить кадрирование" : (IsBatchPaused ? "Продолжить" : "Пауза");

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

    public bool IsSneakersProfile => AppPaths.ReferencesBuiltInSneakersFolders(SelectedProduct);

    partial void OnReferenceFolderChanged(string value)
    {
        RefreshReferenceFiles();
        ConsiderDraftPromotion();
        CommitDraftApplyCommand.NotifyCanExecuteChanged();
        CommitDraftNewCommand.NotifyCanExecuteChanged();
    }

    partial void OnZonaFolderChanged(string value)
    {
        NotifyBatchCommandsChanged();
        SchedulePreviewRefresh();
        ConsiderDraftPromotion();
        CommitDraftApplyCommand.NotifyCanExecuteChanged();
        CommitDraftNewCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedReferenceFileChanged(string value)
    {
        NotifyBatchCommandsChanged();
        ApplyDefaultReferenceToAllCommand.NotifyCanExecuteChanged();
    }

    partial void OnInputFolderChanged(string value) => RebuildMappingRows();

    partial void OnAnalysisMaxEdgeChanged(double value) => SchedulePreviewRefresh();

    partial void OnSelectedMappingRowChanged(CropMappingRowViewModel? value) => SchedulePreviewRefresh();

    partial void OnOutputFolderChanged(string value) => NotifyBatchCommandsChanged();

    partial void OnSaveAsWebPChanged(bool value) => ExportPreferenceStore.SetSaveAsWebP(value);

    partial void OnIsBusyChanged(bool value)
    {
        NotifyBatchCommandsChanged();
        CancelBatchCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(BatchPrimaryButtonText));
        OnPropertyChanged(nameof(IsBatchStatusVisible));
        if (!value)
            IsBatchPaused = false;
    }

    partial void OnIsBatchPausedChanged(bool value)
    {
        OnPropertyChanged(nameof(BatchPrimaryButtonText));
        NotifyBatchCommandsChanged();
        if (IsBusy)
            UpdateBatchStatusText();
    }

    partial void OnIsBatchCompleteChanged(bool value) => OnPropertyChanged(nameof(IsBatchStatusVisible));

    partial void OnIsLogDetachedChanged(bool value) => OnPropertyChanged(nameof(IsLogDockedVisible));

    partial void OnIsLogPanelVisibleChanged(bool value)
    {
        WindowPanelPreferenceStore.SetLogPanelVisible(value);
        OnPropertyChanged(nameof(IsLogDockedVisible));
    }

    partial void OnIsAdvancedViewChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSimpleView));
        SchedulePreviewRefresh();
        CommitDraftApplyCommand.NotifyCanExecuteChanged();
        CommitDraftNewCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsColorProfilePanelVisibleChanged(bool value) =>
        WindowPanelPreferenceStore.SetColorProfilePanelVisible(value);

    partial void OnIsPreviewPanelVisibleChanged(bool value) =>
        WindowPanelPreferenceStore.SetPreviewPanelVisible(value);

    partial void OnUiThemeChanged(AppUiTheme value)
    {
        var app = System.Windows.Application.Current;
        if (app is not null)
            ThemeService.ApplyUserPreference(app, value);
        OnPropertyChanged(nameof(IsThemeMenuLight));
        OnPropertyChanged(nameof(IsThemeMenuDark));
        OnPropertyChanged(nameof(IsThemeMenuSystem));
    }

    public bool IsThemeMenuLight
    {
        get => UiTheme == AppUiTheme.Light;
        set
        {
            if (value)
                UiTheme = AppUiTheme.Light;
            else if (UiTheme == AppUiTheme.Light)
                OnPropertyChanged(nameof(IsThemeMenuLight));
        }
    }

    public bool IsThemeMenuDark
    {
        get => UiTheme == AppUiTheme.Dark;
        set
        {
            if (value)
                UiTheme = AppUiTheme.Dark;
            else if (UiTheme == AppUiTheme.Dark)
                OnPropertyChanged(nameof(IsThemeMenuDark));
        }
    }

    public bool IsThemeMenuSystem
    {
        get => UiTheme == AppUiTheme.System;
        set
        {
            if (value)
                UiTheme = AppUiTheme.System;
            else if (UiTheme == AppUiTheme.System)
                OnPropertyChanged(nameof(IsThemeMenuSystem));
        }
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

        if (AppPaths.ReferencesBuiltInSneakersFolders(profile))
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

        if (AppPaths.IsUserInstallProfile(profile))
        {
            if (profile.Color.XmpFilePath is { } userXmp && File.Exists(userXmp))
            {
                try { return XmpSettingsParser.Parse(userXmp); }
                catch { /* fallback */ }
            }

            return profile.Color;
        }

        // Профили из комплекта приложения (не в %LocalAppData%): переопределения в Roaming
        var shippedStored = ProfileColorOverrideStore.TryGet(profile.DisplayName);
        if (shippedStored is not null)
            return shippedStored;

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
        return ImageFileCatalog.ListImagesRecursive(InputFolder).FirstOrDefault();
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

        if (AppPaths.ReferencesBuiltInSneakersFolders(SelectedProduct))
        {
            ProfileColorOverrideStore.Set(SelectedProduct.DisplayName, settings);
        }
        else if (AppPaths.IsUserInstallProfile(SelectedProduct))
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
        else
        {
            ProfileColorOverrideStore.Set(SelectedProduct.DisplayName, settings);
            for (var i = 0; i < AllProducts.Count; i++)
            {
                if (!ReferenceEquals(AllProducts[i], SelectedProduct))
                    continue;
                var updated = SelectedProduct.WithColor(settings);
                AllProducts[i] = updated;
                SelectedProduct = updated;
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
        NotifyBatchCommandsChanged();
        ApplyDefaultReferenceToAllCommand.NotifyCanExecuteChanged();
    }

    private void OnMappingRowChanged()
    {
        NotifyBatchCommandsChanged();
        SchedulePreviewRefresh();
    }

    private void RefreshReferenceFiles()
    {
        ReferenceFiles.Clear();
        SelectedReferenceFile = string.Empty;

        if (!Directory.Exists(ReferenceFolder))
        {
            SyncMappingRowReferences();
            NotifyBatchCommandsChanged();
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
            NotifyBatchCommandsChanged();
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
            NotifyBatchCommandsChanged();
            ApplyDefaultReferenceToAllCommand.NotifyCanExecuteChanged();
            return;
        }

        var def = DefaultReferenceForNewRow();
        foreach (var path in ImageFileCatalog.ListImagesRecursive(InputFolder))
        {
            var matched = ReferenceNameMatcher.TryMatch(path, ReferenceFiles) ?? def;
            MappingRows.Add(new CropMappingRowViewModel(path, matched, OnMappingRowChanged));
        }

        SelectedMappingRow = MappingRows.FirstOrDefault();

        NotifyBatchCommandsChanged();
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
        dlg.Description = "Папка zona: маркёры технологии Zona (красная зона на изображении)";
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

    [RelayCommand]
    private void ShowTelegramSettings()
    {
        var w = new ZonaTelegramSettingsDialog
        {
            Owner = System.Windows.Application.Current?.MainWindow,
        };
        w.ShowDialog();
    }

    /// <summary>После загрузки главного окна: «что нового» после обновления, затем проверка новой версии.</summary>
    public void SchedulePostLoadUpdateFlow()
    {
        _ = PostLoadUpdateSequenceAsync();
    }

    private async Task PostLoadUpdateSequenceAsync()
    {
        try
        {
            await Task.Delay(900).ConfigureAwait(false);
            await TryShowPendingReleaseNotesAsync().ConfigureAwait(true);
            await TryPromptGitHubUpdateAsync(manual: false).ConfigureAwait(true);
        }
        catch
        {
            /* стартовые проверки не должны ломать запуск */
        }
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        await TryPromptGitHubUpdateAsync(manual: true).ConfigureAwait(true);
    }

    private async Task TryShowPendingReleaseNotesAsync()
    {
        await _dispatcher.InvokeAsync(() =>
        {
            if (!PendingReleaseNotesStore.TryTake(out var ver, out var body))
                return;
            var w = new ReleaseNotesWindow(ver, body)
            {
                Owner = System.Windows.Application.Current?.MainWindow,
            };
            w.ShowDialog();
        });
    }

    private async Task TryPromptGitHubUpdateAsync(bool manual)
    {
        if (!manual && _skipGithubUpdatePromptThisSession)
            return;

        GitHubReleaseOffer? offer = null;
        Exception? err = null;
        try
        {
            offer = manual
                ? await GitHubUpdateService.TryGetLatestOfferAsync().ConfigureAwait(false)
                : await GitHubUpdateService.TryGetLatestOfferNewerThanAsync(AppMetadata.AppVersion).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            err = ex;
        }

        await _dispatcher.InvokeAsync(async () =>
        {
            var owner = System.Windows.Application.Current?.MainWindow;
            if (err is not null)
            {
                if (manual)
                {
                    System.Windows.MessageBox.Show(
                        $"Не удалось проверить обновление:\n{err.Message}",
                        "Проверка обновления",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                return;
            }

            if (offer is null)
            {
                if (manual)
                {
                    System.Windows.MessageBox.Show(
                        "Сейчас нельзя получить сведения о новой версии: данные о релизе недоступны или публикация неполная.\n\n"
                        + "Если вы ставили программу из последнего официального выпуска, у вас, скорее всего, уже последняя версия. Попробуйте проверить обновление позже.",
                        "Проверка обновления",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                return;
            }

            var cur = AppMetadata.AppVersion;
            if (offer.Version <= cur)
            {
                if (manual)
                {
                    var vUi = cur.ToString();
                    System.Windows.MessageBox.Show(
                        $"У вас установлена последняя доступная версия ({vUi}).",
                        "Проверка обновления",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                return;
            }

            var dlg = new UpdateAvailableWindow(offer, cur) { Owner = owner };
            if (dlg.ShowDialog() != true || !dlg.UserChoseInstall)
            {
                if (!manual)
                    _skipGithubUpdatePromptThisSession = true;
                return;
            }

            await RunInstallUpdateFlowAsync(offer).ConfigureAwait(true);
        });
    }

    private async Task RunInstallUpdateFlowAsync(GitHubReleaseOffer offer)
    {
        var owner = System.Windows.Application.Current?.MainWindow;
        var tmp = Path.Combine(Path.GetTempPath(), $"AutoRAW-Setup-{offer.Version}-ru-{Guid.NewGuid():N}.exe");
        var win = new UpdateDownloadProgressWindow { Owner = owner };
        win.Show();
        try
        {
            var progress = new Progress<(long BytesReceived, long? TotalBytes)>(state =>
            {
                _ = win.Dispatcher.BeginInvoke(
                    new Action(() => win.SetProgress(state.BytesReceived, state.TotalBytes)),
                    DispatcherPriority.Normal);
            });
            await GitHubUpdateService.DownloadToFileAsync(offer.DownloadUrl, tmp, progress, CancellationToken.None)
                .ConfigureAwait(false);
            var info = new FileInfo(tmp);
            if (!info.Exists || info.Length < 4096)
                throw new IOException("Загруженный файл слишком мал — проверьте подключение к интернету и повторите попытку позже.");

            PendingReleaseNotesStore.WritePending(offer.TagLabel, offer.BodyMarkdown);

            await win.Dispatcher.InvokeAsync(() => win.Close(), DispatcherPriority.Normal);

            Process.Start(new ProcessStartInfo
            {
                FileName = tmp,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART /SP-",
                UseShellExecute = true,
            });
            await Task.Delay(500).ConfigureAwait(false);
            _dispatcher.Invoke(() => System.Windows.Application.Current?.Shutdown(0));
        }
        catch (Exception ex)
        {
            await win.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    win.Close();
                }
                catch
                {
                    /* ignore */
                }

                System.Windows.MessageBox.Show(
                    $"Не удалось загрузить или запустить обновление.\n\n{ex.Message}",
                    "AutoRAW",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            });
            try
            {
                if (File.Exists(tmp))
                    File.Delete(tmp);
            }
            catch
            {
                /* ignore */
            }
        }
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

        if (!AppPaths.IsUserInstallProfile(src))
        {
            System.Windows.MessageBox.Show(
                "Профиль из комплекта приложения нельзя перезаписать на диске. Используйте «Сохранить как новый профиль…» — данные попадут в %LocalAppData%\\AutoRAW\\user files\\Profile.",
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
        SelectedProduct = pick ?? PreferredProfileFallback;
        ApplyProductFolders();
        AppendLog($"Профиль «{name}» обновлён (%LocalAppData%\\AutoRAW\\user files\\Profile).");
        InvalidateProfileMenu();
    }

    [RelayCommand(CanExecute = nameof(CanCommitDraft))]
    private void CommitDraftNew()
    {
        var nameDlg = new PromptDialog(
                "Новый профиль",
                "Имя профиля (%LocalAppData%\\AutoRAW\\user files\\Profile\\… будут reference, zona для Zona, setting):",
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
        SelectedProduct = pick ?? PreferredProfileFallback;
        ApplyProductFolders();
        AppendLog($"Создан профиль «{name}» (%LocalAppData%\\AutoRAW\\user files\\Profile).");
        InvalidateProfileMenu();
    }

    private void ReloadCustomProfilesFromDisk()
    {
        RemoveDraftFromList();
        for (var i = AllProducts.Count - 1; i >= 0; i--)
        {
            if (AppPaths.IsUserInstallProfile(AllProducts[i]))
                AllProducts.RemoveAt(i);
        }

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
        AllProducts.Insert(CountLeadingNonDraftCatalogProfiles(), draft);
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
        AllProducts.Insert(CountLeadingNonDraftCatalogProfiles(), draft);
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

        if (AppPaths.IsUserInstallProfile(src))
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

            // Если задана папка zona (технология Zona) — референс для строки необязателен
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

    /// <summary>Пауза/продолжение — отдельная команда: async «Запуск» блокирует кнопку до конца пакета.</summary>
    [RelayCommand(CanExecute = nameof(CanPauseOrResumeBatch))]
    private void PauseOrResumeBatch() => TogglePauseBatch();

    private bool CanPauseOrResumeBatch() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanRunBatch))]
    private async Task StartBatchAsync() => await RunBatch();

    [RelayCommand(CanExecute = nameof(CanBatchPrimaryClick))]
    private void BatchPrimaryClick()
    {
        if (!IsBusy)
            _ = StartBatchAsync();
        else
            PauseOrResumeBatch();
    }

    private bool CanBatchPrimaryClick() => IsBusy ? CanPauseOrResumeBatch() : CanRunBatch();

    private void NotifyBatchCommandsChanged()
    {
        BatchPrimaryClickCommand.NotifyCanExecuteChanged();
        PauseOrResumeBatchCommand.NotifyCanExecuteChanged();
        StartBatchCommand.NotifyCanExecuteChanged();
        CancelBatchCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanControlBatch))]
    private void CancelBatch()
    {
        var cancelPhrase = ZonaMessages.NextCancel();
        AppendLog(cancelPhrase, LogLineKind.Cancel, fromZona: true);
        NotifyTelegram($"🚫 ZONA\n{cancelPhrase}");
        _batchRun.Cancel();
    }

    private bool CanControlBatch() => IsBusy;

    private void TogglePauseBatch()
    {
        if (IsBatchPaused)
        {
            _batchRun.Resume();
            IsBatchPaused = false;
            AppendLog(ZonaMessages.NextResume(), LogLineKind.Pause, fromZona: true);
        }
        else
        {
            _batchRun.Pause();
            IsBatchPaused = true;
            BatchStatusText = "На паузе";
            AppendLog(ZonaMessages.NextPause(), LogLineKind.Pause, fromZona: true);
        }
    }

    [RelayCommand]
    private void ToggleLogDetach()
    {
        if (!IsLogPanelVisible)
            IsLogPanelVisible = true;

        IsLogDetached = !IsLogDetached;
    }

    [RelayCommand]
    private void CloseLogPanel()
    {
        IsLogPanelVisible = false;
        IsLogDetached = false;
    }

    private async Task RunBatch()
    {
        bool hasZona = Directory.Exists(ZonaFolder);
        bool hasRef = Directory.Exists(ReferenceFolder);

        var explicitOut = string.IsNullOrWhiteSpace(OutputFolder) ? null : OutputFolder.Trim();
        var inputRoot = Path.GetFullPath(InputFolder);

        var pairs = MappingRows
            .Select(r =>
            {
                var refPath = hasRef && !string.IsNullOrWhiteSpace(r.SelectedReferenceFile)
                    ? Path.Combine(ReferenceFolder, r.SelectedReferenceFile)
                    : r.InputPath;
                return (r.InputPath, refPath);
            })
            .ToList();

        _batchRun.Begin();
        IsBatchPaused = false;
        IsBatchComplete = false;
        BatchProgressMaximum = pairs.Count;
        BatchProgressValue = 0;
        BatchStatusText = pairs.Count > 0 ? $"0/{pairs.Count}" : string.Empty;
        IsBusy = true;
        try
        {
            AppendLog(hasZona
                ? $"Старт пакета (технология Zona, папка {ZonaFolder})…"
                : "Старт пакета (reference-кроп)…");
            AppendLog($"Вход: {inputRoot} (включая вложенные папки, файлов: {pairs.Count}).");

            var formatDir = SaveAsWebP ? "webp" : "jpg";
            if (explicitOut is null)
            {
                AppendLog(SaveAsWebP
                    ? $"Формат: WebP. Выход: {inputRoot}\\{formatDir}\\<подпапка>\\"
                    : $"Формат: JPEG. Выход: {inputRoot}\\{formatDir}\\<подпапка>\\");
            }
            else
            {
                AppendLog(SaveAsWebP ? "Формат: WebP." : "Формат: JPEG.");
                AppendLog($"Выход: {explicitOut}\\<подпапка>\\ (структура как во входе).");
            }

            var edge = (int)Math.Clamp(Math.Round(AnalysisMaxEdge), 256, 8192);
            var zona = hasZona ? ZonaFolder : null;
            var color = GetEffectiveColorFor(SelectedProduct);
            var applyColor = ApplyColorCorrection;
            var webp = SaveAsWebP;

            var result = await Task.Run(() =>
            {
                return _batch.RunMappings(
                    pairs,
                    inputRoot,
                    explicitOut,
                    edge,
                    (msg, kind) => AppendLog(msg, kind),
                    _batchRun,
                    ReportBatchProgress,
                    zona,
                    color,
                    applyColor,
                    webp);
            }, _batchRun.Token);

            if (!result.Cancelled)
            {
                IsBatchComplete = true;
                BatchProgressValue = result.Total;
                BatchProgressMaximum = Math.Max(1, result.Total);
                BatchStatusText = $"Готово {result.Total}/{result.Total}";
                var elapsed = FormatBatchElapsed(result.ActiveElapsed);
                var donePhrase = ZonaMessages.NextDone();
                AppendLog(donePhrase, LogLineKind.Zona, fromZona: true);
                var summary =
                    $"✅ Успешно: {result.Succeeded}, ошибок: {result.Errors}, всего: {result.Total}. Время: {elapsed}.";
                AppendLog(summary, LogLineKind.Done);
                NotifyTelegram(
                    $"{donePhrase}\n\n{summary}\nПрофиль: {SelectedProduct.DisplayName}");
            }
        }
        catch (OperationCanceledException)
        {
            /* отмена — сообщение в журнале и Telegram от CancelBatch */
        }
        catch (Exception ex)
        {
            var errPhrase = ZonaMessages.NextError();
            AppendLog(errPhrase, LogLineKind.Error, fromZona: true);
            AppendLog($"⚠ {ex.Message}", LogLineKind.Error);
            NotifyTelegram($"❌ ZONA — ошибка\n{errPhrase}\n\n{ex.Message}");
        }
        finally
        {
            IsBusy = false;
            IsBatchPaused = false;
            NotifyBatchCommandsChanged();
        }
    }

    private void ReportBatchProgress(int completed, int total, int succeeded, int errors)
    {
        void Apply()
        {
            if (IsBatchPaused)
                return;

            BatchProgressMaximum = Math.Max(1, total);
            BatchProgressValue = completed;
            BatchStatusText = total > 0 ? $"{completed}/{total}" : string.Empty;
        }

        if (_dispatcher.CheckAccess())
            Apply();
        else
            _dispatcher.BeginInvoke(Apply);
    }

    private void UpdateBatchStatusText()
    {
        if (!IsBusy)
            return;

        if (IsBatchPaused)
        {
            BatchStatusText = "На паузе";
            return;
        }

        var total = (int)BatchProgressMaximum;
        var done = (int)Math.Min(BatchProgressValue, total);
        BatchStatusText = total > 0 ? $"{done}/{total}" : string.Empty;
    }

    private static string FormatBatchElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1)
            return $"{(int)elapsed.TotalHours}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
        return $"{elapsed.Minutes}:{elapsed.Seconds:D2}";
    }

    private void AppendLog(string message, LogLineKind kind = LogLineKind.Normal, bool fromZona = false)
    {
        var line = new LogLineViewModel($"[{DateTime.Now:HH:mm:ss}] {message}", kind, fromZona);
        if (_dispatcher.CheckAccess())
            LogLines.Add(line);
        else
            _ = _dispatcher.BeginInvoke(() => LogLines.Add(line));
    }

    private static void NotifyTelegram(string text) =>
        ZonaTelegramNotifyService.TrySendFireAndForget(text);

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

                    a = zonaPath is not null && refPath is not null
                        ? CropPreviewBitmapFactory.LoadZonaCroppedPreview(row.InputPath, zonaPath, refPath, edge, thumb, color, applyColor)
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
