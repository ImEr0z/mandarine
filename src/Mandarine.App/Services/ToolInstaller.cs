using System.Diagnostics;
using Mandarine.Core.Actions;

namespace Mandarine.App.Services;

/// <summary>Installs the optional FFmpeg / LibreOffice through winget, or opens their download page.</summary>
internal static class ToolInstaller
{
    private static readonly Dictionary<ExternalTool, (string WingetId, string DownloadPage)> Packages = new()
    {
        [ExternalTool.Ffmpeg] = ("Gyan.FFmpeg", "https://www.gyan.dev/ffmpeg/builds/"),
        [ExternalTool.LibreOffice] = ("TheDocumentFoundation.LibreOffice", "https://www.libreoffice.org/download/"),
    };

    public static string Purpose(ExternalTool tool) => tool switch
    {
        ExternalTool.Ffmpeg => "FFmpeg (gratuit) convertit l'audio et la vidéo.",
        ExternalTool.LibreOffice => "LibreOffice (gratuit) convertit les documents Word, Excel et PowerPoint.",
        _ => string.Empty,
    };

    /// <summary>Returns false when winget is missing (the download page is opened instead) or failed.</summary>
    public static async Task<bool> InstallAsync(ExternalTool tool)
    {
        var (wingetId, downloadPage) = Packages[tool];
        var winget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "winget.exe");
        if (!File.Exists(winget))
        {
            OpenUrl(downloadPage);
            return false;
        }

        // Visible console: the user sees the download progress and any UAC prompt makes sense.
        var startInfo = new ProcessStartInfo(winget,
            $"install --id {wingetId} --exact --accept-source-agreements --accept-package-agreements")
        {
            UseShellExecute = true,
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            await process.WaitForExitAsync().ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            OpenUrl(downloadPage);
            return false;
        }
    }

    public static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
}
