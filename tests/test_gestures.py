import unittest

from magictrackpad_bridge.config import GestureConfig
from magictrackpad_bridge.gestures import GestureEngine
from magictrackpad_bridge.hid_reports import Touch, TrackpadFrame
from magictrackpad_bridge.input_injector import DryRunInjector, VK_CONTROL, VK_LEFT, VK_LWIN


def touch(tracking_id: int, x: int, y: int, down: bool = True) -> Touch:
    return Touch(
        tracking_id=tracking_id,
        x=x,
        y=y,
        size=10,
        orientation=0,
        touch_major=20,
        touch_minor=18,
        pressure=50,
        down=down,
    )


def frame(touches, clicks: int = 0) -> TrackpadFrame:
    return TrackpadFrame(report_id=0x31, clicks=clicks, touches=tuple(touches), raw=b"")


class GestureEngineTests(unittest.TestCase):
    def test_tap_to_click(self) -> None:
        injector = DryRunInjector()
        engine = GestureEngine(injector)

        engine.process_frame(frame([touch(1, 0, 0)]), now=1.00)
        engine.process_frame(frame([]), now=1.08)

        self.assertEqual(injector.events, [("down", ("left",)), ("up", ("left",))])

    def test_two_finger_scroll(self) -> None:
        injector = DryRunInjector()
        engine = GestureEngine(injector, GestureConfig(scroll_sensitivity=1.0, natural_scroll=True))

        engine.process_frame(frame([touch(1, 0, 0), touch(2, 100, 0)]), now=1.00)
        engine.process_frame(frame([touch(1, 0, 40), touch(2, 100, 40)]), now=1.02)

        self.assertIn(("wheel", (-40,)), injector.events)

    def test_two_finger_physical_click_is_secondary(self) -> None:
        injector = DryRunInjector()
        engine = GestureEngine(injector)

        engine.process_frame(frame([touch(1, 0, 0), touch(2, 100, 0)], clicks=1), now=1.00)
        engine.process_frame(frame([touch(1, 0, 0), touch(2, 100, 0)], clicks=0), now=1.10)

        self.assertEqual(injector.events, [("down", ("right",)), ("up", ("right",))])

    def test_three_finger_left_swipe(self) -> None:
        injector = DryRunInjector()
        engine = GestureEngine(injector, GestureConfig(swipe_threshold=100))

        engine.process_frame(frame([touch(1, 500, 0), touch(2, 600, 0), touch(3, 700, 0)]), now=1.00)
        engine.process_frame(frame([touch(1, 300, 0), touch(2, 400, 0), touch(3, 500, 0)]), now=1.10)

        self.assertEqual(injector.events, [("hotkey", (VK_LWIN, VK_CONTROL, VK_LEFT))])


if __name__ == "__main__":
    unittest.main()

