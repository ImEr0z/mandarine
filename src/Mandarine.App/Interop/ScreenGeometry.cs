using System.Runtime.InteropServices;
using static Mandarine.App.Interop.NativeMethods;

namespace Mandarine.App.Interop;

internal static class ScreenGeometry
{
    /// <summary>Work area (physical pixels, taskbar excluded) and DPI scale of the monitor under a point.</summary>
    public static (RECT WorkArea, double Scale) MonitorAt(POINT point)
    {
        var monitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);
        var scale = GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 ? dpiX / 96.0 : 1.0;
        return (info.rcWork, scale);
    }
}
