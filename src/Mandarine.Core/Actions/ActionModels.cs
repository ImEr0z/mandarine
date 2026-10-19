namespace Mandarine.Core.Actions;

public enum ExternalTool
{
    None,
    Ffmpeg,
    LibreOffice,
}

public sealed record ActionError(string Input, string Message);

public sealed record ActionResult(
    IReadOnlyList<string> OutputFiles,
    IReadOnlyList<ActionError> Errors,
    string? ClipboardText = null)
{
    public static ActionResult Clipboard(string text) => new([], [], text);

    public bool Succeeded => Errors.Count == 0;
}

/// <param name="Index">Zero-based index of the file being processed.</param>
/// <param name="FileFraction">Progress inside the current file (0 to 1) when the operation can report it.</param>
public sealed record ActionProgress(int Index, int Count, string? CurrentFile, double? FileFraction)
{
    public double Overall => Count == 0 ? 1 : Math.Clamp((Index + (FileFraction ?? 0)) / Count, 0, 1);
}

public sealed class ActionContext
{
    /// <summary>Folder receiving outputs. Null writes next to each input file.</summary>
    public string? OutputDirectory { get; init; }

    /// <summary>Encoder quality (1-100) for lossy image formats.</summary>
    public int Quality { get; init; } = 85;

    public ToolLocator Tools { get; init; } = new();
}

public sealed class MissingToolException(ExternalTool tool)
    : Exception($"{ToolLocator.DisplayName(tool)} est nécessaire pour cette conversion.")
{
    public ExternalTool Tool { get; } = tool;
}

public static class Extensions
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jpeg"] = "jpg",
        ["jpe"] = "jpg",
        ["jfif"] = "jpg",
        ["tif"] = "tiff",
        ["heif"] = "heic",
        ["yml"] = "yaml",
        ["htm"] = "html",
        ["aif"] = "aiff",
        ["markdown"] = "md",
    };

    public static string Normalize(string extension)
    {
        var ext = extension.TrimStart('.').ToLowerInvariant();
        return Aliases.TryGetValue(ext, out var alias) ? alias : ext;
    }

    public static bool SameFormat(string a, string b) => Normalize(a) == Normalize(b);
}
