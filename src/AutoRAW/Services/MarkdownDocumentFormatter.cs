using Markdig;

namespace AutoRAW.Services;

/// <summary>Markdown → HTML (GitHub-расширения) для просмотра в WebBrowser.</summary>
public static class MarkdownDocumentFormatter
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private const string HtmlShellPrefix = """
<!DOCTYPE html>
<html lang="ru">
<head>
<meta charset="utf-8"/>
<meta http-equiv="X-UA-Compatible" content="IE=edge"/>
<title>
""";

    private const string HtmlShellMid = """
</title>
<style>
  :root { color-scheme: light; }
  body {
    font-family: 'Segoe UI', system-ui, -apple-system, sans-serif;
    font-size: 14px;
    line-height: 1.55;
    color: #1a1a1a;
    margin: 0;
    padding: 16px 20px 28px;
    max-width: 52rem;
    margin-left: auto;
    margin-right: auto;
    background: #fafbfc;
  }
  body.dialog-narrow {
    max-width: none;
    padding: 12px 14px 18px;
    font-size: 13px;
    line-height: 1.5;
    background: #ffffff;
  }
  body.dialog-narrow h2:first-child,
  body.dialog-narrow h3:first-child { margin-top: 0.35em; }
  h1 { font-size: 1.75rem; font-weight: 600; margin: 0 0 0.75em; border-bottom: 1px solid #e1e4e8; padding-bottom: 0.35em; }
  h2 { font-size: 1.35rem; font-weight: 600; margin: 1.35em 0 0.55em; }
  h3 { font-size: 1.12rem; font-weight: 600; margin: 1.1em 0 0.45em; }
  p { margin: 0.65em 0; }
  a { color: #0969da; text-decoration: none; }
  a:hover { text-decoration: underline; }
  code {
    font-family: ui-monospace, Consolas, monospace;
    font-size: 0.92em;
    background: #f0f3f6;
    padding: 0.12em 0.35em;
    border-radius: 4px;
  }
  pre {
    font-family: ui-monospace, Consolas, monospace;
    font-size: 12.5px;
    background: #f6f8fa;
    border: 1px solid #e1e4e8;
    border-radius: 6px;
    padding: 12px 14px;
    overflow: auto;
    line-height: 1.45;
  }
  pre code { background: none; padding: 0; }
  ul, ol { margin: 0.5em 0; padding-left: 1.5em; }
  li { margin: 0.35em 0; }
  blockquote {
    margin: 0.85em 0;
    padding: 0.35em 0 0.35em 1em;
    border-left: 4px solid #d8dee4;
    color: #444;
    background: #f6f8fa;
    border-radius: 0 6px 6px 0;
  }
  table {
    border-collapse: collapse;
    width: 100%;
    margin: 1em 0;
    font-size: 13px;
  }
  th, td { border: 1px solid #e1e4e8; padding: 8px 10px; text-align: left; }
  th { background: #f3f4f6; font-weight: 600; }
  hr { border: none; border-top: 1px solid #e1e4e8; margin: 1.5em 0; }
  img { max-width: 100%; height: auto; border-radius: 4px; }
  .markdown-body { }
</style>
</head>
""";

    private const string HtmlShellSuffix = """
</body>
</html>
""";

    public static string ToHtmlDocument(string markdown, string? title = null, bool narrowDialog = false)
    {
        var body = Markdown.ToHtml(markdown ?? string.Empty, Pipeline);
        var t = string.IsNullOrWhiteSpace(title) ? "AutoRAW" : System.Net.WebUtility.HtmlEncode(title);
        var bodyOpen = narrowDialog
            ? "<body class=\"markdown-body dialog-narrow\">\n"
            : "<body class=\"markdown-body\">\n";
        return HtmlShellPrefix + t + HtmlShellMid + bodyOpen + body + HtmlShellSuffix;
    }
}
