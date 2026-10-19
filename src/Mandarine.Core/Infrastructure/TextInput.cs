namespace Mandarine.Core;

/// <summary>
/// Text conversions (JSON, YAML, XML, CSV, Markdown, HTML) load the whole file and build a tree
/// several times its size: cap the input so a huge file fails cleanly instead of exhausting memory.
/// </summary>
internal static class TextInput
{
    public const long MaxBytes = 200L * 1024 * 1024;

    public static Task<string> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (new FileInfo(path).Length > MaxBytes)
        {
            throw new InvalidOperationException($"Fichier trop volumineux pour une conversion de texte (plus de {MaxBytes / (1024 * 1024)} Mo).");
        }

        return File.ReadAllTextAsync(path, cancellationToken);
    }
}
