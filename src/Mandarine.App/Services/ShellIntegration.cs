using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Mandarine.App.Services;

/// <summary>Per-user Windows integration (no administrator rights needed).</summary>
internal static class ShellIntegration
{
    private const string AppName = "Mandarine";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string VerbLabel = "Convertir avec Mandarine";
    private static readonly string[] VerbKeys =
    [
        @"Software\Classes\*\shell\Mandarine",
        @"Software\Classes\Directory\shell\Mandarine",
    ];

    private static string ExecutablePath => Environment.ProcessPath ?? throw new InvalidOperationException("Chemin de l'application inconnu.");

    public static void Apply(AppSettings settings)
    {
        SetLaunchAtStartup(settings.LaunchAtStartup);
        SetExplorerIntegration(settings.ExplorerContextMenu);
    }

    public static void SetLaunchAtStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(AppName, $"\"{ExecutablePath}\" --background");
        }
        else
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
        }
    }

    /// <summary>
    /// Adds "Convertir avec Mandarine" to the classic context menu (Windows 11: "Afficher plus d'options")
    /// and to "Envoyer vers", which passes the whole selection in a single launch.
    /// </summary>
    public static void SetExplorerIntegration(bool enabled)
    {
        foreach (var path in VerbKeys)
        {
            if (!enabled)
            {
                Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
                continue;
            }

            using var verb = Registry.CurrentUser.CreateSubKey(path);
            verb.SetValue("MUIVerb", VerbLabel);
            verb.SetValue("Icon", $"\"{ExecutablePath}\",0");
            verb.SetValue("MultiSelectModel", "Player");
            using var command = verb.CreateSubKey("command");
            command.SetValue(null, $"\"{ExecutablePath}\" \"%1\"");
        }

        SetSendToShortcut(enabled);
    }

    private static void SetSendToShortcut(bool enabled)
    {
        var shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SendTo), "Mandarine.lnk");
        if (!enabled)
        {
            File.Delete(shortcut);
            return;
        }

        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            return;
        }

        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic link = shell.CreateShortcut(shortcut);
            link.TargetPath = ExecutablePath;
            link.IconLocation = ExecutablePath + ",0";
            link.Description = VerbLabel;
            link.Save();
            Marshal.FinalReleaseComObject(link);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }
}
