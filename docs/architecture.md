# Architecture

Apple Peripherals for Windows is a native Windows desktop application written in C# on .NET 8. The app is split into small layers so HID access, keyboard remapping, gesture interpretation, settings, and Windows input injection can be tested and changed independently.

## Runtime Shape

- `MagicTrackpad.exe --bridge` starts a per-user background bridge with a tray icon.
- `MagicTrackpad.exe --settings` opens the WinForms settings app.
- `MagicTrackpad.exe --enable` sends the Apple multitouch feature report to any connected Magic Trackpad HID collection that Windows allows user-mode code to open.
- `MagicTrackpad.exe --self-test` runs parser, gesture, keybind, and config checks without needing hardware.

## Main Components

- `Configuration` owns the JSON config file at `%USERPROFILE%\.magictrackpad-bridge.json`.
- `Hid` identifies Apple Magic Trackpad and Magic Keyboard devices, sends feature reports, and parses Apple 9-byte multitouch records.
- `Runtime` hosts the hidden Raw Input window, reacts to Bluetooth reconnect/device-change messages, refreshes multitouch mode, updates keyboard presence, and reloads settings while the bridge runs.
- `Keyboard` installs a low-level keyboard hook for configured Command, Control, Option, Caps Lock, and F13-F19 remaps while Apple keyboard support is enabled.
- `Gestures` turns touch frames into pointer movement, scroll, taps, clicks, pinch zoom, Smart Zoom, rotate, page swipes, multi-finger swipes, and four/five-finger pinch/spread actions.
- `Input` wraps `SendInput` and named system actions so gestures and keyboard mappings can inject mouse events, hotkeys, media keys, volume keys, browser navigation, and monitor brightness adjustments where Windows exposes them.
- `Ui` contains the native WinForms settings app.
- `SelfTest` validates the parser and gesture engine without physical hardware.

## Design Choices

The project uses user-mode Raw Input and HID feature reports because Windows already owns the Bluetooth HID transport. This keeps install per-user and avoids unsigned kernel-driver prompts while still enabling the Apple multitouch report stream when the HID collection is openable.

The settings file is shared by the app and bridge. The running bridge watches the file timestamp and reloads keyboard remaps, gesture settings, sensitivity, direction, click mappings, and keybinds after the settings app saves.
