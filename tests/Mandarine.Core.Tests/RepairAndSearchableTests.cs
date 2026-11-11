using ImageMagick;
using Mandarine.Core.Actions;
using Mandarine.Core.Converters;
using UglyToad.PdfPig;

namespace Mandarine.Core.Tests;

public class RepairAndSearchableTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task Repair_rewrites_a_pdf_whose_xref_offset_is_wrong()
    {
        var pdf = _dir.CreateTextPdf("cassé.pdf", "Bonjour", "Salut");
        var broken = File.ReadAllText(pdf).Replace("startxref\n", "startxref\n9");
        File.WriteAllText(pdf, broken);

        var output = (await _dir.RunAsync("pdf.repair", pdf)).OutputFiles[0];

        Assert.Equal(2, PDFtoImage.Conversion.GetPageCount(File.ReadAllBytes(output)));
        Assert.Contains("réparé", Path.GetFileName(output));
    }

    [Fact]
    public void Searchable_pdf_keeps_the_picture_and_adds_findable_text()
    {
        var png = _dir.CreateImage("page.png", 600, 400);
        var page = new ScannedPage(File.ReadAllBytes(png), 200, [new OcrWord("Bonjour", 40, 40, 200, 50), new OcrWord("monde", 260, 40, 160, 50)]);
        var output = _dir.File("cherchable.pdf");

        SearchablePdf.Write([page], output);

        using var document = PdfDocument.Open(output);
        var text = document.GetPage(1).Text;
        Assert.Contains("Bonjour", text);
        Assert.Contains("monde", text);
    }

    [Fact]
    public async Task Image_conversion_respects_the_maximum_size()
    {
        var image = _dir.CreateImage("big.png", 800, 400);
        var action = _dir.Catalog.Find("image.to.jpg")!;

        var result = await action.RunAsync([image], new ActionContext { MaxDimension = 200 }, null, CancellationToken.None);

        using var output = new MagickImage(result.OutputFiles[0]);
        Assert.Equal(200u, output.Width);
        Assert.Equal(100u, output.Height);
    }

    [Fact]
    public async Task Pdf_to_image_uses_the_requested_resolution()
    {
        var pdf = _dir.CreateTextPdf("a.pdf", "Un");
        var action = _dir.Catalog.Find("pdf.to.png")!;

        var low = await action.RunAsync([pdf], new ActionContext { Dpi = 72 }, null, CancellationToken.None);
        var high = await action.RunAsync([pdf], new ActionContext { Dpi = 144 }, null, CancellationToken.None);

        using var small = new MagickImage(low.OutputFiles[0]);
        using var big = new MagickImage(high.OutputFiles[0]);
        Assert.Equal(small.Width * 2, big.Width);
    }
}
