using System.Globalization;
using Mandarine.Core.Actions;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Mandarine.Core.Converters;

internal static class PdfTools
{
    public static string Required(ActionContext context) =>
        context.Input?.Trim() is { Length: > 0 } text ? text : throw new InvalidOperationException("Aucune valeur saisie.");

    /// <summary>"1-3, 5, 8-" gives zero-based indexes 0,1,2,4,7...; an open end means "to the last page".</summary>
    public static SortedSet<int> ParsePages(string text, int pageCount)
    {
        var pages = new SortedSet<int>();
        foreach (var part in text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            var bounds = part.Split('-', 2);
            if (!int.TryParse(bounds[0], out var first) || (bounds.Length == 2 && bounds[1].Length > 0 && !int.TryParse(bounds[1], out _)))
            {
                throw new InvalidOperationException($"Pages non comprises : « {part} ». Exemple : 2, 4-6, 9-");
            }

            var last = bounds.Length == 1 ? first : bounds[1].Length == 0 ? pageCount : int.Parse(bounds[1], CultureInfo.InvariantCulture);
            if (first < 1 || last < first || last > pageCount)
            {
                throw new InvalidOperationException($"« {part} » est hors du document ({pageCount} page{(pageCount > 1 ? "s" : string.Empty)}).");
            }

            for (var page = first; page <= last; page++)
            {
                pages.Add(page - 1);
            }
        }

        return pages;
    }

    public static void InitFonts()
    {
        try
        {
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        }
        catch (InvalidOperationException)
        {
            // Already set earlier in this process.
        }
    }

    /// <summary>A trailer holds /Encrypt: cheap check on the end of the file, without parsing the document.</summary>
    public static bool IsEncrypted(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var length = (int)Math.Min(stream.Length, 4096);
            stream.Seek(-length, SeekOrigin.End);
            var buffer = new byte[length];
            stream.ReadExactly(buffer);
            return System.Text.Encoding.Latin1.GetString(buffer).Contains("/Encrypt", StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
    }
}

/// <summary>Opens the PDF, changes it, and saves the result next to the original ("name - suffix.pdf").</summary>
internal abstract class PdfModifyAction : PerFileAction
{
    public override ActionGroup Group => ActionGroup.Tools;

    protected abstract string Suffix { get; }

    public override bool Accepts(string path) => Pdf.IsPdf(path);

    protected virtual PdfDocument Open(string input, ActionContext context) => PdfReader.Open(input, PdfDocumentOpenMode.Modify);

    protected abstract void Apply(PdfDocument document, ActionContext context);

    public override Task<string?> BuildPreviewAsync(string input, ActionContext context, CancellationToken cancellationToken) =>
        Task.Run<string?>(() =>
        {
            var firstPage = Path.Combine(Path.GetTempPath(), $"mandarine-preview-{Guid.NewGuid():N}.pdf");
            var result = Path.Combine(Path.GetTempPath(), $"mandarine-preview-{Guid.NewGuid():N}.pdf");
            try
            {
                using (var source = PdfReader.Open(input, PdfDocumentOpenMode.Import))
                using (var one = new PdfDocument())
                {
                    one.AddPage(source.Pages[0]);
                    one.Save(firstPage);
                }

                using (var document = PdfReader.Open(firstPage, PdfDocumentOpenMode.Modify))
                {
                    Apply(document, context);
                    document.Save(result);
                }

                return result;
            }
            catch
            {
                OutputPaths.Discard(result);
                throw;
            }
            finally
            {
                OutputPaths.Discard(firstPage);
            }
        }, cancellationToken);

