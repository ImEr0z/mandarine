namespace Mandarine.App.Services;

/// <summary>PDF windows run in their own process without a tray icon: the running Mandarine shows their notifications.</summary>
internal static class TrayBridge
{
    public static void Notify(string title, string text, string openPath)
    {
        using var instance = new SingleInstance("Mandarine");
        if (!instance.IsFirstInstance)
        {
            instance.TrySendToFirstInstance([CommandLine.NotifyArgument, title, text, openPath]);
        }
    }
}
