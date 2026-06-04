from __future__ import annotations

import argparse
import logging
from pathlib import Path
import re
import sys
from threading import Event, Thread, Timer
import time

from .config import DEFAULT_CONFIG_PATH, AppConfig, load_config, write_default_config
from .devices import find_magic_trackpads, multitouch_feature_report, send_feature_report
from .gestures import GestureEngine
from .hid_reports import parse_reports
from .input_injector import DryRunInjector, Win32InputInjector
from .raw_input import RawInputBridge, enumerate_raw_input_devices


LOGGER = logging.getLogger("magictrackpad")


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    logging.basicConfig(
        level=logging.DEBUG if args.verbose else logging.INFO,
        format="%(asctime)s %(levelname)s %(message)s",
    )
    try:
        return args.func(args)
    except KeyboardInterrupt:
        LOGGER.info("Stopped.")
        return 130
    except Exception as exc:
        LOGGER.error("%s", exc)
        if args.verbose:
            raise
        return 1


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="magictrackpad",
        description="Apple Magic Trackpad Bluetooth bridge for Windows.",
    )
    parser.add_argument("-v", "--verbose", action="store_true", help="Show debug logging.")

    subparsers = parser.add_subparsers(required=True)

    run_parser = subparsers.add_parser("run", help="Run the Raw Input bridge.")
    run_parser.add_argument("--config", type=Path, default=DEFAULT_CONFIG_PATH, help="Path to JSON config.")
    run_parser.add_argument("--dry-run", action="store_true", help="Parse and log gestures without injecting input.")
    run_parser.add_argument("--seconds", type=float, default=None, help="Stop automatically after this many seconds.")
    run_parser.set_defaults(func=run_command)

    list_parser = subparsers.add_parser("list", help="List Raw Input HID devices and detected trackpads.")
    list_parser.set_defaults(func=list_command)

    enable_parser = subparsers.add_parser("enable", help="Send the Apple multitouch feature report.")
    enable_parser.set_defaults(func=enable_command)

    config_parser = subparsers.add_parser("write-config", help="Write a default user config.")
    config_parser.add_argument("--path", type=Path, default=DEFAULT_CONFIG_PATH)
    config_parser.set_defaults(func=write_config_command)

    settings_parser = subparsers.add_parser("settings", help="Open the Windows settings app.")
    settings_parser.add_argument("--config", type=Path, default=DEFAULT_CONFIG_PATH, help="Path to JSON config.")
    settings_parser.set_defaults(func=settings_command)

    return parser


def run_command(args: argparse.Namespace) -> int:
    config = load_config(args.config)
    injector = DryRunInjector() if args.dry_run else Win32InputInjector()
    engine = GestureEngine(injector, config.gestures)
    raw_logger = RawReportLogger(config) if config.log_raw_reports else None
    announced_keys: set[str] = set()
    enabled_keys: set[str] = set()
    stop_event = Event()

    def on_devices_changed(devices):
        groups = _group_trackpads(find_magic_trackpads(devices))
        if not groups:
            LOGGER.warning("No Apple Magic Trackpad devices detected yet.")
            return
        for key, trackpads in groups.items():
            device = trackpads[0]
            if key in announced_keys:
                continue
            announced_keys.add(key)
            LOGGER.info(
                "Detected %s vendor=0x%04x product=0x%04x transport=%s",
                device.product_name,
                device.vendor_id or 0,
                device.product_id or 0,
                "Bluetooth" if device.is_bluetooth else "USB",
            )
            if config.enable_multitouch_on_start and key not in enabled_keys:
                ok = _enable_any_collection(trackpads)
                if ok:
                    enabled_keys.add(key)
                LOGGER.info("Multitouch enable report %s for %s.", "sent" if ok else "failed", device.product_name)

    def reenable_loop():
        while not stop_event.wait(config.reenable_interval_seconds):
            if not config.enable_multitouch_on_start:
                continue
            groups = _group_trackpads(find_magic_trackpads(enumerate_raw_input_devices()))
            for key, trackpads in groups.items():
                if _enable_any_collection(trackpads):
                    enabled_keys.add(key)
                    LOGGER.debug("Refreshed multitouch mode for %s.", trackpads[0].product_name)

    def on_report(device, report: bytes):
        if raw_logger:
            raw_logger.write(device, report)
        frames = parse_reports(report)
        for frame in frames:
            LOGGER.debug("Frame report=0x%02x touches=%d clicks=0x%02x", frame.report_id, len(frame.active_touches), frame.clicks)
            engine.process_frame(frame)
        if args.dry_run and isinstance(injector, DryRunInjector) and injector.events:
            for event in injector.events:
                LOGGER.info("dry-run event: %s", event)
            injector.events.clear()

    LOGGER.info("Starting Magic Trackpad bridge. Press Ctrl+C to stop.")
    bridge = RawInputBridge(on_report=on_report, on_devices_changed=on_devices_changed)
    worker = Thread(target=reenable_loop, name="multitouch-reenable", daemon=True)
    worker.start()
    timer = None
    if args.seconds:
        timer = Timer(args.seconds, bridge.stop)
        timer.daemon = True
        timer.start()
    try:
        bridge.run()
    finally:
        stop_event.set()
        if timer:
            timer.cancel()
    return 0


