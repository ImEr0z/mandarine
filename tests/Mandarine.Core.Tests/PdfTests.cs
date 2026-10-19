using ImageMagick;
using PdfSharp.Pdf.IO;

namespace Mandarine.Core.Tests;

public class PdfTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    private static int PageCount(string pdf) => PDFtoImage.Conversion.GetPageCount(File.ReadAllBytes(pdf));

    [Fact]
    public async Task Merge_keeps_every_page_in_order()
    {
        var a = _dir.CreateTextPdf("a.pdf", "Alpha 1", "Alpha 2");
        var b = _dir.CreateTextPdf("b.pdf", "Bravo 1");

        var merged = (await _dir.RunAsync("pdf.merge", a, b)).OutputFiles[0];

        Assert.Equal(_dir.File("a - fusionné.pdf"), merged);
        Assert.Equal(3, PageCount(merged));
        var text = await File.ReadAllTextAsync((await _dir.RunAsync("pdf.to.txt", merged)).OutputFiles[0]);
        Assert.True(text.IndexOf("Alpha 2") < text.IndexOf("Bravo 1"));
    }

    [Fact]
    public async Task Split_creates_one_file_per_page_in_a_folder()
    {
        var folder = (await _dir.RunAsync("pdf.split", _dir.CreateTextPdf("doc.pdf", "Un", "Deux", "Trois"))).OutputFiles[0];

        Assert.Equal(_dir.File("doc (pages)"), folder);
        var pages = Directory.GetFiles(folder).Order().ToList();
        Assert.Equal(["doc - page 1.pdf", "doc - page 2.pdf", "doc - page 3.pdf"], pages.Select(Path.GetFileName));
        Assert.All(pages, p => Assert.Equal(1, PageCount(p)));
    }

    [Fact]
    public async Task Rotate_turns_every_page_clockwise()
    {
        var rotated = (await _dir.RunAsync("pdf.rotate", _dir.CreateTextPdf("doc.pdf", "Un", "Deux"))).OutputFiles[0];

        using var document = PdfReader.Open(rotated, PdfDocumentOpenMode.Import);
        Assert.All(document.Pages.Cast<PdfSharp.Pdf.PdfPage>(), page => Assert.Equal(90, page.Rotate));
    }

    [Fact]
    public async Task Single_page_renders_to_one_png()
    {
        var png = (await _dir.RunAsync("pdf.to.png", _dir.CreateTextPdf("page.pdf", "Bonjour"))).OutputFiles[0];

        Assert.Equal(_dir.File("page.png"), png);
        var info = new MagickImageInfo(png);
        Assert.Equal(625u, info.Width); // 300 pt at 150 dpi
    }

    [Fact]
    public async Task Multi_page_renders_into_a_folder_of_jpgs()
    {
        var folder = (await _dir.RunAsync("pdf.to.jpg", _dir.CreateTextPdf("doc.pdf", "Un", "Deux"))).OutputFiles[0];

        Assert.Equal(2, Directory.GetFiles(folder, "*.jpg").Length);
    }

    [Fact]
    public async Task Text_extraction()
    {
        var txt = (await _dir.RunAsync("pdf.to.txt", _dir.CreateTextPdf("doc.pdf", "Bonjour Mandarine"))).OutputFiles[0];

        Assert.Contains("Bonjour Mandarine", await File.ReadAllTextAsync(txt));
    }

    public void Dispose() => _dir.Dispose();
}
