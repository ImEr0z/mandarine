using Mandarine.App.Services;
using static Mandarine.App.Interop.NativeMethods;

namespace Mandarine.App.Gestures;

/// <summary>
/// Recognizes "drag a file while holding a modifier" (or shaking it) from raw mouse events.
/// Runs on the mouse hook thread: every method must stay cheap.
/// </summary>
internal sealed class DragGestureDetector
{
    private const int DragThreshold = 8;

    private static readonly HashSet<string> ExplorerWindowClasses =
        ["CabinetWClass", "ExploreWClass", "Progman", "WorkerW"];

    private readonly ShakeTracker _shake = new();
    private volatile TriggerMode _mode = TriggerMode.Shift;
    private volatile bool _explorerOnly = true;
    private bool _buttonDown;
    private bool _eligible;
    private bool _triggered;
    private POINT _origin;
    private POINT _lastPoint;
    private volatile bool _moved;
    private readonly object _gate = new();
    private readonly Timer _poll;

    public DragGestureDetector() => _poll = new Timer(_ => PollModifier(), null, Timeout.Infinite, Timeout.Infinite);

    /// <summary>Raised once per drag when the gesture is recognized, with the cursor position.</summary>
    public event Action<POINT>? Triggered;

    /// <summary>Raised when the mouse button is released after a recognized gesture.</summary>
    public event Action? Released;

    public void Configure(TriggerMode mode, bool explorerOnly)
    {
        _mode = mode;
        _explorerOnly = explorerOnly;
    }

    public void OnButtonDown(POINT point)
    {
        lock (_gate)
        {
            _triggered = false;
        }

        _buttonDown = true;
        _moved = false;
        _origin = point;
        _lastPoint = point;
        _eligible = _mode != TriggerMode.Disabled && IsEligibleSource(point);
        _shake.Reset(point);
    }

    public void OnMove(POINT point)
    {
        if (!_buttonDown || _triggered || !_eligible)
        {
            return;
        }

        if (Math.Abs(point.X - _origin.X) < DragThreshold && Math.Abs(point.Y - _origin.Y) < DragThreshold)
        {
            return;
        }

        _lastPoint = point;
        if (!_moved)
        {
            // A plain click never gets here: the poll that catches a key pressed mid-drag only runs for real drags.
            _moved = true;
            if (_mode is TriggerMode.Shift or TriggerMode.ShiftTap or TriggerMode.Alt or TriggerMode.Ctrl)
            {
                _poll.Change(30, 30);
            }
        }

        var recognized = _mode == TriggerMode.Shake ? _shake.Add(point, Environment.TickCount64) : ModifierHeld();
        if (recognized)
        {
            TryTrigger(point);
        }
    }

    public void OnButtonUp(POINT point)
    {
        _buttonDown = false;
        _poll.Change(Timeout.Infinite, Timeout.Infinite);
        bool wasTriggered;
        lock (_gate)
        {
            wasTriggered = _triggered;
            _triggered = false;
        }

        if (wasTriggered)
        {
            Released?.Invoke();
        }
    }

    private bool ModifierHeld() => _mode switch
    {
        TriggerMode.Shift or TriggerMode.ShiftTap => IsKeyDown(VK_SHIFT),
        TriggerMode.Alt => IsKeyDown(VK_MENU),
        TriggerMode.Ctrl => IsKeyDown(VK_CONTROL),
        _ => false,
    };

    private void TryTrigger(POINT point)
    {
        lock (_gate)
        {
            if (_triggered || !_buttonDown)
            {
                return;
            }

            _triggered = true;
        }

        Triggered?.Invoke(point);
    }

    /// <summary>Lets a key pressed in the middle of a drag work even when the mouse is held still.</summary>
    private void PollModifier()
    {
        if (_buttonDown && _eligible && _moved && !_triggered && ModifierHeld())
        {
            TryTrigger(_lastPoint);
        }
    }

    private bool IsEligibleSource(POINT point)
    {
        var window = WindowFromPoint(point);
        if (window == IntPtr.Zero)
        {
            return false;
        }

        var root = GetAncestor(window, GA_ROOT);
        GetWindowThreadProcessId(root, out var processId);
        if (processId == Environment.ProcessId)
        {
            return false;
        }

        return !_explorerOnly || ExplorerWindowClasses.Contains(ClassNameOf(root));
    }

    /// <summary>Counts quick horizontal direction changes: three within 700 ms is a shake.</summary>
    private sealed class ShakeTracker
    {
        private const int MinimumStroke = 30;
        private const int RequiredReversals = 3;
        private const long WindowMilliseconds = 700;

        private readonly Queue<long> _reversals = new();
        private int _anchorX;
        private int _direction;

        public void Reset(POINT origin)
        {
            _anchorX = origin.X;
            _direction = 0;
            _reversals.Clear();
        }

        public bool Add(POINT point, long now)
        {
            var delta = point.X - _anchorX;
            if (Math.Abs(delta) < MinimumStroke)
            {
                return false;
            }

            var direction = Math.Sign(delta);
            if (_direction != 0 && direction != _direction)
            {
                _reversals.Enqueue(now);
            }

            _direction = direction;
            _anchorX = point.X;
            while (_reversals.Count > 0 && now - _reversals.Peek() > WindowMilliseconds)
            {
                _reversals.Dequeue();
            }

            return _reversals.Count >= RequiredReversals;
        }
    }
}
