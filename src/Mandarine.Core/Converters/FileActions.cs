using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using Mandarine.Core.Actions;

namespace Mandarine.Core.Converters;

/// <summary>Packs files and folders into a single ZIP archive.</summary>
internal sealed class ZipAction : FileAction
{
    public override string Id => "files.zip";

    public override string Title => "Compresser en ZIP";

    public override string Glyph => "\uE7B8";

    public override ActionGroup Group => ActionGroup.Tools;

    public override bool Accepts(string path) => File.Exists(path) || Directory.Exists(path);

    public override bool IsAvailableFor(IReadOnlyList<string> inputs) =>
        inputs.Count > 0 && inputs.All(Accepts)
        && !(inputs.Count == 1 && Extensions.SameFormat(FileKinds.ExtensionOf(inputs[0]), "zip"));

    public override Task<ActionResult> RunAsync(
        IReadOnlyList<string> inputs,
        ActionContext context,
        IProgress<ActionProgress>? progress,
        CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var output = inputs.Count == 1
                ? OutputPaths.ForFile(inputs[0], "zip", context.OutputDirectory)
                : OutputPaths.Named(inputs[0], "Archive.zip", context.OutputDirectory);
            try
            {
                using var archive = new ZipArchive(File.Create(output), ZipArchiveMode.Create);
                for (var i = 0; i < inputs.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report(new ActionProgress(i, inputs.Count, inputs[i], null));
                    var input = Path.TrimEndingDirectorySeparator(Path.GetFullPath(inputs[i]));
                    if (Directory.Exists(input))
                    {
                        var parent = Path.GetDirectoryName(input)!;
                        var hasContent = false;
                        foreach (var file in Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            hasContent = true;
                            archive.CreateEntryFromFile(file, EntryName(Path.GetRelativePath(parent, file)),
                                CompressionLevel.Optimal);
                        }

                        if (!hasContent)
                        {
                            archive.CreateEntry(EntryName(Path.GetFileName(input)) + "/");
                        }
                    }
                    else
                    {
                        archive.CreateEntryFromFile(input, Path.GetFileName(input), CompressionLevel.Optimal);
                    }
                }
            }
            catch
            {
                OutputPaths.Discard(output);
                throw;
            }

            progress?.Report(new ActionProgress(inputs.Count, inputs.Count, null, null));
            return new ActionResult([output], []);
        }, cancellationToken);

    private static string EntryName(string relativePath) => relativePath.Replace('\\', '/');
}

/// <summary>
/// Extracts ZIP, TAR, TAR.GZ, GZ, 7Z and RAR archives into a folder next to the archive.
/// 7Z and RAR go through the bsdtar (libarchive) that ships with Windows as tar.exe.
/// </summary>
internal sealed class ExtractAction : PerFileAction
{
    public override string Id => "archive.extract";

    public override string Title => "Extraire";

    public override string Glyph => "\uE838";

    public override ActionGroup Group => ActionGroup.Tools;

    public override bool Accepts(string path) => FileKinds.Detect(path) == FileKind.Archive;

