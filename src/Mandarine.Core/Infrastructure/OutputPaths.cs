namespace Mandarine.Core;

public static class OutputPaths
{
    /// <summary>
    /// Returns a free path for an output derived from <paramref name="input"/>:
    /// "photo.heic" becomes "photo.png", then "photo (2).png" if taken.
    /// </summary>
    /// <param name="suffix">Optional qualifier appended to the base name: "photo - compressée.jpg".</param>
    public static string ForFile(string input, string extension, string? outputDirectory, string? suffix = null)
    {
        var directory = DirectoryFor(input, outputDirectory);
        var baseName = BaseNameOf(input);
        if (!string.IsNullOrEmpty(suffix))
        {
            baseName = $"{baseName} - {suffix}";
        }

        return Unique(directory, baseName, "." + extension.TrimStart('.'), reserve: true);
    }

    /// <summary>Returns a free folder path next to <paramref name="input"/>, e.g. "rapport (pages)".</summary>
    public static string ForFolder(string input, string? outputDirectory, string? suffix = null)
    {
        var directory = DirectoryFor(input, outputDirectory);
        var name = string.IsNullOrEmpty(suffix) ? BaseNameOf(input) : $"{BaseNameOf(input)} ({suffix})";
        return Unique(directory, name, string.Empty, reserve: false);
    }

    /// <summary>Returns a free path named <paramref name="fileName"/> in the folder of <paramref name="anchor"/>.</summary>
    public static string Named(string anchor, string fileName, string? outputDirectory)
    {
        var directory = DirectoryFor(anchor, outputDirectory);
        return Unique(directory, Path.GetFileNameWithoutExtension(fileName), Path.GetExtension(fileName), reserve: true);
    }

    public static string BaseNameOf(string path)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        if (name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            return name[..^7];
        }

        return Directory.Exists(path) ? name : Path.GetFileNameWithoutExtension(name);
    }

    private static string DirectoryFor(string input, string? outputDirectory)
    {
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
            return outputDirectory;
        }

        return Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(input)))
            ?? Environment.CurrentDirectory;
    }

    /// <summary>
    /// Files are reserved by creating them empty with CreateNew: two conversions running at once can
    /// never pick the same name and overwrite each other (a plain "does it exist?" check would race).
    /// </summary>
    private static string Unique(string directory, string baseName, string extension, bool reserve)
    {
        for (var n = 1; ; n++)
        {
            var candidate = Path.Combine(directory, n == 1 ? baseName + extension : $"{baseName} ({n}){extension}");
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                continue;
            }

            if (!reserve)
            {
                return candidate;
            }

            try
            {
                new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete).Dispose();
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate))
            {
                // Taken between the check and the creation: try the next number.
            }
        }
    }

    /// <summary>Deletes a partially written output after a failure. Never throws.</summary>
    public static void Discard(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            else if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
