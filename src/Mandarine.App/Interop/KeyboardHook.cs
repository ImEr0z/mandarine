using System.ComponentModel;
using System.Runtime.InteropServices;
using static Mandarine.App.Interop.NativeMethods;

namespace Mandarine.App.Interop;

/// <summary>
/// System-wide low-level keyboard hook on its own thread. It only reports key codes (never text) and
/// never swallows a key. Events are raised on the hook thread: handlers must be quick.
/// </summary>
internal sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private readonly LowLevelMouseProc _callback;
    private readonly ManualResetEventSlim _started = new();
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook;
    private Exception? _startError;

    public KeyboardHook() => _callback = OnHook;

    public event Action<int>? KeyDown;

    public event Action<int>? KeyUp;

    public void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        _started.Reset();
        _startError = null;
        _thread = new Thread(Run) { IsBackground = true, Name = "Mandarine keyboard hook" };
        _thread.Start();
        _started.Wait();

        if (_startError is not null)
        {
            _thread = null;
            throw _startError;
        }
    }

    public void Stop()
    {
        if (_thread is null)
        {
            return;
        }

        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(1));
        _thread = null;
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _callback, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
        {
            _startError = new Win32Exception(Marshal.GetLastWin32Error());
            _started.Set();
            return;
        }

        _started.Set();
        while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }

        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr OnHook(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            try
            {
                var virtualKey = Marshal.ReadInt32(data); // KBDLLHOOKSTRUCT.vkCode
                switch ((int)message)
                {
                    case WM_KEYDOWN or WM_SYSKEYDOWN:
                        KeyDown?.Invoke(virtualKey);
                        break;
                    case WM_KEYUP or WM_SYSKEYUP:
                        KeyUp?.Invoke(virtualKey);
                        break;
                }
            }
            catch (Exception)
            {
                // Never let an exception cross the native boundary: it would freeze input system-wide.
            }
        }

        return CallNextHookEx(_hook, code, message, data);
    }

    public void Dispose()
    {
        Stop();
        _started.Dispose();
    }
}
