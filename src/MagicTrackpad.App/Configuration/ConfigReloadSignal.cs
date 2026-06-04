using System.Security.Cryptography;
using System.Text;

namespace MagicTrackpad.Configuration;

internal static class ConfigReloadSignal
{
    private const string EventPrefix = @"Local\ApplePeripheralsForWindows.ConfigReload.";

    public static string NameFor(string configPath)
    {
        var normalized = Path.GetFullPath(configPath).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return EventPrefix + hash[..24];
    }

    public static EventWaitHandle Create(string configPath) =>
        new(false, EventResetMode.AutoReset, NameFor(configPath));

    public static void Notify(string configPath)
    {
        try
        {
            using var signal = EventWaitHandle.OpenExisting(NameFor(configPath));
            signal.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // The settings app can run without the bridge; the next bridge start loads the saved file.
        }
    }
}
