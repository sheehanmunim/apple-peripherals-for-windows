using MagicTrackpad.Configuration;
using MagicTrackpad.Gestures;
using MagicTrackpad.Hid;
using MagicTrackpad.Input;

namespace MagicTrackpad.SelfTest;

internal static class SelfTests
{
    public static int Run()
    {
        try
        {
            TestHotkeys();
            TestReports();
            TestGestures();
            TestConfigRoundTrip();
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

    private static void TestGestures()
    {
        var injector = new DryRunInputInjector();
        var config = new GestureConfig { SwipeThreshold = 100 };
        config.Hotkeys.ThreeFingerSwipeLeft = "Alt+Left";
        var engine = new GestureEngine(injector, config);
        engine.ProcessFrame(Frame([Touch(1, 500, 0), Touch(2, 600, 0), Touch(3, 700, 0)]), DateTimeOffset.UnixEpoch);
        engine.ProcessFrame(Frame([Touch(1, 300, 0), Touch(2, 400, 0), Touch(3, 500, 0)]), DateTimeOffset.UnixEpoch.AddMilliseconds(100));
        Require(injector.Events.Contains("hotkey:18,37"));
    }

    private static void TestConfigRoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), "magic-trackpad-native-self-test.json");
        var config = new AppConfig();
        config.Gestures.PointerSensitivity = 0.73;
        ConfigStore.Save(path, config);
        var loaded = ConfigStore.Load(path);
        File.Delete(path);
        Require(Math.Abs(loaded.Gestures.PointerSensitivity - 0.73) < 0.001);
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

