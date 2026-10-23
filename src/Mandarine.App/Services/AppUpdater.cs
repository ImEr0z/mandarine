using Velopack;
using Velopack.Sources;

namespace Mandarine.App.Services;

/// <summary>
/// Looks for a newer release on GitHub shortly after launch and, when there is one, downloads it
/// and restarts into it. Does nothing when Mandarine was not installed by its installer (dev builds, zip).
/// </summary>
internal static class AppUpdater
{
    private const string RepositoryUrl = "https://github.com/ImEr0z/mandarine";

    /// <summary>True when running from the installer's folder (as opposed to a dev build or a loose exe).</summary>
    public static bool IsInstalled => new UpdateManager(Source()).IsInstalled;

    private static GithubSource Source() => new(RepositoryUrl, accessToken: null, prerelease: false);

    public static async Task CheckAsync()
    {
        try
        {
            // Let the app finish starting (hook, tray, tools warm-up) before touching the network.
            await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            var manager = new UpdateManager(Source());
            if (!manager.IsInstalled)
            {
                return;
            }

            var update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null)
            {
                return;
            }

            await manager.DownloadUpdatesAsync(update).ConfigureAwait(false);
            manager.ApplyUpdatesAndRestart(update, ["--background"]);
        }
        catch (Exception ex)
        {
            // Offline, rate-limited, release without assets...: try again at the next launch.
            ErrorLog.Write(ex);
        }
    }
}
