using MagicTrackpad.Configuration;
using MagicTrackpad.Hid;
using MagicTrackpad.Runtime;
using MagicTrackpad.SelfTest;
using MagicTrackpad.Ui;

namespace MagicTrackpad;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var parsed = CommandLine.Parse(args);
        var configPath = parsed.ConfigPath ?? ConfigStore.DefaultConfigPath;

        try
        {
            if (parsed.Mode == AppMode.SelfTest)
            {
                return SelfTests.Run();
            }

            if (parsed.Mode == AppMode.Enable)
            {
                var ok = DeviceActions.EnableAllMagicTrackpads();
                return ok ? 0 : 2;
            }

            if (parsed.Mode == AppMode.WriteConfig)
            {
                ConfigStore.Save(configPath, new AppConfig());
                return 0;
            }

            if (parsed.Mode == AppMode.Bridge)
            {
                Application.Run(new BridgeApplicationContext(configPath, parsed.DryRun, parsed.Seconds));
                return 0;
            }

            Application.Run(new SettingsForm(configPath));
            return 0;
        }
        catch (Exception ex)
        {
            if (parsed.Mode == AppMode.Settings)
            {
                MessageBox.Show(ex.Message, "Magic Trackpad", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return 1;
        }
    }
}

internal enum AppMode
{
    Settings,
    Bridge,
    Enable,
    SelfTest,
    WriteConfig,
}

internal sealed class CommandLine
{
    public AppMode Mode { get; init; } = AppMode.Settings;
    public string? ConfigPath { get; init; }
    public bool DryRun { get; init; }
    public double? Seconds { get; init; }

    public static CommandLine Parse(string[] args)
    {
        var mode = AppMode.Settings;
        string? configPath = null;
        var dryRun = false;
        double? seconds = null;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg.ToLowerInvariant())
            {
                case "--settings":
                    mode = AppMode.Settings;
                    break;
                case "--bridge":
                case "run":
                    mode = AppMode.Bridge;
                    break;
                case "--enable":
                case "enable":
                    mode = AppMode.Enable;
                    break;
                case "--self-test":
                    mode = AppMode.SelfTest;
                    break;
                case "--write-config":
                case "write-config":
                    mode = AppMode.WriteConfig;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--config":
                    configPath = index + 1 < args.Length ? args[++index] : configPath;
                    break;
                case "--seconds":
                    if (index + 1 < args.Length && double.TryParse(args[++index], out var value))
                    {
                        seconds = value;
                    }
                    break;
            }
        }

        return new CommandLine
        {
            Mode = mode,
            ConfigPath = configPath,
            DryRun = dryRun,
            Seconds = seconds,
        };
    }
}
