using Mandarine.App.Services;
using Velopack;

namespace Mandarine.App;

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        // Must run first: the installer and the updater start Mandarine.exe with special arguments
        // (install, update, uninstall) that this call handles, then exits.
        VelopackApp.Build()
            // Explorer can cache a blank icon while the installer swaps the "current" folder: ask it to look again.
            .OnAfterInstallFastCallback(_ => ShellIntegration.RefreshIcons())
            .OnAfterUpdateFastCallback(_ => ShellIntegration.RefreshIcons())
            .OnBeforeUninstallFastCallback(_ => ShellIntegration.RemoveAll())
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
