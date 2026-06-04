import unittest

from magictrackpad_bridge.hid_reports import parse_reports


def encode_trackpad2_touch(x: int, y: int, tracking_id: int = 3, down: bool = True) -> bytes:
    raw_x = x & 0x1FFF
    raw_y = (-y) & 0x1FFF
    b0 = raw_x & 0xFF
    b1 = ((raw_x >> 8) & 0x1F) | ((raw_y & 0x07) << 5)
    b2 = (raw_y >> 3) & 0xFF
    b3 = ((raw_y >> 11) & 0x03) | (0x80 if down else 0x00)
    return bytes([b0, b1, b2, b3, 20, 18, 11, 64, tracking_id & 0x0F])


class ReportParserTests(unittest.TestCase):
    def test_parse_trackpad2_bt_frame(self) -> None:
        report = bytes([0x31, 0x01, 0x00, 0x00]) + encode_trackpad2_touch(-100, 200, tracking_id=7)

        frames = parse_reports(report)

        self.assertEqual(len(frames), 1)
        frame = frames[0]
        self.assertEqual(frame.report_id, 0x31)
        self.assertEqual(frame.clicks, 1)
        self.assertEqual(len(frame.active_touches), 1)
        touch = frame.active_touches[0]
        self.assertEqual(touch.tracking_id, 7)
        self.assertEqual(touch.x, -100)
        self.assertEqual(touch.y, 200)
        self.assertEqual(touch.pressure, 64)

    def test_rejects_bad_size(self) -> None:
        self.assertEqual(parse_reports(bytes([0x31, 0, 0, 0, 1, 2, 3])), [])

    def test_parse_double_report(self) -> None:
        first = bytes([0x31, 0, 0, 0]) + encode_trackpad2_touch(10, 10, 1)
        second = bytes([0x31, 0, 0, 0]) + encode_trackpad2_touch(20, 20, 2)
        report = bytes([0xF7, len(first)]) + first + second

        frames = parse_reports(report)

        self.assertEqual(len(frames), 2)
        self.assertEqual(frames[0].active_touches[0].tracking_id, 1)
        self.assertEqual(frames[1].active_touches[0].tracking_id, 2)


if __name__ == "__main__":
    unittest.main()

