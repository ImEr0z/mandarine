using System.Formats.Tar;
using System.IO.Compression;

namespace Mandarine.Core.Tests;

public class FileToolTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    [Fact]
    public async Task Zip_and_extract_files_and_folders()
    {
        var folder = Directory.CreateDirectory(_dir.File("projet")).FullName;
        Directory.CreateDirectory(Path.Combine(folder, "src"));
        File.WriteAllText(Path.Combine(folder, "src", "main.txt"), "contenu");
        var loose = _dir.WriteText("lisez-moi.txt", "bonjour");

        var zip = (await _dir.RunAsync("files.zip", folder, loose)).OutputFiles[0];
        Assert.Equal(_dir.File("Archive.zip"), zip);
        using (var archive = ZipFile.OpenRead(zip))
        {
            Assert.Equal(["lisez-moi.txt", "projet/src/main.txt"], archive.Entries.Select(e => e.FullName).Order());
        }

        var extracted = (await _dir.RunAsync("archive.extract", zip)).OutputFiles[0];
        Assert.Equal(_dir.File("Archive"), extracted);
        Assert.Equal("contenu", File.ReadAllText(Path.Combine(extracted, "projet", "src", "main.txt")));
    }

    [Fact]
    public async Task Single_folder_zip_is_named_after_it()
    {
        var folder = Directory.CreateDirectory(_dir.File("photos")).FullName;

        var zip = (await _dir.RunAsync("files.zip", folder)).OutputFiles[0];

        Assert.Equal(_dir.File("photos.zip"), zip);
    }

    [Fact]
    public async Task Extract_tar_gz_and_plain_gz()
    {
        var source = Directory.CreateDirectory(_dir.File("source")).FullName;
        File.WriteAllText(Path.Combine(source, "a.txt"), "A");
        var tarGz = _dir.File("backup.tar.gz");
        await using (var file = File.Create(tarGz))
        await using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        {
            await TarFile.CreateFromDirectoryAsync(source, gzip, includeBaseDirectory: false);
        }

        var extracted = (await _dir.RunAsync("archive.extract", tarGz)).OutputFiles[0];
        Assert.Equal(_dir.File("backup"), extracted);
        Assert.Equal("A", File.ReadAllText(Path.Combine(extracted, "a.txt")));

        var gz = _dir.File("journal.log.gz");
        await using (var file = File.Create(gz))
        await using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        await using (var writer = new StreamWriter(gzip))
        {
            await writer.WriteAsync("ligne");
        }

        var log = (await _dir.RunAsync("archive.extract", gz)).OutputFiles[0];
        Assert.Equal(_dir.File("journal.log"), log);
        Assert.Equal("ligne", File.ReadAllText(log));
    }

    [Fact]
    public async Task Zip_slip_entries_are_rejected()
    {
        var zip = _dir.File("piege.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("../../evil.txt").Open());
            writer.Write("x");
        }

        var result = await _dir.Catalog.Find("archive.extract")!.RunAsync([zip], _dir.Context, null, CancellationToken.None);

        Assert.Single(result.Errors);
        Assert.False(Directory.Exists(_dir.File("piege")));
    }

    [Fact]
    public async Task Hash_and_paths_go_to_clipboard()
    {
        var a = _dir.WriteText("a.txt", "abc");
        var b = _dir.WriteText("b.txt", "");

        var single = await _dir.RunAsync("files.sha256", a);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", single.ClipboardText);

        var both = await _dir.RunAsync("files.sha256", a, b);
        Assert.EndsWith("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855  b.txt", both.ClipboardText);

        var paths = await _dir.RunAsync("files.copypath", a, b);
        Assert.Equal(a + Environment.NewLine + b, paths.ClipboardText);
    }

    public void Dispose() => _dir.Dispose();
}
