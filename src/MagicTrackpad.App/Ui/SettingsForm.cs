using MagicTrackpad.Configuration;
using MagicTrackpad.Input;

namespace MagicTrackpad.Ui;

public sealed class SettingsForm : Form
{
    private readonly string configPath;
    private AppConfig config;
    private readonly Dictionary<string, Control> controlsByName = [];

    public SettingsForm(string configPath)
    {
        this.configPath = configPath;
        config = ConfigStore.Load(configPath);
        Text = "Apple Peripherals Settings";
        Width = 820;
        Height = 680;
        MinimumSize = new Size(720, 560);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);

        Build();
        LoadValues();
    }

    private void Build()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            RowCount = 3,
            ColumnCount = 1,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Text = "Apple Peripherals for Windows",
            Font = new Font("Segoe UI", 16F, FontStyle.Bold),
            AutoSize = true,
        }, 0, 0);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        root.Controls.Add(tabs, 0, 1);

        var pointer = AddTab(tabs, "Pointer");
        AddCheck(pointer, "PointerEnabled", "Enable one-finger pointer movement");
        AddNumber(pointer, "PointerSensitivity", "Pointer sensitivity", 0.05M, 1.0M, 0.01M);
        AddCheck(pointer, "InvertPointerX", "Reverse horizontal pointer direction");
        AddCheck(pointer, "InvertPointerY", "Reverse vertical pointer direction");

        var scroll = AddTab(tabs, "Scroll");
        AddCheck(scroll, "ScrollEnabled", "Enable two-finger scrolling");
        AddCheck(scroll, "NaturalScroll", "Natural scroll direction");
        AddNumber(scroll, "ScrollSensitivity", "Scroll sensitivity", 0.05M, 2.0M, 0.01M);
        AddCheck(scroll, "HorizontalScrollEnabled", "Enable horizontal scrolling");

        var clicks = AddTab(tabs, "Clicks");
        AddCheck(clicks, "TapToClick", "Tap to click");
        AddChoice(clicks, "OneFingerTapButton", "One-finger tap action", ["left", "right", "middle", "none"]);
        AddChoice(clicks, "TwoFingerTapButton", "Two-finger tap action", ["right", "left", "middle", "none"]);
        AddChoice(clicks, "ThreeFingerTapButton", "Three-finger tap action", ["middle", "left", "right", "none"]);
        AddNumber(clicks, "TapMaxSeconds", "Tap max time", 0.05M, 0.50M, 0.01M);
        AddNumber(clicks, "TapMaxDistance", "Tap movement tolerance", 10M, 250M, 1M);
        AddCheck(clicks, "SecondaryClickEnabled", "Two-finger secondary click");
        AddCheck(clicks, "ThreeFingerMiddleClick", "Three-finger middle click");
        AddChoice(clicks, "PhysicalClickButton", "Physical click action", ["left", "right", "middle", "none"]);
        AddChoice(clicks, "MultiFingerPhysicalClickButton", "Multi-finger physical click action", ["right", "left", "middle", "none"]);

        var gestures = AddTab(tabs, "Gestures");
        AddCheck(gestures, "PinchZoomEnabled", "Pinch to zoom");
        AddChoice(gestures, "PinchZoomModifier", "Pinch zoom modifier", ["Ctrl", "Alt", "Shift", "Win", "none"]);
        AddNumber(gestures, "PinchSensitivity", "Pinch sensitivity", 0.10M, 2.0M, 0.01M);
        AddNumber(gestures, "PinchThreshold", "Pinch activation threshold", 2M, 80M, 1M);
        AddCheck(gestures, "ThreeFingerSwipesEnabled", "Enable three- and four-finger swipe keybinds");
        AddNumber(gestures, "SwipeThreshold", "Horizontal swipe distance", 100M, 1400M, 10M);
        AddNumber(gestures, "SwipeVerticalThreshold", "Vertical swipe distance", 100M, 1400M, 10M);
        AddHotkey(gestures, "ThreeFingerSwipeLeft", "3 fingers left");
        AddHotkey(gestures, "ThreeFingerSwipeRight", "3 fingers right");
        AddHotkey(gestures, "ThreeFingerSwipeUp", "3 fingers up");
        AddHotkey(gestures, "ThreeFingerSwipeDown", "3 fingers down");
        AddHotkey(gestures, "FourFingerSwipeLeft", "4 fingers left");
        AddHotkey(gestures, "FourFingerSwipeRight", "4 fingers right");
        AddHotkey(gestures, "FourFingerSwipeUp", "4 fingers up");
        AddHotkey(gestures, "FourFingerSwipeDown", "4 fingers down");

        var keyboard = AddTab(tabs, "Keyboard");
        var keyActions = new[] { "unchanged", "Ctrl", "Alt", "Win", "Shift", "Esc", "CapsLock", "none" };
        AddCheck(keyboard, "KeyboardEnabled", "Enable Apple keyboard support");
        AddCheck(keyboard, "KeyboardOnlyWhenAppleKeyboardPresent", "Only apply while an Apple keyboard is connected");
        AddChoice(keyboard, "KeyboardLeftCommand", "Left Command action", keyActions);
        AddChoice(keyboard, "KeyboardRightCommand", "Right Command action", keyActions);
        AddChoice(keyboard, "KeyboardLeftControl", "Left Control action", keyActions);
        AddChoice(keyboard, "KeyboardRightControl", "Right Control action", keyActions);
        AddChoice(keyboard, "KeyboardLeftOption", "Left Option action", keyActions);
        AddChoice(keyboard, "KeyboardRightOption", "Right Option action", keyActions);
        AddChoice(keyboard, "KeyboardCapsLock", "Caps Lock action", keyActions);
        AddHotkey(keyboard, "KeyboardF13", "F13 keybind");
        AddHotkey(keyboard, "KeyboardF14", "F14 keybind");
        AddHotkey(keyboard, "KeyboardF15", "F15 keybind");
        AddHotkey(keyboard, "KeyboardF16", "F16 keybind");
        AddHotkey(keyboard, "KeyboardF17", "F17 keybind");
        AddHotkey(keyboard, "KeyboardF18", "F18 keybind");
        AddHotkey(keyboard, "KeyboardF19", "F19 keybind");

        var service = AddTab(tabs, "Service");
        AddCheck(service, "EnableMultitouchOnStart", "Enable multitouch automatically");
        AddNumber(service, "ReenableIntervalSeconds", "Bluetooth reconnect refresh interval", 3M, 120M, 1M);
        AddCheck(service, "LogRawReports", "Log raw HID reports");
        AddText(service, "RawLogPath", "Raw log path");
        AddButton(service, "Run Bridge Now", () => StartBridge("--bridge"));
        AddButton(service, "Run 5 Second Dry Test", () => StartBridge("--bridge --dry-run --seconds 5"));
        AddButton(service, "Open Config Folder", OpenConfigFolder);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
        root.Controls.Add(buttons, 0, 2);
        buttons.Controls.Add(new Button { Text = "Save", Width = 100 });
        buttons.Controls[0].Click += (_, _) => Save();
        buttons.Controls.Add(new Button { Text = "Save and Run", Width = 120 });
        buttons.Controls[1].Click += (_, _) => { if (Save(false)) StartBridge("--bridge"); };
        buttons.Controls.Add(new Button { Text = "Reset", Width = 100 });
        buttons.Controls[2].Click += (_, _) => { config = new AppConfig(); LoadValues(); };
    }

    private static FlowLayoutPanel AddTab(TabControl tabs, string title)
    {
        var page = new TabPage(title);
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            AutoScroll = true,
            WrapContents = false,
            Padding = new Padding(12),
        };
        page.Controls.Add(panel);
        tabs.TabPages.Add(page);
        return panel;
    }

    private void AddCheck(FlowLayoutPanel parent, string name, string text)
    {
        var box = new CheckBox { Text = text, Width = 680, AutoSize = true };
        controlsByName[name] = box;
        parent.Controls.Add(box);
    }

    private void AddNumber(FlowLayoutPanel parent, string name, string text, decimal min, decimal max, decimal step)
    {
        var row = Row(text);
        var number = new NumericUpDown { Minimum = min, Maximum = max, Increment = step, DecimalPlaces = step < 1 ? 2 : 0, Width = 120 };
        controlsByName[name] = number;
        row.Controls.Add(number);
        parent.Controls.Add(row);
    }

    private void AddChoice(FlowLayoutPanel parent, string name, string text, string[] choices)
    {
        var row = Row(text);
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        combo.Items.AddRange(choices.Cast<object>().ToArray());
        controlsByName[name] = combo;
        row.Controls.Add(combo);
        parent.Controls.Add(row);
    }

    private void AddHotkey(FlowLayoutPanel parent, string name, string text)
    {
        var row = Row(text);
        var textBox = new TextBox { Width = 190 };
        controlsByName[name] = textBox;
        row.Controls.Add(textBox);
        var record = new Button { Text = "Record", Width = 80 };
        record.Click += (_, _) => RecordHotkey(textBox);
        row.Controls.Add(record);
        parent.Controls.Add(row);
    }

    private void AddText(FlowLayoutPanel parent, string name, string text)
    {
        var row = Row(text);
        var textBox = new TextBox { Width = 360 };
        controlsByName[name] = textBox;
        row.Controls.Add(textBox);
        parent.Controls.Add(row);
    }

    private static void AddButton(FlowLayoutPanel parent, string text, Action action)
    {
        var button = new Button { Text = text, Width = 180, Height = 30 };
        button.Click += (_, _) => action();
        parent.Controls.Add(button);
    }

    private static FlowLayoutPanel Row(string label)
    {
        var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, Width = 700, Height = 32 };
        row.Controls.Add(new Label { Text = label, Width = 280, TextAlign = ContentAlignment.MiddleLeft });
        return row;
    }

    private void LoadValues()
    {
        Set("PointerEnabled", config.Gestures.PointerEnabled);
        Set("PointerSensitivity", config.Gestures.PointerSensitivity);
        Set("InvertPointerX", config.Gestures.InvertPointerX);
        Set("InvertPointerY", config.Gestures.InvertPointerY);
        Set("ScrollEnabled", config.Gestures.ScrollEnabled);
        Set("NaturalScroll", config.Gestures.NaturalScroll);
        Set("ScrollSensitivity", config.Gestures.ScrollSensitivity);
        Set("HorizontalScrollEnabled", config.Gestures.HorizontalScrollEnabled);
        Set("TapToClick", config.Gestures.TapToClick);
        Set("OneFingerTapButton", config.Gestures.OneFingerTapButton);
        Set("TwoFingerTapButton", config.Gestures.TwoFingerTapButton);
        Set("ThreeFingerTapButton", config.Gestures.ThreeFingerTapButton);
        Set("TapMaxSeconds", config.Gestures.TapMaxSeconds);
        Set("TapMaxDistance", config.Gestures.TapMaxDistance);
        Set("SecondaryClickEnabled", config.Gestures.SecondaryClickEnabled);
        Set("ThreeFingerMiddleClick", config.Gestures.ThreeFingerMiddleClick);
        Set("PhysicalClickButton", config.Gestures.PhysicalClickButton);
        Set("MultiFingerPhysicalClickButton", config.Gestures.MultiFingerPhysicalClickButton);
        Set("PinchZoomEnabled", config.Gestures.PinchZoomEnabled);
        Set("PinchZoomModifier", config.Gestures.PinchZoomModifier);
        Set("PinchSensitivity", config.Gestures.PinchSensitivity);
        Set("PinchThreshold", config.Gestures.PinchThreshold);
        Set("ThreeFingerSwipesEnabled", config.Gestures.ThreeFingerSwipesEnabled);
        Set("SwipeThreshold", config.Gestures.SwipeThreshold);
        Set("SwipeVerticalThreshold", config.Gestures.SwipeVerticalThreshold);
        Set("ThreeFingerSwipeLeft", config.Gestures.Hotkeys.ThreeFingerSwipeLeft);
        Set("ThreeFingerSwipeRight", config.Gestures.Hotkeys.ThreeFingerSwipeRight);
        Set("ThreeFingerSwipeUp", config.Gestures.Hotkeys.ThreeFingerSwipeUp);
        Set("ThreeFingerSwipeDown", config.Gestures.Hotkeys.ThreeFingerSwipeDown);
        Set("FourFingerSwipeLeft", config.Gestures.Hotkeys.FourFingerSwipeLeft);
        Set("FourFingerSwipeRight", config.Gestures.Hotkeys.FourFingerSwipeRight);
        Set("FourFingerSwipeUp", config.Gestures.Hotkeys.FourFingerSwipeUp);
        Set("FourFingerSwipeDown", config.Gestures.Hotkeys.FourFingerSwipeDown);
        Set("KeyboardEnabled", config.Keyboard.Enabled);
        Set("KeyboardOnlyWhenAppleKeyboardPresent", config.Keyboard.OnlyWhenAppleKeyboardPresent);
        Set("KeyboardLeftCommand", config.Keyboard.LeftCommand);
        Set("KeyboardRightCommand", config.Keyboard.RightCommand);
        Set("KeyboardLeftControl", config.Keyboard.LeftControl);
        Set("KeyboardRightControl", config.Keyboard.RightControl);
        Set("KeyboardLeftOption", config.Keyboard.LeftOption);
        Set("KeyboardRightOption", config.Keyboard.RightOption);
        Set("KeyboardCapsLock", config.Keyboard.CapsLock);
        Set("KeyboardF13", config.Keyboard.F13);
        Set("KeyboardF14", config.Keyboard.F14);
        Set("KeyboardF15", config.Keyboard.F15);
        Set("KeyboardF16", config.Keyboard.F16);
        Set("KeyboardF17", config.Keyboard.F17);
        Set("KeyboardF18", config.Keyboard.F18);
        Set("KeyboardF19", config.Keyboard.F19);
        Set("EnableMultitouchOnStart", config.EnableMultitouchOnStart);
        Set("ReenableIntervalSeconds", config.ReenableIntervalSeconds);
        Set("LogRawReports", config.LogRawReports);
        Set("RawLogPath", config.RawLogPath);
    }

    private bool Save(bool showMessage = true)
    {
        try
        {
            ReadValues();
            ValidateHotkeys();
            ConfigStore.Save(configPath, config);
            if (showMessage)
            {
                MessageBox.Show(this, "Settings saved.", "Apple Peripherals", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Apple Peripherals", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void ReadValues()
    {
        config.Gestures.PointerEnabled = GetBool("PointerEnabled");
        config.Gestures.PointerSensitivity = GetDouble("PointerSensitivity");
        config.Gestures.InvertPointerX = GetBool("InvertPointerX");
        config.Gestures.InvertPointerY = GetBool("InvertPointerY");
        config.Gestures.ScrollEnabled = GetBool("ScrollEnabled");
        config.Gestures.NaturalScroll = GetBool("NaturalScroll");
        config.Gestures.ScrollSensitivity = GetDouble("ScrollSensitivity");
        config.Gestures.HorizontalScrollEnabled = GetBool("HorizontalScrollEnabled");
        config.Gestures.TapToClick = GetBool("TapToClick");
        config.Gestures.OneFingerTapButton = GetText("OneFingerTapButton");
        config.Gestures.TwoFingerTapButton = GetText("TwoFingerTapButton");
        config.Gestures.ThreeFingerTapButton = GetText("ThreeFingerTapButton");
        config.Gestures.TapMaxSeconds = GetDouble("TapMaxSeconds");
        config.Gestures.TapMaxDistance = GetDouble("TapMaxDistance");
        config.Gestures.SecondaryClickEnabled = GetBool("SecondaryClickEnabled");
        config.Gestures.ThreeFingerMiddleClick = GetBool("ThreeFingerMiddleClick");
        config.Gestures.PhysicalClickButton = GetText("PhysicalClickButton");
        config.Gestures.MultiFingerPhysicalClickButton = GetText("MultiFingerPhysicalClickButton");
        config.Gestures.PinchZoomEnabled = GetBool("PinchZoomEnabled");
        config.Gestures.PinchZoomModifier = GetText("PinchZoomModifier");
        config.Gestures.PinchSensitivity = GetDouble("PinchSensitivity");
        config.Gestures.PinchThreshold = GetDouble("PinchThreshold");
        config.Gestures.ThreeFingerSwipesEnabled = GetBool("ThreeFingerSwipesEnabled");
        config.Gestures.SwipeThreshold = GetDouble("SwipeThreshold");
        config.Gestures.SwipeVerticalThreshold = GetDouble("SwipeVerticalThreshold");
        config.Gestures.Hotkeys.ThreeFingerSwipeLeft = Hotkeys.Normalize(GetText("ThreeFingerSwipeLeft"));
        config.Gestures.Hotkeys.ThreeFingerSwipeRight = Hotkeys.Normalize(GetText("ThreeFingerSwipeRight"));
        config.Gestures.Hotkeys.ThreeFingerSwipeUp = Hotkeys.Normalize(GetText("ThreeFingerSwipeUp"));
        config.Gestures.Hotkeys.ThreeFingerSwipeDown = Hotkeys.Normalize(GetText("ThreeFingerSwipeDown"));
        config.Gestures.Hotkeys.FourFingerSwipeLeft = Hotkeys.Normalize(GetText("FourFingerSwipeLeft"));
        config.Gestures.Hotkeys.FourFingerSwipeRight = Hotkeys.Normalize(GetText("FourFingerSwipeRight"));
        config.Gestures.Hotkeys.FourFingerSwipeUp = Hotkeys.Normalize(GetText("FourFingerSwipeUp"));
        config.Gestures.Hotkeys.FourFingerSwipeDown = Hotkeys.Normalize(GetText("FourFingerSwipeDown"));
        config.Keyboard.Enabled = GetBool("KeyboardEnabled");
        config.Keyboard.OnlyWhenAppleKeyboardPresent = GetBool("KeyboardOnlyWhenAppleKeyboardPresent");
        config.Keyboard.LeftCommand = GetText("KeyboardLeftCommand");
        config.Keyboard.RightCommand = GetText("KeyboardRightCommand");
        config.Keyboard.LeftControl = GetText("KeyboardLeftControl");
        config.Keyboard.RightControl = GetText("KeyboardRightControl");
        config.Keyboard.LeftOption = GetText("KeyboardLeftOption");
        config.Keyboard.RightOption = GetText("KeyboardRightOption");
        config.Keyboard.CapsLock = GetText("KeyboardCapsLock");
        config.Keyboard.F13 = Hotkeys.Normalize(GetText("KeyboardF13"));
        config.Keyboard.F14 = Hotkeys.Normalize(GetText("KeyboardF14"));
        config.Keyboard.F15 = Hotkeys.Normalize(GetText("KeyboardF15"));
        config.Keyboard.F16 = Hotkeys.Normalize(GetText("KeyboardF16"));
        config.Keyboard.F17 = Hotkeys.Normalize(GetText("KeyboardF17"));
        config.Keyboard.F18 = Hotkeys.Normalize(GetText("KeyboardF18"));
        config.Keyboard.F19 = Hotkeys.Normalize(GetText("KeyboardF19"));
        config.EnableMultitouchOnStart = GetBool("EnableMultitouchOnStart");
        config.ReenableIntervalSeconds = GetDouble("ReenableIntervalSeconds");
        config.LogRawReports = GetBool("LogRawReports");
        config.RawLogPath = GetText("RawLogPath");
    }

    private void ValidateHotkeys()
    {
        foreach (var value in new[]
        {
            config.Gestures.PinchZoomModifier,
            config.Gestures.Hotkeys.ThreeFingerSwipeLeft,
            config.Gestures.Hotkeys.ThreeFingerSwipeRight,
            config.Gestures.Hotkeys.ThreeFingerSwipeUp,
            config.Gestures.Hotkeys.ThreeFingerSwipeDown,
            config.Gestures.Hotkeys.FourFingerSwipeLeft,
            config.Gestures.Hotkeys.FourFingerSwipeRight,
            config.Gestures.Hotkeys.FourFingerSwipeUp,
            config.Gestures.Hotkeys.FourFingerSwipeDown,
            config.Keyboard.F13,
            config.Keyboard.F14,
            config.Keyboard.F15,
            config.Keyboard.F16,
            config.Keyboard.F17,
            config.Keyboard.F18,
            config.Keyboard.F19,
        })
        {
            Hotkeys.Parse(value);
        }
    }

    private void RecordHotkey(TextBox textBox)
    {
        using var dialog = new Form
        {
            Text = "Record keybind",
            Width = 360,
            Height = 130,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            KeyPreview = true,
        };
        dialog.Controls.Add(new Label { Text = "Press the key combination.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter });
        dialog.KeyDown += (_, e) =>
        {
            var parts = new List<string>();
            if (e.Control) parts.Add("Ctrl");
            if (e.Shift) parts.Add("Shift");
            if (e.Alt) parts.Add("Alt");
            if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu)
            {
                return;
            }
            parts.Add(KeyName(e.KeyCode));
            textBox.Text = Hotkeys.Normalize(string.Join("+", parts));
            dialog.Close();
        };
        dialog.ShowDialog(this);
    }

    private static string KeyName(Keys key) => key switch
    {
        Keys.Left => "Left",
        Keys.Right => "Right",
        Keys.Up => "Up",
        Keys.Down => "Down",
        Keys.LWin or Keys.RWin => "Win",
        _ => key.ToString(),
    };

    private void StartBridge(string arguments)
    {
        var exe = Application.ExecutablePath;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = exe,
            Arguments = $"{arguments} --config \"{configPath}\"",
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
        });
    }

    private void OpenConfigFolder()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(configPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        System.Diagnostics.Process.Start("explorer.exe", Path.GetDirectoryName(configPath)!);
    }

    private void Set(string name, bool value) => ((CheckBox)controlsByName[name]).Checked = value;
    private void Set(string name, double value) => ((NumericUpDown)controlsByName[name]).Value = (decimal)value;
    private void Set(string name, string value)
    {
        if (controlsByName[name] is ComboBox combo)
        {
            combo.SelectedItem = combo.Items.Contains(value) ? value : combo.Items[0];
        }
        else if (controlsByName[name] is TextBox text)
        {
            text.Text = value;
        }
    }

    private bool GetBool(string name) => ((CheckBox)controlsByName[name]).Checked;
    private double GetDouble(string name) => (double)((NumericUpDown)controlsByName[name]).Value;
    private string GetText(string name) => controlsByName[name] switch
    {
        ComboBox combo => combo.SelectedItem?.ToString() ?? "",
        TextBox text => text.Text,
        _ => "",
    };
}
