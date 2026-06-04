using MagicTrackpad.Configuration;
using MagicTrackpad.Gestures;
using MagicTrackpad.Hid;
using MagicTrackpad.Input;
using MagicTrackpad.Keyboard;

namespace MagicTrackpad.SelfTest;

internal static class SelfTests
{
    public static int Run()
    {
        try
        {
            TestHotkeys();
            TestKeyboardDetection();
            TestReports();
            TestBatteryReports();
            TestGestures();
            TestConfigRoundTrip();
            TestConfigReloadSignal();
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    private static void TestHotkeys()
    {
        var parsed = Hotkeys.Parse("Win+Ctrl+Left");
        Require(parsed.SequenceEqual(new ushort[] { 0x5B, 0x11, 0x25 }));
        Require(Hotkeys.Normalize("windows-control-left") == "Win+Ctrl+Left");
        Require(Hotkeys.Normalize("ctrl-plus") == "Ctrl+Plus");
        Require(Hotkeys.Parse("MediaPlayPause").SequenceEqual(new ushort[] { 0xB3 }));
        Require(Hotkeys.Parse("BrightnessDown").Count == 0);
        Require(Hotkeys.Parse("unchanged").Count == 0);
    }

    private static void TestReports()
    {
        var report = new byte[] { 0x31, 1, 0, 0 }.Concat(EncodeTrackpad2Touch(-100, 200, 7)).ToArray();
        var frames = HidReportParser.ParseReports(report);
        Require(frames.Count == 1);
        Require(frames[0].ActiveTouches[0].TrackingId == 7);
        Require(frames[0].ActiveTouches[0].X == -100);
        Require(frames[0].ActiveTouches[0].Y == 200);
    }

    private static void TestBatteryReports()
    {
        Require(DeviceBattery.TryParseBatteryReport([0x90, 0x02, 66], out var percent, out var charging));
        Require(percent == 66);
        Require(charging == true);
        Require(DeviceBattery.TryParseBatteryReport([0x47, 44], out percent, out charging));
        Require(percent == 44);
    }

    private static void TestKeyboardDetection()
    {
        var name = @"HID\{00001124-0000-1000-8000-00805F9B34FB}_VID&0001004C_PID&0320&COL01\9&346F8316&0&0000";
        var parsed = DeviceCatalog.ParseVidPid(name);
        var device = new HidDeviceInfo(IntPtr.Zero, name, parsed.VendorId, parsed.ProductId, null, null, null);
        Require(parsed.VendorId == DeviceCatalog.AppleBluetoothVendorId);
        Require(parsed.ProductId == DeviceCatalog.MagicKeyboardBluetooth);
        Require(device.IsAppleKeyboard);
        Require(!device.IsAppleMagicTrackpad);
    }

    private static void TestGestures()
    {
        var injector = new DryRunInputInjector();
        var config = new GestureConfig { SwipeThreshold = 100 };
        config.Hotkeys.ThreeFingerSwipeLeft = "Alt+Left";
        var engine = new GestureEngine(injector, config);
        engine.ProcessFrame(Frame([Touch(1, 500, 0), Touch(2, 600, 0), Touch(3, 700, 0)]), DateTimeOffset.UnixEpoch);
        engine.ProcessFrame(Frame([Touch(1, 300, 0), Touch(2, 400, 0), Touch(3, 500, 0)]), DateTimeOffset.UnixEpoch.AddMilliseconds(100));
        Require(injector.Events.Contains("hotkey:18,37"));

        injector = new DryRunInputInjector();
        config = new GestureConfig { TwoFingerSwipeThreshold = 100 };
        config.Hotkeys.TwoFingerSwipeLeft = "Alt+Left";
        engine = new GestureEngine(injector, config);
        engine.ProcessFrame(Frame([Touch(1, 500, 0), Touch(2, 650, 0)]), DateTimeOffset.UnixEpoch);
        engine.ProcessFrame(Frame([Touch(1, 250, 0), Touch(2, 400, 0)]), DateTimeOffset.UnixEpoch.AddMilliseconds(100));
        Require(injector.Events.Contains("hotkey:18,37"));

        injector = new DryRunInputInjector();
        config = new GestureConfig { FourFingerPinchThreshold = 40 };
        config.Hotkeys.FourFingerSpread = "Win+D";
        engine = new GestureEngine(injector, config);
        engine.ProcessFrame(Frame([Touch(1, -50, -50), Touch(2, 50, -50), Touch(3, -50, 50), Touch(4, 50, 50)]), DateTimeOffset.UnixEpoch);
        engine.ProcessFrame(Frame([Touch(1, -120, -120), Touch(2, 120, -120), Touch(3, -120, 120), Touch(4, 120, 120)]), DateTimeOffset.UnixEpoch.AddMilliseconds(100));
        Require(injector.Events.Contains("hotkey:91,68"));
    }

    private static void TestConfigRoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), "magic-trackpad-native-self-test.json");
        var config = new AppConfig();
        Require(config.Keyboard.FnGlobe == "Ctrl");
        Require(KeyboardRemapper.IsHeldModifierAction(config.Keyboard.FnGlobe));
        Require(KeyboardRemapper.ModifierTarget(config.Keyboard.FnGlobe, 0x86).SequenceEqual(new ushort[] { 0xA2 }));
        Require(KeyboardRemapper.ModifierTarget(config.Keyboard.FnGlobe, 0x87).SequenceEqual(new ushort[] { 0xA3 }));
        config.Gestures.PointerSensitivity = 0.73;
        config.Gestures.SwapLeftRightButtons = true;
        config.Keyboard.FKeyMode = "custom";
        config.Keyboard.F1 = "Win+H";
        config.Keyboard.F13 = "Ctrl+Alt+Delete";
        ConfigStore.Save(path, config);
        var loaded = ConfigStore.Load(path);
        File.Delete(path);
        Require(Math.Abs(loaded.Gestures.PointerSensitivity - 0.73) < 0.001);
        Require(loaded.Gestures.SwapLeftRightButtons);
        Require(loaded.Keyboard.FKeyMode == "custom");
        Require(loaded.Keyboard.F1 == "Win+H");
        Require(loaded.Keyboard.F13 == "Ctrl+Alt+Delete");

        File.WriteAllText(path, """{"keyboard":{"fn_globe":"Win+Period"}}""");
        loaded = ConfigStore.Load(path);
        File.Delete(path);
        Require(loaded.Keyboard.FnGlobe == "Ctrl");
    }

    private static void TestConfigReloadSignal()
    {
        var path = Path.Combine(Path.GetTempPath(), $"magic-trackpad-native-signal-{Guid.NewGuid():N}.json");
        using var signal = ConfigReloadSignal.Create(path);
        ConfigReloadSignal.Notify(path);
        Require(signal.WaitOne(1000));
    }

    private static TrackpadFrame Frame(IReadOnlyList<Touch> touches) => new(0x31, 0, touches, []);

    private static Touch Touch(int id, int x, int y) => new(id, x, y, 10, 0, 20, 18, 50, true);

    private static byte[] EncodeTrackpad2Touch(int x, int y, int trackingId)
    {
        var rawX = x & 0x1FFF;
        var rawY = -y & 0x1FFF;
        return
        [
            (byte)(rawX & 0xFF),
            (byte)(((rawX >> 8) & 0x1F) | ((rawY & 0x07) << 5)),
            (byte)((rawY >> 3) & 0xFF),
            (byte)(((rawY >> 11) & 0x03) | 0x80),
            20,
            18,
            11,
            64,
            (byte)(trackingId & 0x0F),
        ];
    }

    private static void Require(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Self-test failed.");
        }
    }
}
