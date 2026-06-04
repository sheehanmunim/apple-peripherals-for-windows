using System.Drawing.Drawing2D;
using MagicTrackpad.Configuration;
using MagicTrackpad.Hid;
using MagicTrackpad.Input;

namespace MagicTrackpad.Ui;

public sealed class SettingsForm : Form
{
    private const int LeftColumnWidth = 470;
    private const int MainColumnWidth = 490;
    private const int KeyboardLeftWidth = 640;
    private const int KeyboardRightWidth = 700;

    private readonly string configPath;
    private readonly Dictionary<string, Control> controlsByName = [];
    private AppConfig config;

    public SettingsForm(string configPath)
    {
        this.configPath = configPath;
        config = ConfigStore.Load(configPath);
        Text = "Apple Peripherals for Windows";
        Width = 1520;
        Height = 920;
        MinimumSize = new Size(1140, 720);
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        Font = new Font("Segoe UI", 10F);
        BackColor = Color.White;

        Build();
        LoadValues();
    }

    private void Build()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(8),
            BackColor = Color.White,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            DrawMode = TabDrawMode.OwnerDrawFixed,
            SizeMode = TabSizeMode.Fixed,
            ItemSize = new Size(285, 34),
            Padding = new Point(14, 4),
        };
        tabs.DrawItem += DrawDeviceTab;
        root.Controls.Add(tabs, 0, 0);

        foreach (var device in DeviceTabs())
        {
            var page = new TabPage(device.Title)
            {
                BackColor = Color.White,
                Padding = new Padding(12),
                AutoScroll = true,
                Tag = device,
            };
            if (device.Kind == DeviceKind.Trackpad)
            {
                BuildTrackpadPage(page, device);
            }
            else
            {
                BuildKeyboardPage(page, device);
            }
            tabs.TabPages.Add(page);
        }

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(0, 8, 0, 0),
        };
        root.Controls.Add(actions, 0, 1);
        AddActionButton(actions, "Save", () => Save());
        AddActionButton(actions, "Save and Run", () => { if (Save(false)) StartBridge(); }, 120);
        AddActionButton(actions, "Reset", () => { config = new AppConfig(); LoadValues(); }, 90);
        AddActionButton(actions, "Open Config", OpenConfigFolder, 110);
    }

    private void BuildTrackpadPage(TabPage page, DeviceTabInfo device)
    {
        var grid = new TableLayoutPanel
        {
            Width = LeftColumnWidth + MainColumnWidth + MainColumnWidth + 36,
            Height = 1240,
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.White,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LeftColumnWidth));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        page.Controls.Add(grid);

        var left = Column();
        var middle = Column();
        var right = Column();
        grid.Controls.Add(left, 0, 0);
        grid.Controls.Add(middle, 1, 0);
        grid.Controls.Add(right, 2, 0);

        BuildTrackpadInfo(left, device);
        BuildStatusGroup(left, "Trackpad Status", device, LeftColumnWidth);

        var one = Group(middle, "1 Finger Gestures", MainColumnWidth);
        AddCheck(one, "OneFingerTap", "Tap to left click");
        AddCheck(one, "IgnorePhysicalClick", "Ignore physical click");
        AddSlider(one, "PointerSensitivity", "Sense:", 5, 100, "Slow", "Fast");
        AddCheck(one, "InvertPointerX", "Reverse horizontal pointer direction");
        AddCheck(one, "InvertPointerY", "Reverse vertical pointer direction");

        var two = Group(middle, "2 Finger Gestures", MainColumnWidth);
        AddCheck(two, "TwoFingerTap", "Tap to right click");
        AddCheck(two, "ScrollEnabled", "Scrolling");
        AddCheck(two, "NoHorizontalScroll", "No horizontal scrolling", 28);
        AddCheck(two, "NaturalScroll", "Inverse both scroll directions", 28);
        AddCheck(two, "PinchZoomEnabled", "Pinch to zoom", 28);
        AddSlider(two, "ScrollSensitivity", "Speed:", 5, 200, "Slow", "Fast");
        AddSlider(two, "PinchSensitivity", "Pinch:", 10, 200, "Light", "Strong");

        var three = Group(middle, "3 Finger Gestures", MainColumnWidth);
        AddCheck(three, "ThreeFingerTap", "Tap to middle click");
        AddCheck(three, "ThreeFingerSwipesEnabled", "Enable 3 and 4 finger swipe keybinds");
        AddHotkey(three, "ThreeFingerSwipeLeft", "3 finger left");
        AddHotkey(three, "ThreeFingerSwipeRight", "3 finger right");
        AddHotkey(three, "ThreeFingerSwipeUp", "3 finger up");
        AddHotkey(three, "ThreeFingerSwipeDown", "3 finger down");

        var four = Group(right, "4 Finger Gestures", MainColumnWidth);
        AddHotkey(four, "FourFingerSwipeLeft", "4 finger left");
        AddHotkey(four, "FourFingerSwipeRight", "4 finger right");
        AddHotkey(four, "FourFingerSwipeUp", "4 finger up");
        AddHotkey(four, "FourFingerSwipeDown", "4 finger down");
        AddSlider(four, "SwipeThreshold", "Left/right sense:", 100, 1400, "Short", "Long");
        AddSlider(four, "SwipeVerticalThreshold", "Up/down sense:", 100, 1400, "Short", "Long");

        var mouse = Group(right, "Mouse Options", MainColumnWidth);
        AddCheck(mouse, "PointerEnabled", "Move pointer");
        AddCheck(mouse, "SwapLeftRightButtons", "Swap left/right clicks");
        AddChoice(mouse, "PhysicalClickButton", "Physical click:", ButtonChoices(), 150);
        AddChoice(mouse, "MultiFingerPhysicalClickButton", "Multi-finger click:", ButtonChoices(), 150);

        var click = Group(right, "Click Options", MainColumnWidth);
        AddSlider(click, "TapMaxSeconds", "Tap time:", 5, 50, "Quick", "Delayed");
        AddSlider(click, "TapMaxDistance", "Tap distance:", 10, 250, "Tight", "Loose");
        AddChoice(click, "OneFingerTapButton", "1 finger tap:", ButtonChoices(), 150);
        AddChoice(click, "TwoFingerTapButton", "2 finger tap:", ButtonChoices(), 150);
        AddChoice(click, "ThreeFingerTapButton", "3 finger tap:", ButtonChoices(), 150);
    }

    private void BuildKeyboardPage(TabPage page, DeviceTabInfo device)
    {
        var grid = new TableLayoutPanel
        {
            Width = KeyboardLeftWidth + KeyboardRightWidth + 36,
            Height = 920,
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.White,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, KeyboardLeftWidth));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.Controls.Add(grid);

        var left = Column();
        var right = Column();
        grid.Controls.Add(left, 0, 0);
        grid.Controls.Add(right, 1, 0);

        BuildKeyboardInfo(left, device);
        BuildStatusGroup(left, "Keyboard Status", device, KeyboardLeftWidth);

        var fkeys = Group(right, "F-Key Mappings", KeyboardRightWidth);
        AddChoice(fkeys, "FKeyMode", "On F1 .. F12 keys pressed:", ["standard", "custom"], 360);
        AddButtonRow(fkeys, "Show all F-key mappings", ShowAllKeyMappings);

        var other = Group(right, "Other Key Mappings", KeyboardRightWidth);
        AddCheck(other, "KeyboardEnabled", "Enable Apple keyboard support");
        AddCheck(other, "KeyboardOnlyWhenAppleKeyboardPresent", "Only apply while an Apple keyboard is connected");
        AddCheck(other, "KeyboardSwapExchangedKeys", "Swap exchanged modifier keys");

        var modifiers = Group(right, "Modifier Key Mappings", KeyboardRightWidth);
        var actions = KeyActionChoices();
        AddChoice(modifiers, "KeyboardCapsLock", "Caps Lock:", actions, 160);
        AddChoice(modifiers, "KeyboardLeftControl", "Left Control:", actions, 160);
        AddChoice(modifiers, "KeyboardLeftOption", "Left Option:", actions, 160);
        AddChoice(modifiers, "KeyboardLeftCommand", "Left Command:", actions, 160);
        AddChoice(modifiers, "KeyboardRightCommand", "Right Command:", actions, 160);
        AddChoice(modifiers, "KeyboardRightOption", "Right Option:", actions, 160);
        AddChoice(modifiers, "KeyboardRightControl", "Right Control:", actions, 160);

        var extra = Group(right, "Extended Keybinds", KeyboardRightWidth);
        AddHotkey(extra, "KeyboardF13", "F13");
        AddHotkey(extra, "KeyboardF14", "F14");
        AddHotkey(extra, "KeyboardF15", "F15");
        AddHotkey(extra, "KeyboardF16", "F16");
        AddHotkey(extra, "KeyboardF17", "F17");
        AddHotkey(extra, "KeyboardF18", "F18");
        AddHotkey(extra, "KeyboardF19", "F19");
    }

    private void BuildTrackpadInfo(FlowLayoutPanel column, DeviceTabInfo device)
    {
        var group = Group(column, "Device Info", LeftColumnWidth);
        AddDeviceHeader(group, device);
        group.Controls.Add(new TrackpadPreview
        {
            Width = LeftColumnWidth - 48,
            Height = 245,
            Margin = new Padding(8, 18, 8, 12),
        });
        var row = Row(width: ContentWidth(group) - 8);
        row.Controls.Add(new Label { Text = "Raw touch log", Width = 150, TextAlign = ContentAlignment.MiddleLeft });
        var log = new CheckBox { Width = 24 };
        controlsByName["LogRawReports"] = log;
        row.Controls.Add(log);
        row.Controls.Add(new Label { Text = "Touch report capture", Width = 210, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DimGray });
        group.Controls.Add(row);
        AddProgress(group, device.Connected ? 100 : 0, device.Connected ? "Connected. Battery unavailable." : "Device not detected.");
    }

    private void BuildKeyboardInfo(FlowLayoutPanel column, DeviceTabInfo device)
    {
        var group = Group(column, "Device Info", KeyboardLeftWidth);
        AddDeviceHeader(group, device);
        group.Controls.Add(new KeyboardPreview
        {
            Width = KeyboardLeftWidth - 48,
            Height = 260,
            Margin = new Padding(8, 16, 8, 16),
        });
        AddProgress(group, device.Connected ? 100 : 0, device.Connected ? "Connected. Battery unavailable." : "Device not detected.");
    }

    private void BuildStatusGroup(FlowLayoutPanel column, string title, DeviceTabInfo device, int width)
    {
        var group = Group(column, title, width);
        group.Controls.Add(new Label
        {
            Text = device.Connected ? "Ready" : "Not detected",
            Width = ContentWidth(group) - 16,
            Height = 28,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = device.Connected ? Color.FromArgb(0, 122, 204) : Color.DimGray,
        });
        group.Controls.Add(new Label
        {
            Text = device.Device?.Name ?? "Open the bridge or reconnect the device to refresh this page.",
            Width = ContentWidth(group) - 16,
            AutoSize = false,
            Height = 54,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.DimGray,
        });
    }

    private void AddDeviceHeader(FlowLayoutPanel group, DeviceTabInfo device)
    {
        var row = Row(width: ContentWidth(group) - 8);
        row.Controls.Add(new Label
        {
            Text = "Model",
            Width = 70,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.DimGray,
        });
        row.Controls.Add(new Label
        {
            Text = device.Device?.ProductName ?? device.Title,
            Width = 225,
            TextAlign = ContentAlignment.MiddleLeft,
        });
        row.Controls.Add(new Label
        {
            Text = device.Device?.IsBluetooth == true ? "Bluetooth" : "USB / HID",
            Width = 105,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.DimGray,
        });
        group.Controls.Add(row);
    }

    private static void AddProgress(FlowLayoutPanel group, int value, string text)
    {
        var bar = new ProgressBar
        {
            Width = ContentWidth(group) - 8,
            Height = 28,
            Value = Math.Clamp(value, 0, 100),
            Margin = new Padding(8, 18, 8, 6),
        };
        group.Controls.Add(bar);
        group.Controls.Add(new Label
        {
            Text = text,
            Width = ContentWidth(group) - 8,
            Height = 44,
            TextAlign = ContentAlignment.MiddleCenter,
        });
    }

    private static FlowLayoutPanel Column() => new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = false,
        BackColor = Color.White,
        Padding = new Padding(4),
    };

    private static FlowLayoutPanel Group(FlowLayoutPanel column, string title, int width)
    {
        var box = new GroupBox
        {
            Text = $"  {title}  ",
            Width = width - 18,
            MinimumSize = new Size(width - 18, 0),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 28, 12, 12),
            Margin = new Padding(4, 4, 8, 12),
        };
        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Width = width - 44,
            MinimumSize = new Size(width - 44, 0),
            Location = new Point(14, 30),
            Margin = new Padding(0),
            Padding = new Padding(0),
            Tag = width - 44,
        };
        box.Controls.Add(panel);
        column.Controls.Add(box);
        return panel;
    }

    private static FlowLayoutPanel Row(int height = 34, int width = 650) => new()
    {
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = false,
        Width = width,
        Height = height,
        Margin = new Padding(0, 2, 0, 2),
    };

    private static int ContentWidth(Control parent) =>
        parent.Tag is int width ? width : Math.Max(260, parent.Width);

    private void AddCheck(FlowLayoutPanel parent, string name, string text, int indent = 0)
    {
        var contentWidth = ContentWidth(parent);
        var row = Row(width: contentWidth - 8);
        row.Padding = new Padding(indent, 0, 0, 0);
        var box = new CheckBox
        {
            Text = text,
            Width = contentWidth - indent - 12,
            Height = 30,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        controlsByName[name] = box;
        row.Controls.Add(box);
        parent.Controls.Add(row);
    }

    private void AddChoice(FlowLayoutPanel parent, string name, string label, string[] choices, int comboWidth)
    {
        var contentWidth = ContentWidth(parent);
        var row = Row(width: contentWidth - 8);
        row.Controls.Add(new Label { Text = label, Width = 205, TextAlign = ContentAlignment.MiddleLeft });
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = Math.Min(comboWidth, Math.Max(120, contentWidth - 235)),
        };
        combo.Items.AddRange(choices.Cast<object>().ToArray());
        controlsByName[name] = combo;
        row.Controls.Add(combo);
        parent.Controls.Add(row);
    }

    private void AddHotkey(FlowLayoutPanel parent, string name, string label)
    {
        var contentWidth = ContentWidth(parent);
        var row = Row(width: contentWidth - 8);
        row.Controls.Add(new Label { Text = label, Width = 145, TextAlign = ContentAlignment.MiddleLeft });
        var textBox = new TextBox { Width = Math.Max(145, contentWidth - 245) };
        controlsByName[name] = textBox;
        row.Controls.Add(textBox);
        var record = new Button { Text = "Record", Width = 76, Height = 28 };
        record.Click += (_, _) => RecordHotkey(textBox);
        row.Controls.Add(record);
        parent.Controls.Add(row);
    }

    private void AddSlider(FlowLayoutPanel parent, string name, string label, int min, int max, string leftText, string rightText)
    {
        var contentWidth = ContentWidth(parent);
        var row = Row(58, contentWidth - 8);
        row.Controls.Add(new Label { Text = label, Width = 118, TextAlign = ContentAlignment.MiddleLeft });
        var sliderWidth = Math.Max(220, contentWidth - 145);
        var sliderPanel = new TableLayoutPanel
        {
            Width = sliderWidth,
            Height = 56,
            RowCount = 2,
            ColumnCount = 1,
            Margin = new Padding(0),
        };
        sliderPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        sliderPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        var track = new TrackBar
        {
            Minimum = min,
            Maximum = max,
            TickFrequency = Math.Max(1, (max - min) / 8),
            Width = sliderWidth - 10,
            Height = 32,
            Margin = new Padding(0),
        };
        controlsByName[name] = track;
        sliderPanel.Controls.Add(track, 0, 0);
        var legend = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
        legend.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        legend.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        legend.Controls.Add(new Label { Text = leftText, Dock = DockStyle.Fill, ForeColor = Color.DimGray }, 0, 0);
        legend.Controls.Add(new Label { Text = rightText, Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopRight, ForeColor = Color.DimGray }, 1, 0);
        sliderPanel.Controls.Add(legend, 0, 1);
        row.Controls.Add(sliderPanel);
        parent.Controls.Add(row);
    }

    private void AddButtonRow(FlowLayoutPanel parent, string text, Action action)
    {
        var row = Row(40, ContentWidth(parent) - 8);
        row.Controls.Add(new Label { Text = "", Width = 205 });
        var button = new Button { Text = text, Width = 250, Height = 30 };
        button.Click += (_, _) => action();
        row.Controls.Add(button);
        parent.Controls.Add(row);
    }

    private static void AddActionButton(FlowLayoutPanel parent, string text, Action action, int width = 90)
    {
        var button = new Button { Text = text, Width = width, Height = 32 };
        button.Click += (_, _) => action();
        parent.Controls.Add(button);
    }

    private void LoadValues()
    {
        Set("PointerEnabled", config.Gestures.PointerEnabled);
        Set("PointerSensitivity", Scale(config.Gestures.PointerSensitivity, 100));
        Set("InvertPointerX", config.Gestures.InvertPointerX);
        Set("InvertPointerY", config.Gestures.InvertPointerY);
        Set("ScrollEnabled", config.Gestures.ScrollEnabled);
        Set("NaturalScroll", config.Gestures.NaturalScroll);
        Set("ScrollSensitivity", Scale(config.Gestures.ScrollSensitivity, 100));
        Set("NoHorizontalScroll", !config.Gestures.HorizontalScrollEnabled);
        Set("OneFingerTap", config.Gestures.TapToClick && config.Gestures.OneFingerTapButton != "none");
        Set("TwoFingerTap", config.Gestures.TapToClick && config.Gestures.TwoFingerTapButton != "none");
        Set("ThreeFingerTap", config.Gestures.ThreeFingerMiddleClick && config.Gestures.ThreeFingerTapButton != "none");
        Set("IgnorePhysicalClick", config.Gestures.PhysicalClickButton == "none");
        Set("PhysicalClickButton", config.Gestures.PhysicalClickButton);
        Set("MultiFingerPhysicalClickButton", config.Gestures.MultiFingerPhysicalClickButton);
        Set("OneFingerTapButton", config.Gestures.OneFingerTapButton);
        Set("TwoFingerTapButton", config.Gestures.TwoFingerTapButton);
        Set("ThreeFingerTapButton", config.Gestures.ThreeFingerTapButton);
        Set("TapMaxSeconds", Scale(config.Gestures.TapMaxSeconds, 100));
        Set("TapMaxDistance", (int)Math.Round(config.Gestures.TapMaxDistance));
        Set("PinchZoomEnabled", config.Gestures.PinchZoomEnabled);
        Set("PinchSensitivity", Scale(config.Gestures.PinchSensitivity, 100));
        Set("ThreeFingerSwipesEnabled", config.Gestures.ThreeFingerSwipesEnabled);
        Set("SwipeThreshold", (int)Math.Round(config.Gestures.SwipeThreshold));
        Set("SwipeVerticalThreshold", (int)Math.Round(config.Gestures.SwipeVerticalThreshold));
        Set("SwapLeftRightButtons", config.Gestures.SwapLeftRightButtons);
        Set("ThreeFingerSwipeLeft", config.Gestures.Hotkeys.ThreeFingerSwipeLeft);
        Set("ThreeFingerSwipeRight", config.Gestures.Hotkeys.ThreeFingerSwipeRight);
        Set("ThreeFingerSwipeUp", config.Gestures.Hotkeys.ThreeFingerSwipeUp);
        Set("ThreeFingerSwipeDown", config.Gestures.Hotkeys.ThreeFingerSwipeDown);
        Set("FourFingerSwipeLeft", config.Gestures.Hotkeys.FourFingerSwipeLeft);
        Set("FourFingerSwipeRight", config.Gestures.Hotkeys.FourFingerSwipeRight);
        Set("FourFingerSwipeUp", config.Gestures.Hotkeys.FourFingerSwipeUp);
        Set("FourFingerSwipeDown", config.Gestures.Hotkeys.FourFingerSwipeDown);
        Set("LogRawReports", config.LogRawReports);

        Set("KeyboardEnabled", config.Keyboard.Enabled);
        Set("KeyboardOnlyWhenAppleKeyboardPresent", config.Keyboard.OnlyWhenAppleKeyboardPresent);
        Set("KeyboardSwapExchangedKeys", config.Keyboard.SwapExchangedKeys);
        Set("FKeyMode", config.Keyboard.FKeyMode);
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
        config.Gestures.PointerSensitivity = GetInt("PointerSensitivity") / 100.0;
        config.Gestures.InvertPointerX = GetBool("InvertPointerX");
        config.Gestures.InvertPointerY = GetBool("InvertPointerY");
        config.Gestures.ScrollEnabled = GetBool("ScrollEnabled");
        config.Gestures.NaturalScroll = GetBool("NaturalScroll");
        config.Gestures.ScrollSensitivity = GetInt("ScrollSensitivity") / 100.0;
        config.Gestures.HorizontalScrollEnabled = !GetBool("NoHorizontalScroll");
        config.Gestures.OneFingerTapButton = GetBool("OneFingerTap") ? GetText("OneFingerTapButton") : "none";
        config.Gestures.TwoFingerTapButton = GetBool("TwoFingerTap") ? GetText("TwoFingerTapButton") : "none";
        config.Gestures.ThreeFingerTapButton = GetBool("ThreeFingerTap") ? GetText("ThreeFingerTapButton") : "none";
        config.Gestures.TapToClick = config.Gestures.OneFingerTapButton != "none" ||
            config.Gestures.TwoFingerTapButton != "none" ||
            config.Gestures.ThreeFingerTapButton != "none";
        config.Gestures.ThreeFingerMiddleClick = GetBool("ThreeFingerTap");
        config.Gestures.PhysicalClickButton = GetBool("IgnorePhysicalClick") ? "none" : GetText("PhysicalClickButton");
        config.Gestures.MultiFingerPhysicalClickButton = GetText("MultiFingerPhysicalClickButton");
        config.Gestures.TapMaxSeconds = GetInt("TapMaxSeconds") / 100.0;
        config.Gestures.TapMaxDistance = GetInt("TapMaxDistance");
        config.Gestures.PinchZoomEnabled = GetBool("PinchZoomEnabled");
        config.Gestures.PinchSensitivity = GetInt("PinchSensitivity") / 100.0;
        config.Gestures.ThreeFingerSwipesEnabled = GetBool("ThreeFingerSwipesEnabled");
        config.Gestures.SwipeThreshold = GetInt("SwipeThreshold");
        config.Gestures.SwipeVerticalThreshold = GetInt("SwipeVerticalThreshold");
        config.Gestures.SwapLeftRightButtons = GetBool("SwapLeftRightButtons");
        config.Gestures.Hotkeys.ThreeFingerSwipeLeft = Hotkeys.Normalize(GetText("ThreeFingerSwipeLeft"));
        config.Gestures.Hotkeys.ThreeFingerSwipeRight = Hotkeys.Normalize(GetText("ThreeFingerSwipeRight"));
        config.Gestures.Hotkeys.ThreeFingerSwipeUp = Hotkeys.Normalize(GetText("ThreeFingerSwipeUp"));
        config.Gestures.Hotkeys.ThreeFingerSwipeDown = Hotkeys.Normalize(GetText("ThreeFingerSwipeDown"));
        config.Gestures.Hotkeys.FourFingerSwipeLeft = Hotkeys.Normalize(GetText("FourFingerSwipeLeft"));
        config.Gestures.Hotkeys.FourFingerSwipeRight = Hotkeys.Normalize(GetText("FourFingerSwipeRight"));
        config.Gestures.Hotkeys.FourFingerSwipeUp = Hotkeys.Normalize(GetText("FourFingerSwipeUp"));
        config.Gestures.Hotkeys.FourFingerSwipeDown = Hotkeys.Normalize(GetText("FourFingerSwipeDown"));
        config.LogRawReports = GetBool("LogRawReports");

        config.Keyboard.Enabled = GetBool("KeyboardEnabled");
        config.Keyboard.OnlyWhenAppleKeyboardPresent = GetBool("KeyboardOnlyWhenAppleKeyboardPresent");
        config.Keyboard.SwapExchangedKeys = GetBool("KeyboardSwapExchangedKeys");
        config.Keyboard.FKeyMode = GetText("FKeyMode");
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
    }

    private void ValidateHotkeys()
    {
        foreach (var value in AllHotkeys())
        {
            Hotkeys.Parse(value);
        }
    }

    private IEnumerable<string> AllHotkeys()
    {
        yield return config.Gestures.PinchZoomModifier;
        yield return config.Gestures.Hotkeys.ThreeFingerSwipeLeft;
        yield return config.Gestures.Hotkeys.ThreeFingerSwipeRight;
        yield return config.Gestures.Hotkeys.ThreeFingerSwipeUp;
        yield return config.Gestures.Hotkeys.ThreeFingerSwipeDown;
        yield return config.Gestures.Hotkeys.FourFingerSwipeLeft;
        yield return config.Gestures.Hotkeys.FourFingerSwipeRight;
        yield return config.Gestures.Hotkeys.FourFingerSwipeUp;
        yield return config.Gestures.Hotkeys.FourFingerSwipeDown;
        yield return config.Keyboard.F1;
        yield return config.Keyboard.F2;
        yield return config.Keyboard.F3;
        yield return config.Keyboard.F4;
        yield return config.Keyboard.F5;
        yield return config.Keyboard.F6;
        yield return config.Keyboard.F7;
        yield return config.Keyboard.F8;
        yield return config.Keyboard.F9;
        yield return config.Keyboard.F10;
        yield return config.Keyboard.F11;
        yield return config.Keyboard.F12;
        yield return config.Keyboard.F13;
        yield return config.Keyboard.F14;
        yield return config.Keyboard.F15;
        yield return config.Keyboard.F16;
        yield return config.Keyboard.F17;
        yield return config.Keyboard.F18;
        yield return config.Keyboard.F19;
    }

    private void ShowAllKeyMappings()
    {
        ReadValues();
        using var dialog = new Form
        {
            Text = "F-key mappings",
            Width = 520,
            Height = 680,
            StartPosition = FormStartPosition.CenterParent,
            Font = Font,
        };
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(14),
        };
        dialog.Controls.Add(panel);

        var edits = new Dictionary<string, TextBox>();
        foreach (var key in Enumerable.Range(1, 19).Select(index => $"F{index}"))
        {
            var row = Row();
            row.Controls.Add(new Label { Text = key, Width = 70, TextAlign = ContentAlignment.MiddleLeft });
            var textBox = new TextBox { Width = 260, Text = KeyboardKeyValue(key) };
            row.Controls.Add(textBox);
            var record = new Button { Text = "Record", Width = 82, Height = 28 };
            record.Click += (_, _) => RecordHotkey(textBox);
            row.Controls.Add(record);
            edits[key] = textBox;
            panel.Controls.Add(row);
        }

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Width = 450, Height = 42 };
        var ok = new Button { Text = "Apply", Width = 90, Height = 30 };
        ok.Click += (_, _) =>
        {
            foreach (var item in edits)
            {
                SetKeyboardKeyValue(item.Key, Hotkeys.Normalize(item.Value.Text));
            }
            Set("KeyboardF13", config.Keyboard.F13);
            Set("KeyboardF14", config.Keyboard.F14);
            Set("KeyboardF15", config.Keyboard.F15);
            Set("KeyboardF16", config.Keyboard.F16);
            Set("KeyboardF17", config.Keyboard.F17);
            Set("KeyboardF18", config.Keyboard.F18);
            Set("KeyboardF19", config.Keyboard.F19);
            dialog.Close();
        };
        buttons.Controls.Add(ok);
        panel.Controls.Add(buttons);
        dialog.ShowDialog(this);
    }

    private string KeyboardKeyValue(string key) => key switch
    {
        "F1" => config.Keyboard.F1,
        "F2" => config.Keyboard.F2,
        "F3" => config.Keyboard.F3,
        "F4" => config.Keyboard.F4,
        "F5" => config.Keyboard.F5,
        "F6" => config.Keyboard.F6,
        "F7" => config.Keyboard.F7,
        "F8" => config.Keyboard.F8,
        "F9" => config.Keyboard.F9,
        "F10" => config.Keyboard.F10,
        "F11" => config.Keyboard.F11,
        "F12" => config.Keyboard.F12,
        "F13" => config.Keyboard.F13,
        "F14" => config.Keyboard.F14,
        "F15" => config.Keyboard.F15,
        "F16" => config.Keyboard.F16,
        "F17" => config.Keyboard.F17,
        "F18" => config.Keyboard.F18,
        "F19" => config.Keyboard.F19,
        _ => "unchanged",
    };

    private void SetKeyboardKeyValue(string key, string value)
    {
        switch (key)
        {
            case "F1": config.Keyboard.F1 = value; break;
            case "F2": config.Keyboard.F2 = value; break;
            case "F3": config.Keyboard.F3 = value; break;
            case "F4": config.Keyboard.F4 = value; break;
            case "F5": config.Keyboard.F5 = value; break;
            case "F6": config.Keyboard.F6 = value; break;
            case "F7": config.Keyboard.F7 = value; break;
            case "F8": config.Keyboard.F8 = value; break;
            case "F9": config.Keyboard.F9 = value; break;
            case "F10": config.Keyboard.F10 = value; break;
            case "F11": config.Keyboard.F11 = value; break;
            case "F12": config.Keyboard.F12 = value; break;
            case "F13": config.Keyboard.F13 = value; break;
            case "F14": config.Keyboard.F14 = value; break;
            case "F15": config.Keyboard.F15 = value; break;
            case "F16": config.Keyboard.F16 = value; break;
            case "F17": config.Keyboard.F17 = value; break;
            case "F18": config.Keyboard.F18 = value; break;
            case "F19": config.Keyboard.F19 = value; break;
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

    private void StartBridge()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = Application.ExecutablePath,
            Arguments = $"--bridge --config \"{configPath}\"",
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
        });
    }

    private void OpenConfigFolder()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(configPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        System.Diagnostics.Process.Start("explorer.exe", Path.GetDirectoryName(configPath)!);
    }

    private List<DeviceTabInfo> DeviceTabs()
    {
        IReadOnlyList<HidDeviceInfo> devices;
        try
        {
            devices = DeviceActions.EnumerateRawInputDevices();
        }
        catch
        {
            devices = [];
        }

        var result = new List<DeviceTabInfo>();
        foreach (var device in DeviceCatalog.FindMagicTrackpads(devices).GroupBy(item => DeviceActions.PhysicalKey(item.Name)).Select(group => group.First()))
        {
            result.Add(new DeviceTabInfo(DeviceKind.Trackpad, DeviceTitle(device, "Magic Trackpad"), device, true));
        }

        foreach (var device in DeviceCatalog.FindAppleKeyboards(devices).GroupBy(item => DeviceActions.PhysicalKey(item.Name)).Select(group => group.First()))
        {
            result.Add(new DeviceTabInfo(DeviceKind.Keyboard, DeviceTitle(device, "Magic Keyboard"), device, true));
        }

        if (!result.Any(item => item.Kind == DeviceKind.Trackpad))
        {
            result.Add(new DeviceTabInfo(DeviceKind.Trackpad, "Magic Trackpad", null, false));
        }

        if (!result.Any(item => item.Kind == DeviceKind.Keyboard))
        {
            result.Add(new DeviceTabInfo(DeviceKind.Keyboard, "Magic Keyboard", null, false));
        }

        return result;
    }

    private static string DeviceTitle(HidDeviceInfo device, string fallback)
    {
        var product = device.ProductName == "Unknown HID device" ? fallback : device.ProductName;
        var transport = device.IsBluetooth ? "Bluetooth" : "USB";
        return $"{product} - {transport}";
    }

    private void DrawDeviceTab(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tabs)
        {
            return;
        }

        var page = tabs.TabPages[e.Index];
        var info = page.Tag as DeviceTabInfo;
        var selected = e.State.HasFlag(DrawItemState.Selected);
        using var background = new SolidBrush(selected ? Color.White : Color.FromArgb(245, 245, 245));
        e.Graphics.FillRectangle(background, e.Bounds);
        using var border = new Pen(Color.FromArgb(220, 220, 220));
        e.Graphics.DrawRectangle(border, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
        using var dot = new SolidBrush(info?.Connected == true ? Color.FromArgb(24, 200, 46) : Color.FromArgb(150, 150, 150));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillEllipse(dot, e.Bounds.X + 13, e.Bounds.Y + 10, 14, 14);
        TextRenderer.DrawText(
            e.Graphics,
            page.Text,
            Font,
            new Rectangle(e.Bounds.X + 34, e.Bounds.Y + 5, e.Bounds.Width - 38, e.Bounds.Height - 8),
            Color.Black,
            TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
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

    private void Set(string name, bool value)
    {
        if (controlsByName.TryGetValue(name, out var control) && control is CheckBox box)
        {
            box.Checked = value;
        }
    }

    private void Set(string name, int value)
    {
        if (controlsByName.TryGetValue(name, out var control) && control is TrackBar slider)
        {
            slider.Value = Math.Clamp(value, slider.Minimum, slider.Maximum);
        }
    }

    private void Set(string name, string value)
    {
        if (!controlsByName.TryGetValue(name, out var control))
        {
            return;
        }

        if (control is ComboBox combo)
        {
            combo.SelectedItem = combo.Items.Contains(value) ? value : combo.Items[0];
        }
        else if (control is TextBox text)
        {
            text.Text = value;
        }
    }

    private bool GetBool(string name) =>
        controlsByName.TryGetValue(name, out var control) && control is CheckBox box && box.Checked;

    private int GetInt(string name) =>
        controlsByName.TryGetValue(name, out var control) && control is TrackBar slider ? slider.Value : 0;

    private string GetText(string name)
    {
        if (!controlsByName.TryGetValue(name, out var control))
        {
            return "";
        }

        return control switch
        {
            ComboBox combo => combo.SelectedItem?.ToString() ?? "",
            TextBox text => text.Text,
            _ => "",
        };
    }

    private static int Scale(double value, int factor) => (int)Math.Round(value * factor);

    private static string[] ButtonChoices() => ["left", "middle", "right", "none"];

    private static string[] KeyActionChoices() => ["unchanged", "Ctrl", "Alt", "Win", "Shift", "Esc", "CapsLock", "none"];
}

internal enum DeviceKind
{
    Trackpad,
    Keyboard,
}

internal sealed record DeviceTabInfo(DeviceKind Kind, string Title, HidDeviceInfo? Device, bool Connected);

internal sealed class TrackpadPreview : Control
{
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var body = new Rectangle(24, 18, Width - 48, Height - 54);
        using var shadow = new SolidBrush(Color.FromArgb(32, 0, 0, 0));
        e.Graphics.FillRectangle(shadow, body.X + 4, body.Y + 6, body.Width, body.Height);
        using var path = Rounded(body, 16);
        using var fill = new LinearGradientBrush(body, Color.FromArgb(250, 250, 250), Color.FromArgb(220, 220, 220), LinearGradientMode.Vertical);
        using var border = new Pen(Color.FromArgb(170, 170, 170));
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
        var buttonHeight = 54;
        var buttonY = body.Bottom - buttonHeight;
        e.Graphics.DrawLine(Pens.Silver, body.Left, buttonY, body.Right, buttonY);
        e.Graphics.DrawLine(Pens.Silver, body.Left + body.Width / 3, buttonY, body.Left + body.Width / 3, body.Bottom);
        e.Graphics.DrawLine(Pens.Silver, body.Left + body.Width * 2 / 3, buttonY, body.Left + body.Width * 2 / 3, body.Bottom);
        DrawCentered(e.Graphics, "Left click", new Rectangle(body.Left, buttonY, body.Width / 3, buttonHeight));
        DrawCentered(e.Graphics, "Middle click", new Rectangle(body.Left + body.Width / 3, buttonY, body.Width / 3, buttonHeight));
        DrawCentered(e.Graphics, "Right click", new Rectangle(body.Left + body.Width * 2 / 3, buttonY, body.Width / 3, buttonHeight));
    }

    private static void DrawCentered(Graphics graphics, string text, Rectangle bounds)
    {
        TextRenderer.DrawText(graphics, text, new Font("Segoe UI", 10F), bounds, Color.DimGray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private static GraphicsPath Rounded(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class KeyboardPreview : Control
{
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var keyboard = new Rectangle(26, 28, Width - 52, Height - 64);
        using var bodyPath = Rounded(keyboard, 12);
        using var body = new SolidBrush(Color.FromArgb(246, 246, 246));
        using var border = new Pen(Color.FromArgb(120, 120, 120));
        e.Graphics.FillPath(body, bodyPath);
        e.Graphics.DrawPath(border, bodyPath);

        var rows = new[]
        {
            new[] { "esc", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12" },
            new[] { "`", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=" },
            new[] { "tab", "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "[", "]" },
            new[] { "caps", "A", "S", "D", "F", "G", "H", "J", "K", "L", ";", "'" },
            new[] { "shift", "Z", "X", "C", "V", "B", "N", "M", ",", ".", "/", "shift" },
            new[] { "fn", "control", "option", "command", "space", "command", "option" },
        };

        var y = keyboard.Top + 10;
        foreach (var row in rows)
        {
            var x = keyboard.Left + 10;
            var keyHeight = 27;
            foreach (var key in row)
            {
                var width = key switch
                {
                    "space" => 145,
                    "shift" => 50,
                    "command" => 54,
                    "control" or "option" => 46,
                    "caps" => 50,
                    "tab" => 46,
                    _ => 35,
                };
                DrawKey(e.Graphics, new Rectangle(x, y, width, keyHeight), key);
                x += width + 4;
            }
            y += keyHeight + 8;
        }
    }

    private static void DrawKey(Graphics graphics, Rectangle rect, string text)
    {
        using var path = Rounded(rect, 4);
        using var fill = new SolidBrush(Color.White);
        using var border = new Pen(Color.FromArgb(80, 80, 80));
        graphics.FillPath(fill, path);
        graphics.DrawPath(border, path);
        TextRenderer.DrawText(graphics, text, new Font("Segoe UI", 7F), rect, Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private static GraphicsPath Rounded(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
