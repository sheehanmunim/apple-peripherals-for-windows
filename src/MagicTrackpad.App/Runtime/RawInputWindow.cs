using System.ComponentModel;
using System.Runtime.InteropServices;
using MagicTrackpad.Hid;
using MagicTrackpad.Interop;

namespace MagicTrackpad.Runtime;

internal sealed class RawInputWindow : NativeWindow, IDisposable
{
    private readonly Action<HidDeviceInfo, byte[]> onReport;
    private readonly Action<IReadOnlyList<HidDeviceInfo>>? onDevicesChanged;
    private Dictionary<IntPtr, HidDeviceInfo> devicesByHandle = [];

    public RawInputWindow(Action<HidDeviceInfo, byte[]> onReport, Action<IReadOnlyList<HidDeviceInfo>>? onDevicesChanged = null)
    {
        this.onReport = onReport;
        this.onDevicesChanged = onDevicesChanged;
        CreateHandle(new CreateParams { Caption = "Apple Peripherals Bridge" });
        RefreshDevices();
        RegisterRawInput();
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == NativeMethods.WM_INPUT)
        {
            var read = ReadRawInput(message.LParam);
            if (read != null && devicesByHandle.TryGetValue(read.Value.DeviceHandle, out var device) && device.IsAppleMagicTrackpad)
            {
                DispatchReports(device, read.Value.Payload);
            }
            return;
        }

        if (message.Msg == NativeMethods.WM_INPUT_DEVICE_CHANGE)
        {
            RefreshDevices();
            return;
        }

        base.WndProc(ref message);
    }

    public void RefreshDevices()
    {
        var devices = DeviceActions.EnumerateRawInputDevices();
        devicesByHandle = devices.ToDictionary(device => device.Handle);
        onDevicesChanged?.Invoke(devices);
    }

    private void RegisterRawInput()
    {
        var records = new[]
        {
            new NativeMethods.RawInputDevice { UsagePage = 0x01, Usage = 0x02, Flags = NativeMethods.RIDEV_INPUTSINK | NativeMethods.RIDEV_DEVNOTIFY, Target = Handle },
            new NativeMethods.RawInputDevice { UsagePage = 0x01, Usage = 0x06, Flags = NativeMethods.RIDEV_INPUTSINK | NativeMethods.RIDEV_DEVNOTIFY, Target = Handle },
            new NativeMethods.RawInputDevice { UsagePage = 0x0D, Usage = 0x05, Flags = NativeMethods.RIDEV_INPUTSINK | NativeMethods.RIDEV_DEVNOTIFY, Target = Handle },
            new NativeMethods.RawInputDevice { UsagePage = 0xFF00, Usage = 0x0B, Flags = NativeMethods.RIDEV_INPUTSINK | NativeMethods.RIDEV_DEVNOTIFY, Target = Handle },
            new NativeMethods.RawInputDevice { UsagePage = 0xFF00, Usage = 0x14, Flags = NativeMethods.RIDEV_INPUTSINK | NativeMethods.RIDEV_DEVNOTIFY, Target = Handle },
        };

        if (!NativeMethods.RegisterRawInputDevices(records, (uint)records.Length, (uint)Marshal.SizeOf<NativeMethods.RawInputDevice>()))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static (IntPtr DeviceHandle, byte[] Payload)? ReadRawInput(IntPtr lParam)
    {
        var size = 0u;
        var headerSize = (uint)Marshal.SizeOf<NativeMethods.RawInputHeader>();
        if (NativeMethods.GetRawInputData(lParam, NativeMethods.RID_INPUT, IntPtr.Zero, ref size, headerSize) == uint.MaxValue)
        {
            return null;
        }

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (NativeMethods.GetRawInputData(lParam, NativeMethods.RID_INPUT, buffer, ref size, headerSize) == uint.MaxValue)
            {
                return null;
            }

            var header = Marshal.PtrToStructure<NativeMethods.RawInputHeader>(buffer);
            if (header.Type != NativeMethods.RIM_TYPEHID)
            {
                return null;
            }

            var offset = Marshal.SizeOf<NativeMethods.RawInputHeader>();
            var hidSize = Marshal.ReadInt32(buffer, offset);
            var hidCount = Marshal.ReadInt32(buffer, offset + 4);
            var payloadLength = hidSize * hidCount;
            var payload = new byte[payloadLength];
            Marshal.Copy(IntPtr.Add(buffer, offset + 8), payload, 0, payloadLength);
            return (header.Device, payload);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void DispatchReports(HidDeviceInfo device, byte[] payload)
    {
        var size = GuessReportSize(payload);
        if (size == null)
        {
            onReport(device, payload);
            return;
        }

        for (var offset = 0; offset < payload.Length; offset += size.Value)
        {
            var report = payload.Skip(offset).Take(size.Value).ToArray();
            if (report.Length > 0)
            {
                onReport(device, report);
            }
        }
    }

    private static int? GuessReportSize(byte[] payload)
    {
        if (payload.Length == 0)
        {
            return null;
        }

        var reportId = payload[0];
        if ((reportId == ReportIds.Trackpad || reportId == ReportIds.Trackpad2Bluetooth) && payload.Length >= 13 && (payload.Length - 4) % 9 == 0)
        {
            return payload.Length;
        }

        if (reportId == ReportIds.Trackpad2Usb && payload.Length >= 21 && (payload.Length - 12) % 9 == 0)
        {
            return payload.Length;
        }

        if (reportId == ReportIds.DoubleReport)
        {
            return payload.Length;
        }

        return null;
    }

    public void Dispose()
    {
        DestroyHandle();
    }
}
