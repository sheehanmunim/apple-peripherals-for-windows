using System.Windows.Forms;
using MagicTrackpad.Interop;

namespace MagicTrackpad.Input;

public static class SystemActions
{
    public static bool IsNamedAction(string value) =>
        Normalize(value) is "brightnessdown" or "brightnessup";

    public static bool TryRun(string value)
    {
        return Normalize(value) switch
        {
            "brightnessdown" => DisplayBrightness.TryAdjust(-10),
            "brightnessup" => DisplayBrightness.TryAdjust(10),
            _ => false,
        };
    }

    private static string Normalize(string value) =>
        value.Trim().Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();
}

internal static class DisplayBrightness
{
    public static bool TryAdjust(int deltaPercent)
    {
        var changed = false;
        foreach (var screen in Screen.AllScreens)
        {
            changed |= TryAdjustScreen(screen, deltaPercent);
        }

        return changed;
    }

    private static bool TryAdjustScreen(Screen screen, int deltaPercent)
    {
        var point = new NativeMethods.Point
        {
            X = screen.Bounds.Left + screen.Bounds.Width / 2,
            Y = screen.Bounds.Top + screen.Bounds.Height / 2,
        };
        var monitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero ||
            !NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out var count) ||
            count == 0)
        {
            return false;
        }

        var physical = new NativeMethods.PhysicalMonitor[count];
        if (!NativeMethods.GetPhysicalMonitorsFromHMONITOR(monitor, count, physical))
        {
            return false;
        }

        try
        {
            var changed = false;
            foreach (var item in physical)
            {
                if (item.Handle == IntPtr.Zero)
                {
                    continue;
                }

                if (!NativeMethods.GetMonitorBrightness(item.Handle, out var min, out var current, out var max) || max <= min)
                {
                    continue;
                }

                var range = max - min;
                var step = Math.Max(1, (uint)Math.Round(range * Math.Abs(deltaPercent) / 100.0));
                var next = deltaPercent < 0
                    ? current <= min + step ? min : current - step
                    : current >= max - step ? max : current + step;

                if (NativeMethods.SetMonitorBrightness(item.Handle, next))
                {
                    changed = true;
                }
            }

            return changed;
        }
        finally
        {
            _ = NativeMethods.DestroyPhysicalMonitors(count, physical);
        }
    }
}
