using Mandarine.Core.Actions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Mandarine.Core.Converters;

/// <summary>
/// Rewrites a damaged PDF into a clean copy: the pages are copied as they are when the file can still be read,
/// else rendered by the engine that displays PDFs (the text is then a picture; "PDF cherchable" brings it back).
/// </summary>
internal sealed class PdfRepairAction : PerFileAction
{
    public override string Id => "pdf.repair";

    public override string Title => "Réparer";

    public override string Glyph => "";

    public override ActionGroup Group => ActionGroup.Tools;

    public override bool Accepts(string path) => Pdf.IsPdf(path);

    protected override Task<string> ProcessAsync(
        string input, ActionContext context, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var output = OutputPaths.ForFile(input, "pdf", context.OutputDirectory, "réparé");
            try
            {
                using var repaired = new PdfDocument();
                if (!PdfMergeAction.TryImportPages(repaired, input) || repaired.PageCount == 0)
                {
                    PdfMergeAction.AddRenderedPages(repaired, input, cancellationToken);
                }

                if (repaired.PageCount == 0)
                {
                    throw new InvalidOperationException("Aucune page récupérable dans ce PDF.");
                }

                repaired.Save(output);
            }
            catch
            {
                OutputPaths.Discard(output);
                throw;
            }

            return output;
        }, cancellationToken);
}

/// <summary>One recognized word, in pixels of the page picture.</summary>
public sealed record OcrWord(string Text, double X, double Y, double Width, double Height);

/// <summary>A page picture (PNG) with its recognized words; <paramref name="Dpi"/> maps pixels to the page size.</summary>
public sealed record ScannedPage(byte[] Png, double Dpi, IReadOnlyList<OcrWord> Words);

/// <summary>
/// A PDF made of the page pictures with an invisible text layer on top: it looks the same,
/// but the text can be searched, selected and copied.
/// </summary>
public static class SearchablePdf
{
    public static void Write(IReadOnlyList<ScannedPage> pages, string output)
    {
        PdfTools.InitFonts();
        using var document = new PdfDocument();
        var font = new XFont("Arial", 10, XFontStyleEx.Regular, new XPdfFontOptions(PdfFontEncoding.Unicode));
        var invisible = new XSolidBrush(XColor.FromArgb(0, 0, 0, 0));

        foreach (var scan in pages)
        {
            using var image = XImage.FromStream(new MemoryStream(scan.Png));
            var factor = 72.0 / scan.Dpi;
            var page = document.AddPage();
            page.Width = XUnit.FromPoint(image.PixelWidth * factor);
            page.Height = XUnit.FromPoint(image.PixelHeight * factor);
            using var graphics = XGraphics.FromPdfPage(page);
            graphics.DrawImage(image, 0, 0, page.Width.Point, page.Height.Point);

            foreach (var word in scan.Words)
            {
                if (string.IsNullOrWhiteSpace(word.Text) || word.Width <= 0 || word.Height <= 0)
                {
                    continue;
                }

                // The text is drawn at 10 pt, then stretched to the box of the word on the picture.
                var measured = graphics.MeasureString(word.Text, font).Width;
                if (measured <= 0)
                {
                    continue;
                }

                graphics.Save();
                graphics.TranslateTransform(word.X * factor, (word.Y + word.Height * 0.8) * factor);
                graphics.ScaleTransform(word.Width * factor / measured, word.Height * factor * 0.9 / 10);
                // The trailing space makes copied text come out with spaces between words.
                graphics.DrawString(word.Text + " ", font, invisible, 0, 0, XStringFormats.BaseLineLeft);
                graphics.Restore();
            }
        }

        document.Save(output);
    }
}
