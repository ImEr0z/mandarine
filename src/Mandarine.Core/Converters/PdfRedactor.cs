using ImageMagick;
using ImageMagick.Drawing;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Mandarine.Core;

/// <summary>A black box on a page. X, Y, Width and Height are fractions (0 to 1) of the page, measured from its top-left corner.</summary>
public sealed record PdfRedaction(int Page, double X, double Y, double Width, double Height);

/// <summary>
/// Redaction that really removes what is under the boxes. A page with boxes is drawn to an image, the boxes are painted
/// on the image, and the page is rebuilt from that image: no text, no hidden layer, nothing left to copy or recover
/// from the pixels that were covered. Pages without a box are copied untouched (their text stays selectable).
/// </summary>
public static class PdfRedactor
{
    public const int Dpi = 200;

    /// <summary>Writes the redacted copy to <paramref name="output"/> and returns how many pages were turned into images.</summary>
    public static int Save(string input, string output, IReadOnlyList<PdfRedaction> marks)
    {
        var byPage = marks.GroupBy(m => m.Page).ToDictionary(g => g.Key, g => g.ToList());
        using var source = PdfReader.Open(input, PdfDocumentOpenMode.Import);
        using var result = new PdfDocument();
        for (var i = 0; i < source.PageCount; i++)
        {
            if (!byPage.TryGetValue(i, out var boxes))
            {
                result.AddPage(source.Pages[i]);
                continue;
            }

            using var image = new MagickImage(Raster.PdfPageToPng(input, i, Dpi));
            image.ColorAlpha(MagickColors.White);
            foreach (var box in boxes)
            {
                var left = Math.Clamp(box.X, 0, 1) * image.Width;
                var top = Math.Clamp(box.Y, 0, 1) * image.Height;
                var right = Math.Clamp(box.X + box.Width, 0, 1) * image.Width;
                var bottom = Math.Clamp(box.Y + box.Height, 0, 1) * image.Height;
                image.Draw(new DrawableFillColor(MagickColors.Black), new DrawableRectangle(left, top, right, bottom));
            }

            // JPEG: the format PDFsharp embeds reliably (the merge fallback does the same).
            image.Quality = 92;
            using var stream = new MemoryStream(image.ToByteArray(MagickFormat.Jpeg));
            var page = result.AddPage();
            page.Width = XUnit.FromPoint(image.Width * 72.0 / Dpi);
            page.Height = XUnit.FromPoint(image.Height * 72.0 / Dpi);
            using var graphics = XGraphics.FromPdfPage(page);
            using var xImage = XImage.FromStream(stream);
            graphics.DrawImage(xImage, 0, 0, page.Width.Point, page.Height.Point);
        }

        result.Save(output);
        return byPage.Count;
    }
}
