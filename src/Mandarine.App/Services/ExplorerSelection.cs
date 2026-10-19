using System.Runtime.InteropServices;
using Mandarine.App.Interop;

namespace Mandarine.App.Services;

/// <summary>
/// Reads the files selected in the active File Explorer window, or on the desktop, through the
/// Shell's public automation objects (the same ones scripts use). Only paths are read.
/// </summary>
internal static class ExplorerSelection
{
    private const int SwcDesktop = 8;
    private const int SwfoNeedDispatch = 1;

    public static IReadOnlyList<string> FromForegroundWindow()
    {
        var foreground = NativeMethods.GetAncestor(NativeMethods.GetForegroundWindow(), NativeMethods.GA_ROOT);
        var className = NativeMethods.ClassNameOf(foreground);
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return [];
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            try
            {
                dynamic windows = shell.Windows();
                if (className is "Progman" or "WorkerW")
                {
                    object location = 0;
                    object root = 0;
                    dynamic? desktop = windows.FindWindowSW(ref location, ref root, SwcDesktop, out int _, SwfoNeedDispatch);
                    return desktop is null ? [] : Selected(desktop.Document);
                }

                if (className is "CabinetWClass" or "ExploreWClass")
                {
                    foreach (dynamic window in windows)
                    {
                        if ((IntPtr)(long)window.HWND == foreground)
                        {
                            return Selected(window.Document);
                        }
                    }
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            ErrorLog.Write(ex);
        }

        return [];
    }

    private static IReadOnlyList<string> Selected(dynamic? document)
    {
        var paths = new List<string>();
        if (document is null)
        {
            return paths;
        }

        foreach (dynamic item in document.SelectedItems())
        {
            if (item.Path is string path && Path.IsPathFullyQualified(path))
            {
                paths.Add(path);
            }
        }

        return paths;
    }
}
