namespace MagicTrackpad.Input;

public static class Hotkeys
{
    private static readonly Dictionary<string, ushort> KeyNameToVk = BuildKeyMap();

    public static IReadOnlyList<ushort> Parse(string value)
    {
        if (SystemActions.IsNamedAction(value))
        {
            return [];
        }

        var keys = new List<ushort>();
        foreach (var rawPart in value.Replace("-", "+", StringComparison.Ordinal).Split('+'))
        {
            var token = rawPart.Trim().ToUpperInvariant();
            if (token.Length == 0 || token is "NONE" or "UNCHANGED")
            {
                continue;
            }

            if (!KeyNameToVk.TryGetValue(token, out var vk))
            {
                throw new InvalidOperationException($"Unknown key in hotkey '{value}': {rawPart.Trim()}");
            }

            keys.Add(vk);
        }

        return keys;
    }

    public static string Normalize(string value)
    {
        var parts = value.Replace("-", "+", StringComparison.Ordinal)
            .Split('+')
            .Select(part => part.Trim())
            .Where(part => part.Length > 0)
            .Select(PrettyKey)
            .ToList();
        return parts.Count == 0 ? "none" : string.Join("+", parts);
    }

    private static string PrettyKey(string token)
    {
        return token.ToUpperInvariant() switch
        {
            "CONTROL" or "CTRL" => "Ctrl",
            "UNCHANGED" => "unchanged",
            "NONE" => "none",
            "WINDOWS" or "META" or "WIN" => "Win",
            "ALT" => "Alt",
            "SHIFT" => "Shift",
            "ESC" or "ESCAPE" => "Esc",
            "DEL" => "Delete",
            "BROWSERBACK" => "BrowserBack",
            "BROWSERFORWARD" => "BrowserForward",
            "BROWSERSEARCH" => "BrowserSearch",
            "BRIGHTNESSDOWN" => "BrightnessDown",
            "BRIGHTNESSUP" => "BrightnessUp",
            "MEDIANEXT" or "MEDIANEXTTRACK" => "MediaNext",
            "MEDIAPLAYPAUSE" or "PLAYPAUSE" => "MediaPlayPause",
            "MEDIAPREVIOUS" or "MEDIAPREVIOUSTRACK" => "MediaPrevious",
            "VOLUMEDOWN" => "VolumeDown",
            "VOLUMEMUTE" or "MUTE" => "VolumeMute",
            "VOLUMEUP" => "VolumeUp",
            "PGUP" => "PageUp",
            "PGDN" => "PageDown",
            "PLUS" or "ADD" => "Plus",
            "MINUS" or "SUBTRACT" => "Minus",
            "PERIOD" or "DOT" => "Period",
            "COMMA" => "Comma",
            "SLASH" => "Slash",
            "BACKTICK" or "GRAVE" => "Backtick",
            var key when key.Length == 1 => key,
            var key when key.StartsWith('F') && int.TryParse(key[1..], out _) => key,
            var key => char.ToUpperInvariant(key[0]) + key[1..].ToLowerInvariant(),
        };
    }

    private static Dictionary<string, ushort> BuildKeyMap()
    {
        var map = new Dictionary<string, ushort>
        {
            ["ALT"] = 0x12,
            ["BACKSPACE"] = 0x08,
            ["BACKTICK"] = 0xC0,
            ["BROWSERBACK"] = 0xA6,
            ["BROWSERFORWARD"] = 0xA7,
            ["BROWSERSEARCH"] = 0xAA,
            ["COMMA"] = 0xBC,
            ["CTRL"] = 0x11,
            ["CONTROL"] = 0x11,
            ["DEL"] = 0x2E,
            ["DELETE"] = 0x2E,
            ["DOWN"] = 0x28,
            ["END"] = 0x23,
            ["ENTER"] = 0x0D,
            ["ESC"] = 0x1B,
            ["ESCAPE"] = 0x1B,
            ["GRAVE"] = 0xC0,
            ["HOME"] = 0x24,
            ["INSERT"] = 0x2D,
            ["LEFT"] = 0x25,
            ["MEDIA_NEXT"] = 0xB0,
            ["MEDIA_NEXT_TRACK"] = 0xB0,
            ["MEDIA_PLAY_PAUSE"] = 0xB3,
            ["MEDIA_PREVIOUS"] = 0xB1,
            ["MEDIA_PREVIOUS_TRACK"] = 0xB1,
            ["MEDIANEXT"] = 0xB0,
            ["MEDIANEXTTRACK"] = 0xB0,
            ["MEDIAPLAYPAUSE"] = 0xB3,
            ["MEDIAPREVIOUS"] = 0xB1,
            ["MEDIAPREVIOUSTRACK"] = 0xB1,
            ["META"] = 0x5B,
            ["MINUS"] = 0xBD,
            ["MUTE"] = 0xAD,
            ["PAGEDOWN"] = 0x22,
            ["PAGEUP"] = 0x21,
            ["PERIOD"] = 0xBE,
            ["PLUS"] = 0xBB,
            ["PGDN"] = 0x22,
            ["PGUP"] = 0x21,
            ["PLAYPAUSE"] = 0xB3,
            ["PRINTSCREEN"] = 0x2C,
            ["RIGHT"] = 0x27,
            ["SLASH"] = 0xBF,
            ["SHIFT"] = 0x10,
            ["SPACE"] = 0x20,
            ["TAB"] = 0x09,
            ["UP"] = 0x26,
            ["VOLUMEDOWN"] = 0xAE,
            ["VOLUMEMUTE"] = 0xAD,
            ["VOLUMEUP"] = 0xAF,
            ["WIN"] = 0x5B,
            ["WINDOWS"] = 0x5B,
        };

        for (var code = 'A'; code <= 'Z'; code++)
        {
            map[code.ToString()] = code;
        }

        for (var code = '0'; code <= '9'; code++)
        {
            map[code.ToString()] = code;
        }

        for (ushort index = 1; index <= 24; index++)
        {
            map[$"F{index}"] = (ushort)(0x6F + index);
        }

        return map;
    }
}
