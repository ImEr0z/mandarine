using System.Globalization;
using System.Text;
using Mandarine.Core.Actions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Mandarine.Core.Converters;

internal static class Pdf
{
    public static bool IsPdf(string path) => FileKinds.Detect(path) == FileKind.Pdf;
}

/// <summary>Renders each page to an image. Multi-page documents go into a dedicated folder.</summary>
internal sealed class PdfToImagesAction(string extension) : PerFileAction
{
    private const int Dpi = 150;

    public override string Id => $"pdf.to.{extension}";

    public override string Title => extension.ToUpperInvariant();

    public override string Subtitle => "Pages";

    public override ActionGroup Group => ActionGroup.Convert;

    public override bool Accepts(string path) => Pdf.IsPdf(path);

    protected override Task<string> ProcessAsync(
        string input, ActionContext context, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var bytes = File.ReadAllBytes(input);
            var pageCount = PDFtoImage.Conversion.GetPageCount(bytes);
            var options = new PDFtoImage.RenderOptions(Dpi: Dpi, WithAnnotations: true, WithFormFill: true,
                BackgroundColor: SkiaSharp.SKColors.White);

            if (pageCount == 1)
            {
                var single = OutputPaths.ForFile(input, extension, context.OutputDirectory);
                Save(single, bytes, 0, options);
                return single;
            }

            var folder = OutputPaths.ForFolder(input, context.OutputDirectory, "pages");
            try
            {
                Directory.CreateDirectory(folder);
                var digits = pageCount.ToString().Length;
                for (var page = 0; page < pageCount; page++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var name = $"{OutputPaths.BaseNameOf(input)} - page {(page + 1).ToString().PadLeft(digits, '0')}.{extension}";
                    Save(Path.Combine(folder, name), bytes, page, options);
                    progress?.Report((page + 1d) / pageCount);
                }
            }
            catch
            {
                OutputPaths.Discard(folder);
                throw;
            }

            return folder;
        }, cancellationToken);

    private void Save(string path, byte[] pdf, int page, PDFtoImage.RenderOptions options)
    {
        if (extension == "jpg")
        {
            PDFtoImage.Conversion.SaveJpeg(path, pdf, page, options: options);
        }
        else
        {
            PDFtoImage.Conversion.SavePng(path, pdf, page, options: options);
        }
    }
}

internal sealed class PdfToTextAction : PerFileAction
{
    public override string Id => "pdf.to.txt";

    public override string Title => "TXT";

    public override string Subtitle => "Texte";

    public override ActionGroup Group => ActionGroup.Convert;

    public override bool Accepts(string path) => Pdf.IsPdf(path);

    protected override Task<string> ProcessAsync(
        string input, ActionContext context, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Run(async () =>
        {
            var text = new StringBuilder();
            using (var document = UglyToad.PdfPig.PdfDocument.Open(input))
            {
                foreach (var page in document.GetPages())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    text.AppendLine(ContentOrderTextExtractor.GetText(page).TrimEnd());
                    text.AppendLine();
                    progress?.Report((double)page.Number / document.NumberOfPages);
                }
            }

            if (string.IsNullOrWhiteSpace(text.ToString()))
            {
                throw new InvalidOperationException("Ce PDF ne contient pas de texte (document scanné ?).");
            }

            var output = OutputPaths.ForFile(input, "txt", context.OutputDirectory);
            await File.WriteAllTextAsync(output, text.ToString().TrimEnd() + Environment.NewLine, Encoding.UTF8,
                cancellationToken).ConfigureAwait(false);
            return output;
        }, cancellationToken);
}

/// <summary>
/// Merges two or more images and/or PDFs into one PDF: each image becomes a page, PDF pages are kept as is.
/// Files are taken in natural name order ("page 2" before "page 10"), whatever order Explorer passed them in.
/// </summary>
internal sealed class PdfMergeAction : FileAction
{
    public override string Id => "pdf.merge";

    public override string Title => "Fusionner";

    public override string Subtitle => "PDF";

    public override string Glyph => "";

    // A conversion, so that it is the first slice of the wheel when several files are selected.
    public override ActionGroup Group => ActionGroup.Convert;

    public override int MinInputs => 2;

    public override bool Accepts(string path) => Pdf.IsPdf(path) || ImageFormats.IsImage(path);

