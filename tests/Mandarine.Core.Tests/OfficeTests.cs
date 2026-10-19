using Mandarine.Core.Actions;

namespace Mandarine.Core.Tests;

/// <summary>Runs only where LibreOffice is installed (skipped otherwise).</summary>
public class OfficeTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    private void RequireLibreOffice() =>
        Skip.If(_dir.Context.Tools.Find(ExternalTool.LibreOffice) is null, "LibreOffice absent");

    private static int PageCount(string pdf) => PDFtoImage.Conversion.GetPageCount(File.ReadAllBytes(pdf));

    [SkippableFact]
    public async Task Text_to_docx_to_pdf_and_odt()
    {
        RequireLibreOffice();
        var txt = _dir.WriteText("note.txt", "Compte rendu de la réunion.\nDeuxième ligne.");

        var docx = (await _dir.RunAsync(Id("docx", txt), txt)).OutputFiles[0];
        Assert.Equal(_dir.File("note.docx"), docx);

        var pdf = (await _dir.RunAsync(Id("pdf", docx), docx)).OutputFiles[0];
        Assert.Equal(1, PageCount(pdf));
        var text = await File.ReadAllTextAsync((await _dir.RunAsync("pdf.to.txt", pdf)).OutputFiles[0]);
        Assert.Contains("réunion", text);

        var odt = (await _dir.RunAsync(Id("odt", docx), docx)).OutputFiles[0];
        Assert.True(new FileInfo(odt).Length > 0);
    }

    [SkippableFact]
    public async Task Csv_to_xlsx_and_back()
    {
        RequireLibreOffice();
        var csv = _dir.WriteText("ventes.csv", "produit,quantité\nmandarine,12\nclémentine,7\n");

        var xlsx = (await _dir.RunAsync(Id("xlsx", csv), csv)).OutputFiles[0];
        var roundTrip = (await _dir.RunAsync(Id("csv", xlsx), xlsx)).OutputFiles[0];

        Assert.Equal(_dir.File("ventes (2).csv"), roundTrip);
        Assert.Contains("clémentine,7", await File.ReadAllTextAsync(roundTrip));
    }

    [SkippableFact]
    public async Task Markdown_to_pdf_and_docx()
    {
        RequireLibreOffice();
        var md = _dir.WriteText("README.md", "# Titre\n\nUn paragraphe avec du **gras**.\n\n- un\n- deux\n");

        var pdf = (await _dir.RunAsync("markdown.to.pdf", md)).OutputFiles[0];
        var text = await File.ReadAllTextAsync((await _dir.RunAsync("pdf.to.txt", pdf)).OutputFiles[0]);
        Assert.Contains("Titre", text);

        var docx = (await _dir.RunAsync("markdown.to.docx", md)).OutputFiles[0];
        Assert.Equal(_dir.File("README.docx"), docx);
        Assert.Empty(Directory.GetFiles(_dir.Path, ".*mandarine-*"));
    }

    [SkippableFact]
    public async Task Html_to_pdf()
    {
        RequireLibreOffice();
        var html = _dir.WriteText("page.html", "<html><body><h1>Bonjour</h1><p>Texte</p></body></html>");

        var pdf = (await _dir.RunAsync(Id("pdf", html), html)).OutputFiles[0];

        Assert.True(PageCount(pdf) >= 1);
    }

    private string Id(string extension, string input) =>
        _dir.Catalog.For([input]).Single(a => a.Id.EndsWith(".to." + extension) && a.Requires == ExternalTool.LibreOffice).Id;

    public void Dispose() => _dir.Dispose();
}
