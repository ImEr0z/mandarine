namespace Mandarine.App.Services;

/// <summary>
/// Mandarine.exe [--background] [--dropzone] [--settings] [files...]
/// Files are what Explorer passes from the context menu or "Send to".
/// </summary>
internal sealed record CommandLine(IReadOnlyList<string> Files, bool Background, bool DropZone, bool Settings)
{
    public static CommandLine Parse(IEnumerable<string> arguments)
    {
        var files = new List<string>();
        bool background = false, dropZone = false, settings = false;
        foreach (var argument in arguments)
        {
            switch (argument.ToLowerInvariant())
            {
                case "--background":
                    background = true;
                    break;
                case "--dropzone":
                    dropZone = true;
                    break;
                case "--settings":
                    settings = true;
                    break;
                default:
                    if (!argument.StartsWith("--", StringComparison.Ordinal) && (File.Exists(argument) || Directory.Exists(argument)))
                    {
                        files.Add(Path.GetFullPath(argument));
                    }

                    break;
            }
        }

        return new CommandLine(files, background, dropZone, settings);
    }
}