    protected override Task<string> ProcessAsync(
        string input, ActionContext context, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var output = OutputPaths.ForFile(input, "pdf", context.OutputDirectory, Suffix);
            try
            {
                using var document = Open(input, context);
                Apply(document, context);
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

internal sealed class PdfDeletePagesAction : PdfModifyAction
{
    public override string Id => "pdf.delete-pages";

    public override string Title => "Supprimer des pages";

    public override string Glyph => "";

    public override ActionPrompt Prompt { get; } = new("Pages à supprimer", "Exemple : 2, 4-6, 9-");

    protected override string Suffix => "sans pages";

    protected override void Apply(PdfDocument document, ActionContext context)
    {
        var pages = PdfTools.ParsePages(PdfTools.Required(context), document.PageCount);
        if (pages.Count >= document.PageCount)
        {
            throw new InvalidOperationException("Impossible de supprimer toutes les pages.");
        }

        foreach (var index in pages.Reverse())
        {
            document.Pages.RemoveAt(index);
        }
    }
}

internal sealed class PdfExtractPagesAction : PerFileAction
{
    public override string Id => "pdf.extract-pages";

    public override string Title => "Extraire des pages";

    public override string Glyph => "";

    public override ActionGroup Group => ActionGroup.Tools;

    public override ActionPrompt Prompt { get; } = new("Pages à garder", "Exemple : 2, 4-6, 9-");

    public override bool Accepts(string path) => Pdf.IsPdf(path);

    protected override Task<string> ProcessAsync(
        string input, ActionContext context, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            using var source = PdfReader.Open(input, PdfDocumentOpenMode.Import);
            var pages = PdfTools.ParsePages(PdfTools.Required(context), source.PageCount);
            var output = OutputPaths.ForFile(input, "pdf", context.OutputDirectory, "extrait");
            try
            {
                using var result = new PdfDocument();
                foreach (var index in pages)
                {
                    result.AddPage(source.Pages[index]);
                }

                result.Save(output);
            }
            catch
            {
                OutputPaths.Discard(output);
                throw;
            }

            return output;
        }, cancellationToken);
}

internal sealed class PdfPageNumbersAction : PdfModifyAction
{
    public override string Id => "pdf.page-numbers";

    public override string Title => "Numéroter les pages";

    public override string Glyph => "";

    protected override string Suffix => "numéroté";

    protected override void Apply(PdfDocument document, ActionContext context)
    {
        PdfTools.InitFonts();
        var font = new XFont("Arial", 10);
        for (var i = 0; i < document.PageCount; i++)
        {
            var page = document.Pages[i];
            using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            graphics.DrawString($"{i + 1} / {document.PageCount}", font, XBrushes.Black,
                new XRect(0, page.Height.Point - 30, page.Width.Point, 20), XStringFormats.Center);
        }
    }
}

/// <summary>How a watermark input says "use this logo" instead of a text.</summary>
public static class WatermarkInput
{
    public const string ImagePrefix = "image:";
}

internal sealed class PdfWatermarkAction : PdfModifyAction
{
    /// <summary>The input is "image:" followed by the path of a logo instead of a text.</summary>
    public const string ImagePrefix = WatermarkInput.ImagePrefix;

    public override string Id => "pdf.watermark";

    public override string Title => "Filigrane";

    public override string Glyph => "";

    public override ActionPrompt Prompt { get; } = new("Texte du filigrane", "Répété en diagonale sur toute la page. Exemple : Copie pour la banque, ne pas réutiliser");

    protected override string Suffix => "filigrané";

    protected override void Apply(PdfDocument document, ActionContext context)
    {
        PdfTools.InitFonts();
        var value = PdfTools.Required(context);
        if (value.StartsWith(ImagePrefix, StringComparison.Ordinal))
        {
            ApplyImage(document, value[ImagePrefix.Length..]);
            return;
        }

        var brush = new XSolidBrush(XColor.FromArgb(95, 90, 90, 90));
        foreach (var page in document.Pages)
        {
            // Tiled over the whole page like an official copy: one stamp in the middle is easy to crop out.
            var size = Math.Clamp(page.Width.Point / 32, 9, 22);
            var font = new XFont("Arial", size, XFontStyleEx.Regular);
            using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            var column = graphics.MeasureString(value, font).Width + size * 3;
            var row = size * 4.2;
            Tile(graphics, page, column, row, (x, y) => graphics.DrawString(value, font, brush, x, y, XStringFormats.TopLeft));
        }
    }

    /// <summary>A logo tiled the same way as the text, semi-transparent.</summary>
    private static void ApplyImage(PdfDocument document, string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("L'image du filigrane est introuvable.");
        }

        byte[] bytes;
        int pixelWidth, pixelHeight;
        using (var logo = new ImageMagick.MagickImage(path))
        {
            logo.Evaluate(ImageMagick.Channels.Alpha, ImageMagick.EvaluateOperator.Multiply, 0.3);
            // Png32: plain 8-bit RGBA. Magick would otherwise pick a palette for a logo with few colours, which PDFsharp refuses.
            bytes = logo.ToByteArray(ImageMagick.MagickFormat.Png32);
            (pixelWidth, pixelHeight) = ((int)logo.Width, (int)logo.Height);
        }

        foreach (var page in document.Pages)
        {
            using var stream = new MemoryStream(bytes);
            using var image = XImage.FromStream(stream);
            var width = Math.Clamp(page.Width.Point / 7, 40, 160);
            var height = width * pixelHeight / pixelWidth;
            using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            Tile(graphics, page, width * 1.9, height * 2.1, (x, y) => graphics.DrawImage(image, x, y, width, height));
        }
    }

    /// <summary>Calls <paramref name="draw"/> on a grid turned by 35 degrees, rows shifted by half a column, covering the whole page.</summary>
    private static void Tile(XGraphics graphics, PdfPage page, double column, double row, Action<double, double> draw)
    {
        var reach = Math.Sqrt((page.Width.Point * page.Width.Point) + (page.Height.Point * page.Height.Point)) / 2;
        graphics.TranslateTransform(page.Width.Point / 2, page.Height.Point / 2);
        graphics.RotateTransform(-35);
        var line = 0;
        for (var y = -reach; y <= reach; y += row, line++)
        {
            for (var x = -reach - (line % 2 * column / 2); x <= reach; x += column)
            {
                draw(x, y);
            }
        }
    }
}

internal sealed class PdfCropAction : PdfModifyAction
{
    public override string Id => "pdf.crop";

