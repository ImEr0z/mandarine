using ImageMagick;
using Mandarine.Core.Converters;

namespace Mandarine.Core;

/// <summary>
/// Images and PDF pages as PNG bytes, for Windows features that take a bitmap (text recognition,
/// clipboard, wallpaper). Images go through the hardened ImageMagick setup like every conversion.
/// </summary>
public static class Raster
{
    public static byte[] ImageToPng(string path, uint maxSide)
    {
        using var image = ImageFormats.ReadFirstFrame(path);
        if (image.Width > maxSide || image.Height > maxSide)
        {
            image.Resize(new MagickGeometry(maxSide, maxSide));
        }

        return image.ToByteArray(MagickFormat.Png);
    }

    public static IEnumerable<byte[]> PdfPagesToPng(string path, int dpi)
    {
        var bytes = File.ReadAllBytes(path);
        var options = new PDFtoImage.RenderOptions(Dpi: dpi, BackgroundColor: SkiaSharp.SKColors.White);
        var pages = PDFtoImage.Conversion.GetPageCount(bytes);
        for (var page = 0; page < pages; page++)
        {
            using var stream = new MemoryStream();
            PDFtoImage.Conversion.SavePng(stream, bytes, page, options: options);
            yield return stream.ToArray();
        }
    }

    public static bool IsImage(string path) => FileKinds.Detect(path) == FileKind.Image;

    public static bool IsPdf(string path) => FileKinds.Detect(path) == FileKind.Pdf;
}
