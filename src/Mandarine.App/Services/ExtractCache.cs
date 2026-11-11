using System.Diagnostics;

namespace Mandarine.App.Services;

/// <summary>
/// The single-file exe unpacks its native libraries (Magick, pdfium, Skia, WPF...) into %TEMP%\.net\Mandarine\&lt;hash&gt;, one
/// folder per build, and .NET never removes the old ones: after a few updates that is hundreds of megabytes of dead copies.
/// This removes the folders of other builds, a little after start-up, unless a running process still uses them.
/// </summary>
internal static class ExtractCache
{
    public static void CleanLater() =>
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(45)).ConfigureAwait(false);
            Clean();
        });

    private static void Clean()
    {
        try
        {
            var current = CurrentFolder();
            var parent = current is null ? null : Path.GetDirectoryName(current);
            if (current is null || parent is null || !Directory.Exists(parent))
            {
                return; // not running from an unpacked single file (development build, or no native extraction)
            }

            var inUse = FoldersInUse(parent);
            foreach (var folder in Directory.EnumerateDirectories(parent))
            {
                if (string.Equals(folder, current, StringComparison.OrdinalIgnoreCase) || inUse is null || inUse.Contains(folder))
                {
                    continue;
                }

                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Best effort: a locked file only means that folder stays until the next start.
        }
    }

    /// <summary>The folder this process unpacked into: where a native library of the app is loaded from.</summary>
    private static string? CurrentFolder()
    {
        foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
        {
            var directory = Path.GetDirectoryName(module.FileName);
            if (directory is not null && directory.Contains(@"\.net\", StringComparison.OrdinalIgnoreCase))
            {
                return directory;
            }
        }

        return null;
    }

    /// <summary>Unpacked folders where another running process (an older Mandarine) has a library loaded; null when that cannot be known.</summary>
    private static HashSet<string>? FoldersInUse(string parent)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcessesByName("Mandarine"))
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId)
                {
                    continue;
                }

                try
                {
                    foreach (ProcessModule module in process.Modules)
                    {
                        var directory = Path.GetDirectoryName(module.FileName);
                        if (directory is not null && directory.StartsWith(parent, StringComparison.OrdinalIgnoreCase))
                        {
                            used.Add(directory);
                        }
                    }
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    return null; // cannot look into that process: delete nothing
                }
            }
        }

        return used;
    }
}
