using System.Runtime.InteropServices;
using System.Windows.Automation;
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

    /// <summary>
    /// Center of the first selected icon of the active Explorer window or the desktop, so the wheel opens on the
    /// file itself; null when it cannot be found (the caller then uses the mouse position).
    /// </summary>
    public static async Task<NativeMethods.POINT?> SelectionAnchorAsync()
    {
        // Off the UI thread and time-boxed: a slow Explorer must never freeze Mandarine, the wheel then opens under the mouse.
        var foreground = NativeMethods.GetAncestor(NativeMethods.GetForegroundWindow(), NativeMethods.GA_ROOT);
        try
        {
            return await Task.Run(() => SelectionAnchor(foreground)).WaitAsync(TimeSpan.FromMilliseconds(500));
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    private static NativeMethods.POINT? SelectionAnchor(IntPtr foreground)
    {
        try
        {
            if (NativeMethods.ClassNameOf(foreground) is "Progman" or "WorkerW")
            {
                return DesktopIcons.SelectedIconCenter();
            }

            var window = AutomationElement.FromHandle(foreground);
            var selected = new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
                new PropertyCondition(SelectionItemPattern.IsSelectedProperty, true));
            foreach (AutomationElement item in window.FindAll(TreeScope.Descendants, selected))
            {
                var bounds = item.Current.BoundingRectangle;
                if (!bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0)
                {
                    return new NativeMethods.POINT((int)(bounds.X + bounds.Width / 2), (int)(bounds.Y + bounds.Height / 2));
                }
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException
                                       or System.ComponentModel.Win32Exception)
        {
            ErrorLog.Write(ex);
        }

        return null;
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
