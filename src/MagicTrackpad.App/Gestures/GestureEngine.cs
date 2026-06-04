using MagicTrackpad.Configuration;
using MagicTrackpad.Hid;
using MagicTrackpad.Input;

namespace MagicTrackpad.Gestures;

public sealed class GestureEngine
{
    private readonly IInputInjector injector;
    private GestureConfig config;
    private TouchSession? session;
    private readonly object pendingTapLock = new();
    private PendingTap? pendingTap;
    private bool lastClickDown;
    private string? activeButton;
    private bool smartZoomed;

    public GestureEngine(IInputInjector injector, GestureConfig config)
    {
        this.injector = injector;
        this.config = config;
    }

    public void UpdateConfig(GestureConfig nextConfig)
    {
        config = nextConfig;
        session = null;
        CancelPendingTap();
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
                LastAngle = TwoTouchAngle(active),
                StartSpreadDistance = SpreadDistance(active),
                LastSpreadDistance = SpreadDistance(active),
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
            HandleTwoFinger(active, dx, dy, totalDx, totalDy, session);
        }
        else if (count >= 3)
        {
            if (count >= 4 && HandleFourOrFiveFingerPinch(active, session))
            {
                session.LastCentroid = center;
                return;
            }

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
            if (config.SmartZoomEnabled)
            {
                if (TryConsumePendingTap())
                {
                    SendSmartZoom();
                    return;
                }

                QueuePendingTap(config.TwoFingerTapButton);
                return;
            }

            ClickConfiguredButton(config.TwoFingerTapButton);
        }
        else if (session.Count == 3 && config.ThreeFingerMiddleClick)
        {
            if (!SendConfiguredHotkey(config.Hotkeys.ThreeFingerTap))
            {
                ClickConfiguredButton(config.ThreeFingerTapButton);
            }
        }
        else if (session.Count >= 4 && config.FourFingerTapEnabled)
        {
            SendConfiguredHotkey(config.Hotkeys.FourFingerTap);
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

    private void HandleTwoFinger(IReadOnlyList<Touch> active, double dx, double dy, double totalDx, double totalDy, TouchSession session)
    {
        if (HandleTwoFingerPageSwipe(totalDx, totalDy, session))
        {
            return;
        }

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

        var didRotate = HandleTwoFingerRotate(active, session);
        if (didPinch || didRotate || !config.ScrollEnabled)
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

    private bool HandleTwoFingerPageSwipe(double totalDx, double totalDy, TouchSession session)
    {
        if (!config.TwoFingerSwipePagesEnabled || session.SwipeFired)
        {
            return false;
        }

        if (Math.Abs(totalDx) <= config.TwoFingerSwipeThreshold || Math.Abs(totalDx) <= Math.Abs(totalDy) * 1.35)
        {
            return false;
        }

        SendConfiguredHotkey(totalDx < 0 ? config.Hotkeys.TwoFingerSwipeLeft : config.Hotkeys.TwoFingerSwipeRight);
        session.SwipeFired = true;
        return true;
    }

    private bool HandleTwoFingerRotate(IReadOnlyList<Touch> active, TouchSession session)
    {
        var currentAngle = TwoTouchAngle(active);
        if (!config.RotateEnabled || currentAngle == null || session.LastAngle == null)
        {
            session.LastAngle = currentAngle;
            return false;
        }

        var delta = NormalizeAngle(currentAngle.Value - session.LastAngle.Value);
        session.LastAngle = currentAngle;
        session.RotationAccumulator += delta;
        if (Math.Abs(session.RotationAccumulator) < config.RotateThresholdDegrees)
        {
            return false;
        }

        SendConfiguredHotkey(session.RotationAccumulator > 0
            ? config.Hotkeys.RotateClockwise
            : config.Hotkeys.RotateCounterClockwise);
        session.RotationAccumulator = 0;
        return true;
    }

    private bool HandleFourOrFiveFingerPinch(IReadOnlyList<Touch> active, TouchSession session)
    {
        var spread = SpreadDistance(active);
        if (spread == null)
        {
            return false;
        }

        session.LastSpreadDistance = spread;
        if (!config.FourFingerPinchEnabled || session.PinchFired || session.StartSpreadDistance == null)
        {
            return false;
        }

        var delta = spread.Value - session.StartSpreadDistance.Value;
        if (Math.Abs(delta) < config.FourFingerPinchThreshold)
        {
            return false;
        }

        SendConfiguredHotkey(delta < 0 ? config.Hotkeys.FourFingerPinchIn : config.Hotkeys.FourFingerSpread);
        session.PinchFired = true;
        session.SwipeFired = true;
        return true;
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

    private bool SendConfiguredHotkey(string value)
    {
        var keys = Hotkeys.Parse(value);
        if (keys.Count > 0)
        {
            injector.Hotkey(keys);
            return true;
        }

        return SystemActions.TryRun(value);
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

    private static double? TwoTouchAngle(IReadOnlyList<Touch> touches)
    {
        if (touches.Count != 2)
        {
            return null;
        }

        return Math.Atan2(touches[1].Y - touches[0].Y, touches[1].X - touches[0].X) * 180.0 / Math.PI;
    }

    private static double? SpreadDistance(IReadOnlyList<Touch> touches)
    {
        if (touches.Count < 4)
        {
            return null;
        }

        var center = HidReportParser.Centroid(touches);
        return touches.Average(touch =>
        {
            var dx = touch.X - center.X;
            var dy = touch.Y - center.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        });
    }

    private static double NormalizeAngle(double value)
    {
        while (value > 180) value -= 360;
        while (value < -180) value += 360;
        return value;
    }

    private void SendSmartZoom()
    {
        SendConfiguredHotkey(smartZoomed ? config.Hotkeys.SmartZoomOut : config.Hotkeys.SmartZoomIn);
        smartZoomed = !smartZoomed;
    }

    private void QueuePendingTap(string button)
    {
        CancelPendingTap();
        var delay = Math.Max(80, (int)Math.Round(config.SmartZoomDoubleTapSeconds * 1000));
        var tap = new PendingTap(button);
        tap.Timer = new System.Threading.Timer(_ => FlushPendingTap(tap), null, delay, Timeout.Infinite);
        lock (pendingTapLock)
        {
            pendingTap = tap;
        }
    }

    private bool TryConsumePendingTap()
    {
        lock (pendingTapLock)
        {
            if (pendingTap == null)
            {
                return false;
            }

            pendingTap.Timer?.Dispose();
            pendingTap = null;
            return true;
        }
    }

    private void FlushPendingTap(PendingTap tap)
    {
        lock (pendingTapLock)
        {
            if (!ReferenceEquals(pendingTap, tap))
            {
                return;
            }

            pendingTap = null;
        }

        tap.Timer?.Dispose();
        ClickConfiguredButton(tap.Button);
    }

    private void CancelPendingTap()
    {
        lock (pendingTapLock)
        {
            pendingTap?.Timer?.Dispose();
            pendingTap = null;
        }
    }
}

internal sealed class TouchSession
{
    public int Count { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public (double X, double Y) StartCentroid { get; init; }
    public (double X, double Y) LastCentroid { get; set; }
    public double MaxDistance { get; set; }
    public bool SwipeFired { get; set; }
    public bool PinchFired { get; set; }
    public double PinchAccumulator { get; set; }
    public double RotationAccumulator { get; set; }
    public double ScrollXAccumulator { get; set; }
    public double ScrollYAccumulator { get; set; }
    public double? LastDistance { get; set; }
    public double? LastAngle { get; set; }
    public double? StartSpreadDistance { get; init; }
    public double? LastSpreadDistance { get; set; }
}

internal sealed class PendingTap(string button)
{
    public string Button { get; } = button;
    public System.Threading.Timer? Timer { get; set; }
}
