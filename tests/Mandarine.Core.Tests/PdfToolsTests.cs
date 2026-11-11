using Mandarine.Core.Converters;
using PdfSharp.Pdf.IO;

namespace Mandarine.Core.Tests;

public class PdfToolsTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    private static int PageCount(string pdf) => PDFtoImage.Conversion.GetPageCount(File.ReadAllBytes(pdf));

    private async Task<string> Run(string id, string? input, params string[] files)
    {
        _dir.Context.Input = input;
        return (await _dir.RunAsync(id, files)).OutputFiles[0];
    }

    [Theory]
    [InlineData("2", 5, new[] { 1 })]
    [InlineData("1-3, 5", 5, new[] { 0, 1, 2, 4 })]
    [InlineData("4-", 5, new[] { 3, 4 })]
    public void Page_ranges_are_parsed(string text, int count, int[] expected) =>
        Assert.Equal(expected, Converters.PdfTools.ParsePages(text, count));

    [Fact]
    public void Page_range_outside_the_document_is_refused() =>
        Assert.Throws<InvalidOperationException>(() => Converters.PdfTools.ParsePages("9", 3));

    [Fact]
    public async Task Delete_pages_removes_them()
    {
        var pdf = _dir.CreateTextPdf("a.pdf", "Un", "Deux", "Trois");

        var output = await Run("pdf.delete-pages", "2", pdf);

        Assert.Equal(2, PageCount(output));
    }

    [Fact]
    public async Task Extract_pages_keeps_only_those()
    {
        var pdf = _dir.CreateTextPdf("a.pdf", "Un", "Deux", "Trois");

        var output = await Run("pdf.extract-pages", "1,3", pdf);

        Assert.Equal(2, PageCount(output));
    }

    [Fact]
    public async Task Page_numbers_watermark_and_crop_keep_the_page_count()
    {
        var pdf = _dir.CreateTextPdf("a.pdf", "Un", "Deux");

        Assert.Equal(2, PageCount(await Run("pdf.page-numbers", null, pdf)));
        Assert.Equal(2, PageCount(await Run("pdf.watermark", "COPIE", pdf)));
        Assert.Equal(2, PageCount(await Run("pdf.crop", "10", pdf)));
    }

    [Fact]
    public async Task Protect_then_unlock_round_trips()
    {
        var pdf = _dir.CreateTextPdf("a.pdf", "Secret");

        var locked = await Run("pdf.protect", "mdp", pdf);
        Assert.True(Converters.PdfTools.IsEncrypted(locked));
        Assert.ThrowsAny<Exception>(() => PdfReader.Open(locked, PdfDocumentOpenMode.Import));

        var unlocked = await Run("pdf.unlock", "mdp", locked);
        Assert.False(Converters.PdfTools.IsEncrypted(unlocked));
        Assert.Equal(1, PageCount(unlocked));
    }

    [Fact]
    public async Task Unlock_with_a_wrong_password_fails()
    {
        var locked = await Run("pdf.protect", "mdp", _dir.CreateTextPdf("a.pdf", "Secret"));
        _dir.Context.Input = "faux";

        var result = await _dir.Catalog.Find("pdf.unlock")!.RunAsync([locked], _dir.Context, null, CancellationToken.None);

        Assert.Single(result.Errors);
    }

    [Fact]
    public void Summary_keeps_sentences_of_the_document()
    {
        var sentences = Enumerable.Range(1, 12).Select(i => $"Le projet Mandarine convertit des fichiers numéro {i} avec une grande simplicité.");
        var pdf = _dir.CreateTextPdf("a.pdf", string.Join(" ", sentences));

        Assert.Contains("Mandarine", string.Join(" ", TextSummary.SummarizePdf(pdf)));
    }

    [Fact]
    public void Compare_reports_added_and_removed_lines()
    {
        var differences = new List<PdfDifference>();

        PdfCompare.Diff(1, ["a", "b", "c"], ["a", "c", "d"], differences);

        Assert.Equal([new PdfDifference(1, false, "b"), new PdfDifference(1, true, "d")], differences);
    }

    [Fact]
    public async Task Compare_finds_the_changed_page()
    {
        var before = _dir.CreateTextPdf("v1.pdf", "Meme", "Ancien");
        var after = _dir.CreateTextPdf("v2.pdf", "Meme", "Nouveau");

        var comparison = PdfCompare.Compare(before, after);

        Assert.All(comparison.Differences, d => Assert.Equal(2, d.Page));
        Assert.Contains(comparison.Differences, d => d is { Added: true, Text: "Nouveau" });
    }

    [Fact]
    public void Stamp_is_drawn_on_the_page()
    {
        var pdf = _dir.CreateTextPdf("a.pdf", "Contrat");
        using var image = new ImageMagick.MagickImage(new ImageMagick.MagickColor(0, 0, 255, 128), 40, 20);
        var stamp = new PdfStamp(0, 50, 50, 80, 40, image.ToByteArray(ImageMagick.MagickFormat.Png));

        PdfEditor.Save(pdf, pdf, [], [stamp]);

        Assert.Equal(1, PageCount(pdf));
        Assert.Contains("Image", System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(pdf)));
    }

    [Fact]
    public void Redaction_removes_the_text_under_the_box_and_keeps_other_pages()
    {
        var pdf = _dir.CreateTextPdf("a.pdf", "Numero secret 1234", "Page publique");
        var output = _dir.File("a - caviardé.pdf");

        // The whole first page is covered: the text must be gone from the file, not just hidden.
        var changed = PdfRedactor.Save(pdf, output, [new PdfRedaction(0, 0, 0, 1, 1)]);

        Assert.Equal(1, changed);
        Assert.Equal(2, PageCount(output));
        using var document = UglyToad.PdfPig.PdfDocument.Open(output);
        Assert.DoesNotContain("secret", document.GetPage(1).Text);
        Assert.Contains("publique", document.GetPage(2).Text);
    }

    [Fact]
    public async Task Watermark_can_tile_a_logo()
    {
        var pdf = _dir.CreateTextPdf("a.pdf", "Contrat");
        var logo = _dir.File("logo.png");
        // A transparent logo with a few flat colours: the kind Magick saves as a palette PNG.
        using (var image = new ImageMagick.MagickImage(ImageMagick.MagickColors.Transparent, 60, 30))
        {
            image.Draw(new ImageMagick.Drawing.DrawableFillColor(ImageMagick.MagickColors.Orange), new ImageMagick.Drawing.DrawableRectangle(2, 2, 30, 28));
            image.Draw(new ImageMagick.Drawing.DrawableFillColor(ImageMagick.MagickColors.Green), new ImageMagick.Drawing.DrawableRectangle(34, 2, 58, 28));
            image.Write(logo, ImageMagick.MagickFormat.Png);
        }

        var output = await Run("pdf.watermark", Converters.PdfWatermarkAction.ImagePrefix + logo, pdf);

        Assert.Equal(1, PageCount(output));
        Assert.Matches(@"/Subtype\s*/Image", System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(output)));
    }

    [Fact]
    public async Task Preview_shows_the_first_page_without_touching_the_file()
    {
        var pdf = _dir.CreateTextPdf("a.pdf", "Un", "Deux");
        _dir.Context.Input = "COPIE";
        var before = File.ReadAllBytes(pdf);

        var preview = await _dir.Catalog.Find("pdf.watermark")!.BuildPreviewAsync(pdf, _dir.Context, CancellationToken.None);

        Assert.NotNull(preview);
        Assert.Equal(1, PageCount(preview!));
        Assert.Equal(before, File.ReadAllBytes(pdf));
        File.Delete(preview!);
    }

    [Fact]
    public void Bulk_rename_numbers_files_and_flags_conflicts()
    {
        var a = _dir.WriteText("photo 2.txt", "a");
        var b = _dir.WriteText("photo 10.txt", "b");

        var plan = BulkRename.Plan([b, a], new RenameOptions("Vacances {n}", 1, 3));

        Assert.Equal(["Vacances 001.txt", "Vacances 002.txt"], plan.Select(p => Path.GetFileName(p.To)));
        Assert.Equal("photo 2.txt", Path.GetFileName(plan[0].From)); // natural order: 2 before 10
        Assert.All(plan, p => Assert.Null(p.Problem));
        Assert.All(BulkRename.Plan([a, b], new RenameOptions("pareil")), p => Assert.NotNull(p.Problem));
    }

    [Fact]
    public void Bulk_rename_applies_and_undoes_even_a_swap()
    {
        var a = _dir.WriteText("a.txt", "A");
        var b = _dir.WriteText("b.txt", "B");

        var applied = BulkRename.Apply([new RenamePlan(a, b, null), new RenamePlan(b, a, null)]);

        Assert.Equal("B", File.ReadAllText(a));
        Assert.Equal("A", File.ReadAllText(b));
        BulkRename.Undo(applied);
        Assert.Equal("A", File.ReadAllText(a));
        Assert.Equal("B", File.ReadAllText(b));
    }

    [Fact]
    public void Folder_sort_groups_by_kind_and_undoes()
    {
        var picture = _dir.WriteText("photo.jpg", "x");
        var document = _dir.WriteText("note.pdf", "x");
        var other = _dir.WriteText("truc.xyz", "x");

        var applied = FolderSort.Apply(FolderSort.Plan(_dir.Path, SortMode.Type));

        Assert.True(File.Exists(_dir.File("Images/photo.jpg")));
        Assert.True(File.Exists(_dir.File("Documents/note.pdf")));
        Assert.True(File.Exists(_dir.File("Autres/truc.xyz")));
        FolderSort.Undo(applied);
        Assert.All(new[] { picture, document, other }, p => Assert.True(File.Exists(p)));
        Assert.False(Directory.Exists(_dir.File("Images")));
    }

    [Fact]
    public void Duplicates_are_found_by_content_not_by_name()
    {
        _dir.WriteText("a.txt", "meme contenu");
        _dir.WriteText("copie de a.txt", "meme contenu");
        _dir.WriteText("autre.txt", "contenu different!");

        var groups = DuplicateFinder.Find(_dir.Path, recursive: true);

        var group = Assert.Single(groups);
        Assert.Equal(["a.txt", "copie de a.txt"], group.Files.Select(Path.GetFileName).Order());
    }

    [Fact]
    public void Adjust_brightens_and_straightening_keeps_the_picture_filled()
    {
        var path = _dir.File("p.png");
        using (var image = new ImageMagick.MagickImage(new ImageMagick.MagickColor(100, 100, 100), 200, 100))
        {
            image.Write(path);
        }

        using var brighter = new ImageMagick.MagickImage(ImageAdjust.Preview(path, new AdjustSettings(Brightness: 50), 0));
        using var original = new ImageMagick.MagickImage(path);
        Assert.True(brighter.Statistics().Composite().Mean > original.Statistics().Composite().Mean);

        using var turned = new ImageMagick.MagickImage(ImageAdjust.Preview(path, new AdjustSettings(Angle: 10), 0));
        Assert.True(turned.Width < 200 && turned.Height < 100); // cropped to the inscribed rectangle
        Assert.Equal(2.0, (double)turned.Width / turned.Height, 1);
    }

    [Fact]
    public void Pdf_metadata_can_be_edited_and_stripped()
    {
        var pdf = _dir.CreateTextPdf("a.pdf", "Contrat");
        var output = _dir.File("b.pdf");

        PdfMetadata.Save(pdf, output, new PdfInfoEdit("Mon titre", "Moi", "Sujet", "a, b", StripHidden: true, StripBookmarks: false));

        var info = PdfMetadata.Read(output);
        Assert.Equal("Mon titre", info.Title);
        Assert.Equal("Moi", info.Author);
        Assert.Equal(string.Empty, info.Creator.Trim());
        // PDFsharp rewrites a small XMP block from the fields above: whatever the original XMP said is gone.
        Assert.Contains("Mon titre", System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(output)));
    }

    [Fact]
    public void Duplicates_of_big_files_are_compared_to_the_end()
    {
        var big = new byte[300_000];
        new Random(1).NextBytes(big);
        File.WriteAllBytes(_dir.File("a.bin"), big);
        File.WriteAllBytes(_dir.File("same.bin"), big);
        var tail = (byte[])big.Clone();
        tail[^1] ^= 0xFF; // same size, same first 64 KB, different last byte
        File.WriteAllBytes(_dir.File("almost.bin"), tail);

        var group = Assert.Single(DuplicateFinder.Find(_dir.Path, recursive: false));

        Assert.Equal(["a.bin", "same.bin"], group.Files.Select(Path.GetFileName).Order());
    }

    public void Dispose() => _dir.Dispose();
}
