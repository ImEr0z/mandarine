using System.Runtime.InteropServices;
using Mandarine.App.Interop;

namespace Mandarine.App.Services;

/// <summary>
/// Position of the selected icon on the desktop. The desktop is a list view inside Explorer that UI
/// Automation does not expose, so its own list-view messages are used (reading one rectangle from Explorer's memory).
/// </summary>
internal static class DesktopIcons
{
    private const uint LVM_GETNEXTITEM = 0x100C;
    private const uint LVM_GETITEMRECT = 0x100E;
    private const int LVNI_SELECTED = 2;
    private const int LVIR_ICON = 1;
    private const uint PROCESS_VM_ACCESS = 0x0008 | 0x0010 | 0x0020; // operation, read, write
    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_RELEASE = 0x8000;
    private const uint PAGE_READWRITE = 0x04;

    public static NativeMethods.POINT? SelectedIconCenter()
    {
        var listView = FindDesktopListView();
        if (listView == IntPtr.Zero)
        {
            return null;
        }

        var index = (int)(Send(listView, LVM_GETNEXTITEM, new IntPtr(-1), new IntPtr(LVNI_SELECTED)) ?? new IntPtr(-1));
        if (index < 0)
        {
            return null;
        }

        GetWindowThreadProcessId(listView, out var processId);
        var process = OpenProcess(PROCESS_VM_ACCESS, false, processId);
        if (process == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var remote = VirtualAllocEx(process, IntPtr.Zero, 16, MEM_COMMIT, PAGE_READWRITE);
            if (remote == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var rect = new int[] { LVIR_ICON, 0, 0, 0 }; // left carries the kind of rectangle asked for
                var buffer = new byte[16];
                Buffer.BlockCopy(rect, 0, buffer, 0, 16);
                if (!WriteProcessMemory(process, remote, buffer, 16, out _)
                    || (Send(listView, LVM_GETITEMRECT, new IntPtr(index), remote) ?? IntPtr.Zero) == IntPtr.Zero
                    || !ReadProcessMemory(process, remote, buffer, 16, out _))
                {
                    return null;
                }

                Buffer.BlockCopy(buffer, 0, rect, 0, 16);
                var center = new NativeMethods.POINT((rect[0] + rect[2]) / 2, (rect[1] + rect[3]) / 2);
                return ClientToScreen(listView, ref center) ? center : null;
            }
            finally
            {
                VirtualFreeEx(process, remote, 0, MEM_RELEASE);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    /// <summary>The list view sits in "Progman", or in a "WorkerW" window when a wallpaper slideshow is running.</summary>
    private static IntPtr FindDesktopListView()
    {
        var defView = FindDefView(FindWindow("Progman", null));
        if (defView == IntPtr.Zero)
        {
            var worker = IntPtr.Zero;
            while (defView == IntPtr.Zero && (worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
            {
                defView = FindDefView(worker);
            }
        }

        return defView == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
    }

    private static IntPtr FindDefView(IntPtr parent) =>
        parent == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(parent, IntPtr.Zero, "SHELLDLL_DefView", null);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? windowName);

    /// <summary>A hung Explorer must not hang Mandarine: give up after a fraction of a second.</summary>
    private static IntPtr? Send(IntPtr window, uint message, IntPtr wParam, IntPtr lParam) =>
        SendMessageTimeout(window, message, wParam, lParam, SMTO_ABORTIFHUNG, 300, out var result) != IntPtr.Zero ? result : null;

    private const uint SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam,
        uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr window, ref NativeMethods.POINT point);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, nuint size, uint type, uint protect);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, nuint size, uint type);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, nuint size, out nuint written);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, nuint size, out nuint read);
}
