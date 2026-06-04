using System.Runtime.InteropServices;
using MagicTrackpad.Interop;

namespace MagicTrackpad.Input;

public interface IInputInjector
{
    void MoveRelative(int dx, int dy);
    void ButtonDown(string button);
    void ButtonUp(string button);
    void Click(string button);
    void Wheel(int vertical = 0, int horizontal = 0);
    void Hotkey(IEnumerable<ushort> keys);
    void HotkeyDown(IEnumerable<ushort> keys);
    void HotkeyUp(IEnumerable<ushort> keys);
}

public sealed class Win32InputInjector : IInputInjector
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseMove = 0x0001;
    private const uint LeftDown = 0x0002;
    private const uint LeftUp = 0x0004;
    private const uint RightDown = 0x0008;
    private const uint RightUp = 0x0010;
    private const uint MiddleDown = 0x0020;
    private const uint MiddleUp = 0x0040;
    private const uint WheelFlag = 0x0800;
    private const uint HWheelFlag = 0x01000;
    private const uint KeyUp = 0x0002;

    public void MoveRelative(int dx, int dy) => SendMouse(dx, dy, 0, MouseMove);

    public void ButtonDown(string button) => SendMouse(0, 0, 0, ButtonFlag(button, true));

    public void ButtonUp(string button) => SendMouse(0, 0, 0, ButtonFlag(button, false));

    public void Click(string button)
    {
        ButtonDown(button);
        ButtonUp(button);
    }

    public void Wheel(int vertical = 0, int horizontal = 0)
    {
        if (vertical != 0)
        {
            SendMouse(0, 0, vertical, WheelFlag);
        }

        if (horizontal != 0)
        {
            SendMouse(0, 0, horizontal, HWheelFlag);
        }
    }

    public void Hotkey(IEnumerable<ushort> keys)
    {
        var list = keys.ToList();
        HotkeyDown(list);
        list.Reverse();
        HotkeyUp(list);
    }

    public void HotkeyDown(IEnumerable<ushort> keys) => SendKeyboard(keys.Select(key => (key, false)).ToList());

    public void HotkeyUp(IEnumerable<ushort> keys) => SendKeyboard(keys.Select(key => (key, true)).ToList());

    private static uint ButtonFlag(string button, bool down)
    {
        return (button.ToLowerInvariant(), down) switch
        {
            ("left", true) => LeftDown,
            ("left", false) => LeftUp,
            ("right", true) => RightDown,
            ("right", false) => RightUp,
            ("middle", true) => MiddleDown,
            ("middle", false) => MiddleUp,
            _ => throw new InvalidOperationException($"Unsupported mouse button: {button}"),
        };
    }

    private static void SendMouse(int dx, int dy, int data, uint flags)
    {
        var input = new NativeMethods.Input
        {
            Type = InputMouse,
            Union = new NativeMethods.InputUnion
            {
                Mouse = new NativeMethods.MouseInput { Dx = dx, Dy = dy, MouseData = unchecked((uint)data), Flags = flags },
            },
        };
        Send([input]);
    }

    private static void SendKeyboard(IReadOnlyList<(ushort Key, bool Up)> events)
    {
        var inputs = events.Select(item => new NativeMethods.Input
        {
            Type = InputKeyboard,
            Union = new NativeMethods.InputUnion
            {
                Keyboard = new NativeMethods.KeyboardInput { Vk = item.Key, Flags = item.Up ? KeyUp : 0 },
            },
        }).ToArray();
        Send(inputs);
    }

    private static void Send(NativeMethods.Input[] inputs)
    {
        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());
        if (sent != inputs.Length)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
    }
}

public sealed class DryRunInputInjector : IInputInjector
{
    public List<string> Events { get; } = [];

    public void MoveRelative(int dx, int dy) => Events.Add($"move:{dx},{dy}");
    public void ButtonDown(string button) => Events.Add($"down:{button}");
    public void ButtonUp(string button) => Events.Add($"up:{button}");
    public void Click(string button) { ButtonDown(button); ButtonUp(button); }
    public void Wheel(int vertical = 0, int horizontal = 0)
    {
        if (vertical != 0) Events.Add($"wheel:{vertical}");
        if (horizontal != 0) Events.Add($"hwheel:{horizontal}");
    }
    public void Hotkey(IEnumerable<ushort> keys) => Events.Add("hotkey:" + string.Join(",", keys));
    public void HotkeyDown(IEnumerable<ushort> keys) => Events.Add("keydown:" + string.Join(",", keys));
    public void HotkeyUp(IEnumerable<ushort> keys) => Events.Add("keyup:" + string.Join(",", keys));
}

