using System.IO;
using System.Windows;
using AutoRAW.Models;
using AutoRAW.Services;

namespace AutoRAW;

public partial class AddProfileDialog : Window
{
    public AddProfileDialog()
    {
        InitializeComponent();
    }

    public ProductProfile? ResultProfile { get; private set; }

    private void BrowseProfileFolder(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Папка профиля (profile.json, reference, zona или profiles\\Имя)"
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            ProfileFolderBox.Text = dlg.SelectedPath;
            if (string.IsNullOrWhiteSpace(ProfileNameBox.Text)
                || string.Equals(ProfileNameBox.Text.Trim(), "Новый профиль", StringComparison.OrdinalIgnoreCase))
            {
                ProfileNameBox.Text = Path.GetFileName(dlg.SelectedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                    ?? "Новый профиль";
            }
        }
    }

    private void OkClick(object sender, RoutedEventArgs e)
    {
        var name = (ProfileNameBox.Text ?? string.Empty).Trim();
        var folder = (ProfileFolderBox.Text ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            System.Windows.MessageBox.Show("Укажите имя профиля.", "AutoRAW",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!Directory.Exists(folder))
        {
            System.Windows.MessageBox.Show("Укажите существующую папку профиля.", "AutoRAW",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            ResultProfile = UserProfileBundleService.ImportFromDirectory(folder, name);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Не удалось импортировать профиль:\n{ex.Message}", "AutoRAW",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
