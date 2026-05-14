using System.Windows;
using AutoRAW.Services;

namespace AutoRAW;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        VersionLine.Text = $"Версия {AppMetadata.DisplayVersion}";
    }

    public void SetStatus(string text) => StatusBarText.Text = text;

    /// <summary>Для будущего длительного старта: детерминированный прогресс (0–100).</summary>
    public void SetBootProgress(double percent)
    {
        BootProgress.IsIndeterminate = false;
        BootProgress.Maximum = 100;
        BootProgress.Value = Math.Clamp(percent, 0, 100);
    }

    public void SetBootProgressIndeterminate()
    {
        BootProgress.IsIndeterminate = true;
    }
}
