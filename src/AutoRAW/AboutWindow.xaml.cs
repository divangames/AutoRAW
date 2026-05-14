using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using AutoRAW.Services;

namespace AutoRAW;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionBlock.Text = $"Версия {AppMetadata.DisplayVersion}";
    }

    private void Link_OnRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            /* ignore */
        }

        e.Handled = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
