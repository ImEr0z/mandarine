using System.Globalization;
using System.Security.Cryptography;

namespace Mandarine.Core;

public enum SortMode
{
    Type,
    Year,
    Month,
}

public sealed record SortMove(string From, string To);

/// <summary>Moves the files of a folder into sub-folders by kind or by date, and puts them back on request.</summary>
public static class FolderSort
{
    private static readonly (string Folder, string[] Extensions)[] Kinds =
    [
        ("Images", ["jpg", "jpeg", "png", "gif", "bmp", "webp", "heic", "heif", "tif", "tiff", "avif", "ico", "svg", "jxl"]),
        ("Documents", ["pdf", "doc", "docx", "odt", "txt", "rtf", "md", "xls", "xlsx", "ods", "csv", "ppt", "pptx", "odp", "epub"]),
        ("Vidéos", ["mp4", "mov", "mkv", "avi", "webm", "wmv", "m4v"]),
        ("Audio", ["mp3", "wav", "flac", "ogg", "m4a", "aac", "opus", "aiff", "wma"]),
        ("Archives", ["zip", "rar", "7z", "tar", "gz", "bz2", "xz"]),
    ];

    public static string FolderOf(string file, SortMode mode)
    {
        switch (mode)
        {
            case SortMode.Year:
                return File.GetLastWriteTime(file).Year.ToString(CultureInfo.InvariantCulture);
            case SortMode.Month:
                var date = File.GetLastWriteTime(file);
                return date.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            default:
                var extension = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
                return Kinds.FirstOrDefault(k => k.Extensions.Contains(extension)).Folder ?? "Autres";
        }
    }

    /// <summary>What would move, for the files directly in <paramref name="folder"/> (sub-folders and hidden files are left alone).</summary>
    public static IReadOnlyList<SortMove> Plan(string folder, SortMode mode)
    {
        var moves = new List<SortMove>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(folder).Where(f => !File.GetAttributes(f).HasFlag(FileAttributes.Hidden)).Order(StringComparer.OrdinalIgnoreCase))
        {
            var directory = Path.Combine(folder, FolderOf(file, mode));
            var target = Unique(directory, Path.GetFileName(file), taken);
            taken.Add(target);
            moves.Add(new SortMove(file, target));
        }

        return moves;
    }

    public static IReadOnlyList<SortMove> Apply(IReadOnlyList<SortMove> plan)
    {
        var done = new List<SortMove>();
        try
        {
            foreach (var move in plan)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(move.To)!);
                File.Move(move.From, move.To);
                done.Add(move);
            }
        }
        catch
        {
            Undo(done);
            throw;
        }

        return done;
    }

    /// <summary>Puts the files back and removes the sub-folders this left empty.</summary>
    public static void Undo(IReadOnlyList<SortMove> done)
    {
        foreach (var move in done.Reverse())
        {
            if (File.Exists(move.To) && !File.Exists(move.From))
            {
                File.Move(move.To, move.From);
            }

            var directory = Path.GetDirectoryName(move.To);
            if (directory is not null && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
    }

    private static string Unique(string directory, string name, HashSet<string> taken)
    {
        var baseName = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (var n = 1; ; n++)
        {
            var candidate = Path.Combine(directory, n == 1 ? name : $"{baseName} ({n}){extension}");
            if (!File.Exists(candidate) && !taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}

public sealed record DuplicateGroup(long Size, IReadOnlyList<string> Files);

/// <summary>Finds identical files (same size, then same SHA-256), oldest first in each group.</summary>
public static class DuplicateFinder
{
    public static IReadOnlyList<DuplicateGroup> Find(string folder, bool recursive, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System };
        var candidates = Directory.EnumerateFiles(folder, "*", options)
            .Select(path => (Path: path, Size: SafeLength(path)))
            .Where(f => f.Size > 0)
            .GroupBy(f => f.Size)
            .Where(g => g.Count() > 1);

        var groups = new List<DuplicateGroup>();
        foreach (var sameSize in candidates)
        {
            // Two passes: the first 64 KB of each file, then the whole of those that still look the same.
            // Different files almost always differ at the start, so big files are rarely read to the end.
            foreach (var sameStart in GroupBy(sameSize.Select(f => f.Path), QuickLimit, progress, cancellationToken))
            {
                foreach (var identical in sameSize.Key <= QuickLimit ? [sameStart] : GroupBy(sameStart, 0, progress, cancellationToken))
                {
                    groups.Add(new DuplicateGroup(sameSize.Key, identical.OrderBy(File.GetLastWriteTimeUtc).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).ToList()));
                }
            }
        }

        return groups.OrderByDescending(g => g.Size * (g.Files.Count - 1)).ToList();
    }

    private static long SafeLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private const int QuickLimit = 1 << 16;

    /// <summary>Groups of 2+ files whose first <paramref name="limit"/> bytes (all of them when 0) hash the same.</summary>
    private static IEnumerable<List<string>> GroupBy(IEnumerable<string> paths, int limit, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var byHash = new Dictionary<string, List<string>>();
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(Path.GetFileName(path));
            if (Hash(path, limit) is { } hash)
            {
                if (!byHash.TryGetValue(hash, out var list))
                {
                    byHash[hash] = list = [];
                }

                list.Add(path);
            }
        }

        return byHash.Values.Where(l => l.Count > 1);
    }

    private static string? Hash(string path, int limit)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
            if (limit == 0)
            {
                return Convert.ToHexString(SHA256.HashData(stream));
            }

            var buffer = new byte[limit];
            var read = stream.ReadAtLeast(buffer, limit, throwOnEndOfStream: false);
            return Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, read)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
