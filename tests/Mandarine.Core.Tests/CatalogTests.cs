using Mandarine.Core.Actions;

namespace Mandarine.Core.Tests;

public class CatalogTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    private IReadOnlyList<string> Ids(params string[] inputs) => _dir.Catalog.For(inputs).Select(a => a.Id).ToList();

    [Fact]
    public void Catalog_has_unique_ids_and_a_meaningful_size()
    {
        var catalog = new ActionCatalog();
        Assert.Equal(catalog.All.Count, catalog.All.Select(a => a.Id).Distinct().Count());
        Assert.True(catalog.All.Count >= 75, $"Seulement {catalog.All.Count} actions");
    }

    [Fact]
    public void Png_offers_other_image_formats_but_not_png()
    {
        var ids = Ids(_dir.CreateImage());
        Assert.Contains("image.to.jpg", ids);
        Assert.Contains("image.to.webp", ids);
        Assert.Contains("image.resize.half", ids);
        Assert.Contains("image.datauri", ids);
        Assert.DoesNotContain("image.to.png", ids);
        Assert.DoesNotContain("image.combine.pdf", ids);
    }

    [Fact]
    public void Jpeg_alias_hides_jpg_target()
    {
        var ids = Ids(_dir.WriteText("photo.JPEG", "x"));
        Assert.DoesNotContain("image.to.jpg", ids);
        Assert.Contains("image.to.png", ids);
    }

    [Fact]
    public void Conversions_come_before_tools()
    {
        var actions = _dir.Catalog.For([_dir.CreateImage()]);
        var firstTool = actions.ToList().FindIndex(a => a.Group == ActionGroup.Tools);
        Assert.True(firstTool > 0);
        Assert.All(actions.Skip(firstTool), a => Assert.Equal(ActionGroup.Tools, a.Group));
    }

    [Fact]
    public void Video_offers_video_audio_and_gif()
    {
        var ids = Ids(_dir.WriteText("clip.mov", "x"));
        Assert.Contains("video.to.mp4", ids);
        Assert.Contains("video.to.gif", ids);
        Assert.Contains("video.audio.mp3", ids);
        Assert.Contains("video.compress", ids);
        Assert.DoesNotContain("video.to.mov", ids);
    }

    [Fact]
    public void Animated_gif_can_become_a_video()
    {
        var ids = Ids(_dir.CreateAnimatedGif());
        Assert.Contains("video.to.mp4", ids);
        Assert.Contains("video.to.webm", ids);
        Assert.DoesNotContain("video.to.avi", ids);
    }

    [Fact]
    public void Several_images_can_be_combined_into_a_pdf()
    {
        var ids = Ids(_dir.CreateImage("a.png"), _dir.CreateImage("b.png"));
        Assert.Contains("image.combine.pdf", ids);
        Assert.DoesNotContain("image.datauri", ids);
    }

    [Fact]
    public void Several_pdfs_can_be_merged()
    {
        Assert.Contains("pdf.merge", Ids(_dir.WriteText("a.pdf", "x"), _dir.WriteText("b.pdf", "x")));
        Assert.DoesNotContain("pdf.merge", Ids(_dir.WriteText("c.pdf", "x")));
    }

    [Fact]
    public void Folders_only_get_generic_tools()
    {
        var folder = Directory.CreateDirectory(_dir.File("dossier")).FullName;
        Assert.Equal(["files.zip", "files.copypath"], Ids(folder));
    }

    [Fact]
    public void Mixed_kinds_only_get_generic_tools()
    {
        var ids = Ids(_dir.CreateImage(), _dir.WriteText("song.mp3", "x"));
        Assert.Equal(["files.zip", "files.sha256", "files.copypath"], ids);
    }

    [Fact]
    public void Unknown_files_still_get_generic_tools()
    {
        Assert.Equal(["files.zip", "files.sha256", "files.copypath"], Ids(_dir.WriteText("data.bin", "x")));
    }

    [Fact]
    public void Office_documents_require_libreoffice()
    {
        var actions = _dir.Catalog.For([_dir.WriteText("cv.docx", "x")]);
        Assert.Contains(actions, a => a.Id.EndsWith(".to.pdf") && a.Requires == ExternalTool.LibreOffice);
    }

    [Fact]
    public void Kinds_and_descriptions()
    {
        Assert.Equal(FileKind.Image, FileKinds.Detect("IMG_0001.HEIC"));
        Assert.Equal(FileKind.Archive, FileKinds.Detect("backup.tar.gz"));
        Assert.Equal("Image HEIC", FileKinds.Describe(["IMG_0001.HEIC"]));
        Assert.Equal("3 vidéos", FileKinds.Describe(["a.mp4", "b.mov", "c.mkv"]));
        Assert.Equal("2 éléments", FileKinds.Describe(["a.mp4", "b.png"]));
    }

    [Fact]
    public void Output_paths_never_overwrite()
    {
        var input = _dir.WriteText("photo.heic", "x");
        var first = OutputPaths.ForFile(input, "png", null);
        Assert.Equal(_dir.File("photo.png"), first);
        File.WriteAllText(first, "x");
        Assert.Equal(_dir.File("photo (2).png"), OutputPaths.ForFile(input, "png", null));
        Assert.Equal(_dir.File("photo - 50%.png"), OutputPaths.ForFile(input, "png", null, "50%"));

        var custom = _dir.File("sortie");
        Assert.Equal(Path.Combine(custom, "photo.png"), OutputPaths.ForFile(input, "png", custom));
    }

    public void Dispose() => _dir.Dispose();
}
