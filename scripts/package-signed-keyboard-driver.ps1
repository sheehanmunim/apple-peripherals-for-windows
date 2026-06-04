param(
    [Parameter(Mandatory = $true)]
    [string]$SignedPackage,
    [string]$OutputZip = "",
    [switch]$RequireMicrosoftSignature
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ArtifactsRoot = Join-Path $RepoRoot "artifacts\keyboard-filter"

function Resolve-RepoPath {
    param([string]$Path)
    if ([IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $RepoRoot $Path
}

function Expand-Package {
    param(
        [string]$Path,
        [string]$Destination
    )

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    if (Test-Path $Path -PathType Container) {
        Copy-Item -LiteralPath $Path -Destination $Destination -Recurse -Force
        return $Destination
    }

    if ($Path.EndsWith(".zip", [StringComparison]::OrdinalIgnoreCase)) {
        Expand-Archive -LiteralPath $Path -DestinationPath $Destination -Force
        return $Destination
    }

    if ($Path.EndsWith(".cab", [StringComparison]::OrdinalIgnoreCase)) {
        $expand = Join-Path $env:SystemRoot "System32\expand.exe"
        & $expand $Path -F:* $Destination | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "expand.exe failed with exit code $LASTEXITCODE."
        }

        return $Destination
    }

    throw "Unsupported signed package type. Provide a .cab, .zip, or directory."
}

function Find-ArchitectureDir {
    param(
        [string]$Root,
        [string]$Architecture
    )

    $candidates = Get-ChildItem -LiteralPath $Root -Directory -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ieq $Architecture } |
        Where-Object {
            (Test-Path (Join-Path $_.FullName "AppleKeyboardFilter.inf") -PathType Leaf) -and
            (Test-Path (Join-Path $_.FullName "AppleKeyboardFilter.sys") -PathType Leaf) -and
            (Get-ChildItem -LiteralPath $_.FullName -Filter "*.cat" -File -ErrorAction SilentlyContinue | Select-Object -First 1)
        }

    $match = $candidates | Select-Object -First 1
    if ($match) {
        return $match.FullName
    }

    throw "Could not find signed Apple Keyboard Filter package directory for $Architecture."
}

function Assert-ValidCatalogSignature {
    param([string]$CatalogPath)

    $signature = Get-AuthenticodeSignature -LiteralPath $CatalogPath
    if ($signature.Status -ne "Valid") {
        throw "$CatalogPath is not signed by a trusted certificate. Status: $($signature.Status)"
    }

    if ($RequireMicrosoftSignature -and (!$signature.SignerCertificate -or $signature.SignerCertificate.Subject -notmatch "Microsoft")) {
        throw "$CatalogPath is signed, but not by a Microsoft driver-signing certificate. Signer: $($signature.SignerCertificate.Subject)"
    }
}

function Copy-ArchitecturePackage {
    param(
        [string]$SourceDir,
        [string]$DestinationDir
    )

    New-Item -ItemType Directory -Force -Path $DestinationDir | Out-Null
    foreach ($pattern in @("AppleKeyboardFilter.inf", "AppleKeyboardFilter.sys", "*.cat")) {
        $file = Get-ChildItem -LiteralPath $SourceDir -Filter $pattern -File | Select-Object -First 1
        if (!$file) {
            throw "Missing $pattern in $SourceDir."
        }

        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $DestinationDir $file.Name) -Force
    }
}

$SignedPackage = Resolve-RepoPath $SignedPackage
if (!(Test-Path $SignedPackage)) {
    throw "Signed keyboard driver package was not found: $SignedPackage"
}

$OutputZip = if ([string]::IsNullOrWhiteSpace($OutputZip)) {
    Join-Path $ArtifactsRoot "AppleKeyboardFilterDriver-signed.zip"
}
else {
    Resolve-RepoPath $OutputZip
}

$workRoot = Join-Path $env:TEMP "AppleKeyboardFilterSigned-$([Guid]::NewGuid().ToString('N'))"
$extractRoot = Join-Path $workRoot "extract"
$packageRoot = Join-Path $workRoot "AppleKeyboardFilterDriver"

try {
    Expand-Package -Path $SignedPackage -Destination $extractRoot | Out-Null
    foreach ($architecture in @("AMD64", "ARM64")) {
        $sourceDir = Find-ArchitectureDir -Root $extractRoot -Architecture $architecture
        $catalog = Get-ChildItem -LiteralPath $sourceDir -Filter "*.cat" -File | Select-Object -First 1
        Assert-ValidCatalogSignature $catalog.FullName
        Copy-ArchitecturePackage -SourceDir $sourceDir -DestinationDir (Join-Path $packageRoot $architecture)
        Write-Host "Packaged trusted signed Apple Keyboard Filter driver for $architecture."
    }

    $outputDir = Split-Path -Parent $OutputZip
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
    if (Test-Path $OutputZip) {
        Remove-Item -LiteralPath $OutputZip -Force
    }

    Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $OutputZip -Force
    & (Join-Path $PSScriptRoot "verify-keyboard-driver-package.ps1") -ZipPath $OutputZip -RequireAllArchitectures -RequireMicrosoftSignature:$RequireMicrosoftSignature
    if ($LASTEXITCODE -ne 0) {
        throw "Packaged signed keyboard driver verification failed with exit code $LASTEXITCODE."
    }

    Write-Host "Installer-ready signed keyboard driver package: $OutputZip"
}
finally {
    if (Test-Path $workRoot) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force
    }
}
