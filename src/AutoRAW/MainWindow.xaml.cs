using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using AutoRAW.Models;
using AutoRAW.Services;
using AutoRAW.ViewModels;

namespace AutoRAW;

public partial class MainWindow : Window
{
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
    }

    private void MenuProfileRoot_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        vm.AllProducts.CollectionChanged -= Vm_AllProducts_CollectionChanged;
        vm.AllProducts.CollectionChanged += Vm_AllProducts_CollectionChanged;

        vm.PropertyChanged -= Vm_OnPropertyChanged;
        vm.PropertyChanged += Vm_OnPropertyChanged;

        vm.ProfileMenuInvalidated -= Vm_ProfileMenuInvalidated;
        vm.ProfileMenuInvalidated += Vm_ProfileMenuInvalidated;

        RebuildProfileSubmenu();
    }

    private void Vm_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedProduct))
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
