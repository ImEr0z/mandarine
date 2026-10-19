using Mandarine.Core.Actions;

namespace Mandarine.Core;

/// <summary>Finds optional command-line tools (FFmpeg, LibreOffice) installed on the machine.</summary>
public sealed class ToolLocator
{
    private readonly Dictionary<ExternalTool, string?> _overrides;
    private readonly Dictionary<ExternalTool, string?> _cache = [];
    private readonly object _gate = new();

    public ToolLocator(IReadOnlyDictionary<ExternalTool, string?>? overrides = null)
    {
        _overrides = overrides?.ToDictionary(p => p.Key, p => p.Value) ?? [];
    }

    public static string DisplayName(ExternalTool tool) => tool switch
    {
        ExternalTool.Ffmpeg => "FFmpeg",
        ExternalTool.LibreOffice => "LibreOffice",
        _ => tool.ToString(),
    };

    public bool IsAvailable(ExternalTool tool) => tool == ExternalTool.None || Find(tool) is not null;

    public string? Find(ExternalTool tool)
    {
        if (tool == ExternalTool.None)
        {
            return null;
        }

        lock (_gate)
        {
            if (!_cache.TryGetValue(tool, out var path))
            {
                path = Candidates(tool).FirstOrDefault(File.Exists);
                _cache[tool] = path;
            }

            return path;
        }
    }

    public string Require(ExternalTool tool) => Find(tool) ?? throw new MissingToolException(tool);

    /// <summary>Forgets cached lookups, e.g. after the user installed a tool.</summary>
    public void Refresh()
    {
        lock (_gate)
        {
            _cache.Clear();
        }
    }

    private IEnumerable<string> Candidates(ExternalTool tool)
    {
        if (_overrides.TryGetValue(tool, out var configured) && !string.IsNullOrWhiteSpace(configured))
        {
            yield return configured;
        }

        var executable = tool switch
        {
            ExternalTool.Ffmpeg => "ffmpeg",
            ExternalTool.LibreOffice => "soffice",
            _ => throw new ArgumentOutOfRangeException(nameof(tool)),
        };
        if (OperatingSystem.IsWindows())
        {
            executable += ".exe";
        }

        foreach (var path in WellKnownLocations(tool, executable))
        {
            yield return path;
        }

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            // A relative entry (".") would resolve against the current folder, e.g. a downloaded one.
            var trimmed = directory.Trim('"');
            if (Path.IsPathFullyQualified(trimmed))
            {
                yield return Path.Combine(trimmed, executable);
            }
        }
    }

    private static IEnumerable<string> WellKnownLocations(ExternalTool tool, string executable)
    {
        var appDirectory = AppContext.BaseDirectory;
        yield return Path.Combine(appDirectory, "tools", executable);

        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return Path.Combine(localAppData, "Mandarine", "tools", executable);

            if (tool == ExternalTool.Ffmpeg)
            {
                // winget adds a link folder to PATH, but processes started earlier do not see the new PATH.
                yield return Path.Combine(localAppData, "Microsoft", "WinGet", "Links", executable);
                foreach (var path in WingetPackageBinaries(localAppData, "Gyan.FFmpeg", executable))
                {
                    yield return path;
                }

                yield return Path.Combine(userProfile, "scoop", "shims", executable);
                yield return @"C:\ProgramData\chocolatey\bin\" + executable;
                yield return Path.Combine(programFiles, "ffmpeg", "bin", executable);
                yield return @"C:\ffmpeg\bin\" + executable;
            }
            else
            {
                yield return Path.Combine(programFiles, "LibreOffice", "program", executable);
                yield return Path.Combine(programFilesX86, "LibreOffice", "program", executable);
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            if (tool == ExternalTool.Ffmpeg)
            {
                yield return "/opt/homebrew/bin/ffmpeg";
                yield return "/usr/local/bin/ffmpeg";
            }
            else
            {
                yield return "/Applications/LibreOffice.app/Contents/MacOS/soffice";
            }
        }
        else if (tool == ExternalTool.LibreOffice)
        {
            yield return "/usr/lib/libreoffice/program/soffice";
        }
    }

    private static IEnumerable<string> WingetPackageBinaries(string localAppData, string packagePrefix, string executable)
    {
        var packages = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
        if (!Directory.Exists(packages))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateDirectories(packages, packagePrefix + "*")
                .SelectMany(dir => Directory.EnumerateFiles(dir, executable, SearchOption.AllDirectories))
                .ToList();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }
}
