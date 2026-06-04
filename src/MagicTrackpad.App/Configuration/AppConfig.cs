using System.Text.Json;

namespace MagicTrackpad.Configuration;

public sealed class AppConfig
{
    public GestureConfig Gestures { get; set; } = new();
    public KeyboardConfig Keyboard { get; set; } = new();
    public bool EnableMultitouchOnStart { get; set; } = true;
    public double ReenableIntervalSeconds { get; set; } = 15.0;
    public bool LogRawReports { get; set; }
    public string RawLogPath { get; set; } = "logs/raw-reports.hex";
}

public sealed class GestureConfig
{
    public bool PointerEnabled { get; set; } = true;
    public double PointerSensitivity { get; set; } = 0.18;
    public bool InvertPointerX { get; set; }
    public bool InvertPointerY { get; set; }
    public bool ScrollEnabled { get; set; } = true;
    public bool NaturalScroll { get; set; } = true;
    public double ScrollSensitivity { get; set; } = 0.42;
    public bool HorizontalScrollEnabled { get; set; } = true;
    public bool TapToClick { get; set; } = true;
    public string OneFingerTapButton { get; set; } = "left";
    public string TwoFingerTapButton { get; set; } = "right";
    public string ThreeFingerTapButton { get; set; } = "middle";
    public string PhysicalClickButton { get; set; } = "left";
    public string MultiFingerPhysicalClickButton { get; set; } = "right";
    public double TapMaxSeconds { get; set; } = 0.18;
    public double TapMaxDistance { get; set; } = 95.0;
    public bool SecondaryClickEnabled { get; set; } = true;
    public bool ThreeFingerMiddleClick { get; set; } = true;
    public bool PinchZoomEnabled { get; set; } = true;
    public string PinchZoomModifier { get; set; } = "Ctrl";
    public double PinchSensitivity { get; set; } = 0.55;
    public double PinchThreshold { get; set; } = 14.0;
    public bool ThreeFingerSwipesEnabled { get; set; } = true;
    public double SwipeThreshold { get; set; } = 650.0;
    public double SwipeVerticalThreshold { get; set; } = 540.0;
    public HotkeyConfig Hotkeys { get; set; } = new();
}

public sealed class HotkeyConfig
{
    public string ThreeFingerSwipeLeft { get; set; } = "Win+Ctrl+Left";
    public string ThreeFingerSwipeRight { get; set; } = "Win+Ctrl+Right";
    public string ThreeFingerSwipeUp { get; set; } = "Win+Tab";
    public string ThreeFingerSwipeDown { get; set; } = "Win+D";
    public string FourFingerSwipeLeft { get; set; } = "Win+Ctrl+Left";
    public string FourFingerSwipeRight { get; set; } = "Win+Ctrl+Right";
    public string FourFingerSwipeUp { get; set; } = "Win+Tab";
    public string FourFingerSwipeDown { get; set; } = "Win+D";
}

public sealed class KeyboardConfig
{
    public bool Enabled { get; set; } = true;
    public bool OnlyWhenAppleKeyboardPresent { get; set; } = true;
    public string LeftCommand { get; set; } = "Ctrl";
    public string RightCommand { get; set; } = "Ctrl";
    public string LeftControl { get; set; } = "Win";
    public string RightControl { get; set; } = "Win";
    public string LeftOption { get; set; } = "Alt";
    public string RightOption { get; set; } = "Alt";
    public string CapsLock { get; set; } = "CapsLock";
    public string F13 { get; set; } = "none";
    public string F14 { get; set; } = "none";
    public string F15 { get; set; } = "none";
    public string F16 { get; set; } = "none";
    public string F17 { get; set; } = "none";
    public string F18 { get; set; } = "none";
    public string F19 { get; set; } = "none";
}

public static class ConfigStore
{
    public static string DefaultConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".magictrackpad-bridge.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            return new AppConfig();
        }

        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
        config.Gestures ??= new GestureConfig();
        config.Gestures.Hotkeys ??= new HotkeyConfig();
        config.Keyboard ??= new KeyboardConfig();
        return config;
    }

    public static void Save(string path, AppConfig config)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(path, json + Environment.NewLine);
    }
}
