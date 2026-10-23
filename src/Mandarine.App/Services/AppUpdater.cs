using Velopack;
using Velopack.Sources;

namespace Mandarine.App.Services;

/// <summary>
/// Looks for a newer release on GitHub (at launch, or on demand from the tray menu) and restarts into it.
/// Does nothing when Mandarine was not installed by its installer (dev builds, zip).
/// </summary>
internal static class AppUpdater
{
    private const string RepositoryUrl = "https://github.com/ImEr0z/mandarine";

    private static readonly UpdateManager Manager = new(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));

    /// <summary>True when running from the installer's folder (as opposed to a dev build or a loose exe).</summary>
    public static bool IsInstalled => Manager.IsInstalled;

    public static string? CurrentVersion => Manager.CurrentVersion?.ToString();

    /// <summary>The newer release, or null when up to date. Throws when offline or when it takes longer than <paramref name="timeout"/>.</summary>
    public static async Task<UpdateInfo?> FindAsync(TimeSpan timeout)
    {
        var check = Manager.CheckForUpdatesAsync();
        if (await Task.WhenAny(check, Task.Delay(timeout)) != check)
        {
            throw new TimeoutException("La recherche de mise à jour a pris trop de temps.");
        }

        return await check;
    }

    public static Task DownloadAsync(UpdateInfo update, Action<int>? progress = null) =>
        Manager.DownloadUpdatesAsync(update, progress);

    /// <summary>Installs the downloaded version and starts it with <paramref name="arguments"/>; this process exits.</summary>
    public static void ApplyAndRestart(UpdateInfo update, string[] arguments) =>
        Manager.ApplyUpdatesAndRestart(update, arguments);
}
