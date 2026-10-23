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

    /// <summary>
    /// Re-registers the entries when they point at another copy of the exe (installed over a zip copy,
    /// moved folder): they must always launch the copy that is running.
    /// </summary>
    public static void RefreshPaths(AppSettings settings)
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey);
        using var verb = Registry.CurrentUser.OpenSubKey(VerbKeys[0] + @"\command");
        var stale = (run?.GetValue(AppName) as string is { } startup && !startup.Contains(ExecutablePath, StringComparison.OrdinalIgnoreCase))
                    || (verb?.GetValue(null) as string is { } command && !command.Contains(ExecutablePath, StringComparison.OrdinalIgnoreCase));
        if (stale)
        {
            Apply(settings);
        }
    }

    /// <summary>Tells Explorer and the taskbar that icons changed (same as an association change).</summary>
    public static void RefreshIcons() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    /// <summary>Removes every trace Mandarine left in Windows: startup entry, context menu, "Send to", Start menu.</summary>
    public static void RemoveAll()
    {
        SetLaunchAtStartup(false);
        SetExplorerIntegration(false);
        var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Mandarine.lnk");
        if (File.Exists(startMenu))
        {
            File.Delete(startMenu);
        }
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