    public override string Title => "Recadrer";

    public override string Glyph => "";

    public override ActionPrompt Prompt { get; } = new("Marge à retirer sur chaque bord (mm)", "Exemple : 10");

    protected override string Suffix => "recadré";

    protected override void Apply(PdfDocument document, ActionContext context)
    {
        if (!double.TryParse(PdfTools.Required(context).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var mm) || mm <= 0)
        {
            throw new InvalidOperationException("Indiquez un nombre de millimètres supérieur à 0.");
        }

        var margin = mm * 72 / 25.4;
        foreach (var page in document.Pages)
        {
            var box = page.MediaBox;
            if (box.Width <= margin * 2 || box.Height <= margin * 2)
            {
                throw new InvalidOperationException("La marge est plus grande que la page.");
            }

            page.CropBox = new PdfRectangle(new XPoint(box.X1 + margin, box.Y1 + margin), new XPoint(box.X2 - margin, box.Y2 - margin));
        }
    }
}

internal sealed class PdfProtectAction : PdfModifyAction
{
    public override string Id => "pdf.protect";

    public override string Title => "Protéger par mot de passe";

    public override string Glyph => "";

    public override ActionPrompt Prompt { get; } = new("Mot de passe à définir", "Il sera demandé à l'ouverture du PDF", Secret: true);

    protected override string Suffix => "protégé";

    public override bool IsAvailableFor(IReadOnlyList<string> inputs) => base.IsAvailableFor(inputs) && !inputs.Any(PdfTools.IsEncrypted);

    protected override void Apply(PdfDocument document, ActionContext context)
    {
        var password = PdfTools.Required(context);
        document.SecuritySettings.UserPassword = password;
        document.SecuritySettings.OwnerPassword = password;
    }
}

internal sealed class PdfUnlockAction : PdfModifyAction
{
    public override string Id => "pdf.unlock";

    public override string Title => "Retirer le mot de passe";

    public override string Glyph => "";

    public override ActionPrompt Prompt { get; } = new("Mot de passe du PDF", "Celui demandé à l'ouverture", Secret: true);

    protected override string Suffix => "déverrouillé";

    public override bool IsAvailableFor(IReadOnlyList<string> inputs) => base.IsAvailableFor(inputs) && inputs.All(PdfTools.IsEncrypted);

    protected override PdfDocument Open(string input, ActionContext context)
    {
        try
        {
            // Pages copied into a fresh document: the copy is saved without encryption.
            using var source = PdfReader.Open(input, PdfTools.Required(context), PdfDocumentOpenMode.Import);
            var copy = new PdfDocument();
            foreach (var page in source.Pages)
            {
                copy.AddPage(page);
            }

            return copy;
        }
        catch (PdfReaderException)
        {
            throw new InvalidOperationException("Mot de passe incorrect.");
        }
    }

    protected override void Apply(PdfDocument document, ActionContext context)
    {
    }
}

internal sealed class PdfExtractImagesAction : PerFileAction
{
    public override string Id => "pdf.extract-images";

    public override string Title => "Extraire les images";

    public override string Glyph => "";

    public override ActionGroup Group => ActionGroup.Tools;

    public override bool Accepts(string path) => Pdf.IsPdf(path);

    protected override Task<string> ProcessAsync(
        string input, ActionContext context, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var folder = OutputPaths.ForFolder(input, context.OutputDirectory, "images");
            try
            {
                Directory.CreateDirectory(folder);
                var count = 0;
                using var document = UglyToad.PdfPig.PdfDocument.Open(input);
                foreach (var page in document.GetPages())
                {
                    foreach (var image in page.GetImages())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var raw = image.RawMemory.Span;
                        var isJpeg = raw.Length > 2 && raw[0] == 0xFF && raw[1] == 0xD8;
                        byte[] bytes;
                        if (image.TryGetPng(out var png))
                        {
                            bytes = png;
                        }
                        else if (isJpeg)
                        {
                            bytes = raw.ToArray();
                        }
                        else
                        {
                            continue;
                        }

                        count++;
                        File.WriteAllBytes(Path.Combine(folder, $"{OutputPaths.BaseNameOf(input)} - image {count}.{(image.TryGetPng(out _) ? "png" : "jpg")}"), bytes);
                    }

                    progress?.Report((double)page.Number / document.NumberOfPages);
                }

                if (count == 0)
                {
                    throw new InvalidOperationException("Ce PDF ne contient aucune image extractible.");
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
