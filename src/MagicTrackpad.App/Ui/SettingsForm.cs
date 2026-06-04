using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using MagicTrackpad.Configuration;
using MagicTrackpad.Hid;
using MagicTrackpad.Input;

namespace MagicTrackpad.Ui;

public sealed class SettingsForm : Form
{
    private const int LeftColumnWidth = 340;
    private const int MainColumnWidth = 380;
    private const int KeyboardLeftWidth = 620;
    private const int KeyboardRightWidth = 500;
    private const int TrackpadPageGutter = 36;
    private const int KeyboardPageGutter = 36;
    private static readonly Color Shell = ThemePalette.Shell;
    private static readonly Color Surface = ThemePalette.Surface;
    private static readonly Color SurfaceAlt = ThemePalette.SurfaceAlt;
    private static readonly Color Stroke = ThemePalette.Stroke;
    private static readonly Color TextMain = ThemePalette.TextMain;
    private static readonly Color TextMuted = ThemePalette.TextMuted;
    private static readonly Color Accent = ThemePalette.Accent;

    private readonly string configPath;
    private readonly Dictionary<string, Control> controlsByName = [];
    private readonly List<Action> layoutSyncs = [];
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
        BackColor = Shell;
        ForeColor = TextMain;

        Build();
        ApplyPeripheralTheme(this);
        LoadValues();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        TryUseDarkTitleBar();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        SyncLayouts();
        BeginInvoke(new Action(SyncLayouts));
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        SyncLayouts();
    }

    private void TryUseDarkTitleBar()
    {
        try
        {
            var enabled = 1;
            _ = DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
            _ = DwmSetWindowAttribute(Handle, 19, ref enabled, sizeof(int));
        }
        catch
        {
            // Older Windows builds ignore this; the app body still owns the dark theme.
        }
    }

    private void SyncLayouts()
    {
        foreach (var sync in layoutSyncs)
        {
            sync();
        }
    }

    private void Build()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(8),
            BackColor = Shell,
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
            BackColor = Shell,
        };
        tabs.DrawItem += DrawDeviceTab;
        tabs.Resize += (_, _) => SyncLayouts();
        root.Controls.Add(tabs, 0, 0);

        foreach (var device in DeviceTabs())
        {
            var page = new TabPage(device.Title)
            {
                BackColor = Shell,
                ForeColor = TextMain,
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
            BackColor = Shell,
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
            BackColor = Shell,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LeftColumnWidth));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, MainColumnWidth));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, MainColumnWidth));
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
        AddCheck(two, "SmartZoomEnabled", "Smart zoom double tap", 28);
        AddCheck(two, "RotateEnabled", "Rotate with two fingers", 28);
        AddCheck(two, "TwoFingerSwipePagesEnabled", "Swipe left/right between pages", 28);
        AddSlider(two, "ScrollSensitivity", "Speed:", 5, 200, "Slow", "Fast");
        AddSlider(two, "PinchSensitivity", "Pinch:", 10, 200, "Light", "Strong");
        AddActionChoice(two, "TwoFingerSwipeLeft", "Swipe left:", TwoFingerLeftActions(), 210);
        AddActionChoice(two, "TwoFingerSwipeRight", "Swipe right:", TwoFingerRightActions(), 210);
        AddActionChoice(two, "SmartZoomIn", "Smart zoom:", SmartZoomInActions(), 210);
        AddActionChoice(two, "SmartZoomOut", "Zoom again:", SmartZoomOutActions(), 210);
        AddActionChoice(two, "RotateClockwise", "Rotate CW:", RotateClockwiseActions(), 210);
        AddActionChoice(two, "RotateCounterClockwise", "Rotate CCW:", RotateCounterClockwiseActions(), 210);

        var three = Group(middle, "3 Finger Gestures", MainColumnWidth);
        AddCheck(three, "ThreeFingerTap", "Tap to middle click");
        AddActionChoice(three, "ThreeFingerTapHotkey", "Tap action:", TapActions(), 210);
        AddCheck(three, "ThreeFingerSwipesEnabled", "Enable 3 and 4 finger gestures");
        AddActionChoice(three, "ThreeFingerSwipeLeft", "Swipe left:", DesktopLeftActions(), 210);
        AddActionChoice(three, "ThreeFingerSwipeRight", "Swipe right:", DesktopRightActions(), 210);
        AddActionChoice(three, "ThreeFingerSwipeUp", "Swipe up:", SwipeUpActions(), 210);
        AddActionChoice(three, "ThreeFingerSwipeDown", "Swipe down:", SwipeDownActions(), 210);

        var four = Group(right, "4 Finger Gestures", MainColumnWidth);
        AddActionChoice(four, "FourFingerSwipeLeft", "Swipe left:", DesktopLeftActions(), 210);
        AddActionChoice(four, "FourFingerSwipeRight", "Swipe right:", DesktopRightActions(), 210);
        AddActionChoice(four, "FourFingerSwipeUp", "Swipe up:", SwipeUpActions(), 210);
        AddActionChoice(four, "FourFingerSwipeDown", "Swipe down:", SwipeDownActions(), 210);
        AddCheck(four, "FourFingerPinchEnabled", "Pinch/spread for Launchpad and desktop");
        AddCheck(four, "FourFingerTapEnabled", "Tap for Notification Center");
        AddActionChoice(four, "FourFingerPinchIn", "Pinch in:", PinchInActions(), 210);
        AddActionChoice(four, "FourFingerSpread", "Spread out:", SpreadActions(), 210);
        AddActionChoice(four, "FourFingerTap", "Tap:", FourFingerTapActions(), 210);
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

        void SyncLayout() => FitTrackpadLayout(page, grid, left, middle, right);
        layoutSyncs.Add(SyncLayout);
        page.HandleCreated += (_, _) => SyncLayout();
        page.Resize += (_, _) => SyncLayout();
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
            BackColor = Shell,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, KeyboardLeftWidth));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, KeyboardRightWidth));
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
        AddHotkey(other, "KeyboardFnGlobe", "Fn / Globe key");

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

        void SyncLayout() => FitKeyboardLayout(page, grid, left, right);
        layoutSyncs.Add(SyncLayout);
        page.HandleCreated += (_, _) => SyncLayout();
        page.Resize += (_, _) => SyncLayout();
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
        row.Controls.Add(new Label { Text = "Touch report capture", Width = 210, TextAlign = ContentAlignment.MiddleLeft, ForeColor = TextMuted });
        group.Controls.Add(row);
        AddProgress(group, device.Battery.Percent, BatteryText(device));
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
        AddProgress(group, device.Battery.Percent, BatteryText(device));
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
            ForeColor = device.Connected ? Accent : TextMuted,
        });
        group.Controls.Add(new Label
        {
            Text = device.Device?.Name ?? "Open the bridge or reconnect the device to refresh this page.",
            Width = ContentWidth(group) - 16,
            AutoSize = false,
            Height = 54,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = TextMuted,
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
            ForeColor = TextMuted,
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
            ForeColor = TextMuted,
        });
        group.Controls.Add(row);
    }

    private static void AddProgress(FlowLayoutPanel group, int? value, string text)
    {
        var bar = new LevelMeter
        {
            Width = ContentWidth(group) - 8,
            Height = 28,
            Value = value is int percent ? Math.Clamp(percent, 0, 100) : null,
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

    private static string BatteryText(DeviceTabInfo device)
    {
        if (!device.Connected)
        {
            return "Device not detected.";
        }

        if (device.Battery.Percent is not int percent)
        {
            return "Connected. Battery not reported yet.";
        }

        var state = device.Battery.Charging switch
        {
            true => "charging",
            false => "on battery",
            _ => "reported",
        };
        return $"Battery: {percent}% ({state}).";
    }

    private static void FitTrackpadLayout(TabPage page, TableLayoutPanel grid, FlowLayoutPanel left, FlowLayoutPanel middle, FlowLayoutPanel right)
    {
        var available = Math.Max(LeftColumnWidth + (MainColumnWidth * 2) + TrackpadPageGutter, AvailablePageWidth(page) - 20);
        var leftWidth = Math.Clamp((int)Math.Round(available * 0.24), LeftColumnWidth, 470);
        var mainWidth = Math.Clamp((available - leftWidth - TrackpadPageGutter) / 2, MainColumnWidth, 650);

        grid.Width = leftWidth + (mainWidth * 2) + TrackpadPageGutter;
        grid.ColumnStyles[0].Width = leftWidth;
        grid.ColumnStyles[1].Width = mainWidth;
        grid.ColumnStyles[2].Width = mainWidth;
        ResizeColumn(left, leftWidth);
        ResizeColumn(middle, mainWidth);
        ResizeColumn(right, mainWidth);
    }

    private static void FitKeyboardLayout(TabPage page, TableLayoutPanel grid, FlowLayoutPanel left, FlowLayoutPanel right)
    {
        var available = Math.Max(KeyboardLeftWidth + KeyboardRightWidth + KeyboardPageGutter, AvailablePageWidth(page) - 20);
        var leftWidth = Math.Clamp((int)Math.Round(available * 0.42), KeyboardLeftWidth, 760);
        var rightWidth = Math.Clamp(available - leftWidth - KeyboardPageGutter, KeyboardRightWidth, 980);

        grid.Width = leftWidth + rightWidth + KeyboardPageGutter;
        grid.ColumnStyles[0].Width = leftWidth;
        grid.ColumnStyles[1].Width = rightWidth;
        ResizeColumn(left, leftWidth);
        ResizeColumn(right, rightWidth);
    }

    private static int AvailablePageWidth(TabPage page)
    {
        var parentWidth = page.Parent?.ClientSize.Width - 8 ?? 0;
        var formWidth = page.FindForm()?.ClientSize.Width - 32 ?? 0;
        return Math.Max(page.ClientSize.Width, Math.Max(parentWidth, formWidth));
    }

    private static void ResizeColumn(FlowLayoutPanel column, int width)
    {
        column.Width = width;
        foreach (Control child in column.Controls)
        {
            if (child is ThemedGroupBox box)
            {
                ResizeGroup(box, width);
            }
        }
    }

    private static void ResizeGroup(ThemedGroupBox box, int columnWidth)
    {
        var boxWidth = Math.Max(260, columnWidth - 18);
        var contentWidth = Math.Max(236, columnWidth - 44);
        box.Width = boxWidth;
        box.MinimumSize = new Size(boxWidth, 0);

        foreach (Control child in box.Controls)
        {
            if (child is FlowLayoutPanel panel)
            {
                panel.Width = contentWidth;
                panel.MinimumSize = new Size(contentWidth, 0);
                panel.Tag = contentWidth;
                ResizeGroupContent(panel, contentWidth);
            }
        }
    }

    private static void ResizeGroupContent(FlowLayoutPanel panel, int contentWidth)
    {
        foreach (Control child in panel.Controls)
        {
            switch (child)
            {
                case FlowLayoutPanel row:
                    ResizeRow(row, contentWidth);
                    break;
                case TrackpadPreview or KeyboardPreview:
                    child.Width = Math.Max(180, contentWidth - 4);
                    break;
                case LevelMeter:
                    child.Width = Math.Max(180, contentWidth - 8);
                    break;
                case Label label:
                    label.Width = Math.Max(180, contentWidth - 8);
                    break;
            }
        }
    }

    private static void ResizeRow(FlowLayoutPanel row, int contentWidth)
    {
        row.Width = Math.Max(180, contentWidth - 8);
        var available = Math.Max(160, row.Width - row.Padding.Left - 4);
        var labels = row.Controls.OfType<Label>().ToArray();
        var button = row.Controls.OfType<Button>().FirstOrDefault();
        var textBox = row.Controls.OfType<TextBox>().FirstOrDefault();
        var combo = row.Controls.OfType<ComboBox>().FirstOrDefault();
        var sliderPanel = row.Controls.OfType<TableLayoutPanel>().FirstOrDefault();
        var check = row.Controls.OfType<CheckBox>().FirstOrDefault();

        if (textBox is not null && button is not null && labels.Length > 0)
        {
            labels[0].Width = Math.Clamp(labels[0].Width, 90, 125);
            button.Width = Math.Clamp(button.Width, 60, 76);
            textBox.Width = Math.Max(120, available - labels[0].Width - button.Width - 14);
        }
        else if (combo is not null && labels.Length > 0)
        {
            labels[0].Width = Math.Min(205, Math.Max(120, available - 155));
            combo.Width = Math.Max(120, available - labels[0].Width - 12);
        }
        else if (sliderPanel is not null && labels.Length > 0)
        {
            labels[0].Width = Math.Min(125, Math.Max(94, available / 3));
            sliderPanel.Width = Math.Max(180, available - labels[0].Width - 12);
            foreach (Control nested in sliderPanel.Controls)
            {
                if (nested is TrackBar track)
                {
                    track.Width = Math.Max(160, sliderPanel.Width - 10);
                }
            }
        }
        else if (button is not null && labels.Length > 0)
        {
            labels[0].Width = Math.Min(165, Math.Max(20, available - button.Width - 12));
            button.Width = Math.Max(160, Math.Min(260, available - labels[0].Width - 12));
        }
        else if (check is not null && labels.Length >= 2)
        {
            labels[0].Width = Math.Min(150, Math.Max(105, available / 3));
            check.Width = 24;
            labels[1].Width = Math.Max(95, available - labels[0].Width - check.Width - 16);
        }
        else if (check is not null)
        {
            check.Width = Math.Max(120, available - 4);
        }
    }

    private static FlowLayoutPanel Column() => new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = false,
        BackColor = Shell,
        Padding = new Padding(4),
    };

    private static FlowLayoutPanel Group(FlowLayoutPanel column, string title, int width)
    {
        var box = new ThemedGroupBox
        {
            Text = $"  {title}  ",
            Width = width - 18,
            MinimumSize = new Size(width - 18, 0),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 28, 12, 12),
            Margin = new Padding(4, 4, 8, 12),
            BackColor = Surface,
            ForeColor = TextMain,
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
            BackColor = Surface,
            ForeColor = TextMain,
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
        BackColor = Color.Transparent,
        ForeColor = TextMain,
    };

    private static int ContentWidth(Control parent) =>
        parent.Tag is int width ? width : Math.Max(260, parent.Width);

    private static Label ThemedLabel(string text, int width) => new()
    {
        Text = text,
        Width = width,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = TextMain,
        BackColor = Color.Transparent,
    };

    private static Button ThemedButton(string text, int width, int height) => new()
    {
        Text = text,
        Width = width,
        Height = height,
        BackColor = SurfaceAlt,
        ForeColor = TextMain,
        FlatStyle = FlatStyle.Flat,
    };

    private static void ApplyPeripheralTheme(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is TabPage)
            {
                child.BackColor = Shell;
                child.ForeColor = TextMain;
            }
            else if (child is GroupBox)
            {
                child.BackColor = Surface;
                child.ForeColor = TextMain;
            }
            else if (child is FlowLayoutPanel or TableLayoutPanel or Panel)
            {
                child.BackColor = child.Parent is GroupBox or FlowLayoutPanel ? Surface : Shell;
                child.ForeColor = TextMain;
            }
            else if (child is Label label)
            {
                if (label.ForeColor == SystemColors.ControlText || label.ForeColor == Color.Black || label.ForeColor == Color.DimGray)
                {
                    label.ForeColor = label.ForeColor == Color.DimGray ? TextMuted : TextMain;
                }
                label.BackColor = Color.Transparent;
            }
            else if (child is CheckBox check)
            {
                check.ForeColor = TextMain;
                check.BackColor = Color.Transparent;
            }
            else if (child is Button button)
            {
                button.BackColor = SurfaceAlt;
                button.ForeColor = TextMain;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Stroke;
                button.FlatAppearance.MouseOverBackColor = ThemePalette.ControlHover;
                button.FlatAppearance.MouseDownBackColor = ThemePalette.ControlPressed;
            }
            else if (child is TextBox text)
            {
                text.BackColor = ThemePalette.Control;
                text.ForeColor = TextMain;
                text.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (child is ComboBox combo)
            {
                combo.BackColor = ThemePalette.Control;
                combo.ForeColor = TextMain;
                combo.FlatStyle = FlatStyle.Flat;
            }
            else if (child is TrackBar track)
            {
                track.BackColor = Surface;
                track.ForeColor = TextMain;
            }

            ApplyPeripheralTheme(child);
        }
    }

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
            ForeColor = TextMain,
            BackColor = Color.Transparent,
        };
        controlsByName[name] = box;
        row.Controls.Add(box);
        parent.Controls.Add(row);
    }

    private void AddChoice(FlowLayoutPanel parent, string name, string label, string[] choices, int comboWidth)
    {
        var contentWidth = ContentWidth(parent);
        var row = Row(width: contentWidth - 8);
        row.Controls.Add(ThemedLabel(label, 205));
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = Math.Min(comboWidth, Math.Max(120, contentWidth - 235)),
            BackColor = ThemePalette.Control,
            ForeColor = TextMain,
            FlatStyle = FlatStyle.Flat,
        };
        combo.Items.AddRange(choices.Cast<object>().ToArray());
        controlsByName[name] = combo;
        row.Controls.Add(combo);
        parent.Controls.Add(row);
    }

    private void AddActionChoice(FlowLayoutPanel parent, string name, string label, ActionOption[] choices, int comboWidth)
    {
        var contentWidth = ContentWidth(parent);
        var row = Row(width: contentWidth - 8);
        row.Controls.Add(ThemedLabel(label, 125));
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = Math.Min(comboWidth, Math.Max(160, contentWidth - 145)),
            BackColor = ThemePalette.Control,
            ForeColor = TextMain,
            FlatStyle = FlatStyle.Flat,
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
        row.Controls.Add(ThemedLabel(label, 115));
        var textBox = new TextBox { Width = Math.Max(120, contentWidth - 195), BackColor = ThemePalette.Control, ForeColor = TextMain, BorderStyle = BorderStyle.FixedSingle };
        controlsByName[name] = textBox;
        row.Controls.Add(textBox);
        var record = ThemedButton("Record", 64, 28);
        record.Click += (_, _) => RecordHotkey(textBox);
        row.Controls.Add(record);
        parent.Controls.Add(row);
    }

    private void AddSlider(FlowLayoutPanel parent, string name, string label, int min, int max, string leftText, string rightText)
    {
        var contentWidth = ContentWidth(parent);
        var row = Row(58, contentWidth - 8);
        row.Controls.Add(ThemedLabel(label, 110));
        var sliderWidth = Math.Max(180, contentWidth - 130);
        var sliderPanel = new TableLayoutPanel
        {
            Width = sliderWidth,
            Height = 56,
            RowCount = 2,
            ColumnCount = 1,
            Margin = new Padding(0),
            BackColor = Surface,
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
            BackColor = Surface,
            ForeColor = TextMain,
        };
        controlsByName[name] = track;
        sliderPanel.Controls.Add(track, 0, 0);
        var legend = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0), BackColor = Surface };
        legend.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        legend.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        legend.Controls.Add(new Label { Text = leftText, Dock = DockStyle.Fill, ForeColor = TextMuted, BackColor = Surface }, 0, 0);
        legend.Controls.Add(new Label { Text = rightText, Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopRight, ForeColor = TextMuted, BackColor = Surface }, 1, 0);
        sliderPanel.Controls.Add(legend, 0, 1);
        row.Controls.Add(sliderPanel);
        parent.Controls.Add(row);
    }

    private void AddButtonRow(FlowLayoutPanel parent, string text, Action action)
    {
        var row = Row(40, ContentWidth(parent) - 8);
        row.Controls.Add(ThemedLabel("", 165));
        var button = ThemedButton(text, 220, 30);
        button.Click += (_, _) => action();
        row.Controls.Add(button);
        parent.Controls.Add(row);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    private static void AddActionButton(FlowLayoutPanel parent, string text, Action action, int width = 90)
    {
        var button = ThemedButton(text, width, 32);
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
        Set("SmartZoomEnabled", config.Gestures.SmartZoomEnabled);
        Set("RotateEnabled", config.Gestures.RotateEnabled);
        Set("TwoFingerSwipePagesEnabled", config.Gestures.TwoFingerSwipePagesEnabled);
        Set("FourFingerPinchEnabled", config.Gestures.FourFingerPinchEnabled);
        Set("FourFingerTapEnabled", config.Gestures.FourFingerTapEnabled);
        Set("ThreeFingerSwipesEnabled", config.Gestures.ThreeFingerSwipesEnabled);
        Set("SwipeThreshold", (int)Math.Round(config.Gestures.SwipeThreshold));
        Set("SwipeVerticalThreshold", (int)Math.Round(config.Gestures.SwipeVerticalThreshold));
        Set("SwapLeftRightButtons", config.Gestures.SwapLeftRightButtons);
        Set("TwoFingerSwipeLeft", config.Gestures.Hotkeys.TwoFingerSwipeLeft);
        Set("TwoFingerSwipeRight", config.Gestures.Hotkeys.TwoFingerSwipeRight);
        Set("SmartZoomIn", config.Gestures.Hotkeys.SmartZoomIn);
        Set("SmartZoomOut", config.Gestures.Hotkeys.SmartZoomOut);
        Set("RotateClockwise", config.Gestures.Hotkeys.RotateClockwise);
        Set("RotateCounterClockwise", config.Gestures.Hotkeys.RotateCounterClockwise);
        Set("FourFingerPinchIn", config.Gestures.Hotkeys.FourFingerPinchIn);
        Set("FourFingerSpread", config.Gestures.Hotkeys.FourFingerSpread);
        Set("FourFingerTap", config.Gestures.Hotkeys.FourFingerTap);
        Set("ThreeFingerTapHotkey", config.Gestures.Hotkeys.ThreeFingerTap);
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
        Set("KeyboardFnGlobe", config.Keyboard.FnGlobe);
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
        config.Gestures.SmartZoomEnabled = GetBool("SmartZoomEnabled");
        config.Gestures.RotateEnabled = GetBool("RotateEnabled");
        config.Gestures.TwoFingerSwipePagesEnabled = GetBool("TwoFingerSwipePagesEnabled");
        config.Gestures.FourFingerPinchEnabled = GetBool("FourFingerPinchEnabled");
        config.Gestures.FourFingerTapEnabled = GetBool("FourFingerTapEnabled");
        config.Gestures.ThreeFingerSwipesEnabled = GetBool("ThreeFingerSwipesEnabled");
        config.Gestures.SwipeThreshold = GetInt("SwipeThreshold");
        config.Gestures.SwipeVerticalThreshold = GetInt("SwipeVerticalThreshold");
        config.Gestures.SwapLeftRightButtons = GetBool("SwapLeftRightButtons");
        config.Gestures.Hotkeys.TwoFingerSwipeLeft = Hotkeys.Normalize(GetText("TwoFingerSwipeLeft"));
        config.Gestures.Hotkeys.TwoFingerSwipeRight = Hotkeys.Normalize(GetText("TwoFingerSwipeRight"));
        config.Gestures.Hotkeys.SmartZoomIn = Hotkeys.Normalize(GetText("SmartZoomIn"));
        config.Gestures.Hotkeys.SmartZoomOut = Hotkeys.Normalize(GetText("SmartZoomOut"));
        config.Gestures.Hotkeys.RotateClockwise = Hotkeys.Normalize(GetText("RotateClockwise"));
        config.Gestures.Hotkeys.RotateCounterClockwise = Hotkeys.Normalize(GetText("RotateCounterClockwise"));
        config.Gestures.Hotkeys.FourFingerPinchIn = Hotkeys.Normalize(GetText("FourFingerPinchIn"));
        config.Gestures.Hotkeys.FourFingerSpread = Hotkeys.Normalize(GetText("FourFingerSpread"));
        config.Gestures.Hotkeys.FourFingerTap = Hotkeys.Normalize(GetText("FourFingerTap"));
        config.Gestures.Hotkeys.ThreeFingerTap = Hotkeys.Normalize(GetText("ThreeFingerTapHotkey"));
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
        config.Keyboard.FnGlobe = Hotkeys.Normalize(GetText("KeyboardFnGlobe"));
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
        yield return config.Gestures.Hotkeys.TwoFingerSwipeLeft;
        yield return config.Gestures.Hotkeys.TwoFingerSwipeRight;
        yield return config.Gestures.Hotkeys.SmartZoomIn;
        yield return config.Gestures.Hotkeys.SmartZoomOut;
        yield return config.Gestures.Hotkeys.RotateClockwise;
        yield return config.Gestures.Hotkeys.RotateCounterClockwise;
        yield return config.Gestures.Hotkeys.FourFingerPinchIn;
        yield return config.Gestures.Hotkeys.FourFingerSpread;
        yield return config.Gestures.Hotkeys.FourFingerTap;
        yield return config.Gestures.Hotkeys.ThreeFingerTap;
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
        yield return config.Keyboard.FnGlobe;
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

        var batteries = DeviceBattery.QueryApplePeripheralBatteries(devices);
        var result = new List<DeviceTabInfo>();
        foreach (var device in DeviceCatalog.FindMagicTrackpads(devices).GroupBy(item => DeviceActions.PhysicalKey(item.Name)).Select(group => group.First()))
        {
            result.Add(new DeviceTabInfo(DeviceKind.Trackpad, DeviceTitle(device, "Magic Trackpad"), device, true, BatteryFor(device, batteries)));
        }

        foreach (var device in DeviceCatalog.FindAppleKeyboards(devices).GroupBy(item => DeviceActions.PhysicalKey(item.Name)).Select(group => group.First()))
        {
            result.Add(new DeviceTabInfo(DeviceKind.Keyboard, DeviceTitle(device, "Magic Keyboard"), device, true, BatteryFor(device, batteries)));
        }

        if (!result.Any(item => item.Kind == DeviceKind.Trackpad))
        {
            result.Add(new DeviceTabInfo(DeviceKind.Trackpad, "Magic Trackpad", null, false, DeviceBattery.Unknown()));
        }

        if (!result.Any(item => item.Kind == DeviceKind.Keyboard))
        {
            result.Add(new DeviceTabInfo(DeviceKind.Keyboard, "Magic Keyboard", null, false, DeviceBattery.Unknown()));
        }

        return result;
    }

    private static BatteryStatus BatteryFor(HidDeviceInfo device, IReadOnlyDictionary<string, BatteryStatus> batteries) =>
        batteries.TryGetValue(DeviceActions.PhysicalKey(device.Name), out var status)
            ? status
            : DeviceBattery.Unknown("Battery not reported");

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
        using var background = new SolidBrush(selected ? SurfaceAlt : Shell);
        e.Graphics.FillRectangle(background, e.Bounds);
        using var border = new Pen(Stroke);
        e.Graphics.DrawRectangle(border, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (selected)
        {
            using var accentLine = new Pen(Accent, 3);
            e.Graphics.DrawLine(accentLine, e.Bounds.Left + 1, e.Bounds.Bottom - 2, e.Bounds.Right - 2, e.Bounds.Bottom - 2);
        }

        using var dot = new SolidBrush(info?.Connected == true ? Accent : Color.FromArgb(88, 88, 88));
        e.Graphics.FillEllipse(dot, e.Bounds.X + 13, e.Bounds.Y + 10, 14, 14);
        TextRenderer.DrawText(
            e.Graphics,
            page.Text,
            Font,
            new Rectangle(e.Bounds.X + 34, e.Bounds.Y + 5, e.Bounds.Width - 38, e.Bounds.Height - 8),
            selected ? TextMain : TextMuted,
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
            combo.SelectedItem = ComboItemForValue(combo, value) ?? combo.Items[0];
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
            ComboBox combo => combo.SelectedItem is ActionOption action ? action.Value : combo.SelectedItem?.ToString() ?? "",
            TextBox text => text.Text,
            _ => "",
        };
    }

    private static object? ComboItemForValue(ComboBox combo, string value)
    {
        foreach (var item in combo.Items)
        {
            if (item is ActionOption action && string.Equals(action.Value, value, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }

            if (item is string text && string.Equals(text, value, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return null;
    }

    private static int Scale(double value, int factor) => (int)Math.Round(value * factor);

    private static string[] ButtonChoices() => ["left", "middle", "right", "none"];

    private static string[] KeyActionChoices() => ["unchanged", "Ctrl", "Alt", "Win", "Shift", "Esc", "CapsLock", "none"];

    private static ActionOption[] TwoFingerLeftActions() =>
    [
        Action("Previous page", "BrowserBack"),
        Action("Previous desktop", "Win+Ctrl+Left"),
        Action("Task View", "Win+Tab"),
        Action("Show desktop", "Win+D"),
        Action("Do nothing", "none"),
    ];

    private static ActionOption[] TwoFingerRightActions() =>
    [
        Action("Next page", "BrowserForward"),
        Action("Next desktop", "Win+Ctrl+Right"),
        Action("Task View", "Win+Tab"),
        Action("Show desktop", "Win+D"),
        Action("Do nothing", "none"),
    ];

    private static ActionOption[] SmartZoomInActions() =>
    [
        Action("Zoom in", "Ctrl+Plus"),
        Action("Reset zoom", "Ctrl+0"),
        Action("Search / Spotlight", "Win+S"),
        Action("Do nothing", "none"),
    ];

    private static ActionOption[] SmartZoomOutActions() =>
    [
        Action("Reset zoom", "Ctrl+0"),
        Action("Zoom out", "Ctrl+Minus"),
        Action("Do nothing", "none"),
    ];

    private static ActionOption[] RotateClockwiseActions() =>
    [
        Action("Rotate clockwise", "Ctrl+R"),
        Action("Do nothing", "none"),
    ];

    private static ActionOption[] RotateCounterClockwiseActions() =>
    [
        Action("Rotate counterclockwise", "Ctrl+Shift+R"),
        Action("Do nothing", "none"),
    ];

    private static ActionOption[] TapActions() =>
    [
        Action("Middle click", "none"),
        Action("Search / Spotlight", "Win+S"),
        Action("Task View", "Win+Tab"),
        Action("Notification Center", "Win+N"),
    ];

    private static ActionOption[] DesktopLeftActions() =>
    [
        Action("Previous desktop", "Win+Ctrl+Left"),
        Action("Previous page", "BrowserBack"),
        Action("Task View", "Win+Tab"),
        Action("Do nothing", "none"),
    ];

    private static ActionOption[] DesktopRightActions() =>
    [
        Action("Next desktop", "Win+Ctrl+Right"),
        Action("Next page", "BrowserForward"),
        Action("Task View", "Win+Tab"),
        Action("Do nothing", "none"),
    ];

    private static ActionOption[] SwipeUpActions() =>
    [
        Action("Task View", "Win+Tab"),
        Action("Search / Spotlight", "Win+S"),
        Action("Start / Launchpad", "Win"),
        Action("Do nothing", "none"),
    ];

    private static ActionOption[] SwipeDownActions() =>
    [
        Action("Show desktop", "Win+D"),
        Action("App switcher", "Alt+Tab"),
        Action("Notification Center", "Win+N"),
        Action("Do nothing", "none"),
    ];

    private static ActionOption[] PinchInActions() =>
    [
        Action("Start / Launchpad", "Win"),
        Action("Search / Spotlight", "Win+S"),
        Action("Do nothing", "none"),
    ];

    private static ActionOption[] SpreadActions() =>
    [
        Action("Show desktop", "Win+D"),
        Action("Task View", "Win+Tab"),
        Action("Do nothing", "none"),
    ];

    private static ActionOption[] FourFingerTapActions() =>
    [
        Action("Notification Center", "Win+N"),
        Action("Start / Launchpad", "Win"),
        Action("Search / Spotlight", "Win+S"),
        Action("Show desktop", "Win+D"),
        Action("Do nothing", "none"),
    ];

    private static ActionOption Action(string label, string value) => new(label, value);
}

internal sealed record ActionOption(string Label, string Value)
{
    public override string ToString() => Label;
}

internal static class ThemePalette
{
    public static readonly Color Shell = Color.FromArgb(25, 26, 30);
    public static readonly Color Surface = Color.FromArgb(34, 35, 40);
    public static readonly Color SurfaceAlt = Color.FromArgb(44, 46, 52);
    public static readonly Color SurfaceRaised = Color.FromArgb(54, 56, 63);
    public static readonly Color Control = Color.FromArgb(19, 20, 24);
    public static readonly Color ControlHover = Color.FromArgb(45, 57, 76);
    public static readonly Color ControlPressed = Color.FromArgb(35, 86, 139);
    public static readonly Color Stroke = Color.FromArgb(75, 78, 88);
    public static readonly Color StrokeSoft = Color.FromArgb(54, 57, 65);
    public static readonly Color TextMain = Color.FromArgb(245, 245, 247);
    public static readonly Color TextMuted = Color.FromArgb(166, 169, 178);
    public static readonly Color Accent = Color.FromArgb(10, 132, 255);
    public static readonly Color AccentDim = Color.FromArgb(54, 116, 190);
    public static readonly Color AccentSurface = Color.FromArgb(31, 50, 76);
}

internal enum DeviceKind
{
    Trackpad,
    Keyboard,
}

internal sealed record DeviceTabInfo(DeviceKind Kind, string Title, HidDeviceInfo? Device, bool Connected, BatteryStatus Battery);

internal sealed class ThemedGroupBox : GroupBox
{
    public ThemedGroupBox()
    {
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var title = Text.Trim();
        var titleSize = TextRenderer.MeasureText(e.Graphics, title, Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
        var titleRect = new Rectangle(16, 0, titleSize.Width + 12, Math.Max(22, titleSize.Height + 2));
        var top = titleRect.Height / 2;

        using var border = new Pen(ThemePalette.StrokeSoft);
        e.Graphics.DrawLine(border, 0, top, Math.Max(0, titleRect.Left - 6), top);
        e.Graphics.DrawLine(border, titleRect.Right + 6, top, Width - 1, top);
        e.Graphics.DrawLine(border, 0, top, 0, Height - 1);
        e.Graphics.DrawLine(border, Width - 1, top, Width - 1, Height - 1);
        e.Graphics.DrawLine(border, 0, Height - 1, Width - 1, Height - 1);

        TextRenderer.DrawText(e.Graphics, title, Font, titleRect, ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

internal sealed class LevelMeter : Control
{
    public int? Value { get; init; }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var background = new SolidBrush(ThemePalette.Control);
        using var border = new Pen(ThemePalette.StrokeSoft);
        e.Graphics.FillRectangle(background, bounds);
        e.Graphics.DrawRectangle(border, bounds);

        var fillWidth = Value is int value
            ? Math.Max(0, (int)Math.Round((Width - 2) * Math.Clamp(value, 0, 100) / 100.0))
            : 0;
        if (fillWidth > 0)
        {
            using var fill = new LinearGradientBrush(new Rectangle(1, 1, fillWidth, Height - 2), ThemePalette.Accent, ThemePalette.AccentDim, LinearGradientMode.Horizontal);
            e.Graphics.FillRectangle(fill, 1, 1, fillWidth, Height - 2);
        }

        TextRenderer.DrawText(e.Graphics, Value is int percent ? $"{percent}%" : "--", Font, bounds, ThemePalette.TextMain, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

internal sealed class TrackpadPreview : Control
{
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var width = Math.Max(220, Width - 54);
        var height = Math.Min(170, (int)Math.Round(width * 0.64));
        var body = new Rectangle((Width - width) / 2, Math.Max(24, (Height - height) / 2 - 8), width, height);
        var side = new Rectangle(body.Left + 3, body.Bottom - 14, body.Width - 6, 22);
        var shadow = new Rectangle(body.Left + 10, side.Bottom - 2, body.Width - 20, 16);

        using (var shadowPath = Rounded(shadow, 18))
        using (var shadowBrush = new PathGradientBrush(shadowPath)
        {
            CenterColor = Color.FromArgb(105, 0, 0, 0),
            SurroundColors = [Color.FromArgb(0, 0, 0, 0)],
        })
        {
            e.Graphics.FillPath(shadowBrush, shadowPath);
        }

        using (var sidePath = Rounded(side, 14))
        using (var sideFill = new LinearGradientBrush(side, Color.FromArgb(205, 208, 214), Color.FromArgb(138, 143, 151), LinearGradientMode.Vertical))
        using (var sideBorder = new Pen(Color.FromArgb(118, 124, 134)))
        {
            e.Graphics.FillPath(sideFill, sidePath);
            e.Graphics.DrawPath(sideBorder, sidePath);
        }

        using var bodyPath = Rounded(body, 18);
        using var glassFill = new LinearGradientBrush(body, Color.FromArgb(249, 250, 252), Color.FromArgb(219, 222, 227), LinearGradientMode.Vertical);
        using var glassBorder = new Pen(Color.FromArgb(164, 169, 178));
        e.Graphics.FillPath(glassFill, bodyPath);
        e.Graphics.DrawPath(glassBorder, bodyPath);

        var inner = Rectangle.Inflate(body, -10, -10);
        using var highlight = new Pen(Color.FromArgb(248, 255, 255, 255), 2);
        e.Graphics.DrawArc(highlight, inner.Left, inner.Top, inner.Width, inner.Height, 202, 136);

        var bottomLine = new Rectangle(body.Left + 18, body.Bottom - 20, body.Width - 36, 1);
        using var glassLine = new LinearGradientBrush(bottomLine, Color.FromArgb(0, 255, 255, 255), Color.FromArgb(90, 255, 255, 255), LinearGradientMode.Horizontal);
        e.Graphics.FillRectangle(glassLine, bottomLine);
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
    private readonly record struct KeyboardKey(string Text, float Units);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var width = Math.Max(480, Width - 54);
        var height = Math.Min(205, (int)Math.Round(width * 0.36));
        var keyboard = new Rectangle((Width - width) / 2, Math.Max(24, (Height - height) / 2 - 4), width, height);

        using (var shadowPath = Rounded(new Rectangle(keyboard.Left + 10, keyboard.Bottom - 2, keyboard.Width - 20, 18), 20))
        using (var shadowBrush = new PathGradientBrush(shadowPath)
        {
            CenterColor = Color.FromArgb(105, 0, 0, 0),
            SurroundColors = [Color.FromArgb(0, 0, 0, 0)],
        })
        {
            e.Graphics.FillPath(shadowBrush, shadowPath);
        }

        using (var bodyPath = Rounded(keyboard, 14))
        using (var bodyFill = new LinearGradientBrush(keyboard, Color.FromArgb(224, 227, 232), Color.FromArgb(174, 179, 187), LinearGradientMode.Vertical))
        using (var border = new Pen(Color.FromArgb(128, 134, 143)))
        {
            e.Graphics.FillPath(bodyFill, bodyPath);
            e.Graphics.DrawPath(border, bodyPath);
        }

        var rows = KeyboardRows();
        const int padX = 15;
        const int padY = 14;
        const int rowGap = 6;
        const int keyGap = 5;
        var innerWidth = keyboard.Width - padX * 2;
        var keyHeight = (keyboard.Height - padY * 2 - rowGap * (rows.Length - 1)) / rows.Length;
        var y = keyboard.Top + padY;
        foreach (var row in rows)
        {
            var totalUnits = row.Sum(key => key.Units);
            var unit = (innerWidth - keyGap * (row.Length - 1)) / totalUnits;
            var rowWidth = (int)Math.Round(totalUnits * unit + keyGap * (row.Length - 1));
            var x = keyboard.Left + padX + Math.Max(0, (innerWidth - rowWidth) / 2);

            foreach (var key in row)
            {
                var keyWidth = (int)Math.Round(key.Units * unit);
                DrawKey(e.Graphics, new Rectangle(x, y, keyWidth, keyHeight), key.Text);
                x += keyWidth + keyGap;
            }
            y += keyHeight + rowGap;
        }
    }

    private static void DrawKey(Graphics graphics, Rectangle rect, string text)
    {
        using var shadowPath = Rounded(new Rectangle(rect.Left + 1, rect.Top + 1, rect.Width, rect.Height), 4);
        using var shadow = new SolidBrush(Color.FromArgb(35, 0, 0, 0));
        graphics.FillPath(shadow, shadowPath);

        using var path = Rounded(rect, 4);
        using var fill = new LinearGradientBrush(rect, Color.FromArgb(255, 255, 255), Color.FromArgb(236, 238, 242), LinearGradientMode.Vertical);
        using var border = new Pen(Color.FromArgb(174, 179, 187));
        graphics.FillPath(fill, path);
        graphics.DrawPath(border, path);

        if (text.Length == 0)
        {
            return;
        }

        using var font = new Font("Segoe UI", text.Length > 5 ? 6.2F : 7.4F);
        TextRenderer.DrawText(graphics, text, font, rect, Color.FromArgb(31, 35, 42), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static KeyboardKey[][] KeyboardRows() =>
    [
        [Key("esc", 1.1F), Key("F1", 1), Key("F2", 1), Key("F3", 1), Key("F4", 1), Key("F5", 1), Key("F6", 1), Key("F7", 1), Key("F8", 1), Key("F9", 1), Key("F10", 1), Key("F11", 1), Key("F12", 1), Key("pwr", 1.1F)],
        [Key("`", 1), Key("1", 1), Key("2", 1), Key("3", 1), Key("4", 1), Key("5", 1), Key("6", 1), Key("7", 1), Key("8", 1), Key("9", 1), Key("0", 1), Key("-", 1), Key("=", 1), Key("del", 1.6F)],
        [Key("tab", 1.45F), Key("Q", 1), Key("W", 1), Key("E", 1), Key("R", 1), Key("T", 1), Key("Y", 1), Key("U", 1), Key("I", 1), Key("O", 1), Key("P", 1), Key("[", 1), Key("]", 1), Key("\\", 1.15F)],
        [Key("caps", 1.7F), Key("A", 1), Key("S", 1), Key("D", 1), Key("F", 1), Key("G", 1), Key("H", 1), Key("J", 1), Key("K", 1), Key("L", 1), Key(";", 1), Key("'", 1), Key("return", 1.95F)],
        [Key("shift", 2.2F), Key("Z", 1), Key("X", 1), Key("C", 1), Key("V", 1), Key("B", 1), Key("N", 1), Key("M", 1), Key(",", 1), Key(".", 1), Key("/", 1), Key("shift", 2.35F)],
        [Key("fn", 1), Key("ctrl", 1.22F), Key("opt", 1.22F), Key("cmd", 1.55F), Key("", 5.35F), Key("cmd", 1.55F), Key("opt", 1.22F), Key("<", 1), Key("^", 1), Key("v", 1), Key(">", 1)],
    ];

    private static KeyboardKey Key(string text, float units)
    {
        return new KeyboardKey(text, units);
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
