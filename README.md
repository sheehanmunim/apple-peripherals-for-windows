# Apple Peripherals for Windows

Native Windows settings app and background bridge for Apple Magic Trackpad multitouch over Bluetooth.

Windows can pair the Magic Trackpad as a Bluetooth HID pointer, but many of the useful trackpad behaviors are missing. This repo provides a C#/.NET Windows app that enables Apple multitouch mode, reads the trackpad through Windows Raw Input, and applies the gestures you configure.

## Native App

- Built with C# on .NET 8 and WinForms.
- Installs `MagicTrackpad.exe` as a per-user background bridge.
- Adds Start Menu shortcuts for settings and manual bridge launch.
- Registers a per-user scheduled task when Windows allows it, and falls back to a Startup shortcut when task registration is blocked.
- Keeps Bluetooth multitouch mode refreshed after reconnects and wake events.
- Reloads saved settings while the bridge is running.

## Controls

The settings app lets you configure:

- Pointer movement, sensitivity, and X/Y direction.
- Two-finger vertical and horizontal scrolling.
- Natural or traditional scroll direction.
- Tap-to-click and one-/two-/three-finger tap actions.
- Physical click and multi-finger physical click actions.
- Pinch-to-zoom modifier and sensitivity.
- Three- and four-finger swipe keybinds.
- Swipe thresholds, raw report logging, and reconnect refresh interval.

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
%LOCALAPPDATA%\MagicTrackpadBridge\app\MagicTrackpad.exe
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

See [docs/architecture.md](docs/architecture.md). The app is split into configuration, HID device access, Raw Input runtime, gesture translation, input injection, WinForms UI, and self-test layers.

## License And Attribution

Licensed under GPL-2.0-or-later. The Apple report parser behavior is based on the GPL Linux `hid-magicmouse` driver; see [docs/references.md](docs/references.md).
