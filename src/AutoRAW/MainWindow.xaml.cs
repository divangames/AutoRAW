using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AutoRAW.Helpers;
using AutoRAW.Models;
using AutoRAW.Services;
using AutoRAW.ViewModels;
using Mouse = System.Windows.Input.Mouse;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonState = System.Windows.Input.MouseButtonState;
using WpfPoint = System.Windows.Point;

namespace AutoRAW;

public partial class MainWindow : Window
{
    private LogPanelWindow? _detachedLogWindow;
    private WpfPoint? _logHeaderDragOrigin;

    public MainWindow()
    {
        InitializeComponent();
        Title = $"AutoRAW — {AppMetadata.DisplayVersion}";
        DataContext = new MainViewModel(Dispatcher);
        Loaded += MainWindow_Loaded;
    }


    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RebuildProfileSubmenu();

        if (DataContext is MainViewModel vm)
        {
            vm.SchedulePostLoadUpdateFlow();
            vm.PropertyChanged += Vm_OnPropertyChanged;
            SyncDetachedLogWindow();
            FolderDropTarget.Register(this, folder => vm.InputFolder = folder);

            // Авто-прокрутка журнала к последнему сообщению при добавлении
            vm.LogLines.CollectionChanged += (_, _) =>
            {
                if (LogListBox.Items.Count > 0)
                    LogListBox.ScrollIntoView(LogListBox.Items[^1]);
            };
        }
    }

    private void Vm_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainViewModel vm)
            return;

        if (e.PropertyName == nameof(MainViewModel.SelectedProduct))
            RebuildProfileSubmenu();

        if (e.PropertyName is nameof(MainViewModel.IsLogDetached) or nameof(MainViewModel.IsLogPanelVisible))
            SyncDetachedLogWindow();
    }

    private void SyncDetachedLogWindow()
    {
        if (DataContext is not MainViewModel vm)
            return;

        if (vm.IsLogPanelVisible && vm.IsLogDetached)
        {
            if (_detachedLogWindow is null)
            {
                _detachedLogWindow = new LogPanelWindow
                {
                    Owner = this,
                    DataContext = vm
                };
                _detachedLogWindow.Closed += DetachedLogWindow_Closed;
                _detachedLogWindow.Show();
            }
        }
        else if (_detachedLogWindow is not null)
        {
            _detachedLogWindow.Closed -= DetachedLogWindow_Closed;
            _detachedLogWindow.Close();
            _detachedLogWindow = null;
        }
    }

    private void DetachedLogWindow_Closed(object? sender, EventArgs e)
    {
        _detachedLogWindow = null;
        if (DataContext is MainViewModel vm && vm.IsLogDetached)
            vm.IsLogDetached = false;
    }

    // ─── Drag-to-detach для шапки журнала ───
    private void LogHeader_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
            _logHeaderDragOrigin = (WpfPoint?)e.GetPosition(this);
    }

    private void LogHeader_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _logHeaderDragOrigin is null)
        {
            _logHeaderDragOrigin = null;
            return;
        }

        var delta = e.GetPosition(this) - (WpfPoint)_logHeaderDragOrigin.Value;
        if (Math.Abs(delta.X) + Math.Abs(delta.Y) < 8)
            return;

        _logHeaderDragOrigin = null;

        if (DataContext is not MainViewModel vm || vm.IsLogDetached)
            return;

        vm.ToggleLogDetachCommand.Execute(null);

        // После того как SyncDetachedLogWindow синхронно создал окно,
        // запускаем DragMove при условии что кнопка всё ещё зажата.
        Dispatcher.BeginInvoke(() =>
        {
            if (_detachedLogWindow is { } w && Mouse.LeftButton == MouseButtonState.Pressed)
            {
                try { w.DragMove(); }
                catch { /* кнопка отпущена до вызова */ }
            }
        }, DispatcherPriority.Loaded);
    }

    private void LogHeader_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        _logHeaderDragOrigin = null;

    private void MenuProfileRoot_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        vm.AllProducts.CollectionChanged -= Vm_AllProducts_CollectionChanged;
        vm.AllProducts.CollectionChanged += Vm_AllProducts_CollectionChanged;

        vm.ProfileMenuInvalidated -= Vm_ProfileMenuInvalidated;
        vm.ProfileMenuInvalidated += Vm_ProfileMenuInvalidated;

        RebuildProfileSubmenu();
    }

    private void Vm_AllProducts_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        RebuildProfileSubmenu();

    private void Vm_ProfileMenuInvalidated(object? sender, EventArgs e) =>
        RebuildProfileSubmenu();

    private void ProductSubmenu_OnSubmenuOpened(object sender, RoutedEventArgs e) =>
        RebuildProfileSubmenu();

    private void RebuildProfileSubmenu()
    {
        if (DataContext is not MainViewModel vm)
            return;

        var menu = MenuProfileRoot;
        menu.Items.Clear();

        foreach (var p in vm.AllProducts)
        {
            var prefix = ReferenceEquals(vm.SelectedProduct, p) ? "✓ " : "";
            menu.Items.Add(new MenuItem
            {
                Header = prefix + p.DisplayName,
                Command = vm.SelectProductCommand,
                CommandParameter = p
            });
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem
        {
            Header = "Добавить новый профиль…",
            Command = vm.AddNewProfileCommand
        });
        menu.Items.Add(new MenuItem
        {
            Header = "Перейти в мой каталог профилей…",
            Command = vm.OpenUserProfilesFolderCommand
        });
    }
}
