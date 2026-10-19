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

        return Unique(directory, baseName, "." + extension.TrimStart('.'));
    }

    /// <summary>Returns a free folder path next to <paramref name="input"/>, e.g. "rapport (pages)".</summary>
    public static string ForFolder(string input, string? outputDirectory, string? suffix = null)
    {
        var directory = DirectoryFor(input, outputDirectory);
        var name = string.IsNullOrEmpty(suffix) ? BaseNameOf(input) : $"{BaseNameOf(input)} ({suffix})";
        return Unique(directory, name, string.Empty);
    }

    /// <summary>Returns a free path named <paramref name="fileName"/> in the folder of <paramref name="anchor"/>.</summary>
    public static string Named(string anchor, string fileName, string? outputDirectory)
    {
        var directory = DirectoryFor(anchor, outputDirectory);
        return Unique(directory, Path.GetFileNameWithoutExtension(fileName), Path.GetExtension(fileName));
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

    private static string Unique(string directory, string baseName, string extension)
    {
        var candidate = Path.Combine(directory, baseName + extension);
        for (var n = 2; File.Exists(candidate) || Directory.Exists(candidate); n++)
        {
            candidate = Path.Combine(directory, $"{baseName} ({n}){extension}");
        }

        return candidate;
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
