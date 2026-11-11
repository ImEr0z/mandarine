namespace Mandarine.App.Gestures;

/// <summary>
/// Recognizes a lone tap on Shift: pressed and released quickly with no other key or mouse click in between
/// (so Shift+letter and Shift+click never count). Runs on the keyboard hook thread.
/// </summary>
internal sealed class ShiftTapDetector
{
    private const int VK_SHIFT = 0x10;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const long MaxTapMilliseconds = 400;

    private long _pressedAt; // 0 = not armed
    private volatile bool _mouseDown; // Shift during a drag is the drag gesture's job

    /// <summary>Raised when Shift was tapped on its own.</summary>
    public event Action? Tapped;

    public void OnKeyDown(int virtualKey)
    {
        if (virtualKey is VK_SHIFT or VK_LSHIFT or VK_RSHIFT)
        {
            // Auto-repeat keeps sending key-downs: only the first one arms.
            if (_pressedAt == 0 && !_mouseDown)
            {
                _pressedAt = Environment.TickCount64;
            }
        }
        else
        {
            _pressedAt = 0;
        }
    }

    public void OnKeyUp(int virtualKey)
    {
        if (virtualKey is not (VK_SHIFT or VK_LSHIFT or VK_RSHIFT))
        {
            return;
        }

        var pressedAt = _pressedAt;
        _pressedAt = 0;
        if (pressedAt != 0 && Environment.TickCount64 - pressedAt <= MaxTapMilliseconds)
        {
            Tapped?.Invoke();
        }
    }

    /// <summary>A mouse click while Shift is down is a selection (Shift+click), not a tap.</summary>
    public void OnMouseDown()
    {
        _mouseDown = true;
        _pressedAt = 0;
    }

    public void OnMouseUp() => _mouseDown = false;
}
