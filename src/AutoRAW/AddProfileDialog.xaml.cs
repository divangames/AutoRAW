using System.IO;
using System.Windows;
using AutoRAW.Models;
using AutoRAW.Services;

namespace AutoRAW;

public partial class AddProfileDialog : Window
{
    private ColorCorrectionSettings _parsedColor = ColorCorrectionSettings.Neutral;

    public AddProfileDialog()
    {
        InitializeComponent();
    }

    public ProductProfile? ResultProfile { get; private set; }

    private void BrowseRef(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Папка с референсами (jpg и т.д.)"
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            RefPathBox.Text = dlg.SelectedPath;
    }

    private void BrowseZona(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Папка zona — маркёры технологии Zona (красная зона)"
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            ZonaPathBox.Text = dlg.SelectedPath;
    }

    private void BrowseXmp(object sender, RoutedEventArgs e)
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

        try
        {
            _parsedColor = XmpSettingsParser.Parse(dlg.FileName);
            XmpPathBox.Text = dlg.FileName;
            XmpSummaryBlock.Text = BuildSummaryLines(_parsedColor);
            XmpSummaryBorder.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Не удалось прочитать XMP:\n{ex.Message}", "AutoRAW",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string BuildSummaryLines(ColorCorrectionSettings s)
    {
        var lines = new List<string>
        {
            $"Температура: {s.TemperatureKelvin:0} K   Оттенок: {s.Tint:+0;-0;0}",
            $"Экспозиция: {s.Exposure:+0.00;-0.00;0.00}   Контраст: {s.Contrast:+0;-0;0}",
            $"Света: {s.Highlights:+0;-0;0}   Тени: {s.Shadows:+0;-0;0}",
        };
        if (Math.Abs(s.Saturation) > 0.5 || Math.Abs(s.Vibrance) > 0.5)
            lines.Add($"Насыщенность: {s.Saturation:+0;-0;0}   Вибранс: {s.Vibrance:+0;-0;0}");
        return string.Join("\n", lines);
    }

    private void OkClick(object sender, RoutedEventArgs e)
    {
        var name = (ProfileNameBox.Text ?? string.Empty).Trim();
        var r    = (RefPathBox.Text  ?? string.Empty).Trim();
        var z    = (ZonaPathBox.Text ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            System.Windows.MessageBox.Show("Укажите имя профиля.", "AutoRAW",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!Directory.Exists(r) || !Directory.Exists(z))
        {
            System.Windows.MessageBox.Show("Укажите существующие папки: референсы и каталог zona (Zona).", "AutoRAW",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var color = _parsedColor with { UseStandardColorSpace = StdColorCheck.IsChecked == true };
            ResultProfile = UserProfileBundleService.WriteBundle(name, r, z, color);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Не удалось сохранить профиль:\n{ex.Message}", "AutoRAW",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
