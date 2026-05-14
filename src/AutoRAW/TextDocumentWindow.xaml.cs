using System.Diagnostics;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Navigation;
using AutoRAW.Services;

namespace AutoRAW;

public partial class TextDocumentWindow : Window
{
    public TextDocumentWindow(string title, string filePath)
    {
        InitializeComponent();
        Title = title;
        PathText.Text = filePath;
        BodyBrowser.Navigating += BodyBrowser_OnNavigating;

        try
        {
            string raw;
            if (File.Exists(filePath))
                raw = File.ReadAllText(filePath);
            else
                raw = $"# Файл не найден\n\nПуть: `{filePath}`\n\nПоложите файл рядом с **AutoRAW.exe** или обновите сборку.";

            var isMd = filePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
            var html = isMd
                ? MarkdownDocumentFormatter.ToHtmlDocument(raw, title)
                : WrapPlainAsHtml(WebUtility.HtmlEncode(raw));

            BodyBrowser.NavigateToString(html);
        }
        catch (Exception ex)
        {
            var err = "# Ошибка чтения\n\n```\n" + WebUtility.HtmlEncode(ex.Message) + "\n```";
            BodyBrowser.NavigateToString(MarkdownDocumentFormatter.ToHtmlDocument(err, title));
        }
    }

    private static string WrapPlainAsHtml(string encodedText) =>
        """
<!DOCTYPE html>
<html lang="ru"><head><meta charset="utf-8"/><title>document</title>
<style>body{font-family:Consolas,monospace;font-size:13px;padding:16px;background:#fafbfc;} pre{white-space:pre-wrap;}</style>
</head><body><pre>
""" + encodedText + """
</pre></body></html>
""";

    private void BodyBrowser_OnNavigating(object? sender, NavigatingCancelEventArgs e)
    {
        if (e.Uri is null)
            return;
        var scheme = e.Uri.Scheme;
        if (scheme is "http" or "https" or "mailto")
        {
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
    }
}
