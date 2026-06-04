from __future__ import annotations

from dataclasses import dataclass
from typing import Iterable


TRACKPAD_REPORT_ID = 0x28
TRACKPAD2_USB_REPORT_ID = 0x02
TRACKPAD2_BT_REPORT_ID = 0x31
DOUBLE_REPORT_ID = 0xF7


@dataclass(frozen=True)
class Touch:
    tracking_id: int
    x: int
    y: int
    size: int
    orientation: int
    touch_major: int
    touch_minor: int
    pressure: int
    down: bool


@dataclass(frozen=True)
class TrackpadFrame:
    report_id: int
    clicks: int
    touches: tuple[Touch, ...]
    raw: bytes

    @property
    def active_touches(self) -> tuple[Touch, ...]:
        return tuple(touch for touch in self.touches if touch.down)


def parse_reports(data: bytes) -> list[TrackpadFrame]:
    if not data:
        return []

    report_id = data[0]
    if report_id == DOUBLE_REPORT_ID:
        if len(data) < 3:
            return []
        first_size = data[1]
        first = data[2 : 2 + first_size]
        second = data[2 + first_size :]
        return parse_reports(first) + parse_reports(second)

    if report_id in {TRACKPAD_REPORT_ID, TRACKPAD2_BT_REPORT_ID}:
        return _parse_frame(data, prefix_size=4, trackpad2=report_id == TRACKPAD2_BT_REPORT_ID)

    if report_id == TRACKPAD2_USB_REPORT_ID:
        return _parse_frame(data, prefix_size=12, trackpad2=True)

    return []


def _parse_frame(data: bytes, prefix_size: int, trackpad2: bool) -> list[TrackpadFrame]:
    if len(data) < prefix_size or (len(data) - prefix_size) % 9 != 0:
        return []
    count = (len(data) - prefix_size) // 9
    if count > 15:
        return []

    touches = []
    for offset in range(prefix_size, len(data), 9):
        chunk = data[offset : offset + 9]
        touches.append(_parse_trackpad2_touch(chunk) if trackpad2 else _parse_legacy_touch(chunk))
    return [TrackpadFrame(report_id=data[0], clicks=data[1], touches=tuple(touches), raw=data)]


def _parse_trackpad2_touch(tdata: bytes) -> Touch:
    tracking_id = tdata[8] & 0x0F
    x = _sar32((tdata[1] << 27) | (tdata[0] << 19), 19)
    y = -_sar32((tdata[3] << 30) | (tdata[2] << 22) | (tdata[1] << 14), 19)
    size = tdata[6]
    orientation = (tdata[8] >> 5) - 4
    state = tdata[3] & 0xC0
    return Touch(
        tracking_id=tracking_id,
        x=x,
        y=y,
        size=size,
        orientation=orientation,
        touch_major=tdata[4],
        touch_minor=tdata[5],
        pressure=tdata[7],
        down=state == 0x80,
    )


def _parse_legacy_touch(tdata: bytes) -> Touch:
    tracking_id = ((tdata[7] << 2) | (tdata[6] >> 6)) & 0x0F
    x = _sar32((tdata[1] << 27) | (tdata[0] << 19), 19)
    y = -_sar32((tdata[3] << 30) | (tdata[2] << 22) | (tdata[1] << 14), 19)
    size = tdata[6] & 0x3F
    orientation = (tdata[7] >> 2) - 32
    state = tdata[8] & 0xF0
    return Touch(
        tracking_id=tracking_id,
        x=x,
        y=y,
        size=size,
        orientation=orientation,
        touch_major=tdata[4],
        touch_minor=tdata[5],
        pressure=0,
        down=state != 0,
    )


def centroid(touches: Iterable[Touch]) -> tuple[float, float]:
    active = list(touches)
    if not active:
        return 0.0, 0.0
    return (
        sum(touch.x for touch in active) / len(active),
        sum(touch.y for touch in active) / len(active),
    )


def distance_between(first: Touch, second: Touch) -> float:
    return ((first.x - second.x) ** 2 + (first.y - second.y) ** 2) ** 0.5


def _sar32(value: int, shift: int) -> int:
    value &= 0xFFFFFFFF
    if value & 0x80000000:
        value -= 0x100000000
    return value >> shift

