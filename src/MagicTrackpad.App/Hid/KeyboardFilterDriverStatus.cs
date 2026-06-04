using Microsoft.Win32;

namespace MagicTrackpad.Hid;

public sealed record KeyboardFilterDriverState(
    bool AppleKeyboardPresent,
    bool FilterTargetPresent,
    bool DriverStoreInstalled,
    bool FilterBound,
    string Diagnosis,
    string? TargetDriverInfPath,
    string? TargetService,
    IReadOnlyList<KeyboardFilterTargetState> Targets)
{
    public bool Ready => AppleKeyboardPresent && FilterTargetPresent && DriverStoreInstalled && FilterBound;
}

public sealed record KeyboardFilterTargetState(
    string InstanceId,
    bool FilterBound,
    string? DriverInfPath,
    string? Service,
    IReadOnlyList<string> LowerFilters);

public static class KeyboardFilterDriverStatus
{
    private const string FilterServiceName = "AppleKeyboardFilter";
    private static readonly string[] BluetoothPids = ["0320", "0267", "026C"];
    private static readonly string[] UsbPids = ["0321", "0267", "026C"];

    public static KeyboardFilterDriverState Query()
    {
        var appleKeyboardPresent = false;
        try
        {
            appleKeyboardPresent = DeviceCatalog.FindAppleKeyboards(DeviceActions.EnumerateRawInputDevices()).Count > 0;
        }
        catch
        {
            appleKeyboardPresent = false;
        }

        var targets = EnumerateTargets().ToList();
        var driverStoreInstalled = DriverStoreInstalled();
        return Evaluate(appleKeyboardPresent, targets, driverStoreInstalled);
    }

    internal static KeyboardFilterDriverState Evaluate(
        bool appleKeyboardPresent,
        IReadOnlyList<KeyboardFilterTargetState> targets,
        bool driverStoreInstalled)
    {
        var target = targets.FirstOrDefault(item => item.FilterBound) ?? targets.FirstOrDefault();
        var filterTargetPresent = targets.Count > 0;
        var filterBound = targets.Any(item => item.FilterBound);
        var diagnosis = DiagnosisFor(appleKeyboardPresent, filterTargetPresent, driverStoreInstalled, filterBound);

        return new KeyboardFilterDriverState(
            appleKeyboardPresent,
            filterTargetPresent,
            driverStoreInstalled,
            filterBound,
            diagnosis,
            target?.DriverInfPath,
            target?.Service,
            targets);
    }

    private static string DiagnosisFor(
        bool appleKeyboardPresent,
        bool filterTargetPresent,
        bool driverStoreInstalled,
        bool filterBound)
    {
        if (!appleKeyboardPresent)
        {
            return "Magic Keyboard not detected.";
        }

        if (!filterTargetPresent)
        {
            return "Keyboard driver target not found.";
        }

        if (!driverStoreInstalled)
        {
            return "Globe/Fn driver is not installed.";
        }

        if (!filterBound)
        {
            return "Globe/Fn driver is waiting for reconnect or restart.";
        }

        return "Globe/Fn driver is active.";
    }

    private static IEnumerable<KeyboardFilterTargetState> EnumerateTargets()
    {
        foreach (var target in EnumerateBluetoothTargets())
        {
            yield return target;
        }

        foreach (var target in EnumerateUsbTargets())
        {
            yield return target;
        }
    }

    private static IEnumerable<KeyboardFilterTargetState> EnumerateBluetoothTargets()
    {
        using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\BTHENUM");
        if (root == null)
        {
            yield break;
        }

        foreach (var parentName in root.GetSubKeyNames())
        {
            if (!BluetoothPids.Any(pid => parentName.Contains($"VID&0001004C_PID&{pid}", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            using var parent = root.OpenSubKey(parentName);
            if (parent == null)
            {
                continue;
            }

            foreach (var instanceName in parent.GetSubKeyNames())
            {
                using var instance = parent.OpenSubKey(instanceName);
                if (instance != null)
                {
                    yield return TargetFromRegistryKey($"BTHENUM\\{parentName}\\{instanceName}", instance);
                }
            }
        }
    }

    private static IEnumerable<KeyboardFilterTargetState> EnumerateUsbTargets()
    {
        using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
        if (root == null)
        {
            yield break;
        }

        foreach (var parentName in root.GetSubKeyNames())
        {
            if (!UsbPids.Any(pid => parentName.Contains($"VID_05AC&PID_{pid}", StringComparison.OrdinalIgnoreCase)) ||
                !parentName.Contains("MI_01", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var parent = root.OpenSubKey(parentName);
            if (parent == null)
            {
                continue;
            }

            foreach (var instanceName in parent.GetSubKeyNames())
            {
                using var instance = parent.OpenSubKey(instanceName);
                if (instance != null)
                {
                    yield return TargetFromRegistryKey($"USB\\{parentName}\\{instanceName}", instance);
                }
            }
        }
    }

    private static KeyboardFilterTargetState TargetFromRegistryKey(string instanceId, RegistryKey instance)
    {
        var lowerFilters = RegistryStringArray(instance.GetValue("LowerFilters"));
        var driverKey = instance.GetValue("Driver") as string;
        var service = instance.GetValue("Service") as string;
        var driverInfPath = DriverInfPath(driverKey);
        var filterBound = lowerFilters.Any(item => string.Equals(item, FilterServiceName, StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(driverInfPath, "AppleKeyboardFilter.inf", StringComparison.OrdinalIgnoreCase);

        return new KeyboardFilterTargetState(instanceId, filterBound, driverInfPath, service, lowerFilters);
    }

    private static string? DriverInfPath(string? driverKey)
    {
        if (string.IsNullOrWhiteSpace(driverKey))
        {
            return null;
        }

        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Control\Class\{driverKey}");
        return key?.GetValue("InfPath") as string;
    }

    private static bool DriverStoreInstalled()
    {
        var repository = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "DriverStore",
            "FileRepository");
        try
        {
            return Directory.Exists(repository) &&
                Directory.EnumerateDirectories(repository, "applekeyboardfilter.inf_*").Any();
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<string> RegistryStringArray(object? value)
    {
        return value switch
        {
            string[] array => array,
            string text when !string.IsNullOrWhiteSpace(text) => [text],
            _ => [],
        };
    }
}
