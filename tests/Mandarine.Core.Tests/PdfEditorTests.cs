namespace Mandarine.Core.Tests;

public class PdfEditorTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    [Fact]
    public void Edited_line_shows_new_text_at_the_same_place()
    {
        var source = _dir.CreateTextPdf("a.pdf", "Bonjour monde");
        var content = PdfEditor.Read(source);
        var line = Assert.Single(content.Lines);
        Assert.Equal("Bonjour monde", line.Text);

        var output = _dir.File("b.pdf");
        PdfEditor.Save(source, output, [new PdfEdit(line, line with { Text = "Salut tout le monde" })]);

        var edited = PdfEditor.Read(output);
        Assert.Contains(edited.Lines, l => l.Text == "Salut tout le monde" && Math.Abs(l.X - line.X) < 1);
        Assert.DoesNotContain(edited.Lines, l => l.Text.Contains("Bonjour"));
    }

    [Theory]
    [InlineData("ABCDEF+Arial-BoldMT", "Arial", true, false)]
    [InlineData("TimesNewRomanPS-ItalicMT", "Times New Roman", false, true)]
    [InlineData("Calibri", "Calibri", false, false)]
    public void Font_names_map_to_installed_families(string name, string family, bool bold, bool italic) =>
        Assert.Equal((family, bold, italic), PdfEditor.ParseFont(name));

    public void Dispose() => _dir.Dispose();
}
