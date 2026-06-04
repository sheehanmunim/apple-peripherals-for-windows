using System.Text.RegularExpressions;

namespace MagicTrackpad.Hid;

public sealed record HidDeviceInfo(
    IntPtr Handle,
    string Name,
    int? VendorId,
    int? ProductId,
    int? VersionNumber,
    int? UsagePage,
    int? Usage)
{
    public string ProductName => ProductId switch
    {
        DeviceCatalog.MagicTrackpad => "Magic Trackpad",
        DeviceCatalog.MagicTrackpad2 => "Magic Trackpad 2",
        DeviceCatalog.MagicTrackpad2UsbC => "Magic Trackpad USB-C",
        DeviceCatalog.MagicKeyboardUsbC => "Magic Keyboard USB-C",
        DeviceCatalog.MagicKeyboardBluetooth => "Magic Keyboard",
        _ => "Unknown HID device",
    };

    public bool IsAppleMagicTrackpad =>
        (VendorId is DeviceCatalog.AppleUsbVendorId or DeviceCatalog.AppleBluetoothVendorId || PathMentionsApple()) &&
        ProductId is DeviceCatalog.MagicTrackpad or DeviceCatalog.MagicTrackpad2 or DeviceCatalog.MagicTrackpad2UsbC;

    public bool IsAppleKeyboard =>
        (VendorId is DeviceCatalog.AppleUsbVendorId or DeviceCatalog.AppleBluetoothVendorId || PathMentionsApple()) &&
        !IsAppleMagicTrackpad &&
        (ProductId is DeviceCatalog.MagicKeyboardBluetooth or DeviceCatalog.MagicKeyboardUsbC ||
            UsagePage == 0x01 && Usage == 0x06 ||
            Name.Contains("COL01", StringComparison.OrdinalIgnoreCase));

    public bool IsBluetooth =>
        VendorId == DeviceCatalog.AppleBluetoothVendorId ||
        Name.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase) ||
        Name.Contains("VID_004C", StringComparison.OrdinalIgnoreCase);

    private bool PathMentionsApple() =>
        Name.Contains("VID_05AC", StringComparison.OrdinalIgnoreCase) ||
        Name.Contains("VID_004C", StringComparison.OrdinalIgnoreCase);
}

public static partial class DeviceCatalog
{
    public const int AppleUsbVendorId = 0x05AC;
    public const int AppleBluetoothVendorId = 0x004C;
    public const int MagicTrackpad = 0x030E;
    public const int MagicTrackpad2 = 0x0265;
    public const int MagicTrackpad2UsbC = 0x0324;
    public const int MagicKeyboardBluetooth = 0x0320;
    public const int MagicKeyboardUsbC = 0x0321;

    public static IReadOnlyList<HidDeviceInfo> FindMagicTrackpads(IEnumerable<HidDeviceInfo> devices) =>
        devices.Where(device => device.IsAppleMagicTrackpad).ToList();

    public static IReadOnlyList<HidDeviceInfo> FindAppleKeyboards(IEnumerable<HidDeviceInfo> devices) =>
        devices.Where(device => device.IsAppleKeyboard).ToList();

    public static byte[]? MultitouchFeatureReport(HidDeviceInfo device)
    {
        return device.ProductId switch
        {
            MagicTrackpad2 or MagicTrackpad2UsbC when device.IsBluetooth => [0xF1, 0x02, 0x01],
            MagicTrackpad2 or MagicTrackpad2UsbC => [0x02, 0x01],
            MagicTrackpad => [0xD7, 0x01],
            _ => null,
        };
    }

    public static (int? VendorId, int? ProductId) ParseVidPid(string name)
    {
        foreach (var regex in VidPidPatterns())
        {
            var match = regex.Match(name);
            if (!match.Success)
            {
                continue;
            }

            var vendorText = match.Groups[1].Value;
            var productText = match.Groups[2].Value;
            var vendor = Convert.ToInt32(vendorText[^4..], 16);
            var product = Convert.ToInt32(productText, 16);
            return (vendor, product);
        }

        return (null, null);
    }

    [GeneratedRegex(@"(?:VID|VEN)_([0-9A-F]{4}).*?(?:PID|DEV)_([0-9A-F]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex UsbVidPidRegex();

    [GeneratedRegex(@"(?:VID|VEN)&([0-9A-F]{4,8}).*?(?:PID|DEV)&([0-9A-F]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex BluetoothVidPidRegex();

    private static IEnumerable<Regex> VidPidPatterns()
    {
        yield return UsbVidPidRegex();
        yield return BluetoothVidPidRegex();
    }
}
