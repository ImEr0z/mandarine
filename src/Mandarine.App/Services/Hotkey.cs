using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace Mandarine.App.Services;

/// <summary>
/// System-wide shortcut registered with RegisterHotKey: Windows only tells Mandarine when that exact
/// combination is pressed. No keyboard hook, nothing else typed is ever seen.
/// </summary>
internal sealed class Hotkey : IDisposable
{
    public const uint Alt = 0x0001;
    public const uint Control = 0x0002;
    public const uint Shift = 0x0004;
    public const uint Win = 0x0008;
    private const uint NoRepeat = 0x4000;
    private const int WmHotkey = 0x0312;
    private const int Id = 0x4D41; // "MA"

    private readonly HwndSource _window;
    private bool _registered;

    public Hotkey()
    {
        // Message-only window: receives WM_HOTKEY, never shown.
        _window = new HwndSource(new HwndSourceParameters("Mandarine hotkey") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
        _window.AddHook(WndProc);
    }

    public event Action? Pressed;

    /// <summary>Replaces the current shortcut. False when another application already owns the combination.</summary>
    public bool Set(uint modifiers, int key)
    {
        Clear();
        if (key == 0)
        {
            return true;
        }

        _registered = RegisterHotKey(_window.Handle, Id, modifiers | NoRepeat, (uint)key);
        return _registered;
    }

    public void Clear()
    {
        if (_registered)
        {
            UnregisterHotKey(_window.Handle, Id);
            _registered = false;
        }
    }

    /// <summary>"Ctrl + Maj + Espace".</summary>
    public static string Describe(uint modifiers, int key)
    {
        if (key == 0)
        {
            return "Aucun";
        }

        var parts = new List<string>();
        if ((modifiers & Control) != 0) parts.Add("Ctrl");
        if ((modifiers & Alt) != 0) parts.Add("Alt");
        if ((modifiers & Shift) != 0) parts.Add("Maj");
        if ((modifiers & Win) != 0) parts.Add("Win");
        parts.Add(KeyName(KeyInterop.KeyFromVirtualKey(key)));
        return string.Join(" + ", parts);
    }

    private static string KeyName(Key key) => key switch
    {
        Key.Space => "Espace",
        Key.Enter => "Entrée",
        Key.Tab => "Tab",
        Key.Insert => "Inser",
        Key.Delete => "Suppr",
        Key.Home => "Début",
        Key.End => "Fin",
        Key.PageUp => "Page préc.",
        Key.PageDown => "Page suiv.",
        Key.Pause => "Pause",
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Pavé " + (int)(key - Key.NumPad0),
        Key.Oem3 => "²",
        _ => key.ToString(),
    };

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == Id)
        {
            handled = true;
            Pressed?.Invoke();
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Clear();
        _window.RemoveHook(WndProc);
        _window.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
