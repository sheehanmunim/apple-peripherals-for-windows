using MagicTrackpad.Configuration;
using MagicTrackpad.Hid;
using MagicTrackpad.Input;

namespace MagicTrackpad.Gestures;

public sealed class GestureEngine
{
    private readonly IInputInjector injector;
    private GestureConfig config;
    private TouchSession? session;
    private bool lastClickDown;
    private string? activeButton;

    public GestureEngine(IInputInjector injector, GestureConfig config)
    {
        this.injector = injector;
        this.config = config;
    }

    public void UpdateConfig(GestureConfig nextConfig)
    {
        config = nextConfig;
        session = null;
    }

    public void ProcessFrame(TrackpadFrame frame, DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        var active = frame.ActiveTouches;
        var clickDown = (frame.Clicks & 1) != 0;

        HandlePhysicalClick(clickDown, active);
        HandleTouches(active, timestamp, clickDown);
        lastClickDown = clickDown;
    }

    private void HandlePhysicalClick(bool clickDown, IReadOnlyList<Touch> active)
    {
        if (clickDown == lastClickDown)
        {
            return;
        }

        if (clickDown)
        {
            activeButton = ButtonOrNone(config.SecondaryClickEnabled && active.Count >= 2
                ? config.MultiFingerPhysicalClickButton
                : config.PhysicalClickButton);
            if (activeButton != "none")
            {
                injector.ButtonDown(EffectiveButton(activeButton));
            }
        }
        else if (activeButton != null)
        {
            if (activeButton != "none")
            {
                injector.ButtonUp(EffectiveButton(activeButton));
            }
            activeButton = null;
        }
    }

    private void HandleTouches(IReadOnlyList<Touch> active, DateTimeOffset now, bool physicalClickActive)
    {
        var count = active.Count;
        var center = HidReportParser.Centroid(active);

        if (count == 0)
        {
            FinishSession(now, physicalClickActive);
            session = null;
            return;
        }

        if (session == null || session.Count != count)
        {
            session = new TouchSession
            {
                Count = count,
                StartedAt = now,
                StartCentroid = center,
                LastCentroid = center,
                LastDistance = TwoTouchDistance(active),
            };
            return;
        }

        var dx = center.X - session.LastCentroid.X;
        var dy = center.Y - session.LastCentroid.Y;
        var totalDx = center.X - session.StartCentroid.X;
        var totalDy = center.Y - session.StartCentroid.Y;
        session.MaxDistance = Math.Max(session.MaxDistance, Math.Sqrt(totalDx * totalDx + totalDy * totalDy));

        if (count == 1)
        {
            HandlePointer(dx, dy);
        }
        else if (count == 2)
        {
            HandleTwoFinger(active, dx, dy, session);
        }
        else if (count >= 3)
        {
            HandleSwipe(totalDx, totalDy, session);
        }

        session.LastCentroid = center;
    }

    private void FinishSession(DateTimeOffset now, bool physicalClickActive)
    {
        if (session == null || physicalClickActive || !config.TapToClick)
        {
            return;
        }

        if ((now - session.StartedAt).TotalSeconds > config.TapMaxSeconds || session.MaxDistance > config.TapMaxDistance)
        {
            return;
        }

        if (session.Count == 1)
        {
            ClickConfiguredButton(config.OneFingerTapButton);
        }
        else if (session.Count == 2 && config.SecondaryClickEnabled)
        {
            ClickConfiguredButton(config.TwoFingerTapButton);
        }
        else if (session.Count == 3 && config.ThreeFingerMiddleClick)
        {
            ClickConfiguredButton(config.ThreeFingerTapButton);
        }
    }

    private void HandlePointer(double dx, double dy)
    {
        if (!config.PointerEnabled)
        {
            return;
        }

        if (config.InvertPointerX) dx = -dx;
        if (config.InvertPointerY) dy = -dy;

        var moveX = (int)Math.Round(dx * config.PointerSensitivity);
        var moveY = (int)Math.Round(dy * config.PointerSensitivity);
        if (moveX != 0 || moveY != 0)
        {
            injector.MoveRelative(moveX, moveY);
        }
    }

