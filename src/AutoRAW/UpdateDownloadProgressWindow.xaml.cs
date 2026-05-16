using System.Windows;

namespace AutoRAW;

public partial class UpdateDownloadProgressWindow : Window
{
    public UpdateDownloadProgressWindow()
    {
        InitializeComponent();
    }

    public void SetProgress(long bytesReceived, long? totalBytes)
    {
        if (totalBytes is > 0)
        {
            DownloadProgress.IsIndeterminate = false;
            DownloadProgress.Maximum = 100;
            DownloadProgress.Value = 100.0 * bytesReceived / totalBytes.Value;
            StatusBlock.Text = $"{FormatMb(bytesReceived)} / {FormatMb(totalBytes.Value)}";
        }
        else
        {
            DownloadProgress.IsIndeterminate = true;
            StatusBlock.Text = $"{FormatMb(bytesReceived)}…";
        }
    }

    private static string FormatMb(long bytes)
    {
        var mb = bytes / 1048576.0;
        return mb >= 0.1 ? $"{mb:0.0} МБ" : $"{bytes / 1024} КБ";
    }
}
