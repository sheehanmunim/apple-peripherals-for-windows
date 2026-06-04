from __future__ import annotations

from dataclasses import asdict, dataclass, fields
import json
from pathlib import Path
from typing import Any


DEFAULT_CONFIG_PATH = Path.home() / ".magictrackpad-bridge.json"


@dataclass(frozen=True)
class GestureConfig:
    pointer_enabled: bool = True
    pointer_sensitivity: float = 0.18
    invert_pointer_x: bool = False
    invert_pointer_y: bool = False
    scroll_enabled: bool = True
    natural_scroll: bool = True
    scroll_sensitivity: float = 0.42
    horizontal_scroll_enabled: bool = True
    tap_to_click: bool = True
    tap_max_seconds: float = 0.18
    tap_max_distance: float = 95.0
    secondary_click_enabled: bool = True
    three_finger_middle_click: bool = True
    pinch_zoom_enabled: bool = True
    pinch_sensitivity: float = 0.55
    pinch_threshold: float = 14.0
    three_finger_swipes_enabled: bool = True
    swipe_threshold: float = 650.0
    swipe_vertical_threshold: float = 540.0


@dataclass(frozen=True)
class AppConfig:
    gestures: GestureConfig = GestureConfig()
    enable_multitouch_on_start: bool = True
    log_raw_reports: bool = False
    raw_log_path: str = "logs/raw-reports.hex"


def load_config(path: Path | None = None) -> AppConfig:
    path = path or DEFAULT_CONFIG_PATH
    if not path.exists():
        return AppConfig()

    with path.open("r", encoding="utf-8") as handle:
        data = json.load(handle)
    return _build_dataclass(AppConfig, data)


def write_default_config(path: Path | None = None) -> Path:
    path = path or DEFAULT_CONFIG_PATH
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as handle:
        json.dump(asdict(AppConfig()), handle, indent=2)
        handle.write("\n")
    return path


def _build_dataclass(cls: type[Any], data: dict[str, Any]) -> Any:
    allowed = {field.name: field for field in fields(cls)}
    values: dict[str, Any] = {}
    for key, value in data.items():
        if key not in allowed:
            continue
        field_type = allowed[key].type
        if key == "gestures" and isinstance(value, dict):
            values[key] = _build_dataclass(GestureConfig, value)
        else:
            values[key] = value
    return cls(**values)

