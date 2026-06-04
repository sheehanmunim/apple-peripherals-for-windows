using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using MagicTrackpad.Interop;

namespace MagicTrackpad.Hid;

public static class DeviceActions
{
    private const int HidpStatusSuccess = 0x00110000;

    public static IReadOnlyList<HidDeviceInfo> EnumerateRawInputDevices()
    {
        var count = 0u;
        var result = NativeMethods.GetRawInputDeviceList(null, ref count, (uint)Marshal.SizeOf<NativeMethods.RawInputDeviceList>());
        if (result == uint.MaxValue)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var records = new NativeMethods.RawInputDeviceList[count];
        result = NativeMethods.GetRawInputDeviceList(records, ref count, (uint)Marshal.SizeOf<NativeMethods.RawInputDeviceList>());
        if (result == uint.MaxValue)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var devices = new List<HidDeviceInfo>();
        foreach (var record in records)
        {
            var info = DeviceInfoFromHandle(record.Device);
            if (info != null)
            {
                devices.Add(info);
            }
        }

        return devices;
    }

    public static bool EnableAllMagicTrackpads()
    {
        var groups = DeviceCatalog.FindMagicTrackpads(EnumerateRawInputDevices())
            .GroupBy(device => PhysicalKey(device.Name));
        var any = false;
        foreach (var group in groups)
        {
            any |= EnableAnyCollection(group);
        }

        return any;
    }

    public static bool EnableAnyCollection(IEnumerable<HidDeviceInfo> devices)
    {
        foreach (var device in devices)
        {
            var report = DeviceCatalog.MultitouchFeatureReport(device);
            if (report != null && SendFeatureReport(device, report))
            {
                return true;
            }
        }

        return false;
    }

    public static bool SendFeatureReport(HidDeviceInfo device, byte[] report)
    {
        var accessAttempts = new[] { NativeMethods.GenericRead | NativeMethods.GenericWrite, NativeMethods.GenericWrite, 0u };
        foreach (var access in accessAttempts)
        {
            var handle = NativeMethods.CreateFile(
                device.Name,
                access,
                NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
                IntPtr.Zero,
                NativeMethods.OpenExisting,
                NativeMethods.FileAttributeNormal,
                IntPtr.Zero);

            if (handle == new IntPtr(-1))
            {
                continue;
            }

            try
            {
                if (NativeMethods.HidD_SetFeature(handle, report, (uint)report.Length))
                {
                    return true;
                }

                var error = Marshal.GetLastWin32Error();
                if (error == 1 && device.IsAppleMagicTrackpad)
                {
                    return true;
                }
            }
            finally
            {
                NativeMethods.CloseHandle(handle);
            }
        }

        return false;
    }

    public static int InputReportLength(IntPtr handle)
    {
        if (!NativeMethods.HidD_GetPreparsedData(handle, out var preparsedData))
        {
            return 64;
        }

        try
        {
            return NativeMethods.HidP_GetCaps(preparsedData, out var caps) == HidpStatusSuccess
                ? Math.Max(3, (int)caps.InputReportByteLength)
                : 64;
        }
        finally
        {
            NativeMethods.HidD_FreePreparsedData(preparsedData);
        }
    }

    public static bool IsReadableTrackpadCollection(HidDeviceInfo device)
    {
        if (!device.IsAppleMagicTrackpad)
        {
            return false;
        }

        if (device.UsagePage is null)
        {
            return true;
        }

        return device.UsagePage == 0x0D ||
            device.UsagePage >= 0xFF00 ||
            device.Name.Contains("COL02", StringComparison.OrdinalIgnoreCase) ||
            device.Name.Contains("COL03", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsReadableAppleKeyboardCollection(HidDeviceInfo device)
    {
        if (!device.IsAppleKeyboard)
        {
            return false;
        }

        if (device.UsagePage == 0x01 && device.Usage == 0x06)
        {
            return true;
        }

        return device.Name.Contains("COL01", StringComparison.OrdinalIgnoreCase) ||
            device.Name.Contains("COL02", StringComparison.OrdinalIgnoreCase);
    }

    public static string PhysicalKey(string name)
    {
        var marker = name.IndexOf("&Col", StringComparison.OrdinalIgnoreCase);
        return marker >= 0 ? name[..marker].ToLowerInvariant() : name.ToLowerInvariant();
    }

    private static HidDeviceInfo? DeviceInfoFromHandle(IntPtr handle)
    {
        var nameSize = 0u;
        NativeMethods.GetRawInputDeviceInfo(handle, NativeMethods.RIDI_DEVICENAME, (StringBuilder?)null, ref nameSize);
        if (nameSize == 0)
        {
            return null;
        }

        var builder = new StringBuilder((int)nameSize + 1);
        if (NativeMethods.GetRawInputDeviceInfo(handle, NativeMethods.RIDI_DEVICENAME, builder, ref nameSize) == uint.MaxValue)
        {
            return null;
        }

        var name = builder.ToString();
        var size = (uint)Marshal.SizeOf<NativeMethods.RidDeviceInfo>();
        var pointer = Marshal.AllocHGlobal((int)size);
        try
        {
            Marshal.StructureToPtr(new NativeMethods.RidDeviceInfo { CbSize = size }, pointer, false);
            if (NativeMethods.GetRawInputDeviceInfo(handle, NativeMethods.RIDI_DEVICEINFO, pointer, ref size) == uint.MaxValue)
            {
                var parsed = DeviceCatalog.ParseVidPid(name);
                return new HidDeviceInfo(handle, name, parsed.VendorId, parsed.ProductId, null, null, null);
            }

            var info = Marshal.PtrToStructure<NativeMethods.RidDeviceInfo>(pointer);
            var pathParsed = DeviceCatalog.ParseVidPid(name);
            int? vendorId = null;
            int? productId = null;
            int? versionNumber = null;
            int? usagePage = null;
            int? usage = null;
            if (info.Type == NativeMethods.RIM_TYPEHID)
            {
                vendorId = (int)info.Union.Hid.VendorId;
                productId = (int)info.Union.Hid.ProductId;
                versionNumber = (int)info.Union.Hid.VersionNumber;
                usagePage = info.Union.Hid.UsagePage;
                usage = info.Union.Hid.Usage;
            }

            return new HidDeviceInfo(
                handle,
                name,
                vendorId ?? pathParsed.VendorId,
                productId ?? pathParsed.ProductId,
                versionNumber,
                usagePage,
                usage);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }
}
