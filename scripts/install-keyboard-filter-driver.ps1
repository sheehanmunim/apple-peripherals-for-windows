param(
    [string]$DriverDir = "",
    [switch]$AllowTestSigned
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ArchitectureFolder = if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq [Runtime.InteropServices.Architecture]::Arm64) {
    "ARM64"
}
else {
    "AMD64"
}

if ([string]::IsNullOrWhiteSpace($DriverDir)) {
    $DriverDir = Join-Path $RepoRoot "artifacts\keyboard-filter\$ArchitectureFolder"
}
elseif (![IO.Path]::IsPathRooted($DriverDir)) {
    $DriverDir = Join-Path $RepoRoot $DriverDir
}

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-Signed {
    param([string]$Path)
    $sig = Get-AuthenticodeSignature -LiteralPath $Path
    if ($sig.Status -eq "Valid") {
        return
    }

    if ($AllowTestSigned) {
        Write-Warning "$Path is not trusted by this machine ($($sig.Status)). Continuing because -AllowTestSigned was provided."
        return
    }

    throw "$Path is not signed by a trusted certificate ($($sig.Status)). Build or bundle a signed keyboard filter driver package."
}

if (!(Test-Administrator)) {
    throw "Installing the Apple Keyboard Filter driver requires an elevated PowerShell window."
}

$infPath = Join-Path $DriverDir "AppleKeyboardFilter.inf"
$sysPath = Join-Path $DriverDir "AppleKeyboardFilter.sys"
$catPath = Join-Path $DriverDir "AppleKeyboardFilter.cat"
foreach ($required in @($infPath, $sysPath, $catPath)) {
    if (!(Test-Path $required)) {
        throw "Missing required driver package file: $required"
    }
}

Assert-Signed $catPath

Write-Host "Installing Apple Keyboard Filter driver..."
$pnputil = Join-Path $env:SystemRoot "System32\pnputil.exe"
$process = Start-Process -FilePath $pnputil -ArgumentList @("/add-driver", "`"$infPath`"", "/install") -Wait -PassThru -NoNewWindow
if ($process.ExitCode -notin @(0, 3010)) {
    throw "pnputil failed with exit code $($process.ExitCode)."
}

Write-Host "Installed Apple Keyboard Filter driver."
if ($process.ExitCode -eq 3010) {
    Write-Warning "Windows reported that a reboot is required to finish binding the keyboard filter driver."
}