def list_command(args: argparse.Namespace) -> int:
    devices = enumerate_raw_input_devices()
    trackpads = find_magic_trackpads(devices)
    print(f"Raw Input devices: {len(devices)}")
    for device in devices:
        marker = "*" if device in trackpads else " "
        vendor = f"0x{device.vendor_id:04x}" if device.vendor_id is not None else "unknown"
        product = f"0x{device.product_id:04x}" if device.product_id is not None else "unknown"
        usage_page = f"0x{device.usage_page:02x}" if device.usage_page is not None else "unknown"
        usage = f"0x{device.usage:02x}" if device.usage is not None else "unknown"
        print(f"{marker} {device.product_name}: vendor={vendor} product={product} usage={usage_page}:{usage}")
        print(f"    {device.name}")
    return 0


def enable_command(args: argparse.Namespace) -> int:
    groups = _group_trackpads(find_magic_trackpads(enumerate_raw_input_devices()))
    if not groups:
        print("No Apple Magic Trackpad devices found.")
        return 2
    failures = 0
    for trackpads in groups.values():
        device = trackpads[0]
        ok = _enable_any_collection(trackpads)
        status = "sent" if ok else "failed"
        print(f"{status}: {device.product_name} vendor=0x{device.vendor_id or 0:04x} product=0x{device.product_id or 0:04x}")
        if not ok:
            failures += 1
    return 1 if failures else 0


def write_config_command(args: argparse.Namespace) -> int:
    path = write_default_config(args.path)
    print(f"Wrote {path}")
    return 0


def settings_command(args: argparse.Namespace) -> int:
    from .settings_app import run_settings_app

    run_settings_app(args.config)
    return 0


class RawReportLogger:
    def __init__(self, config: AppConfig) -> None:
        self.path = Path(config.raw_log_path)
        self.path.parent.mkdir(parents=True, exist_ok=True)

    def write(self, device, report: bytes) -> None:
        with self.path.open("a", encoding="utf-8") as handle:
            handle.write(f"{device.product_name} {device.name} {report.hex(' ')}\n")


def _group_trackpads(trackpads) -> dict[str, list]:
    groups: dict[str, list] = {}
    for device in trackpads:
        groups.setdefault(_physical_trackpad_key(device.name), []).append(device)
    return groups


def _physical_trackpad_key(name: str) -> str:
    return re.sub(r"&Col[0-9A-F]+#.*", "", name, flags=re.I).lower()


def _enable_any_collection(trackpads) -> bool:
    for device in trackpads:
        report = multitouch_feature_report(device)
        if report and send_feature_report(device, report):
            return True
    return False


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
