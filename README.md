# Apple Peripherals for Windows

Windows settings app and background bridge for Apple Magic Trackpad multitouch over Bluetooth.

Windows can pair the Magic Trackpad as a Bluetooth HID pointer, but the useful trackpad behavior is often missing: two-finger scroll, tap-to-click, secondary click, pinch zoom, and multi-finger shortcuts. This repo adds a Windows settings app plus a background bridge that reads Apple multitouch HID reports and applies the gestures you configure.

## What Works

- Detects Apple Magic Trackpad devices exposed through Windows Raw Input.
- Knows Apple vendor/product IDs for Magic Trackpad, Magic Trackpad 2, and the USB-C Magic Trackpad.
- Sends the Apple multitouch feature report when the HID collection is openable from user mode.
- Parses Apple 9-byte multitouch reports.
- Injects one-finger pointer movement, physical click, tap-to-click, two-finger scroll, horizontal scroll, two-finger secondary click, pinch-to-zoom through Ctrl+wheel, three-finger desktop swipes, and three-finger middle click.
- Lets you change pointer sensitivity, pointer direction, scroll direction, scroll speed, tap buttons, physical click buttons, pinch settings, swipe thresholds, and swipe keybinds.
- Installs a Start Menu settings app and a per-user background task.
- Keeps Bluetooth multitouch mode refreshed after reconnects and wake events.

## Settings App

Open the settings app from the Start Menu after installing, or run it from the repo:

```powershell
python -m magictrackpad_bridge settings
```

The app has tabs for:

- Pointer: enable movement, sensitivity, and X/Y direction.
- Scroll: natural scrolling, speed, and horizontal scrolling.
- Clicks: tap-to-click, two-/three-finger tap actions, and physical click actions.
- Gestures: pinch zoom modifier, swipe thresholds, and three-/four-finger swipe keybinds.
- Service: automatic multitouch refresh, raw report logging, and bridge controls.

## Quick Start

Use PowerShell from the repo root:

```powershell
python -m magictrackpad_bridge list
python -m magictrackpad_bridge enable
python -m magictrackpad_bridge run
python -m magictrackpad_bridge settings
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

Use the settings app for normal changes. `config.example.json` is also available as an editable reference. The most useful options are:

- `pointer_sensitivity`
- `natural_scroll`
- `scroll_sensitivity`
- `pinch_zoom_enabled`
- `three_finger_swipes_enabled`
- `gestures.hotkeys`
- `log_raw_reports`

## Test

```powershell
python -m unittest discover -s tests
```

## License And Attribution

Licensed under GPL-2.0-or-later. The Apple report parser behavior is based on the GPL Linux `hid-magicmouse` driver; see [docs/references.md](docs/references.md).
