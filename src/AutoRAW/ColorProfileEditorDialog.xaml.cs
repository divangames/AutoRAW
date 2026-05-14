using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using AutoRAW.Models;
using AutoRAW.Services;
using ImageMagick;

namespace AutoRAW;

public partial class ColorProfileEditorDialog : Window
{
    private readonly string? _previewPath;
    private ColorCorrectionSettings _current;

    public ColorCorrectionSettings? ResultSettings { get; private set; }

    public ColorProfileEditorDialog(ColorCorrectionSettings initial, string? previewImagePath)
    {
        InitializeComponent();
        _previewPath = previewImagePath;
        _current = initial;

        StdColorCheck.IsChecked = initial.UseStandardColorSpace;

        if (!string.IsNullOrWhiteSpace(initial.XmpFilePath))
        {
            XmpPathBox.Text = initial.XmpFilePath;
            UpdateSummary(initial);
        }

        Loaded += (_, _) => RefreshPreview();
    }

    private void BrowseXmpClick(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.OpenFileDialog
        {
            Title  = "Выберите XMP-пресет",
            Filter = "XMP-пресет (*.xmp)|*.xmp|Все файлы (*.*)|*.*",
        };

        var startDir = AppPaths.DefaultSettingFolder;
        if (Directory.Exists(startDir))
            dlg.InitialDirectory = startDir;

        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;

        var path = dlg.FileName;
        try
        {
            var parsed = XmpSettingsParser.Parse(path);
            _current = parsed with { UseStandardColorSpace = StdColorCheck.IsChecked == true };
            XmpPathBox.Text = path;
            UpdateSummary(_current);
            RefreshPreview();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Не удалось прочитать XMP:\n{ex.Message}", "AutoRAW",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdateSummary(ColorCorrectionSettings s)
    {
        var lines = new List<string>
        {
            $"Температура: {s.TemperatureKelvin:0} K   Оттенок: {s.Tint:+0;-0;0}",
            $"Экспозиция: {s.Exposure:+0.00;-0.00;0.00}   Контраст: {s.Contrast:+0;-0;0}",
            $"Света: {s.Highlights:+0;-0;0}   Тени: {s.Shadows:+0;-0;0}   Белые: {s.Whites:+0;-0;0}   Чёрные: {s.Blacks:+0;-0;0}",
        };
        if (Math.Abs(s.Saturation) > 0.5 || Math.Abs(s.Vibrance) > 0.5)
            lines.Add($"Насыщенность: {s.Saturation:+0;-0;0}   Вибранс: {s.Vibrance:+0;-0;0}");
        if (Math.Abs(s.Clarity) > 0.5 || Math.Abs(s.Dehaze) > 0.5)
            lines.Add($"Чёткость: {s.Clarity:+0;-0;0}   Dehaze: {s.Dehaze:+0;-0;0}");
        if (Math.Abs(s.Sharpness - 40) > 0.5)
            lines.Add($"Резкость: {s.Sharpness:0}");
        XmpSummaryBlock.Text = string.Join("\n", lines);
    }

    private ColorCorrectionSettings BuildCurrent() =>
        _current with { UseStandardColorSpace = StdColorCheck.IsChecked == true };

    private void RefreshPreview()
    {
        if (string.IsNullOrWhiteSpace(_previewPath) || !File.Exists(_previewPath))
        {
            BeforeImage.Source = null;
            AfterImage.Source  = null;
            return;
        }

        try
        {
            using var src   = RasterImageLoader.Load(_previewPath);
            using var small = AutoCropComputation.CloneResizedLongEdge(src, 900);

            using var before = (MagickImage)small.Clone();
            BeforeImage.Source = ToBitmap(before);

            using var after = (MagickImage)small.Clone();
            ColorCorrectionService.ApplyIfEnabled(after, BuildCurrent(), true);
            AfterImage.Source = ToBitmap(after);
        }
        catch
        {
            BeforeImage.Source = null;
            AfterImage.Source  = null;
        }
    }

    private static BitmapSource ToBitmap(MagickImage img)
    {
        using var ms = new MemoryStream();
        img.Format = MagickFormat.Png;
        img.Write(ms);
        ms.Position = 0;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption  = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private void RefreshPreviewClick(object sender, RoutedEventArgs e) => RefreshPreview();

    private void OkClick(object sender, RoutedEventArgs e)
    {
        ResultSettings = BuildCurrent();
        DialogResult   = true;
        Close();
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
