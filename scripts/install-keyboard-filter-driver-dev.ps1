param(
    [ValidateSet("current", "x64", "ARM64")]
    [string]$Platform = "current",
    [string]$CertificateSubject = "CN=Apple Peripherals for Windows Test Driver",
    [switch]$EnableTestSigning,
    [switch]$NoInstall
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-EffectivePlatform {
    if ($Platform -ne "current") {
        return $Platform
    }

    if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq [Runtime.InteropServices.Architecture]::Arm64) {
        return "ARM64"
    }

    return "x64"
}

function Get-TestSigningEnabled {
    $output = & bcdedit.exe /enum "{current}" 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "bcdedit /enum failed: $output"
    }

    return ($output -join "`n") -match '(?im)^\s*testsigning\s+(Yes|On|True)\s*$'
}

function Enable-TestSigningIfRequested {
    if (Get-TestSigningEnabled) {
        return $false
    }

    if (!$EnableTestSigning) {
        throw "Windows test-signing mode is not enabled. Re-run with -EnableTestSigning from elevated PowerShell, then reboot before expecting the driver to load."
    }

    Write-Warning "Enabling Windows test-signing mode for local development. A reboot is required before the test-signed keyboard filter can load."
    $output = & bcdedit.exe /set testsigning on 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "bcdedit /set testsigning on failed: $output"
    }

    return $true
}

function Get-OrCreateCodeSigningCertificate {
    $existing = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -eq $CertificateSubject -and $_.NotAfter -gt [DateTime]::Now.AddDays(30) } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1
    if ($existing) {
        return $existing
    }

    Write-Host "Creating local test code-signing certificate: $CertificateSubject"
    return New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $CertificateSubject `
        -CertStoreLocation Cert:\CurrentUser\My `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -HashAlgorithm SHA256 `
        -NotAfter ([DateTime]::Now.AddYears(3))
}

function Trust-CertificateForLocalMachine {
    param([System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate)

    $tempCert = Join-Path $env:TEMP "AppleKeyboardFilterTest-$($Certificate.Thumbprint).cer"
    try {
        Export-Certificate -Cert $Certificate -FilePath $tempCert -Force | Out-Null
        Import-Certificate -FilePath $tempCert -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
        Import-Certificate -FilePath $tempCert -CertStoreLocation Cert:\LocalMachine\TrustedPublisher | Out-Null
    }
    finally {
        if (Test-Path $tempCert) {
            Remove-Item -LiteralPath $tempCert -Force
        }
    }
}

if (!(Test-Administrator)) {
    throw "The development keyboard filter installer must run from elevated PowerShell."
}

$effectivePlatform = Get-EffectivePlatform
$architectureFolder = if ($effectivePlatform -eq "ARM64") { "ARM64" } else { "AMD64" }
$rebootRequired = Enable-TestSigningIfRequested
$certificate = Get-OrCreateCodeSigningCertificate
Trust-CertificateForLocalMachine $certificate

Write-Host "Building and test-signing Apple Keyboard Filter driver for $effectivePlatform..."
& (Join-Path $PSScriptRoot "build-keyboard-filter-driver.ps1") -Platform $effectivePlatform -CertificateThumbprint $certificate.Thumbprint
if ($LASTEXITCODE -ne 0) {
    throw "build-keyboard-filter-driver.ps1 failed with exit code $LASTEXITCODE."
}

$driverDir = Join-Path $RepoRoot "artifacts\keyboard-filter\$architectureFolder"
if (!$NoInstall) {
    Write-Host "Installing local test-signed Apple Keyboard Filter driver..."
    & (Join-Path $PSScriptRoot "install-keyboard-filter-driver.ps1") -DriverDir $driverDir -AllowTestSigned
    if ($LASTEXITCODE -ne 0) {
        throw "install-keyboard-filter-driver.ps1 failed with exit code $LASTEXITCODE."
    }
}

Write-Host ""
& (Join-Path $PSScriptRoot "check-keyboard-filter-driver.ps1")

if ($rebootRequired) {
    Write-Warning "Reboot Windows, then run scripts\check-keyboard-filter-driver.ps1 again. The Globe/Fn key cannot work through the filter until test-signing is active after reboot."
}
elseif (!$NoInstall) {
    Write-Warning "If the filter is not bound yet, reconnect the Magic Keyboard or reboot Windows."
}
