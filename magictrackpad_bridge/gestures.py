from __future__ import annotations

from dataclasses import dataclass
import math
import time

from .config import GestureConfig
from .hid_reports import TrackpadFrame, Touch, centroid, distance_between
from .input_injector import (
    InputInjector,
    VK_CONTROL,
    VK_D,
    VK_LEFT,
    VK_LWIN,
    VK_RIGHT,
    VK_TAB,
)


@dataclass
class TouchSession:
    count: int
    started_at: float
    start_centroid: tuple[float, float]
    last_centroid: tuple[float, float]
    max_distance: float = 0.0
    swipe_fired: bool = False
    pinch_accumulator: float = 0.0
    scroll_x_accumulator: float = 0.0
    scroll_y_accumulator: float = 0.0
    last_distance: float | None = None


class GestureEngine:
    def __init__(self, injector: InputInjector, config: GestureConfig | None = None) -> None:
        self.injector = injector
        self.config = config or GestureConfig()
        self.session: TouchSession | None = None
        self.last_click_down = False
        self.active_button: str | None = None

    def process_frame(self, frame: TrackpadFrame, now: float | None = None) -> None:
        now = time.monotonic() if now is None else now
        active = frame.active_touches
        click_down = bool(frame.clicks & 1)

        self._handle_physical_click(click_down, active)
        self._handle_touches(active, now, physical_click_active=click_down)
        self.last_click_down = click_down

    def _handle_physical_click(self, click_down: bool, active: tuple[Touch, ...]) -> None:
        if click_down == self.last_click_down:
            return

        if click_down:
            if self.config.secondary_click_enabled and len(active) >= 2:
                self.active_button = "right"
            else:
                self.active_button = "left"
            self.injector.button_down(self.active_button)
        elif self.active_button:
            self.injector.button_up(self.active_button)
            self.active_button = None

    def _handle_touches(self, active: tuple[Touch, ...], now: float, physical_click_active: bool) -> None:
        count = len(active)
        center = centroid(active)

        if count == 0:
            self._finish_session(now, physical_click_active)
            self.session = None
            return

        if self.session is None or self.session.count != count:
            self.session = TouchSession(
                count=count,
                started_at=now,
                start_centroid=center,
                last_centroid=center,
                last_distance=_two_touch_distance(active),
            )
            return

        session = self.session
        dx = center[0] - session.last_centroid[0]
        dy = center[1] - session.last_centroid[1]
        total_dx = center[0] - session.start_centroid[0]
        total_dy = center[1] - session.start_centroid[1]
        session.max_distance = max(session.max_distance, math.hypot(total_dx, total_dy))

        if count == 1:
            self._handle_pointer(dx, dy)
        elif count == 2:
            self._handle_two_finger(active, dx, dy, session)
        elif count >= 3:
            self._handle_swipe(total_dx, total_dy, session)

        session.last_centroid = center

    def _finish_session(self, now: float, physical_click_active: bool) -> None:
        session = self.session
        if session is None or physical_click_active:
            return
        if not self.config.tap_to_click:
            return
        if now - session.started_at > self.config.tap_max_seconds:
            return
        if session.max_distance > self.config.tap_max_distance:
            return

        if session.count == 1:
            self.injector.click("left")
        elif session.count == 2 and self.config.secondary_click_enabled:
            self.injector.click("right")
        elif session.count == 3 and self.config.three_finger_middle_click:
            self.injector.click("middle")

    def _handle_pointer(self, dx: float, dy: float) -> None:
        if not self.config.pointer_enabled:
            return
        if self.config.invert_pointer_x:
            dx = -dx
        if self.config.invert_pointer_y:
            dy = -dy
        move_x = int(round(dx * self.config.pointer_sensitivity))
        move_y = int(round(dy * self.config.pointer_sensitivity))
        if move_x or move_y:
            self.injector.move_relative(move_x, move_y)

    def _handle_two_finger(
        self,
        active: tuple[Touch, ...],
        dx: float,
        dy: float,
        session: TouchSession,
    ) -> None:
        current_distance = _two_touch_distance(active)
        pinch_delta = 0.0
        if current_distance is not None and session.last_distance is not None:
            pinch_delta = current_distance - session.last_distance
        session.last_distance = current_distance

        did_pinch = False
        if self.config.pinch_zoom_enabled and abs(pinch_delta) > self.config.pinch_threshold:
            did_pinch = True
            session.pinch_accumulator += pinch_delta * self.config.pinch_sensitivity
            steps = int(session.pinch_accumulator / 120)
            if steps:
                self.injector.ctrl_wheel(steps * 120)
                session.pinch_accumulator -= steps * 120

        if did_pinch or not self.config.scroll_enabled:
            return

        direction = -1 if self.config.natural_scroll else 1
        session.scroll_y_accumulator += dy * self.config.scroll_sensitivity * direction
        wheel_y = int(session.scroll_y_accumulator)
        if wheel_y:
            self.injector.wheel(vertical=wheel_y)
            session.scroll_y_accumulator -= wheel_y

        if not self.config.horizontal_scroll_enabled:
            return
        session.scroll_x_accumulator += dx * self.config.scroll_sensitivity * -direction
        wheel_x = int(session.scroll_x_accumulator)
        if wheel_x:
            self.injector.wheel(horizontal=wheel_x)
            session.scroll_x_accumulator -= wheel_x

    def _handle_swipe(self, total_dx: float, total_dy: float, session: TouchSession) -> None:
        if not self.config.three_finger_swipes_enabled or session.swipe_fired:
            return

        if abs(total_dx) > self.config.swipe_threshold and abs(total_dx) > abs(total_dy):
            if total_dx > 0:
                self.injector.hotkey([VK_LWIN, VK_CONTROL, VK_RIGHT])
            else:
                self.injector.hotkey([VK_LWIN, VK_CONTROL, VK_LEFT])
            session.swipe_fired = True
            return

        if abs(total_dy) > self.config.swipe_vertical_threshold and abs(total_dy) > abs(total_dx):
            if total_dy < 0:
                self.injector.hotkey([VK_LWIN, VK_TAB])
            else:
                self.injector.hotkey([VK_LWIN, VK_D])
            session.swipe_fired = True


def _two_touch_distance(active: tuple[Touch, ...]) -> float | None:
    if len(active) != 2:
        return None
    return distance_between(active[0], active[1])

