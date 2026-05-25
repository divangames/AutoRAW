using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoRAW.ViewModels;
using WpfImage = System.Windows.Controls.Image;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfPoint = System.Windows.Point;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace AutoRAW;

public partial class VisualShotEditorWindow : Window
{
    private WpfPoint _dragStart;
    private bool _dragging;
    private bool _syncingFolderSelection;

    public VisualShotEditorWindow()
    {
        InitializeComponent();
        Loaded += VisualShotEditorWindow_OnLoaded;
        Closed += VisualShotEditorWindow_OnClosed;
    }

    private void VisualShotEditorWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is VisualShotEditorViewModel vm)
        {
            vm.SyncFolderSelectionAfterReload += OnSyncFolderSelectionAfterReload;
            vm.ReloadFolderList();
        }
    }

    private void VisualShotEditorWindow_OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is VisualShotEditorViewModel vm)
        {
            vm.SyncFolderSelectionAfterReload -= OnSyncFolderSelectionAfterReload;
            vm.PersistIfEditingForClose();
            vm.DisposeCropResources();
        }
    }

    private VisualShotEditorViewModel? Vm => DataContext as VisualShotEditorViewModel;

    private void OnSyncFolderSelectionAfterReload(IReadOnlyList<FolderListItemViewModel> folders)
    {
        _syncingFolderSelection = true;
        try
        {
            FoldersList.SelectedItems.Clear();
            foreach (var f in folders)
            {
                if (FoldersList.Items.Contains(f))
                    FoldersList.SelectedItems.Add(f);
            }
        }
        finally
        {
            _syncingFolderSelection = false;
        }
    }

    private void FoldersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingFolderSelection || Vm is null || sender is not WpfListBox lb)
            return;

        var sel = lb.SelectedItems.Cast<FolderListItemViewModel>().ToList();
        Vm.NotifyFoldersSelectionChanged(sel);
    }

    private void Thumb_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Vm is null)
            return;

        var mods = Keyboard.Modifiers;
        if ((mods & ModifierKeys.Control) != 0 || (mods & ModifierKeys.Shift) != 0)
            return;

        if ((sender as FrameworkElement)?.DataContext is FileThumbItemViewModel item)
            Vm.OpenFileCommand.Execute(item);
    }

    private void FileName_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
            return;

        e.Handled = true;
        if (Vm is null || sender is not FrameworkElement fe || fe.DataContext is not FileThumbItemViewModel item)
            return;

        Vm.BeginRenameFile(item);
    }

    private void RenameBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is WpfTextBox tb)
        {
            tb.Focus();
            tb.SelectAll();
        }
    }

    private void RenameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (Vm is null || sender is not WpfTextBox tb || tb.DataContext is not FileThumbItemViewModel item)
            return;

        if (!item.IsRenaming)
            return;

        Vm.CommitRenameFile(item);
    }

    private void RenameBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (Vm is null || sender is not WpfTextBox tb || tb.DataContext is not FileThumbItemViewModel item)
            return;

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Vm.CommitRenameFile(item);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Vm.CancelRenameFile(item);
        }
    }

    private void ResultImage_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not WpfImage img || Vm is null)
            return;

        Vm.NotifyEditorResultPanStarted();
        _dragging = true;
        _dragStart = e.GetPosition(img);
        img.CaptureMouse();
    }

    private void ResultImage_OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragging || sender is not WpfImage img || Vm is null)
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            StopDrag(img);
            return;
        }

        var p = e.GetPosition(img);
        var dx = p.X - _dragStart.X;
        var dy = p.Y - _dragStart.Y;
        _dragStart = p;
        var sx = Vm.ResultOutputWidth / Math.Max(1.0, img.ActualWidth);
        var sy = Vm.ResultOutputHeight / Math.Max(1.0, img.ActualHeight);
        Vm.ApplyDragDelta(dx, dy, sx, sy);
    }

    private void ResultImage_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is WpfImage img)
            StopDrag(img);
    }

    private void ResultImage_OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is WpfImage img && e.LeftButton != MouseButtonState.Pressed)
            StopDrag(img);
    }

    private void StopDrag(WpfImage img)
    {
        var wasDragging = _dragging;
        _dragging = false;
        img.ReleaseMouseCapture();
        if (wasDragging && Vm is not null)
            Vm.NotifyEditorResultPanEnded();
    }
}
