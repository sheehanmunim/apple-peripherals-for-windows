using System.Text.Json;
using MagicTrackpad.Runtime;

namespace MagicTrackpad.Hid;

internal static class HidDiagnostics
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static int Run(string path, double seconds)
    {
        var captureSeconds = Math.Clamp(seconds, 1, 60);
        var devices = DeviceActions.EnumerateRawInputDevices();
        var trackpads = DeviceCatalog.FindMagicTrackpads(devices).ToList();
        var featureResults = trackpads
            .GroupBy(device => DeviceActions.PhysicalKey(device.Name))
            .Select(group => new FeatureResult(
                group.Key,
                group.Select(DeviceSnapshot.FromDevice).ToList(),
                DeviceActions.EnableAnyCollection(group)))
            .ToList();

        var sync = new object();
        var statuses = new List<DirectHidReaderStatus>();
        var reportStats = new Dictionary<string, MutableReportStats>(StringComparer.OrdinalIgnoreCase);

        void OnStatus(DirectHidReaderStatus status)
        {
            lock (sync)
            {
                statuses.Add(status);
            }
        }

        void OnReport(HidDeviceInfo device, byte[] report)
        {
            if (!device.IsAppleMagicTrackpad)
            {
                return;
            }

            lock (sync)
            {
                var key = device.Name;
                if (!reportStats.TryGetValue(key, out var stats))
                {
                    stats = new MutableReportStats(device);
                    reportStats[key] = stats;
                }

                stats.ReportCount++;
                if (report.Length > 0)
                {
                    stats.ReportIds.Add(report[0]);
                }

                if (stats.SampleReports.Count < 8)
                {
                    stats.SampleReports.Add(Convert.ToHexString(report));
                }

                var frames = HidReportParser.ParseReports(report);
                stats.ParsedFrameCount += frames.Count;
                stats.MaxTouches = Math.Max(stats.MaxTouches, frames.Select(frame => frame.ActiveTouches.Count).DefaultIfEmpty(0).Max());
            }
        }

        using (var reader = new DirectHidReportReader(OnReport, OnStatus))
        {
            reader.UpdateDevices(devices);
            Thread.Sleep(TimeSpan.FromSeconds(captureSeconds));
        }

        DiagnosticsDocument document;
        lock (sync)
        {
            document = new DiagnosticsDocument(
                DateTimeOffset.Now,
                captureSeconds,
                trackpads.Select(DeviceSnapshot.FromDevice).ToList(),
                featureResults,
                statuses,
                reportStats.Values.Select(stats => stats.ToReportStats()).ToList());
        }

        var targetPath = Path.IsPathRooted(path)
            ? path
            : Path.Combine(AppContext.BaseDirectory, path);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? AppContext.BaseDirectory);
        File.WriteAllText(targetPath, JsonSerializer.Serialize(document, JsonOptions));
        return document.Reports.Any(item => item.ParsedFrameCount > 0) ? 0 : 3;
    }

    private sealed record DiagnosticsDocument(
        DateTimeOffset CapturedAt,
        double Seconds,
        IReadOnlyList<DeviceSnapshot> Trackpads,
        IReadOnlyList<FeatureResult> FeatureResults,
        IReadOnlyList<DirectHidReaderStatus> ReaderStatuses,
        IReadOnlyList<ReportStats> Reports);

    private sealed record FeatureResult(
        string PhysicalKey,
        IReadOnlyList<DeviceSnapshot> Collections,
        bool EnableFeatureReportAccepted);

    private sealed record DeviceSnapshot(
        string Name,
        string ProductName,
        int? VendorId,
        int? ProductId,
        int? UsagePage,
        int? Usage,
        bool ReadableCandidate,
        bool Bluetooth)
    {
        public static DeviceSnapshot FromDevice(HidDeviceInfo device) =>
            new(
                device.Name,
                device.ProductName,
                device.VendorId,
                device.ProductId,
                device.UsagePage,
                device.Usage,
                DeviceActions.IsReadableTrackpadCollection(device),
                device.IsBluetooth);
    }

    private sealed record ReportStats(
        string DeviceName,
        string ProductName,
        int? UsagePage,
        int? Usage,
        int ReportCount,
        int ParsedFrameCount,
        int MaxTouches,
        IReadOnlyList<string> ReportIds,
        IReadOnlyList<string> SampleReports);

    private sealed class MutableReportStats
    {
        public MutableReportStats(HidDeviceInfo device)
        {
            Device = device;
        }

        public HidDeviceInfo Device { get; }
        public int ReportCount { get; set; }
        public int ParsedFrameCount { get; set; }
        public int MaxTouches { get; set; }
        public HashSet<byte> ReportIds { get; } = [];
        public List<string> SampleReports { get; } = [];

        public ReportStats ToReportStats() =>
            new(
                Device.Name,
                Device.ProductName,
                Device.UsagePage,
                Device.Usage,
                ReportCount,
                ParsedFrameCount,
                MaxTouches,
                ReportIds.Select(id => $"0x{id:X2}").Order().ToList(),
                SampleReports);
    }
}