    protected override async Task<string> ProcessAsync(
        string input, ActionContext context, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(input).ToLowerInvariant();
        if (!name.EndsWith(".7z") && !name.EndsWith(".rar"))
        {
            return await Task.Run(() => ExtractManaged(input, name, context), cancellationToken).ConfigureAwait(false);
        }

        var tar = FindBsdtar()
            ?? throw new ExternalToolException("L'extraction 7Z et RAR n\u00E9cessite tar.exe (Windows 10 version 1803 ou plus r\u00E9cente).");
        var destination = OutputPaths.ForFolder(input, context.OutputDirectory);
        try
        {
            Directory.CreateDirectory(destination);
            // bsdtar refuses absolute paths and ".." entries unless -P is given; -k never overwrites.
            await ProcessRunner.RunAsync(tar, ["-x", "-k", "-f", input, "-C", destination], cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            OutputPaths.Discard(destination);
            throw;
        }

        return destination;
    }

    private static string? FindBsdtar()
    {
        if (OperatingSystem.IsWindows())
        {
            var system = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");
            return File.Exists(system) ? system : null;
        }

        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir, "bsdtar"))
            .FirstOrDefault(File.Exists);
    }

    private static string ExtractManaged(string input, string name, ActionContext context)
    {
        if (name.EndsWith(".gz") && !name.EndsWith(".tar.gz"))
        {
            return Gunzip(input, context);
        }

        var destination = OutputPaths.ForFolder(input, context.OutputDirectory);
        try
        {
            Directory.CreateDirectory(destination);
            if (name.EndsWith(".zip"))
            {
                RefuseZipBomb(input, destination);
                // Rejects entries that would escape the destination folder ("zip slip").
                ZipFile.ExtractToDirectory(input, destination, overwriteFiles: false);
            }
            else if (name.EndsWith(".tar"))
            {
                TarFile.ExtractToDirectory(input, destination, overwriteFiles: false);
            }
            else
            {
                using var file = File.OpenRead(input);
                using var gzip = new GZipStream(file, CompressionMode.Decompress);
                TarFile.ExtractToDirectory(gzip, destination, overwriteFiles: false);
            }
        }
        catch
        {
            OutputPaths.Discard(destination);
            throw;
        }

        return destination;
    }

    /// <summary>
    /// A few kilobytes of ZIP can declare terabytes of content: check the announced size against the
    /// free space (and an absurd compression ratio) before writing anything.
    /// </summary>
    private static void RefuseZipBomb(string input, string destination)
    {
        using var archive = ZipFile.OpenRead(input);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            total += entry.Length;
        }

        var compressed = Math.Max(1, new FileInfo(input).Length);
        long free;
        try
        {
            free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(destination))!).AvailableFreeSpace;
        }
        catch (ArgumentException)
        {
            free = long.MaxValue; // Network share: free space unknown, the ratio check still applies.
        }
        if (total > free || (total > 1024L * 1024 * 1024 && total / compressed > 200))
        {
            throw new InvalidOperationException("Archive refusée : son contenu décompressé serait démesuré (bombe de décompression ?).");
        }
    }

    private static string Gunzip(string input, ActionContext context)
    {
        var inner = Path.GetFileNameWithoutExtension(input);
        var output = OutputPaths.Named(input, inner, context.OutputDirectory);
        try
        {
            using var source = File.OpenRead(input);
            using var gzip = new GZipStream(source, CompressionMode.Decompress);
            using var target = File.Create(output);
            gzip.CopyTo(target);
        }
        catch
        {
            OutputPaths.Discard(output);
            throw;
        }

        return output;
    }
}

internal sealed class CopyPathAction : FileAction
{
    public override string Id => "files.copypath";

    public override string Title => "Copier le chemin";

    public override string Glyph => "\uE71B";

    public override ActionGroup Group => ActionGroup.Tools;

    public override bool Accepts(string path) => true;

    public override Task<ActionResult> RunAsync(
        IReadOnlyList<string> inputs,
        ActionContext context,
        IProgress<ActionProgress>? progress,
        CancellationToken cancellationToken) =>
        Task.FromResult(ActionResult.Clipboard(string.Join(Environment.NewLine, inputs.Select(Path.GetFullPath))));
}

internal sealed class HashAction : FileAction
{
    public override string Id => "files.sha256";

    public override string Title => "Empreinte SHA-256";

    public override string Glyph => "\uE72E";

    public override ActionGroup Group => ActionGroup.Tools;

    public override bool Accepts(string path) => File.Exists(path);

    public override async Task<ActionResult> RunAsync(
        IReadOnlyList<string> inputs,
        ActionContext context,
        IProgress<ActionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        for (var i = 0; i < inputs.Count; i++)
        {
            progress?.Report(new ActionProgress(i, inputs.Count, inputs[i], null));
            await using var stream = File.OpenRead(inputs[i]);
            var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
            lines.Add(inputs.Count == 1 ? hash : $"{hash}  {Path.GetFileName(inputs[i])}");
        }

        return ActionResult.Clipboard(string.Join(Environment.NewLine, lines));
    }
}
