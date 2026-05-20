using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoRAW.ViewModels;
using WpfImage = System.Windows.Controls.Image;
using WpfPoint = System.Windows.Point;

namespace AutoRAW;

public partial class VisualShotEditorWindow : Window
{
    private WpfPoint _dragStart;
    private bool _dragging;

    public VisualShotEditorWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is VisualShotEditorViewModel vm)
                vm.ReloadFolderList();
        };
        Closed += VisualShotEditorWindow_OnClosed;
    }

    private void VisualShotEditorWindow_OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is VisualShotEditorViewModel vm)
        {
            vm.PersistIfEditingForClose();
            vm.DisposeCropResources();
        }
    }

    private VisualShotEditorViewModel? Vm => DataContext as VisualShotEditorViewModel;

    private void Thumb_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Vm is null)
            return;

        if ((sender as FrameworkElement)?.DataContext is FileThumbItemViewModel item)
            Vm.OpenFileCommand.Execute(item);
    }

    private void ResultImage_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not WpfImage img || Vm is null)
            return;

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
        _dragging = false;
        img.ReleaseMouseCapture();
    }
}
