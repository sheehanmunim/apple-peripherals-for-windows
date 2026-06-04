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

Install Visual Studio Build Tools plus the Windows Driver Kit, or use the Enterprise WDK. Microsoft documents that the EWDK is a standalone command-line driver build environment that includes Visual Studio Build Tools, the SDK, and the WDK. Then build:

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

The installer and `scripts\install-keyboard-filter-driver.ps1` verify the driver catalog signature before installing unless `-AllowTestSigned` is explicitly supplied for a development machine.

Verify a signed package before bundling it:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify-keyboard-driver-package.ps1 -ZipPath .\artifacts\keyboard-filter\AppleKeyboardFilterDriver.zip -RequireAllArchitectures -RequireMicrosoftSignature
```

## Verify The Live Driver Stack

After installing or reconnecting a Magic Keyboard, check whether Windows actually has the filter in the driver store and bound to the keyboard target:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\check-keyboard-filter-driver.ps1
```

For a gating check:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\check-keyboard-filter-driver.ps1 -RequireReady
```

Use `-RequireMicrosoftSigner` when verifying a public-release machine. A healthy public install should report `Ready: True`; if it reports that the filter is not in the driver store, Globe/Fn cannot be remapped by the app because Windows is still hiding that bit in the stock keyboard stack.

## Local Development Test Signing

For local driver development only, run from elevated PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-keyboard-filter-driver-dev.ps1 -EnableTestSigning
```

If the machine does not have Visual Studio Build Tools and WDK installed, use an existing unsigned GitHub Actions artifact or local package directory:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-keyboard-filter-driver-dev.ps1 -DriverDir .\artifacts\keyboard-filter\AMD64 -SkipBuild -EnableTestSigning -Elevate
```

The helper creates or reuses a local code-signing certificate, trusts it on the machine, signs a temporary copy of the catalog, enables Windows test-signing mode, and installs the filter with `-AllowTestSigned`. Reboot after enabling test-signing and rerun `scripts\check-keyboard-filter-driver.ps1`. This path is deliberately not used by the public setup executable.

## GitHub Artifact Build

The `keyboard-driver` GitHub Actions workflow builds unsigned AMD64 and ARM64 driver packages on a Windows runner with WDK 26100:

```powershell
gh workflow run keyboard-driver.yml
```

Those artifacts are for signing/submission only. Do not install them on normal Windows systems until the catalog is trusted by Microsoft attestation/HLK signing or by a local test-signing setup.

The workflow also packages both architecture builds into `AppleKeyboardFilterDriver-attestation-cab-unsigned`. Use that CAB as the input for Hardware Dev Center attestation after signing the CAB with the account's EV or registered code-signing certificate.

Build the CAB locally with:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-keyboard-driver-submission-cab.ps1
```

After Partner Center returns the Microsoft-signed package, convert it into the installer-ready zip with:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\package-signed-keyboard-driver.ps1 -SignedPackage .\path\from-partner-center.cab -RequireMicrosoftSignature
```

## Bundle With Setup

Build the setup `.exe` with the signed keyboard driver package:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1 -Runtime win-x64 -KeyboardDriverZip .\artifacts\keyboard-filter\AppleKeyboardFilterDriver-signed.zip -RequireKeyboardDriver
```

When the signed package is bundled, the setup app installs it together with the Magic Trackpad Precision Touchpad driver.

The GitHub `release` workflow refuses to create a full Globe/Fn release unless a trusted signed keyboard driver zip is supplied. See [release.md](release.md).
