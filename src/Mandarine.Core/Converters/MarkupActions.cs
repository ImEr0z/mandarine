using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Mandarine.Core.Actions;

namespace Mandarine.Core.Converters;

internal static partial class Markup
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public static string MarkdownToHtmlDocument(string markdown, string title)
    {
        var body = Markdown.ToHtml(markdown, Pipeline);
        return $$"""
            <!doctype html>
            <html lang="fr">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{WebUtility.HtmlEncode(title)}}</title>
            <style>
              body { font-family: system-ui, -apple-system, "Segoe UI", sans-serif; line-height: 1.6; max-width: 46rem; margin: 2.5rem auto; padding: 0 1rem; color: #1f2328; }
              pre, code { font-family: ui-monospace, Consolas, monospace; background: #f6f8fa; border-radius: 6px; }
              code { padding: .15em .35em; }
              pre { padding: 1rem; overflow-x: auto; }
              pre code { padding: 0; }
              table { border-collapse: collapse; }
              th, td { border: 1px solid #d0d7de; padding: .4rem .7rem; }
              blockquote { margin: 0; padding-left: 1rem; border-left: 4px solid #d0d7de; color: #59636e; }
              img { max-width: 100%; }
            </style>
            </head>
            <body>
            {{body}}
            </body>
            </html>
            """;
    }

    public static string HtmlToMarkdown(string html)
    {
        var bodyMatch = BodyRegex().Match(html);
        var body = bodyMatch.Success ? bodyMatch.Groups[1].Value : html;
        body = ScriptOrStyleRegex().Replace(body, string.Empty);

        var converter = new ReverseMarkdown.Converter(new ReverseMarkdown.Config
        {
            GithubFlavored = true,
            Formatting = { RemoveComments = true },
            Links = { SmartHref = true },
            Tags = { Unknown = ReverseMarkdown.Config.UnknownTagsOption.Bypass },
        });
        return converter.Convert(body).Trim() + Environment.NewLine;
    }

    [GeneratedRegex(@"<body[^>]*>(.*)</body>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BodyRegex();

    [GeneratedRegex(@"<(script|style|noscript)[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptOrStyleRegex();
}

/// <summary>A conversion that reads a text file and writes a transformed text file.</summary>
internal sealed class TextTransformAction(
    string id,
    FileKind kind,
    string extension,
    string? subtitle,
    Func<string, string, string> transform) : PerFileAction
{
    public override string Id => id;

    public override string Title => extension.ToUpperInvariant();

    public override string? Subtitle => subtitle;

    public override ActionGroup Group => ActionGroup.Convert;

    public override string TargetExtension => extension;

    public override bool Accepts(string path) => FileKinds.Detect(path) == kind;

    protected override async Task<string> ProcessAsync(
        string input, ActionContext context, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var source = await TextInput.ReadAsync(input, cancellationToken).ConfigureAwait(false);
        var result = transform(source, input);
        var output = OutputPaths.ForFile(input, extension, context.OutputDirectory);
        await File.WriteAllTextAsync(output, result, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        return output;
    }
}

/// <summary>Markdown to PDF/DOCX: renders HTML first, then lets LibreOffice lay it out.</summary>
internal sealed class MarkdownViaLibreOfficeAction(string extension, string convertTo, string subtitle) : PerFileAction
{
    public override string Id => $"markdown.to.{extension}";

    public override string Title => extension.ToUpperInvariant();

    public override string Subtitle => subtitle;

    public override ActionGroup Group => ActionGroup.Convert;

    public override ExternalTool Requires => ExternalTool.LibreOffice;

    public override string TargetExtension => extension;

    public override bool Accepts(string path) => FileKinds.Detect(path) == FileKind.Markdown;

    protected override async Task<string> ProcessAsync(
        string input, ActionContext context, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var markdown = await TextInput.ReadAsync(input, cancellationToken).ConfigureAwait(false);
        var html = Markup.MarkdownToHtmlDocument(markdown, OutputPaths.BaseNameOf(input));

        // Same base name so LibreOffice names its output like the source; same folder so relative images resolve.
        var temporaryHtml = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(input))!,
            $".{OutputPaths.BaseNameOf(input)}.mandarine-{Guid.NewGuid():N}.html");
        var output = OutputPaths.ForFile(input, extension, context.OutputDirectory);
        try
        {
            await File.WriteAllTextAsync(temporaryHtml, html, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            File.SetAttributes(temporaryHtml, File.GetAttributes(temporaryHtml) | FileAttributes.Hidden);
            await LibreOffice.ConvertAsync(context, temporaryHtml, convertTo, output, cancellationToken, "HTML (StarWriter)")
                .ConfigureAwait(false);
        }
        finally
        {
            OutputPaths.Discard(temporaryHtml);
        }

        return output;
    }
}

internal static class MarkupActions
{
    public static IEnumerable<FileAction> All()
    {
        yield return new TextTransformAction("markdown.to.html", FileKind.Markdown, "html", "Page web",
            (markdown, path) => Markup.MarkdownToHtmlDocument(markdown, OutputPaths.BaseNameOf(path)));
        yield return new MarkdownViaLibreOfficeAction("pdf", "pdf:writer_pdf_Export", "Document");
        yield return new MarkdownViaLibreOfficeAction("docx", "docx:MS Word 2007 XML", "Word");
        yield return new TextTransformAction("html.to.md", FileKind.Html, "md", "Markdown",
            (html, _) => Markup.HtmlToMarkdown(html));
    }
}
