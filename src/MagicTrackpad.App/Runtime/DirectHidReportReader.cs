using System.Runtime.InteropServices;
using MagicTrackpad.Hid;
using MagicTrackpad.Interop;

namespace MagicTrackpad.Runtime;

internal sealed class DirectHidReportReader : IDisposable
{
    private readonly Action<HidDeviceInfo, byte[]> onReport;
    private readonly Action<DirectHidReaderStatus>? onStatus;
    private readonly object sync = new();
    private readonly Dictionary<string, ReaderWorker> workers = new(StringComparer.OrdinalIgnoreCase);
    private bool disposed;

    public DirectHidReportReader(Action<HidDeviceInfo, byte[]> onReport, Action<DirectHidReaderStatus>? onStatus = null)
    {
        this.onReport = onReport;
        this.onStatus = onStatus;
    }

    public void UpdateDevices(IEnumerable<HidDeviceInfo> devices)
    {
        var candidates = devices
            .Where(device => DeviceActions.IsReadableTrackpadCollection(device) || DeviceActions.IsReadableAppleKeyboardCollection(device))
            .GroupBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            var candidateNames = candidates.Select(device => device.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var stale in workers.Keys.Where(name => !candidateNames.Contains(name)).ToList())
            {
                workers[stale].Dispose();
                workers.Remove(stale);
            }

            foreach (var device in candidates)
            {
                if (workers.ContainsKey(device.Name))
                {
                    continue;
                }

                var worker = ReaderWorker.Start(device, onReport, out var openError);
                if (worker != null)
                {
                    workers[device.Name] = worker;
                    onStatus?.Invoke(DirectHidReaderStatus.Opened(device, worker.ReportLength));
                }
                else
                {
                    onStatus?.Invoke(DirectHidReaderStatus.OpenFailed(device, openError));
                }
            }
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            foreach (var worker in workers.Values)
            {
                worker.Dispose();
            }

            workers.Clear();
        }
    }

    private sealed class ReaderWorker : IDisposable
    {
        private readonly HidDeviceInfo device;
        private readonly Action<HidDeviceInfo, byte[]> onReport;
        private readonly IntPtr handle;
        private readonly int reportLength;
        private readonly Thread thread;
        private volatile bool stopping;

        public int ReportLength => reportLength;

        private ReaderWorker(HidDeviceInfo device, Action<HidDeviceInfo, byte[]> onReport, IntPtr handle, int reportLength)
        {
            this.device = device;
            this.onReport = onReport;
            this.handle = handle;
            this.reportLength = Math.Clamp(reportLength, 3, 4096);
            thread = new Thread(ReadLoop)
            {
                IsBackground = true,
                Name = $"Apple HID reader {device.ProductName}",
            };
        }

        public static ReaderWorker? Start(HidDeviceInfo device, Action<HidDeviceInfo, byte[]> onReport, out int? openError)
        {
            var handle = NativeMethods.CreateFile(
                device.Name,
                NativeMethods.GenericRead | NativeMethods.GenericWrite,
                NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
                IntPtr.Zero,
                NativeMethods.OpenExisting,
                NativeMethods.FileAttributeNormal,
                IntPtr.Zero);
            openError = handle == IntPtr.Zero || handle == new IntPtr(-1)
                ? Marshal.GetLastWin32Error()
                : null;

            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                handle = NativeMethods.CreateFile(
                    device.Name,
                    NativeMethods.GenericRead,
                    NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
                    IntPtr.Zero,
                    NativeMethods.OpenExisting,
                    NativeMethods.FileAttributeNormal,
                    IntPtr.Zero);
                openError = handle == IntPtr.Zero || handle == new IntPtr(-1)
                    ? Marshal.GetLastWin32Error()
                    : null;
            }

            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                return null;
            }

            var worker = new ReaderWorker(device, onReport, handle, DeviceActions.InputReportLength(handle));
            worker.thread.Start();
            return worker;
        }

        private void ReadLoop()
        {
            while (!stopping)
            {
                var buffer = new byte[reportLength];
                if (NativeMethods.ReadFile(handle, buffer, (uint)buffer.Length, out var bytesRead, IntPtr.Zero))
                {
                    if (bytesRead > 0)
                    {
                        var report = buffer.Take((int)Math.Min(bytesRead, (uint)buffer.Length)).ToArray();
                        try
                        {
                            onReport(device, report);
                        }
                        catch
                        {
                            // Report processing must not kill the HID reader thread.
                        }
                    }

                    continue;
                }

                var error = Marshal.GetLastWin32Error();
                if (stopping || error is NativeMethods.ErrorOperationAborted or NativeMethods.ErrorInvalidHandle)
                {
                    return;
                }

                Thread.Sleep(250);
            }
        }

        public void Dispose()
        {
            stopping = true;
            try
            {
                NativeMethods.CancelIoEx(handle, IntPtr.Zero);
            }
            catch
            {
                // The handle may already be closing; the thread exits on the read error.
            }

            NativeMethods.CloseHandle(handle);
        }
    }
}

internal sealed record DirectHidReaderStatus(
    string DeviceName,
    string ProductName,
    int? UsagePage,
    int? Usage,
    string State,
    int? ReportLength,
    int? Error)
{
    public static DirectHidReaderStatus Opened(HidDeviceInfo device, int reportLength) =>
        new(device.Name, device.ProductName, device.UsagePage, device.Usage, "opened", reportLength, null);

    public static DirectHidReaderStatus OpenFailed(HidDeviceInfo device, int? error) =>
        new(device.Name, device.ProductName, device.UsagePage, device.Usage, "open_failed", null, error);
}
