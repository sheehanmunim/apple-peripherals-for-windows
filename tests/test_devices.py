import unittest

from magictrackpad_bridge.devices import parse_vid_pid


class DeviceParsingTests(unittest.TestCase):
    def test_parse_usb_style_vid_pid(self) -> None:
        self.assertEqual(parse_vid_pid(r"\\?\HID#VID_05AC&PID_0265#x"), (0x05AC, 0x0265))

    def test_parse_bluetooth_style_vid_pid(self) -> None:
        name = r"\\?\HID#{00001124}_VID&0001004c_PID&0324&Col02#x"
        self.assertEqual(parse_vid_pid(name), (0x004C, 0x0324))


if __name__ == "__main__":
    unittest.main()

