using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using AutoRAW.Services;

namespace AutoRAW;

public partial class ReleaseNotesWindow : Window
{
    public ReleaseNotesWindow(string versionLabel, string body)
    {
        InitializeComponent();
        TitleBlock.Text = $"Версия {versionLabel}";
        BodyBrowser.Navigating += BodyBrowser_OnNavigating;

        var md = string.IsNullOrWhiteSpace(body) ? "*Описание релиза отсутствует.*" : body;
        BodyBrowser.NavigateToString(MarkdownDocumentFormatter.ToHtmlDocument(md, $"AutoRAW {versionLabel}", narrowDialog: true));
    }

    private static void BodyBrowser_OnNavigating(object? sender, NavigatingCancelEventArgs e)
    {
        if (e.Uri is null)
            return;
        var scheme = e.Uri.Scheme;
        if (scheme is not ("http" or "https" or "mailto"))
            return;
        e.Cancel = true;
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            /* ignore */
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Close();
}
