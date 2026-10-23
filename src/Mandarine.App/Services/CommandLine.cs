namespace Mandarine.App.Services;

/// <summary>
/// Mandarine.exe [--background] [--settings] [files...]
/// Mandarine.exe --worker runs one conversion for the tray app (see ConversionWorker) and exits.
/// Files are what Explorer passes from the context menu or "Send to".
/// </summary>
internal sealed record CommandLine(IReadOnlyList<string> Files, bool Background, bool Settings, bool Worker = false)
{
    public static CommandLine Parse(IEnumerable<string> arguments)
    {
        var files = new List<string>();
        bool background = false, settings = false, worker = false;
        foreach (var argument in arguments)
        {
            switch (argument.ToLowerInvariant())
            {
                case "--background":
                    background = true;
                    break;
                case "--settings":
                    settings = true;
                    break;
                case ConversionWorker.Argument:
                    worker = true;
                    break;
                default:
                    if (!argument.StartsWith("--", StringComparison.Ordinal) && (File.Exists(argument) || Directory.Exists(argument)))
                    {
                        files.Add(Path.GetFullPath(argument));
                    }

                    break;
            }
        }

        return new CommandLine(files, background, settings, worker);
    }
}
