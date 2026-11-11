using System.Runtime.InteropServices;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Mandarine.App.Services;

/// <summary>
/// Notification area icon and its menu. The menu is a native Windows popup (like other tray apps use):
/// a WinForms ContextMenuStrip takes focus in a way that makes the "hidden icons" flyout close.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint MF_STRING = 0x0000;
    private const uint MF_CHECKED = 0x0008;
    private const uint MF_SEPARATOR = 0x0800;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_BOTTOMALIGN = 0x0020;
    private const uint WM_NULL = 0x0000;

    private const int CmdSettings = 1;
    private const int CmdUpdate = 2;
    private const int CmdPause = 3;
    private const int CmdUninstall = 4;
    private const int CmdExit = 5;

    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.NativeWindow _owner = new();
    private bool _paused;

    public TrayIcon(bool paused)
    {
        _paused = paused;
        _owner.CreateHandle(new Forms.CreateParams());
        AllowDarkMenus();

        using var stream = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Mandarine;component/Assets/mandarine.ico")).Stream;
        _icon = new Forms.NotifyIcon
        {
            Icon = new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize),
            Text = "Mandarine",
            Visible = true,
        };
        // Left click opens the same menu as right click.
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button is Forms.MouseButtons.Left or Forms.MouseButtons.Right)
            {
                ShowMenu();
            }
        };
    }

    public event Action? SettingsRequested;

    public event Action? UpdateCheckRequested;

    public event Action<bool>? PauseChanged;

    public event Action? ExitRequested;

    public event Action? UninstallRequested;

    public void SetPaused(bool paused) => _paused = paused;

    public void Notify(string title, string text) => _icon.ShowBalloonTip(6000, title, text, Forms.ToolTipIcon.None);

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        try
        {
            AppendMenu(menu, MF_STRING, CmdSettings, "Paramètres…");
            AppendMenu(menu, MF_STRING, CmdUpdate, "Rechercher une mise à jour");
            AppendMenu(menu, MF_SEPARATOR, 0, null);
            AppendMenu(menu, MF_STRING | (_paused ? MF_CHECKED : 0), CmdPause, "Suspendre le geste");
            AppendMenu(menu, MF_SEPARATOR, 0, null);
            AppendMenu(menu, MF_STRING, CmdUninstall, "Désinstaller Mandarine…");
            AppendMenu(menu, MF_STRING, CmdExit, "Quitter Mandarine");

            // Documented requirement: without this the menu would not close when clicking elsewhere.
            SetForegroundWindow(_owner.Handle);
            var cursor = Forms.Cursor.Position;
            var (anchorY, alignFlag) = AnchorAbove(cursor);
            var command = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | alignFlag, cursor.X, anchorY, _owner.Handle, IntPtr.Zero);
            PostMessage(_owner.Handle, WM_NULL, IntPtr.Zero, IntPtr.Zero);

            switch (command)
            {
                case CmdSettings: SettingsRequested?.Invoke(); break;
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
    private static (int Y, uint Align) AnchorAbove(Drawing.Point cursor)
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

        var screen = Forms.Screen.FromPoint(cursor);
        return screen.WorkingArea.Bottom < screen.Bounds.Bottom && cursor.Y >= screen.WorkingArea.Bottom
            ? (screen.WorkingArea.Bottom, TPM_BOTTOMALIGN)
            : (cursor.Y, 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out Rect rect);

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
        _icon.Visible = false;
        _icon.Icon?.Dispose();
        _icon.Dispose();
        _owner.DestroyHandle();
    }

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
