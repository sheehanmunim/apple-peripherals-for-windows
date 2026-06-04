# Magic Keyboard Globe/Fn Filter Driver

The Magic Keyboard exposes the Globe/Fn key as a vendor-specific bit in its HID keyboard report. On Windows, the main keyboard HID collection is owned by the system keyboard stack, so the user-mode bridge often cannot read that raw bit. The filter driver in `src/AppleKeyboardFilter.Driver` sits below HIDClass for matching Apple keyboards and converts the hidden Globe/Fn bit into standard HID F23.

The app already treats F23/F24 as the configurable Globe/Fn action. Emitting F23 in the driver is intentional: it lets the app map Globe/Fn to Control while the real Apple Control key can still be mapped to Windows.

## Supported Hardware IDs

The INF targets these Apple keyboard IDs:

- Bluetooth Magic Keyboard USB-C: `BTHENUM\{00001124-0000-1000-8000-00805f9b34fb}_VID&0001004c_PID&0320`
- USB-C Magic Keyboard: `USB\VID_05AC&PID_0321&MI_01`
- Legacy Magic Keyboard A1644: Bluetooth PID `0267`, USB PID `0267`
- Magic Keyboard with Numeric Keypad: Bluetooth PID `026C`, USB PID `026C`

## Build

Install Visual Studio Build Tools plus the Windows Driver Kit, then build:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-keyboard-filter-driver.ps1 -Platform x64
```

The script writes a package to:

```text
artifacts\keyboard-filter\AMD64
artifacts\keyboard-filter\AppleKeyboardFilterDriver.zip
```

For ARM64:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-keyboard-filter-driver.ps1 -Platform ARM64
```

## Signing

Normal Windows 11 systems require a trusted kernel driver catalog. Do not ship or install an unsigned public build. Sign the package through Microsoft attestation or HLK signing, or provide a certificate thumbprint for a local trusted signing flow:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-keyboard-filter-driver.ps1 -Platform x64 -CertificateThumbprint <thumbprint>
```

The installer and `scripts\install-keyboard-filter-driver.ps1` verify the `.sys` and `.cat` signatures before installing unless `-AllowTestSigned` is explicitly supplied for a development machine.

## Bundle With Setup

Build the setup `.exe` with the signed keyboard driver package:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1 -Runtime win-x64 -KeyboardDriverZip .\artifacts\keyboard-filter\AppleKeyboardFilterDriver.zip
```

When the signed package is bundled, the setup app installs it together with the Magic Trackpad Precision Touchpad driver.
