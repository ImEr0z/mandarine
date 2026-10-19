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
    public async Task Images_and_pdfs_merge_together_in_natural_name_order()
    {
        var page10 = _dir.CreateTextPdf("page 10.pdf", "Dix");
        var page2 = _dir.CreateImage("page 2.png");
        var page1 = _dir.CreateTextPdf("page 1.pdf", "Un");

        // Explorer may pass the selection in any order.
        var merged = (await _dir.RunAsync("pdf.merge", page10, page2, page1)).OutputFiles[0];

        Assert.Equal(_dir.File("page 1 - fusionné.pdf"), merged);
        Assert.Equal(3, PageCount(merged));
        var text = await File.ReadAllTextAsync((await _dir.RunAsync("pdf.to.txt", merged)).OutputFiles[0]);
        Assert.True(text.IndexOf("Un") < text.IndexOf("Dix"));
    }

    [Fact]
    public async Task Pdfs_protected_against_editing_still_merge()
    {
        // Like most invoices and statements: opens without a password, but "owner"-protected against changes.
        var a = _dir.CreateTextPdf("facture.pdf", "Facture 1", "Facture 2");
        var protectedPdf = _dir.File("releve.pdf");
        using (var document = PdfReader.Open(a, PdfDocumentOpenMode.Modify))
        {
            document.SecuritySettings.OwnerPassword = "secret";
            document.SecuritySettings.PermitModifyDocument = false;
            document.SecuritySettings.PermitAssembleDocument = false;
            document.Save(protectedPdf);
        }

        var b = _dir.CreateTextPdf("autre.pdf", "Autre");
        var result = await _dir.Catalog.Find("pdf.merge")!.RunAsync([protectedPdf, b], _dir.Context, null, CancellationToken.None);

        Assert.Empty(result.Errors);
        Assert.Equal(3, PageCount(result.OutputFiles[0]));
    }

    [Fact]
    public async Task Pdf_that_cannot_be_imported_is_merged_as_rendered_pages()
    {
        // Broken cross-reference offset: PDF viewers repair it on the fly, PDFsharp refuses the file.
        var damaged = _dir.CreateTextPdf("abime.pdf", "Page A", "Page B");
        var bytes = File.ReadAllBytes(damaged);
        var marker = System.Text.Encoding.ASCII.GetBytes("startxref");
        var at = bytes.AsSpan().LastIndexOf(marker) + marker.Length;
        while (bytes[at] is (byte)'\r' or (byte)'\n')
        {
            at++;
        }

        while (bytes[at] is >= (byte)'0' and <= (byte)'9')
        {
            bytes[at++] = (byte)'9';
        }

        File.WriteAllBytes(damaged, bytes);
        var other = _dir.CreateTextPdf("bon.pdf", "Page C");

        var result = await _dir.Catalog.Find("pdf.merge")!.RunAsync([damaged, other], _dir.Context, null, CancellationToken.None);

        Assert.Empty(result.Errors);
        Assert.Equal(3, PageCount(result.OutputFiles[0]));
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
