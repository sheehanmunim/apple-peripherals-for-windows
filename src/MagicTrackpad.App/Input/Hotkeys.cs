namespace MagicTrackpad.Input;

public static class Hotkeys
{
    private static readonly Dictionary<string, ushort> KeyNameToVk = BuildKeyMap();

    public static IReadOnlyList<ushort> Parse(string value)
    {
        var keys = new List<ushort>();
        foreach (var rawPart in value.Replace("-", "+", StringComparison.Ordinal).Split('+'))
        {
            var token = rawPart.Trim().ToUpperInvariant();
            if (token.Length == 0 || token == "NONE")
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
            "WINDOWS" or "META" or "WIN" => "Win",
            "ALT" => "Alt",
            "SHIFT" => "Shift",
            "ESC" or "ESCAPE" => "Esc",
            "DEL" => "Delete",
            "PGUP" => "PageUp",
            "PGDN" => "PageDown",
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
            ["CTRL"] = 0x11,
            ["CONTROL"] = 0x11,
            ["DEL"] = 0x2E,
            ["DELETE"] = 0x2E,
            ["DOWN"] = 0x28,
            ["END"] = 0x23,
            ["ENTER"] = 0x0D,
            ["ESC"] = 0x1B,
            ["ESCAPE"] = 0x1B,
            ["HOME"] = 0x24,
            ["LEFT"] = 0x25,
            ["META"] = 0x5B,
            ["PAGEDOWN"] = 0x22,
            ["PAGEUP"] = 0x21,
            ["PGDN"] = 0x22,
            ["PGUP"] = 0x21,
            ["RIGHT"] = 0x27,
            ["SHIFT"] = 0x10,
            ["SPACE"] = 0x20,
            ["TAB"] = 0x09,
            ["UP"] = 0x26,
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

