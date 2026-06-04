from __future__ import annotations

from .input_injector import (
    VK_CONTROL,
    VK_D,
    VK_DOWN,
    VK_LEFT,
    VK_LWIN,
    VK_RIGHT,
    VK_TAB,
    VK_UP,
)


VK_MENU = 0x12
VK_SHIFT = 0x10
VK_ESCAPE = 0x1B
VK_SPACE = 0x20
VK_RETURN = 0x0D
VK_BACK = 0x08
VK_DELETE = 0x2E
VK_HOME = 0x24
VK_END = 0x23
VK_PRIOR = 0x21
VK_NEXT = 0x22


KEY_NAME_TO_VK = {
    "ALT": VK_MENU,
    "BACKSPACE": VK_BACK,
    "CTRL": VK_CONTROL,
    "CONTROL": VK_CONTROL,
    "D": VK_D,
    "DEL": VK_DELETE,
    "DELETE": VK_DELETE,
    "DOWN": VK_DOWN,
    "END": VK_END,
    "ENTER": VK_RETURN,
    "ESC": VK_ESCAPE,
    "ESCAPE": VK_ESCAPE,
    "HOME": VK_HOME,
    "LEFT": VK_LEFT,
    "META": VK_LWIN,
    "PAGEDOWN": VK_NEXT,
    "PAGEUP": VK_PRIOR,
    "PGDN": VK_NEXT,
    "PGUP": VK_PRIOR,
    "RIGHT": VK_RIGHT,
    "SHIFT": VK_SHIFT,
    "SPACE": VK_SPACE,
    "TAB": VK_TAB,
    "UP": VK_UP,
    "WIN": VK_LWIN,
    "WINDOWS": VK_LWIN,
}

for code in range(ord("A"), ord("Z") + 1):
    KEY_NAME_TO_VK[chr(code)] = code

for code in range(ord("0"), ord("9") + 1):
    KEY_NAME_TO_VK[chr(code)] = code

for index in range(1, 25):
    KEY_NAME_TO_VK[f"F{index}"] = 0x6F + index


def parse_hotkey(value: str) -> list[int]:
    keys = []
    for part in value.replace("-", "+").split("+"):
        token = part.strip().upper()
        if not token:
            continue
        try:
            keys.append(KEY_NAME_TO_VK[token])
        except KeyError as exc:
            raise ValueError(f"Unknown key in hotkey '{value}': {part.strip()}") from exc
    if not keys:
        raise ValueError("Hotkey cannot be empty.")
    return keys


def normalize_hotkey(value: str) -> str:
    parts = []
    for part in value.replace("-", "+").split("+"):
        token = part.strip()
        if token:
            parts.append(_pretty_key(token))
    return "+".join(parts)


def _pretty_key(token: str) -> str:
    upper = token.upper()
    aliases = {
        "CONTROL": "Ctrl",
        "CTRL": "Ctrl",
        "WINDOWS": "Win",
        "META": "Win",
        "WIN": "Win",
        "ALT": "Alt",
        "SHIFT": "Shift",
        "ESC": "Esc",
        "ESCAPE": "Esc",
        "DEL": "Delete",
        "PGUP": "PageUp",
        "PGDN": "PageDown",
    }
    if upper in aliases:
        return aliases[upper]
    if len(upper) == 1:
        return upper
    if upper.startswith("F") and upper[1:].isdigit():
        return upper
    return upper.title().replace("Pageup", "PageUp").replace("Pagedown", "PageDown")

