import unittest

from magictrackpad_bridge.hotkeys import normalize_hotkey, parse_hotkey
from magictrackpad_bridge.input_injector import VK_CONTROL, VK_LEFT, VK_LWIN


class HotkeyTests(unittest.TestCase):
    def test_parse_common_hotkey(self) -> None:
        self.assertEqual(parse_hotkey("Win+Ctrl+Left"), [VK_LWIN, VK_CONTROL, VK_LEFT])

    def test_normalize_aliases(self) -> None:
        self.assertEqual(normalize_hotkey("windows-control-left"), "Win+Ctrl+Left")

    def test_reject_unknown_key(self) -> None:
        with self.assertRaises(ValueError):
            parse_hotkey("Win+Nope")


if __name__ == "__main__":
    unittest.main()

