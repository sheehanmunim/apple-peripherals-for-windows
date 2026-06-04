namespace MagicTrackpad.Hid;

public static class ReportIds
{
    public const byte Trackpad = 0x28;
    public const byte Trackpad2Usb = 0x02;
    public const byte Trackpad2Bluetooth = 0x31;
    public const byte DoubleReport = 0xF7;
}

public sealed record Touch(
    int TrackingId,
    int X,
    int Y,
    int Size,
    int Orientation,
    int TouchMajor,
    int TouchMinor,
    int Pressure,
    bool Down);

public sealed record TrackpadFrame(byte ReportId, byte Clicks, IReadOnlyList<Touch> Touches, byte[] Raw)
{
    public IReadOnlyList<Touch> ActiveTouches => Touches.Where(touch => touch.Down).ToList();
}

public static class HidReportParser
{
    public static IReadOnlyList<TrackpadFrame> ParseReports(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return [];
        }

        var reportId = data[0];
        if (reportId == ReportIds.DoubleReport)
        {
            if (data.Length < 3)
            {
                return [];
            }

            var firstSize = data[1];
            var first = data.Slice(2, Math.Min(firstSize, data.Length - 2));
            var second = data[(2 + first.Length)..];
            return ParseReports(first).Concat(ParseReports(second)).ToList();
        }

        if (reportId is ReportIds.Trackpad or ReportIds.Trackpad2Bluetooth)
        {
            return ParseFrame(data, 4, reportId == ReportIds.Trackpad2Bluetooth);
        }

        if (reportId == ReportIds.Trackpad2Usb)
        {
            return ParseFrame(data, 12, true);
        }

        return [];
    }

    private static IReadOnlyList<TrackpadFrame> ParseFrame(ReadOnlySpan<byte> data, int prefixSize, bool trackpad2)
    {
        if (data.Length < prefixSize || (data.Length - prefixSize) % 9 != 0)
        {
            return [];
        }

        var count = (data.Length - prefixSize) / 9;
        if (count > 15)
        {
            return [];
        }

        var touches = new List<Touch>(count);
        for (var offset = prefixSize; offset < data.Length; offset += 9)
        {
            var chunk = data.Slice(offset, 9);
            touches.Add(trackpad2 ? ParseTrackpad2Touch(chunk) : ParseLegacyTouch(chunk));
        }

        return [new TrackpadFrame(data[0], data[1], touches, data.ToArray())];
    }

    private static Touch ParseTrackpad2Touch(ReadOnlySpan<byte> tdata)
    {
        var trackingId = tdata[8] & 0x0F;
        var x = Sar32((tdata[1] << 27) | (tdata[0] << 19), 19);
        var y = -Sar32((tdata[3] << 30) | (tdata[2] << 22) | (tdata[1] << 14), 19);
        var state = tdata[3] & 0xC0;
        return new Touch(
            trackingId,
            x,
            y,
            tdata[6],
            (tdata[8] >> 5) - 4,
            tdata[4],
            tdata[5],
            tdata[7],
            state == 0x80);
    }

    private static Touch ParseLegacyTouch(ReadOnlySpan<byte> tdata)
    {
        var trackingId = ((tdata[7] << 2) | (tdata[6] >> 6)) & 0x0F;
        var x = Sar32((tdata[1] << 27) | (tdata[0] << 19), 19);
        var y = -Sar32((tdata[3] << 30) | (tdata[2] << 22) | (tdata[1] << 14), 19);
        var state = tdata[8] & 0xF0;
        return new Touch(
            trackingId,
            x,
            y,
            tdata[6] & 0x3F,
            (tdata[7] >> 2) - 32,
            tdata[4],
            tdata[5],
            0,
            state != 0);
    }

    private static int Sar32(int value, int shift) => value >> shift;

    public static (double X, double Y) Centroid(IReadOnlyList<Touch> touches)
    {
        if (touches.Count == 0)
        {
            return (0, 0);
        }

        return (touches.Average(t => t.X), touches.Average(t => t.Y));
    }

    public static double Distance(Touch first, Touch second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

