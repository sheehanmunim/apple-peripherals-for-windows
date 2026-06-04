from __future__ import annotations

import ctypes
import sys
from typing import Iterable


MOUSEEVENTF_MOVE = 0x0001
MOUSEEVENTF_LEFTDOWN = 0x0002
MOUSEEVENTF_LEFTUP = 0x0004
MOUSEEVENTF_RIGHTDOWN = 0x0008
MOUSEEVENTF_RIGHTUP = 0x0010
MOUSEEVENTF_MIDDLEDOWN = 0x0020
MOUSEEVENTF_MIDDLEUP = 0x0040
MOUSEEVENTF_WHEEL = 0x0800
MOUSEEVENTF_HWHEEL = 0x01000

KEYEVENTF_KEYUP = 0x0002
INPUT_MOUSE = 0
INPUT_KEYBOARD = 1

VK_CONTROL = 0x11
VK_LWIN = 0x5B
VK_LEFT = 0x25
VK_UP = 0x26
VK_RIGHT = 0x27
VK_DOWN = 0x28
VK_TAB = 0x09
VK_D = 0x44


class InputInjector:
    def move_relative(self, dx: int, dy: int) -> None:
        raise NotImplementedError

    def button_down(self, button: str) -> None:
        raise NotImplementedError

    def button_up(self, button: str) -> None:
        raise NotImplementedError

    def click(self, button: str) -> None:
        self.button_down(button)
        self.button_up(button)

    def wheel(self, vertical: int = 0, horizontal: int = 0) -> None:
        raise NotImplementedError

    def hotkey(self, keys: Iterable[int]) -> None:
        raise NotImplementedError

    def ctrl_wheel(self, amount: int) -> None:
        self.hotkey_down([VK_CONTROL])
        try:
            self.wheel(vertical=amount)
        finally:
            self.hotkey_up([VK_CONTROL])

    def hotkey_down(self, keys: Iterable[int]) -> None:
        raise NotImplementedError

    def hotkey_up(self, keys: Iterable[int]) -> None:
        raise NotImplementedError


class DryRunInjector(InputInjector):
    def __init__(self) -> None:
        self.events: list[tuple[str, tuple[int | str, ...]]] = []

    def move_relative(self, dx: int, dy: int) -> None:
        self.events.append(("move", (dx, dy)))

    def button_down(self, button: str) -> None:
        self.events.append(("down", (button,)))

    def button_up(self, button: str) -> None:
        self.events.append(("up", (button,)))

    def wheel(self, vertical: int = 0, horizontal: int = 0) -> None:
        if vertical:
            self.events.append(("wheel", (vertical,)))
        if horizontal:
            self.events.append(("hwheel", (horizontal,)))

    def hotkey(self, keys: Iterable[int]) -> None:
        self.events.append(("hotkey", tuple(keys)))

    def hotkey_down(self, keys: Iterable[int]) -> None:
        self.events.append(("keydown", tuple(keys)))

    def hotkey_up(self, keys: Iterable[int]) -> None:
        self.events.append(("keyup", tuple(keys)))


class Win32InputInjector(InputInjector):
    def __init__(self) -> None:
        if sys.platform != "win32":
            raise RuntimeError("Win32 input injection is only available on Windows.")
        self.user32 = ctypes.WinDLL("user32", use_last_error=True)
        self.SendInput = self.user32.SendInput
        self.SendInput.argtypes = [ctypes.c_uint, ctypes.POINTER(INPUT), ctypes.c_int]
        self.SendInput.restype = ctypes.c_uint

    def move_relative(self, dx: int, dy: int) -> None:
        self._send_mouse(dx=dx, dy=dy, flags=MOUSEEVENTF_MOVE)

    def button_down(self, button: str) -> None:
        self._send_mouse(flags=_button_flag(button, down=True))

    def button_up(self, button: str) -> None:
        self._send_mouse(flags=_button_flag(button, down=False))

    def wheel(self, vertical: int = 0, horizontal: int = 0) -> None:
        if vertical:
            self._send_mouse(data=vertical, flags=MOUSEEVENTF_WHEEL)
        if horizontal:
            self._send_mouse(data=horizontal, flags=MOUSEEVENTF_HWHEEL)

    def hotkey(self, keys: Iterable[int]) -> None:
        keys = list(keys)
        self.hotkey_down(keys)
        self.hotkey_up(reversed(keys))

    def hotkey_down(self, keys: Iterable[int]) -> None:
        self._send_keyboard([(key, False) for key in keys])

    def hotkey_up(self, keys: Iterable[int]) -> None:
        self._send_keyboard([(key, True) for key in keys])

    def _send_mouse(self, dx: int = 0, dy: int = 0, data: int = 0, flags: int = 0) -> None:
        input_record = INPUT()
        input_record.type = INPUT_MOUSE
        input_record.union.mi = MOUSEINPUT(dx, dy, data, flags, 0, 0)
        self._send([input_record])

    def _send_keyboard(self, events: list[tuple[int, bool]]) -> None:
        records = []
        for vk, key_up in events:
            input_record = INPUT()
            input_record.type = INPUT_KEYBOARD
            input_record.union.ki = KEYBDINPUT(vk, 0, KEYEVENTF_KEYUP if key_up else 0, 0, 0)
            records.append(input_record)
        self._send(records)

    def _send(self, records: list["INPUT"]) -> None:
        array_type = INPUT * len(records)
        sent = self.SendInput(len(records), array_type(*records), ctypes.sizeof(INPUT))
        if sent != len(records):
            raise ctypes.WinError(ctypes.get_last_error())


def _button_flag(button: str, down: bool) -> int:
    flags = {
        ("left", True): MOUSEEVENTF_LEFTDOWN,
        ("left", False): MOUSEEVENTF_LEFTUP,
        ("right", True): MOUSEEVENTF_RIGHTDOWN,
        ("right", False): MOUSEEVENTF_RIGHTUP,
        ("middle", True): MOUSEEVENTF_MIDDLEDOWN,
        ("middle", False): MOUSEEVENTF_MIDDLEUP,
    }
    try:
        return flags[(button, down)]
    except KeyError as exc:
        raise ValueError(f"Unsupported mouse button: {button}") from exc


ULONG_PTR = ctypes.c_ulonglong if ctypes.sizeof(ctypes.c_void_p) == 8 else ctypes.c_uint32


class MOUSEINPUT(ctypes.Structure):
    _fields_ = [
        ("dx", ctypes.c_long),
        ("dy", ctypes.c_long),
        ("mouseData", ctypes.c_uint32),
        ("dwFlags", ctypes.c_uint32),
        ("time", ctypes.c_uint32),
        ("dwExtraInfo", ULONG_PTR),
    ]


class KEYBDINPUT(ctypes.Structure):
    _fields_ = [
        ("wVk", ctypes.c_uint16),
        ("wScan", ctypes.c_uint16),
        ("dwFlags", ctypes.c_uint32),
        ("time", ctypes.c_uint32),
        ("dwExtraInfo", ULONG_PTR),
    ]


class HARDWAREINPUT(ctypes.Structure):
    _fields_ = [
        ("uMsg", ctypes.c_uint32),
        ("wParamL", ctypes.c_uint16),
        ("wParamH", ctypes.c_uint16),
    ]


class INPUTUNION(ctypes.Union):
    _fields_ = [
        ("mi", MOUSEINPUT),
        ("ki", KEYBDINPUT),
        ("hi", HARDWAREINPUT),
    ]


class INPUT(ctypes.Structure):
    _fields_ = [
        ("type", ctypes.c_uint32),
        ("union", INPUTUNION),
    ]

