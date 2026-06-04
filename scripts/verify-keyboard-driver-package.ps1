param(
    [Parameter(Mandatory = $true)]
    [string]$ZipPath,
    [ValidateSet("win-x64", "win-arm64", "current")]
    [string]$Runtime = "current",
    [switch]$RequireAllArchitectures
)

$ErrorActionPreference = "Stop"

if (![IO.Path]::IsPathRooted($ZipPath)) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
    $ZipPath = Join-Path $RepoRoot $ZipPath
}

if (!(Test-Path $ZipPath)) {
    throw "Keyboard driver package was not found: $ZipPath"
}

function Get-ArchitectureFolders {
    if ($RequireAllArchitectures) {
        return @("AMD64", "ARM64")
    }

    if ($Runtime -eq "current") {
        if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq [Runtime.InteropServices.Architecture]::Arm64) {
            return @("ARM64")
        }

        return @("AMD64")
    }

    if ($Runtime -eq "win-arm64") {
        return @("ARM64")
    }

    return @("AMD64")
}

function Assert-ValidSignature {
    param([string]$Path)
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne "Valid") {
        throw "$Path is not signed by a trusted certificate. Status: $($signature.Status)"
    }
}

$extractRoot = Join-Path $env:TEMP "AppleKeyboardFilterVerify-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null

try {
    Expand-Archive -LiteralPath $ZipPath -DestinationPath $extractRoot -Force

    foreach ($architecture in Get-ArchitectureFolders) {
        $driverDir = Get-ChildItem -LiteralPath $extractRoot -Directory -Recurse |
            Where-Object { $_.Name -ieq $architecture } |
            Select-Object -First 1
        if (!$driverDir) {
            throw "Keyboard driver package does not contain a $architecture directory."
        }

        $inf = Get-ChildItem -LiteralPath $driverDir.FullName -Filter "AppleKeyboardFilter.inf" -File -Recurse |
            Select-Object -First 1
        if (!$inf) {
            throw "Keyboard driver package does not contain AppleKeyboardFilter.inf for $architecture."
        }

        $sys = Get-ChildItem -LiteralPath $driverDir.FullName -Filter "AppleKeyboardFilter.sys" -File -Recurse |
            Select-Object -First 1
        if (!$sys) {
            throw "Keyboard driver package does not contain AppleKeyboardFilter.sys for $architecture."
        }

        $catalog = Get-ChildItem -LiteralPath $driverDir.FullName -Filter "*.cat" -File -Recurse |
            Select-Object -First 1
        if (!$catalog) {
            throw "Keyboard driver package does not contain a catalog file for $architecture."
        }

        Assert-ValidSignature $catalog.FullName
        Write-Host "Verified trusted Apple Keyboard Filter package for $architecture."
    }
}
finally {
    if (Test-Path $extractRoot) {
        Remove-Item -LiteralPath $extractRoot -Recurse -Force
    }
}
