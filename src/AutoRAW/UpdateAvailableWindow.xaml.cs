using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using AutoRAW.Services;

namespace AutoRAW;

public partial class UpdateAvailableWindow : Window
{
    public bool UserChoseInstall { get; private set; }

    public UpdateAvailableWindow(GitHubReleaseOffer offer, Version currentVersion)
    {
        InitializeComponent();
        HeadlineBlock.Text =
            $"Версия {AppMetadata.FormatVersionUi(offer.Version)} (у вас {AppMetadata.FormatVersionUi(currentVersion)}) — {offer.ReleaseTitle}";

        BodyBrowser.Navigating += BodyBrowser_OnNavigating;

        var md = string.IsNullOrWhiteSpace(offer.BodyMarkdown)
            ? "*Описание обновления не указано.*"
            : offer.BodyMarkdown;
        var title = $"AutoRAW {AppMetadata.FormatVersionUi(offer.Version)}";
        BodyBrowser.NavigateToString(MarkdownDocumentFormatter.ToHtmlDocument(md, title, narrowDialog: true));
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

    private void Update_Click(object sender, RoutedEventArgs e)
    {
        UserChoseInstall = true;
        DialogResult = true;
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        UserChoseInstall = false;
        DialogResult = false;
    }
}
