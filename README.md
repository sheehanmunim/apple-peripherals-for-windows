# Apple Peripherals for Windows

Experimental Windows bridge for Apple Magic Trackpad multitouch over Bluetooth.

Windows can pair the Magic Trackpad as a Bluetooth HID pointer, but the useful trackpad behavior is often missing: two-finger scroll, tap-to-click, secondary click, pinch zoom, and three-finger gestures. This repo adds a user-mode bridge that reads Apple multitouch HID reports and injects Windows input events.

## What Works

- Detects Apple Magic Trackpad devices exposed through Windows Raw Input.
- Knows Apple vendor/product IDs for Magic Trackpad, Magic Trackpad 2, and the USB-C Magic Trackpad.
- Sends the Apple multitouch feature report when the HID collection is openable from user mode.
- Parses Apple 9-byte multitouch reports.
- Injects one-finger pointer movement, physical click, tap-to-click, two-finger scroll, horizontal scroll, two-finger secondary click, pinch-to-zoom through Ctrl+wheel, three-finger desktop swipes, and three-finger middle click.
- Installs as a per-user scheduled task.

## Current Limits

This is not yet a signed kernel driver. Because of that:

- Windows may deny user-mode access to `HidD_SetFeature` on some Bluetooth stacks.
- Gestures are synthesized with `SendInput`, not presented as a native Precision Touchpad.
- Pinch and three-finger gestures are approximations using standard Windows keyboard and mouse events.
- Real public-driver distribution requires Microsoft driver signing.

See [docs/driver-roadmap.md](docs/driver-roadmap.md) for the path to a native signed driver.

## Quick Start

Use PowerShell from the repo root:

```powershell
python -m magictrackpad_bridge list
python -m magictrackpad_bridge enable
python -m magictrackpad_bridge run
```

Short smoke test:

```powershell
python -m magictrackpad_bridge -v run --dry-run --seconds 5
```

Install it at logon:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1
```

Uninstall:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\uninstall.ps1
```

## Configuration

Create a user config:

```powershell
python -m magictrackpad_bridge write-config
```

The default path is:

```text
%USERPROFILE%\.magictrackpad-bridge.json
```

Use `config.example.json` as the editable reference. The most useful options are:

- `pointer_sensitivity`
- `natural_scroll`
- `scroll_sensitivity`
- `pinch_zoom_enabled`
- `three_finger_swipes_enabled`
- `log_raw_reports`

## Test

```powershell
python -m unittest discover -s tests
```

## License And Attribution

Licensed under GPL-2.0-or-later. The Apple report parser behavior is based on the GPL Linux `hid-magicmouse` driver; see [docs/references.md](docs/references.md).
