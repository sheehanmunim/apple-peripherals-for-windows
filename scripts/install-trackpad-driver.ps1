param(
    [string]$PackageUrl = "https://github.com/vitoplantamura/MagicTrackpad2ForWindows/releases/download/v2.0/MT2FW11-20260223-MSSigned.zip",
    [string]$CacheDir = "$env:LOCALAPPDATA\ApplePeripheralsForWindows\drivers",
    [switch]$KeepPackage
)

$ErrorActionPreference = "Stop"

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-DriverArchitecture {
    $arch = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture
    if ($arch -eq [Runtime.InteropServices.Architecture]::Arm64) {
        return "ARM64"
    }

    return "AMD64"
}

function Assert-ValidSignature {
    param([string]$Path)

    $signature = Get-AuthenticodeSignature -FilePath $Path
    if ($signature.Status -ne "Valid") {
        throw "Signature check failed for $Path. Status: $($signature.Status)"
    }
}

if (!(Test-Administrator)) {
    throw "Installing the Precision Touchpad driver requires an elevated PowerShell window. Re-run this script as Administrator."
}

New-Item -ItemType Directory -Force -Path $CacheDir | Out-Null
$zipPath = Join-Path $CacheDir "MagicTrackpad2ForWindows-MSSigned.zip"
$extractRoot = Join-Path $CacheDir "MagicTrackpad2ForWindows-MSSigned"

Write-Host "Downloading Microsoft-signed Magic Trackpad Precision driver..."
Invoke-WebRequest -Uri $PackageUrl -OutFile $zipPath

if (Test-Path $extractRoot) {
    Remove-Item -LiteralPath $extractRoot -Recurse -Force
}

Expand-Archive -Path $zipPath -DestinationPath $extractRoot
$packageRoot = Get-ChildItem -LiteralPath $extractRoot -Directory | Select-Object -First 1
if ($packageRoot -eq $null) {
    throw "Could not find extracted driver package root."
}

$architecture = Get-DriverArchitecture
$driverDir = Join-Path $packageRoot.FullName $architecture
$infPath = Join-Path $driverDir "AmtPtpDevice.inf"
if (!(Test-Path $infPath)) {
    throw "Could not find $architecture driver INF at $infPath"
}

Get-ChildItem -LiteralPath $driverDir -File | Where-Object { $_.Extension -in @(".cat", ".sys", ".dll") } | ForEach-Object {
    Assert-ValidSignature -Path $_.FullName
}

$controlPanel = Join-Path $packageRoot.FullName "AmtPtpControlPanel.exe"
if (Test-Path $controlPanel) {
    Assert-ValidSignature -Path $controlPanel
}

Write-Host "Installing $architecture Precision Touchpad driver..."
$pnputil = Join-Path $env:SystemRoot "System32\pnputil.exe"
$process = Start-Process -FilePath $pnputil -ArgumentList @("/add-driver", "`"$infPath`"", "/install") -Wait -PassThru -NoNewWindow
if ($process.ExitCode -notin @(0, 3010)) {
    throw "pnputil failed with exit code $($process.ExitCode)."
}

$rebootRequired = $process.ExitCode -eq 3010

if (!$KeepPackage -and (Test-Path $zipPath)) {
    Remove-Item -LiteralPath $zipPath -Force
}

Write-Host "Installed Magic Trackpad Precision Touchpad driver."
if ($rebootRequired) {
    Write-Warning "Windows reported that a reboot is required to finish binding the Precision Touchpad driver."
}
else {
    Write-Host "If the trackpad still behaves as a basic mouse, disconnect/reconnect it or reboot Windows."
}
