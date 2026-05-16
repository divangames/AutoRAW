using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AutoRAW.Helpers;

/// <summary>Перетаскивание папки/файла из Проводника в зону ввода.</summary>
public static class FolderDropTarget
{
    public const string ZoneTag = "InputDropZone";

    public static void Register(FrameworkElement root, Action<string> onFolder)
    {
        if (root is null)
            throw new ArgumentNullException(nameof(root));

        void Walk(DependencyObject parent)
        {
            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is FrameworkElement fe && IsDropZone(fe))
                    Attach(fe, onFolder);

                Walk(child);
            }
        }

        Walk(root);
    }

    private static bool IsDropZone(FrameworkElement fe) =>
        ZoneTag.Equals(fe.Tag as string, StringComparison.Ordinal);

    private static void Attach(FrameworkElement zone, Action<string> onFolder)
    {
        zone.AllowDrop = true;
        zone.PreviewDragOver += (_, e) => OnDragOver(zone, e);
        zone.DragOver += (_, e) => OnDragOver(zone, e);
        zone.DragEnter += (_, e) => OnDragOver(zone, e);
        zone.DragLeave += (_, e) => OnDragLeave(zone, e);
        zone.PreviewDrop += (_, e) => OnDrop(zone, e, onFolder);
        zone.Drop += (_, e) => OnDrop(zone, e, onFolder);

        foreach (var child in EnumerateDescendants(zone).OfType<FrameworkElement>())
        {
            if (child is System.Windows.Controls.Button)
                continue;

            child.AllowDrop = true;
            child.PreviewDragOver += (_, e) => OnDragOver(zone, e);
            child.DragOver += (_, e) => OnDragOver(zone, e);
        }
    }

    private static IEnumerable<DependencyObject> EnumerateDescendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in EnumerateDescendants(child))
                yield return nested;
        }
    }

    private static void OnDragOver(FrameworkElement zone, System.Windows.DragEventArgs e)
    {
        if (TryExtractFolder(e, out _))
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
            e.Handled = true;
            SetHighlight(zone, true);
            return;
        }

        e.Effects = System.Windows.DragDropEffects.None;
        e.Handled = true;
        SetHighlight(zone, false);
    }

    private static void OnDragLeave(FrameworkElement zone, System.Windows.DragEventArgs e)
    {
        if (!zone.IsMouseOver)
            SetHighlight(zone, false);
    }

    private static void OnDrop(FrameworkElement zone, System.Windows.DragEventArgs e, Action<string> onFolder)
    {
        if (e.Handled)
            return;

        SetHighlight(zone, false);

        if (!TryExtractFolder(e, out var folder))
            return;

        onFolder(folder);
        e.Handled = true;
    }

    private static bool TryExtractFolder(System.Windows.DragEventArgs e, out string folder)
    {
        folder = string.Empty;
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            return false;

        var raw = e.Data.GetData(System.Windows.DataFormats.FileDrop, autoConvert: true);
        switch (raw)
        {
            case string single when Directory.Exists(single):
                folder = Path.GetFullPath(single);
                return true;
            case string[] paths:
                foreach (var path in paths)
                {
                    if (string.IsNullOrWhiteSpace(path))
                        continue;

                    if (Directory.Exists(path))
                    {
                        folder = Path.GetFullPath(path);
                        return true;
                    }

                    if (File.Exists(path))
                    {
                        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        {
                            folder = dir;
                            return true;
                        }
                    }
                }

                break;
        }

        return false;
    }

    private static void SetHighlight(FrameworkElement zone, bool active)
    {
        if (zone is not Border border)
            return;

        if (active)
        {
            border.BorderBrush = GetBrush(border, "Theme.Accent");
            border.BorderThickness = new Thickness(2);
            border.Background = GetBrush(border, "Theme.ListItemHover");
            return;
        }

        border.BorderBrush = GetBrush(border, "Theme.CardBorder");
        border.BorderThickness = new Thickness(1);
        border.Background = System.Windows.Media.Brushes.Transparent;
    }

    private static System.Windows.Media.Brush GetBrush(FrameworkElement fe, string key) =>
        fe.TryFindResource(key) as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray;
}