    public override Task<ActionResult> RunAsync(
        IReadOnlyList<string> inputs,
        ActionContext context,
        IProgress<ActionProgress>? progress,
        CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var ordered = inputs
                .Order(Comparer<string>.Create((a, b) => string.Compare(Path.GetFileName(a), Path.GetFileName(b),
                    CultureInfo.CurrentCulture, CompareOptions.NumericOrdering | CompareOptions.IgnoreCase)))
                .ToList();
            var output = OutputPaths.ForFile(ordered[0], "pdf", context.OutputDirectory, "fusionné");
            var scratch = Directory.CreateTempSubdirectory("mandarine-merge-").FullName;
            try
            {
                using var merged = new PdfDocument();
                for (var i = 0; i < ordered.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report(new ActionProgress(i, ordered.Count, ordered[i], null));
                    var source = ordered[i];
                    if (!Pdf.IsPdf(source))
                    {
                        // One A4-sized page per image, through the same path as "Convertir en PDF".
                        var page = Path.Combine(scratch, $"{i}.pdf");
                        using (var image = ImageFormats.ReadFirstFrame(source))
                        {
                            image.Quality = (uint)Math.Clamp(context.Quality, 1, 100);
                            ImageFormats.FlattenIfOpaqueTarget(image, "pdf");
                            ImageFormats.Write(image, page, "pdf");
                        }

                        source = page;
                    }

                    if (!TryImportPages(merged, source))
                    {
                        AddRenderedPages(merged, source, cancellationToken);
                    }
                }

                merged.Save(output);
            }
            catch
            {
                OutputPaths.Discard(output);
                throw;
            }
            finally
            {
                try
                {
                    Directory.Delete(scratch, recursive: true);
                }
                catch (IOException)
                {
                }
            }

            progress?.Report(new ActionProgress(ordered.Count, ordered.Count, null, null));
            return new ActionResult([output], []);
        }, cancellationToken);

    /// <summary>Copies the pages as they are (text stays selectable). False if this PDF cannot be imported.</summary>
    private static bool TryImportPages(PdfDocument merged, string path)
    {
        PdfDocument document;
        try
        {
            document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Modern encryption, unusual producers, slightly broken files: PDFsharp refuses, rendering still works.
            return false;
        }

        using (document)
        {
            foreach (var page in document.Pages)
            {
                merged.AddPage(page);
            }
        }

        return true;
    }

    /// <summary>Fallback: each page rendered at 200 dpi (the engine that displays PDFs) and placed at its real size.</summary>
    private static void AddRenderedPages(PdfDocument merged, string path, CancellationToken cancellationToken)
    {
        const int Dpi = 200;
        var bytes = File.ReadAllBytes(path);
        int count;
        try
        {
            count = PDFtoImage.Conversion.GetPageCount(bytes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"« {Path.GetFileName(path)} » ne peut pas être ouvert (protégé par un mot de passe ou endommagé).", ex);
        }

        var options = new PDFtoImage.RenderOptions(Dpi: Dpi, WithAnnotations: true, WithFormFill: true,
            BackgroundColor: SkiaSharp.SKColors.White);
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var jpeg = new MemoryStream();
            PDFtoImage.Conversion.SaveJpeg(jpeg, bytes, index, options: options);
            jpeg.Position = 0;
            using var image = XImage.FromStream(jpeg);
            var page = merged.AddPage();
            page.Width = XUnit.FromPoint(image.PixelWidth * 72.0 / Dpi);
            page.Height = XUnit.FromPoint(image.PixelHeight * 72.0 / Dpi);
            using var graphics = XGraphics.FromPdfPage(page);
            graphics.DrawImage(image, 0, 0, page.Width.Point, page.Height.Point);
        }
    }
}

internal sealed class PdfSplitAction : PerFileAction
{
    public override string Id => "pdf.split";

    public override string Title => "Une page par fichier";

    public override string Glyph => "\uE8C6";

    public override ActionGroup Group => ActionGroup.Tools;

    public override bool Accepts(string path) => Pdf.IsPdf(path);

    protected override Task<string> ProcessAsync(
        string input, ActionContext context, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            using var source = PdfReader.Open(input, PdfDocumentOpenMode.Import);
            if (source.PageCount < 2)
            {
                throw new InvalidOperationException("Ce PDF ne contient qu'une page.");
            }

            var folder = OutputPaths.ForFolder(input, context.OutputDirectory, "pages");
            try
            {
                Directory.CreateDirectory(folder);
                var digits = source.PageCount.ToString().Length;
                for (var i = 0; i < source.PageCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var single = new PdfDocument();
                    single.AddPage(source.Pages[i]);
                    var name = $"{OutputPaths.BaseNameOf(input)} - page {(i + 1).ToString().PadLeft(digits, '0')}.pdf";
                    single.Save(Path.Combine(folder, name));
                    progress?.Report((i + 1d) / source.PageCount);
                }
            }
            catch
            {
                OutputPaths.Discard(folder);
                throw;
            }

            return folder;
        }, cancellationToken);
}

internal sealed class PdfRotateAction : PerFileAction
{
    public override string Id => "pdf.rotate";

    public override string Title => "Pivoter les pages";

    public override string Glyph => "\uE7A6";

    public override ActionGroup Group => ActionGroup.Tools;

    public override bool Accepts(string path) => Pdf.IsPdf(path);

    protected override Task<string> ProcessAsync(
        string input, ActionContext context, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var output = OutputPaths.ForFile(input, "pdf", context.OutputDirectory, "pivoté");
            try
            {
                using var document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
                foreach (var page in document.Pages)
                {
                    page.Rotate = (page.Rotate + 90) % 360;
                }

                document.Save(output);
            }
            catch
            {
                OutputPaths.Discard(output);
                throw;
            }

            return output;
        }, cancellationToken);
}
