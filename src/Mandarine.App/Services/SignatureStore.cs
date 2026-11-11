namespace Mandarine.App.Services;

/// <summary>Signatures kept between sessions: one PNG per name in %AppData%\Mandarine\signatures.</summary>
internal static class SignatureStore
{
    private static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mandarine", "signatures");

    private static string FileOf(string name) => Path.Combine(Folder, Clean(name) + ".png");

    public static IReadOnlyList<string> Names()
    {
        try
        {
            return Directory.Exists(Folder)
                ? Directory.EnumerateFiles(Folder, "*.png").Select(Path.GetFileNameWithoutExtension).OfType<string>()
                    .Order(StringComparer.CurrentCultureIgnoreCase).ToList()
                : [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    public static byte[]? Load(string name)
    {
        try
        {
            return File.ReadAllBytes(FileOf(name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Saves (or replaces) the signature called <paramref name="name"/>.</summary>
    public static void Save(string name, byte[] png)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllBytes(FileOf(name), png);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write(ex);
        }
    }

    /// <summary>False when another signature already has the new name.</summary>
    public static bool Rename(string name, string newName)
    {
        var target = FileOf(newName);
        if (string.Equals(FileOf(name), target, StringComparison.OrdinalIgnoreCase) && !string.Equals(Clean(name), Clean(newName), StringComparison.Ordinal))
        {
            // Only the case changes: Windows sees the same file, move through a temporary name.
            var temporary = FileOf(name) + ".tmp";
            File.Move(FileOf(name), temporary);
            File.Move(temporary, target);
            return true;
        }

        if (File.Exists(target))
        {
            return string.Equals(Clean(name), Clean(newName), StringComparison.Ordinal);
        }

        try
        {
            File.Move(FileOf(name), target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write(ex);
            return false;
        }
    }

    public static void Delete(string name)
    {
        try
        {
            File.Delete(FileOf(name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write(ex);
        }
    }

    private static string Clean(string name)
    {
        var text = string.Concat(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
        return text.Length == 0 ? "Signature" : text[..Math.Min(text.Length, 40)];
    }
}
