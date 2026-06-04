# Apple Peripherals for Windows

Native Windows settings app and background bridge for Apple Magic Trackpad and Magic Keyboard support over Bluetooth.

Windows can pair Apple peripherals as Bluetooth HID devices, but many of the useful Mac-style behaviors are missing. This repo provides one C#/.NET Windows app that enables Magic Trackpad multitouch mode, reads the trackpad through Windows Raw Input, and applies the trackpad gestures and keyboard remaps you configure.

## Native App

- Built with C# on .NET 8 and WinForms.
- Installs `MagicTrackpad.exe` as one per-user background bridge for keyboard and trackpad support.
- Uses a dense device-tab settings UI with separate Magic Trackpad and Magic Keyboard pages.
- Adds Start Menu shortcuts for settings and manual bridge launch.
- Registers a per-user scheduled task when Windows allows it, and falls back to a Startup shortcut when task registration is blocked.
- Keeps Bluetooth multitouch mode refreshed after reconnects and wake events.
- Reloads saved settings while the bridge is running.
- Detects Apple Magic Keyboard devices and applies the configured keyboard remaps while one is connected.

## Controls

The settings app lets you configure:

Keyboard:

- Command, Control, Option, and Caps Lock remaps.
- Mac-like F1 through F12 icon-row defaults: brightness, Mission Control, Spotlight/Search, Dictation, Notification Center, media, mute, and volume.
- Fn/Globe action mapping when Windows exposes the key, plus F13 through F19 keybinds and a full F-key mapping dialog.
- Whether keyboard remaps are active only while an Apple keyboard is connected.

Trackpad:

- Pointer movement, sensitivity, and X/Y direction.
- Two-finger vertical and horizontal scrolling.
- Natural or traditional scroll direction.
- Two-finger Smart Zoom, rotate, and page back/forward swipes.
- Tap-to-click and one-/two-/three-finger tap actions.
- Physical click and multi-finger physical click actions.
- Pinch-to-zoom modifier and sensitivity.
- Three- and four-finger swipe keybinds for Windows desktops, task view, desktop reveal, App Expose-style actions, or any hotkey.
- Four/five-finger pinch/spread actions for Launchpad/Start and Show Desktop equivalents, plus four-finger tap for Notification Center.
- Swipe thresholds, raw report logging, and reconnect refresh interval.

Hardware/Windows limits:

- Force Touch pressure and haptic feedback depend on the Apple HID report stream and Windows Bluetooth stack. The bridge preserves pressure values where reports expose them, but Windows does not provide macOS's haptic feedback engine.
- Display brightness uses Windows monitor brightness APIs. It works on monitors/drivers that expose brightness control and is ignored by hardware that refuses software brightness changes.

## Build And Test

Requirements:

- Windows 10/11
- .NET 8 SDK

From the repo root:

```powershell
dotnet build .\ApplePeripheralsForWindows.sln -c Release
dotnet run --project .\src\MagicTrackpad.App\MagicTrackpad.App.csproj -c Release -- --self-test
```

The repo also keeps the original parser tests as extra coverage:

```powershell
python -m unittest discover -s tests
```

## Run From Source

```powershell
dotnet run --project .\src\MagicTrackpad.App\MagicTrackpad.App.csproj -c Release -- --enable
dotnet run --project .\src\MagicTrackpad.App\MagicTrackpad.App.csproj -c Release -- --bridge
dotnet run --project .\src\MagicTrackpad.App\MagicTrackpad.App.csproj -c Release -- --settings
```

Short bridge smoke test:

```powershell
.\scripts\run.ps1 -DryRun -Seconds 5
```

## Install

Use PowerShell from the repo root:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1
```

The installer publishes the native app to:

```text
%LOCALAPPDATA%\ApplePeripheralsForWindows\app\MagicTrackpad.exe
```

It creates or reuses this config file:

```text
%USERPROFILE%\.magictrackpad-bridge.json
```

Open settings after install from the Start Menu, or run:

```powershell
.\scripts\settings.ps1 -Installed
```

Uninstall:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\uninstall.ps1
```

## System Design

See [docs/architecture.md](docs/architecture.md). The app is split into configuration, HID device access, Raw Input runtime, keyboard remapping, gesture translation, input injection, WinForms UI, and self-test layers.

## License And Attribution

Licensed under GPL-2.0-or-later. The Apple report parser behavior is based on the GPL Linux `hid-magicmouse` driver; see [docs/references.md](docs/references.md).
