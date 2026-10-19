using System.ComponentModel;
using System.Runtime.InteropServices;
using static Mandarine.App.Interop.NativeMethods;

namespace Mandarine.App.Interop;

/// <summary>
/// System-wide low-level mouse hook running on its own thread, so that a busy UI thread never makes
/// the cursor lag. Events are raised on the hook thread: handlers must be quick and marshal UI work.
/// </summary>
internal sealed class MouseHook : IDisposable
{
    private readonly LowLevelMouseProc _callback;
    private readonly ManualResetEventSlim _started = new();
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook;
    private Exception? _startError;

    public MouseHook()
    {
        // Kept in a field: the delegate must outlive the native hook.
        _callback = OnHook;
    }

    public event Action<POINT>? LeftButtonDown;

    public event Action<POINT>? LeftButtonUp;

    public event Action<POINT>? Moved;

    public bool IsRunning => _thread is not null;

    public void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        _started.Reset();
        _startError = null;
        _thread = new Thread(Run) { IsBackground = true, Name = "Mandarine mouse hook" };
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
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _callback, GetModuleHandle(null), 0);
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
                // Called for every mouse event system-wide: read the point in place, without marshaling.
                var msg = (int)message;
                if (msg is WM_LBUTTONDOWN or WM_LBUTTONUP or WM_MOUSEMOVE)
                {
                    var pt = new POINT(Marshal.ReadInt32(data), Marshal.ReadInt32(data, 4));
                    switch (msg)
                    {
                        case WM_LBUTTONDOWN:
                            LeftButtonDown?.Invoke(pt);
                            break;
                        case WM_LBUTTONUP:
                            LeftButtonUp?.Invoke(pt);
                            break;
                        default:
                            Moved?.Invoke(pt);
                            break;
                    }
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
