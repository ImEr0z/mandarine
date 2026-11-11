using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Mandarine.App.Services;

/// <summary>
/// Notification area icon and its menu, straight on the Windows shell API (no WinForms: that library alone weighed 22 MB).
/// The menu is a native Windows popup: a WPF or WinForms menu takes focus in a way that makes the "hidden icons" flyout close.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint MF_STRING = 0x0000;
    private const uint MF_GRAYED = 0x0001;
    private const uint MF_CHECKED = 0x0008;
    private const uint MF_SEPARATOR = 0x0800;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_BOTTOMALIGN = 0x0020;
    private const uint WM_NULL = 0x0000;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;
    private const int NIN_BALLOONUSERCLICK = 0x0405;
    private const int TrayMessage = 0x8001; // WM_APP + 1

    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_INFO = 0x10;

    private const int CmdSettings = 1;
    private const int CmdUpdate = 2;
    private const int CmdPause = 3;
    private const int CmdUninstall = 4;
    private const int CmdExit = 5;
    private const int CmdNews = 6;
    private const int CmdUndo = 7;
    private const int CmdHistory = 8;

    private readonly HwndSource _window;
    private readonly IntPtr _icon;
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private bool _paused;
    private string? _balloonPath;

    public TrayIcon(bool paused)
    {
        _paused = paused;
        AllowDarkMenus();

        // A hidden top-level window that receives the icon's messages and owns the menu. It must be the very window the icon
        // belongs to, and a real top-level one (not message-only): that is what keeps an auto-hidden taskbar visible while
        // the menu is open, and what receives the "TaskbarCreated" broadcast.
        _window = new HwndSource(new HwndSourceParameters("Mandarine tray") { Width = 0, Height = 0, WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x80 /* WS_EX_TOOLWINDOW */ });
        _window.AddHook(WndProc);

        // The icon of the exe itself, at the size of the notification area.
        var small = new IntPtr[1];
        _icon = ExtractIconEx(Environment.ProcessPath ?? string.Empty, 0, null, small, 1) > 0 ? small[0] : LoadIcon(IntPtr.Zero, new IntPtr(32512));
        AddIcon();
    }

    public event Action? SettingsRequested;

    public event Action? NewsRequested;

    public event Action? UndoRequested;

    public event Action? HistoryRequested;

    public event Action? UpdateCheckRequested;

    public event Action<bool>? PauseChanged;

    public event Action? ExitRequested;

    public event Action? UninstallRequested;

    public void SetPaused(bool paused) => _paused = paused;

    /// <summary>
    /// With a result to open or undo: the Mandarine bubble, which has buttons. Plain messages stay Windows balloons;
    /// clicking one of them opens <paramref name="openPath"/> when given.
    /// </summary>
    public void Notify(string title, string text, string? openPath = null, bool canUndo = false)
    {
        if (openPath is not null || canUndo)
        {
            Mandarine.App.Views.ToastWindow.ShowToast(title, text, openPath, canUndo);
            return;
        }

        _balloonPath = openPath;
        var data = NewData(NIF_INFO);
        data.szInfoTitle = title;
        data.szInfo = text;
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    private NotifyIconData NewData(uint flags) => new()
    {
        cbSize = Marshal.SizeOf<NotifyIconData>(),
        hWnd = _window.Handle,
        uID = 1,
        uFlags = flags,
        szTip = string.Empty,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private void AddIcon()
    {
        var data = NewData(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        data.uCallbackMessage = TrayMessage;
        data.hIcon = _icon;
        data.szTip = "Mandarine";
        Shell_NotifyIcon(NIM_ADD, ref data);    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == TrayMessage)
        {
            switch ((int)(lParam.ToInt64() & 0xFFFF))
            {
                case WM_LBUTTONUP or WM_RBUTTONUP: // left click opens the same menu as right click
                    handled = true;
                    ShowMenu();
                    break;
                case NIN_BALLOONUSERCLICK:
                    handled = true;
                    if (_balloonPath is { } path && (File.Exists(path) || Directory.Exists(path)))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
                    }

                    break;
            }
        }
        else if (message == _taskbarCreated)
        {
            AddIcon(); // Explorer restarted: its notification area is empty again
        }

        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        try
        {
            AppendMenu(menu, MF_STRING, CmdSettings, "Paramètres…");
            AppendMenu(menu, MF_STRING, CmdNews, "Nouveautés");
            AppendMenu(menu, MF_STRING, CmdUpdate, "Rechercher une mise à jour");
            AppendMenu(menu, MF_SEPARATOR, 0, null);
            AppendMenu(menu, MF_STRING | (_paused ? MF_CHECKED : 0), CmdPause, "Suspendre le geste");
            AppendMenu(menu, MF_STRING | (LastConversion.CanUndo ? 0 : MF_GRAYED), CmdUndo, "Annuler la dernière conversion");
            AppendMenu(menu, MF_STRING, CmdHistory, "Conversions récentes…");
            AppendMenu(menu, MF_SEPARATOR, 0, null);
            AppendMenu(menu, MF_STRING, CmdUninstall, "Désinstaller Mandarine…");
            AppendMenu(menu, MF_STRING, CmdExit, "Quitter Mandarine");

            // Documented requirement: without this the menu would not close when clicking elsewhere.
            SetForegroundWindow(_window.Handle);
            GetCursorPos(out var cursor);
            var (anchorY, alignFlag) = AnchorAbove(cursor);
            var command = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | alignFlag, cursor.X, anchorY, _window.Handle, IntPtr.Zero);
            PostMessage(_window.Handle, WM_NULL, IntPtr.Zero, IntPtr.Zero);

            switch (command)
            {
                case CmdSettings: SettingsRequested?.Invoke(); break;
                case CmdNews: NewsRequested?.Invoke(); break;
                case CmdUndo: UndoRequested?.Invoke(); break;
                case CmdHistory: HistoryRequested?.Invoke(); break;
                case CmdUpdate: UpdateCheckRequested?.Invoke(); break;
                case CmdPause:
                    _paused = !_paused;
                    PauseChanged?.Invoke(_paused);
                    break;
                case CmdUninstall: UninstallRequested?.Invoke(); break;
                case CmdExit: ExitRequested?.Invoke(); break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    /// <summary>
    /// The menu should sit above what was clicked: above the "hidden icons" flyout when the icon is in it
    /// (so it never covers the other icons), else above the taskbar.
    /// </summary>
    private static (int Y, uint Align) AnchorAbove(Point cursor)
    {
        foreach (var className in new[] { "TopLevelWindowForOverflowXamlIsland", "NotifyIconOverflowWindow" })
        {
            var flyout = FindWindow(className, null);
            if (flyout != IntPtr.Zero && IsWindowVisible(flyout) && GetWindowRect(flyout, out var rect)
                && cursor.X >= rect.Left && cursor.X <= rect.Right && cursor.Y >= rect.Top && cursor.Y <= rect.Bottom)
            {
                return (rect.Top, TPM_BOTTOMALIGN);
            }
        }

        var monitor = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (GetMonitorInfo(MonitorFromPoint(cursor, 2 /* nearest */), ref monitor)
            && monitor.rcWork.Bottom < monitor.rcMonitor.Bottom && cursor.Y >= monitor.rcWork.Bottom)
        {
            return (monitor.rcWork.Bottom, TPM_BOTTOMALIGN);
        }

        return (cursor.Y, 0);
    }

    /// <summary>Lets native menus follow the system dark mode (undocumented uxtheme entry points; ignored if missing).</summary>
    private static void AllowDarkMenus()
    {
        try
        {
            SetPreferredAppMode(1); // AllowDark
            FlushMenuThemes();
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
        }
    }

    public void Dispose()
    {
        var data = NewData(0);
        Shell_NotifyIcon(NIM_DELETE, ref data);
        if (_icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
        }

        _window.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public Rect rcMonitor, rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID, uFlags, uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[]? small, uint count);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, nuint id, string? text);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr parameters);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    private static extern int SetPreferredAppMode(int mode);

    [DllImport("uxtheme.dll", EntryPoint = "#136")]
    private static extern void FlushMenuThemes();
}
