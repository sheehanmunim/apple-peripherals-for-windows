param(
    [string]$Amd64Package = "",
    [string]$Arm64Package = "",
    [string]$OutputCab = "",
    [string]$CertificateThumbprint = "",
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ArtifactsRoot = Join-Path $RepoRoot "artifacts\keyboard-filter"

function Resolve-RepoPath {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) {
        return ""
    }

    if ([IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $RepoRoot $Path
}

function Find-Tool {
    param([string]$Name)

    $fromPath = Get-Command $Name -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source
    if ($fromPath) {
        return $fromPath
    }

    Get-ChildItem "C:\Program Files (x86)\Windows Kits" -Recurse -Filter $Name -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

function Expand-IfZip {
    param(
        [string]$Path,
        [string]$Destination
    )

    if ((Test-Path $Path -PathType Leaf) -and $Path.EndsWith(".zip", [StringComparison]::OrdinalIgnoreCase)) {
        New-Item -ItemType Directory -Force -Path $Destination | Out-Null
        Expand-Archive -LiteralPath $Path -DestinationPath $Destination -Force
        return $Destination
    }

    return $Path
}

function Resolve-DriverPackageDir {
    param(
        [string]$PackagePath,
        [string]$Architecture
    )

    if (!(Test-Path $PackagePath)) {
        throw "Driver package path does not exist: $PackagePath"
    }

    $searchRoot = $PackagePath
    $architectureDir = Get-ChildItem -LiteralPath $searchRoot -Directory -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ieq $Architecture } |
        Where-Object {
            Test-Path (Join-Path $_.FullName "AppleKeyboardFilter.inf") -PathType Leaf
        } |
        Select-Object -First 1
    if ($architectureDir) {
        return $architectureDir.FullName
    }

    $inf = Get-ChildItem -LiteralPath $searchRoot -Filter "AppleKeyboardFilter.inf" -File -Recurse |
        Select-Object -First 1
    if (!$inf) {
        throw "Could not find AppleKeyboardFilter.inf for $Architecture under $PackagePath"
    }

    return $inf.DirectoryName
}

function Copy-PackageForCab {
    param(
        [string]$SourceDir,
        [string]$DestinationDir,
        [string]$Architecture
    )

    New-Item -ItemType Directory -Force -Path $DestinationDir | Out-Null
    foreach ($pattern in @("AppleKeyboardFilter.inf", "AppleKeyboardFilter.sys", "*.cat")) {
        $file = Get-ChildItem -LiteralPath $SourceDir -Filter $pattern -File | Select-Object -First 1
        if (!$file) {
            throw "Missing $pattern in $SourceDir for $Architecture."
        }

        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $DestinationDir $file.Name) -Force
    }
}

function Write-Ddf {
    param(
        [string]$DdfPath,
        [string]$SourceRoot,
        [string]$CabName,
        [string]$CabDir
    )

    $lines = @(
        ".OPTION EXPLICIT",
        ".Set CabinetFileCountThreshold=0",
        ".Set FolderFileCountThreshold=0",
        ".Set FolderSizeThreshold=0",
        ".Set MaxCabinetSize=0",
        ".Set MaxDiskFileCount=0",
        ".Set MaxDiskSize=0",
        ".Set CompressionType=MSZIP",
        ".Set Cabinet=on",
        ".Set Compress=on",
        ".Set UniqueFiles=off",
        ".Set CabinetNameTemplate=$CabName",
        ".Set DiskDirectory1=$CabDir"
    )

    foreach ($architecture in @("AMD64", "ARM64")) {
        $packageDir = Join-Path $SourceRoot $architecture
        $lines += ".Set DestinationDir=$architecture"
        foreach ($file in Get-ChildItem -LiteralPath $packageDir -File | Sort-Object Name) {
            $lines += "`"$($file.FullName)`""
        }
    }

    Set-Content -LiteralPath $DdfPath -Value $lines -Encoding ASCII
}

$Amd64Package = if ([string]::IsNullOrWhiteSpace($Amd64Package)) {
    Join-Path $ArtifactsRoot "AMD64"
}
else {
    Resolve-RepoPath $Amd64Package
}

$Arm64Package = if ([string]::IsNullOrWhiteSpace($Arm64Package)) {
    Join-Path $ArtifactsRoot "ARM64"
}
else {
    Resolve-RepoPath $Arm64Package
}

$OutputCab = if ([string]::IsNullOrWhiteSpace($OutputCab)) {
    Join-Path $ArtifactsRoot "AppleKeyboardFilterAttestation.cab"
}
else {
    Resolve-RepoPath $OutputCab
}

$makecab = Find-Tool "makecab.exe"
if (!$makecab) {
    throw "makecab.exe was not found."
}

$outputDir = Split-Path -Parent $OutputCab
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
$cabName = Split-Path -Leaf $OutputCab

$workRoot = Join-Path $env:TEMP "AppleKeyboardFilterCab-$([Guid]::NewGuid().ToString('N'))"
$extractRoot = Join-Path $workRoot "extract"
$sourceRoot = Join-Path $workRoot "source"
$ddfPath = Join-Path $workRoot "AppleKeyboardFilterAttestation.ddf"

try {
    New-Item -ItemType Directory -Force -Path $workRoot, $extractRoot, $sourceRoot | Out-Null

    $amd64Root = Expand-IfZip -Path $Amd64Package -Destination (Join-Path $extractRoot "AMD64")
    $arm64Root = Expand-IfZip -Path $Arm64Package -Destination (Join-Path $extractRoot "ARM64")

    $amd64Dir = Resolve-DriverPackageDir -PackagePath $amd64Root -Architecture "AMD64"
    $arm64Dir = Resolve-DriverPackageDir -PackagePath $arm64Root -Architecture "ARM64"

    Copy-PackageForCab -SourceDir $amd64Dir -DestinationDir (Join-Path $sourceRoot "AMD64") -Architecture "AMD64"
    Copy-PackageForCab -SourceDir $arm64Dir -DestinationDir (Join-Path $sourceRoot "ARM64") -Architecture "ARM64"

    if (Test-Path $OutputCab) {
        Remove-Item -LiteralPath $OutputCab -Force
    }

    Write-Ddf -DdfPath $ddfPath -SourceRoot $sourceRoot -CabName $cabName -CabDir $outputDir
    Write-Host "Creating Hardware Dev Center submission CAB..."
    & $makecab /F $ddfPath
    if ($LASTEXITCODE -ne 0) {
        throw "makecab failed with exit code $LASTEXITCODE."
    }

    if (!(Test-Path $OutputCab)) {
        throw "makecab completed but did not create $OutputCab"
    }

    if (![string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        $signtool = Find-Tool "signtool.exe"
        if (!$signtool) {
            throw "signtool.exe was not found."
        }

        Write-Host "Signing submission CAB..."
        & $signtool sign /fd SHA256 /sha1 $CertificateThumbprint /tr $TimestampUrl /td SHA256 $OutputCab
        if ($LASTEXITCODE -ne 0) {
            throw "signtool failed with exit code $LASTEXITCODE."
        }
    }

    Write-Host "Submission CAB: $OutputCab"
}
finally {
    if (Test-Path $workRoot) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force
    }
}
