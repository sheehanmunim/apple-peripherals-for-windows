using MagicTrackpad.Configuration;
using MagicTrackpad.Gestures;
using MagicTrackpad.Hid;
using MagicTrackpad.Input;

namespace MagicTrackpad.Runtime;

internal sealed class BridgeApplicationContext : ApplicationContext
{
    private readonly string configPath;
    private AppConfig config;
    private readonly IInputInjector injector;
    private readonly GestureEngine engine;
    private readonly System.Windows.Forms.Timer reenableTimer = new();
    private readonly System.Windows.Forms.Timer reloadTimer = new();
    private readonly System.Windows.Forms.Timer? stopTimer;
    private readonly NotifyIcon notifyIcon;
    private readonly HashSet<string> announced = [];
    private readonly HashSet<string> enabled = [];
    private readonly RawInputWindow window;
    private DateTime lastConfigWrite;

    public BridgeApplicationContext(string configPath, bool dryRun, double? seconds)
    {
        this.configPath = configPath;
        config = ConfigStore.Load(configPath);
        lastConfigWrite = ConfigLastWrite();
        injector = dryRun ? new DryRunInputInjector() : new Win32InputInjector();
        engine = new GestureEngine(injector, config.Gestures);

        notifyIcon = new NotifyIcon
        {
            Text = "Magic Trackpad Bridge",
            Icon = SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };

        window = new RawInputWindow(OnReport, OnDevicesChanged);
        reenableTimer.Interval = Math.Max(3000, (int)(config.ReenableIntervalSeconds * 1000));
        reenableTimer.Tick += (_, _) => ReenableTrackpads();
        reenableTimer.Start();

        reloadTimer.Interval = 2000;
        reloadTimer.Tick += (_, _) => ReloadConfigIfChanged();
        reloadTimer.Start();

        if (seconds != null)
        {
            stopTimer = new System.Windows.Forms.Timer { Interval = Math.Max(1, (int)(seconds.Value * 1000)) };
            stopTimer.Tick += (_, _) => ExitThread();
            stopTimer.Start();
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings", null, (_, _) => ShowSettings());
        menu.Items.Add("Refresh Trackpad", null, (_, _) => ReenableTrackpads());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        return menu;
    }

    private void ShowSettings()
    {
        var form = new Ui.SettingsForm(configPath);
        form.FormClosed += (_, _) => ReloadConfig(force: true);
        form.Show();
    }

    private void ReloadConfigIfChanged()
    {
        var writeTime = ConfigLastWrite();
        if (writeTime > lastConfigWrite)
        {
            ReloadConfig(force: true);
        }
    }

    private void ReloadConfig(bool force = false)
    {
        var writeTime = ConfigLastWrite();
        if (!force && writeTime <= lastConfigWrite)
        {
            return;
        }

        config = ConfigStore.Load(configPath);
        engine.UpdateConfig(config.Gestures);
        reenableTimer.Interval = Math.Max(3000, (int)(config.ReenableIntervalSeconds * 1000));
        lastConfigWrite = writeTime;
    }

    private DateTime ConfigLastWrite() =>
        File.Exists(configPath) ? File.GetLastWriteTimeUtc(configPath) : DateTime.MinValue;

    private void OnDevicesChanged(IReadOnlyList<HidDeviceInfo> devices)
    {
        var groups = DeviceCatalog.FindMagicTrackpads(devices).GroupBy(device => DeviceActions.PhysicalKey(device.Name));
        foreach (var group in groups)
        {
            if (announced.Add(group.Key) && config.EnableMultitouchOnStart)
            {
                if (DeviceActions.EnableAnyCollection(group))
                {
                    enabled.Add(group.Key);
                }
            }
        }
    }

    private void ReenableTrackpads()
    {
        if (!config.EnableMultitouchOnStart)
        {
            return;
        }

        foreach (var group in DeviceCatalog.FindMagicTrackpads(DeviceActions.EnumerateRawInputDevices()).GroupBy(device => DeviceActions.PhysicalKey(device.Name)))
        {
            if (DeviceActions.EnableAnyCollection(group))
            {
                enabled.Add(group.Key);
            }
        }
    }

    private void OnReport(HidDeviceInfo device, byte[] report)
    {
        if (config.LogRawReports)
        {
            LogRawReport(device, report);
        }

        foreach (var frame in HidReportParser.ParseReports(report))
        {
            engine.ProcessFrame(frame);
        }
    }

    private void LogRawReport(HidDeviceInfo device, byte[] report)
    {
        var path = Path.IsPathRooted(config.RawLogPath)
            ? config.RawLogPath
            : Path.Combine(AppContext.BaseDirectory, config.RawLogPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? AppContext.BaseDirectory);
        File.AppendAllText(path, $"{device.ProductName} {device.Name} {Convert.ToHexString(report)}{Environment.NewLine}");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            stopTimer?.Dispose();
            reenableTimer.Dispose();
            reloadTimer.Dispose();
            window.Dispose();
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