    private void HandleTwoFinger(IReadOnlyList<Touch> active, double dx, double dy, TouchSession session)
    {
        var currentDistance = TwoTouchDistance(active);
        var pinchDelta = currentDistance != null && session.LastDistance != null
            ? currentDistance.Value - session.LastDistance.Value
            : 0;
        session.LastDistance = currentDistance;

        var didPinch = false;
        if (config.PinchZoomEnabled && Math.Abs(pinchDelta) > config.PinchThreshold)
        {
            didPinch = true;
            session.PinchAccumulator += pinchDelta * config.PinchSensitivity;
            var steps = (int)(session.PinchAccumulator / 120);
            if (steps != 0)
            {
                SendPinchWheel(steps * 120);
                session.PinchAccumulator -= steps * 120;
            }
        }

        if (didPinch || !config.ScrollEnabled)
        {
            return;
        }

        var direction = config.NaturalScroll ? -1 : 1;
        session.ScrollYAccumulator += dy * config.ScrollSensitivity * direction;
        var wheelY = (int)session.ScrollYAccumulator;
        if (wheelY != 0)
        {
            injector.Wheel(vertical: wheelY);
            session.ScrollYAccumulator -= wheelY;
        }

        if (!config.HorizontalScrollEnabled)
        {
            return;
        }

        session.ScrollXAccumulator += dx * config.ScrollSensitivity * -direction;
        var wheelX = (int)session.ScrollXAccumulator;
        if (wheelX != 0)
        {
            injector.Wheel(horizontal: wheelX);
            session.ScrollXAccumulator -= wheelX;
        }
    }

    private void HandleSwipe(double totalDx, double totalDy, TouchSession session)
    {
        if (!config.ThreeFingerSwipesEnabled || session.SwipeFired)
        {
            return;
        }

        if (Math.Abs(totalDx) > config.SwipeThreshold && Math.Abs(totalDx) > Math.Abs(totalDy))
        {
            SendConfiguredHotkey(SwipeHotkey(session.Count, horizontal: totalDx));
            session.SwipeFired = true;
            return;
        }

        if (Math.Abs(totalDy) > config.SwipeVerticalThreshold && Math.Abs(totalDy) > Math.Abs(totalDx))
        {
            SendConfiguredHotkey(SwipeHotkey(session.Count, vertical: totalDy));
            session.SwipeFired = true;
        }
    }

    private string SwipeHotkey(int count, double horizontal = 0, double vertical = 0)
    {
        var prefix = count >= 4 ? "four" : "three";
        return (prefix, horizontal, vertical) switch
        {
            ("four", > 0, _) => config.Hotkeys.FourFingerSwipeRight,
            ("four", < 0, _) => config.Hotkeys.FourFingerSwipeLeft,
            ("four", _, < 0) => config.Hotkeys.FourFingerSwipeUp,
            ("four", _, _) => config.Hotkeys.FourFingerSwipeDown,
            ("three", > 0, _) => config.Hotkeys.ThreeFingerSwipeRight,
            ("three", < 0, _) => config.Hotkeys.ThreeFingerSwipeLeft,
            ("three", _, < 0) => config.Hotkeys.ThreeFingerSwipeUp,
            _ => config.Hotkeys.ThreeFingerSwipeDown,
        };
    }

    private void SendConfiguredHotkey(string value)
    {
        var keys = Hotkeys.Parse(value);
        if (keys.Count > 0)
        {
            injector.Hotkey(keys);
        }
    }

    private void SendPinchWheel(int amount)
    {
        var modifiers = Hotkeys.Parse(config.PinchZoomModifier);
        if (modifiers.Count == 0)
        {
            injector.Wheel(vertical: amount);
            return;
        }

        injector.HotkeyDown(modifiers);
        try
        {
            injector.Wheel(vertical: amount);
        }
        finally
        {
            injector.HotkeyUp(modifiers.Reverse());
        }
    }

    private void ClickConfiguredButton(string button)
    {
        button = ButtonOrNone(button);
        if (button != "none")
        {
            injector.Click(EffectiveButton(button));
        }
    }

    private string EffectiveButton(string button)
    {
        return config.SwapLeftRightButtons
            ? button switch
            {
                "left" => "right",
                "right" => "left",
                _ => button,
            }
            : button;
    }

    private static string ButtonOrNone(string button) =>
        button.ToLowerInvariant() is "left" or "right" or "middle" ? button.ToLowerInvariant() : "none";

    private static double? TwoTouchDistance(IReadOnlyList<Touch> touches) =>
        touches.Count == 2 ? HidReportParser.Distance(touches[0], touches[1]) : null;
}

internal sealed class TouchSession
{
    public int Count { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public (double X, double Y) StartCentroid { get; init; }
    public (double X, double Y) LastCentroid { get; set; }
    public double MaxDistance { get; set; }
    public bool SwipeFired { get; set; }
    public double PinchAccumulator { get; set; }
    public double ScrollXAccumulator { get; set; }
    public double ScrollYAccumulator { get; set; }
    public double? LastDistance { get; set; }
}
