param(
    [ValidateSet("x64", "ARM64")]
    [string]$Platform = "x64",
    [string]$Configuration = "Release",
    [string]$OutputDir = "",
    [string]$CertificateThumbprint = "",
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ProjectDir = Join-Path $RepoRoot "src\AppleKeyboardFilter.Driver"
$ProjectPath = Join-Path $ProjectDir "AppleKeyboardFilter.vcxproj"
$ArtifactsRoot = if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    Join-Path $RepoRoot "artifacts\keyboard-filter"
}
else {
    if ([IO.Path]::IsPathRooted($OutputDir)) { $OutputDir } else { Join-Path $RepoRoot $OutputDir }
}

$ArchitectureFolder = if ($Platform -eq "ARM64") { "ARM64" } else { "AMD64" }
$PackageDir = Join-Path $ArtifactsRoot $ArchitectureFolder
$ResolvedRepo = [IO.Path]::GetFullPath($RepoRoot)
$ResolvedArtifacts = [IO.Path]::GetFullPath($ArtifactsRoot)
if (!$ResolvedArtifacts.StartsWith($ResolvedRepo, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDir must be inside the repository."
}

function Find-Tool {
    param([string]$Name)
    Get-ChildItem "C:\Program Files (x86)\Windows Kits" -Recurse -Filter $Name -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

function Find-MSBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $path = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
        if ($path) {
            return $path
        }
    }

    Get-ChildItem ${env:ProgramFiles(x86)} -Recurse -Filter MSBuild.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -like "*Microsoft Visual Studio*" } |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

function Add-WdkToolsToPath {
    $infVerifDll = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\Tools" -Recurse -Filter InfVerif.dll -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if (!$infVerifDll) {
        return
    }

    $toolsRoot = Split-Path -Parent (Split-Path -Parent $infVerifDll.FullName)
    if ($env:PATH -notlike "*$toolsRoot*") {
        $env:PATH = "$toolsRoot;$env:PATH"
    }
}

$msbuild = Find-MSBuild
if (!$msbuild) {
    throw "MSBuild was not found. Install Visual Studio Build Tools with the Windows Driver Kit before building AppleKeyboardFilter.sys."
}

Add-WdkToolsToPath

Write-Host "Building AppleKeyboardFilter driver ($Configuration|$Platform)..."
& $msbuild $ProjectPath "/p:Configuration=$Configuration" "/p:Platform=$Platform" /m
if ($LASTEXITCODE -ne 0) {
    throw "MSBuild failed with exit code $LASTEXITCODE."
}

$sys = Get-ChildItem $ProjectDir -Recurse -Filter AppleKeyboardFilter.sys -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (!$sys) {
    throw "Build completed but AppleKeyboardFilter.sys was not found."
}

if (Test-Path $PackageDir) {
    Remove-Item -LiteralPath $PackageDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $PackageDir | Out-Null
Copy-Item -LiteralPath $sys.FullName -Destination (Join-Path $PackageDir "AppleKeyboardFilter.sys") -Force
Copy-Item -LiteralPath (Join-Path $ProjectDir "AppleKeyboardFilter.inf") -Destination (Join-Path $PackageDir "AppleKeyboardFilter.inf") -Force

$inf2cat = Find-Tool "inf2cat.exe"
if ($inf2cat) {
    $os = if ($Platform -eq "ARM64") { "10_ARM64" } else { "10_X64" }
    Write-Host "Creating driver catalog with Inf2Cat..."
    & $inf2cat "/driver:$PackageDir" "/os:$os"
    if ($LASTEXITCODE -ne 0) {
        throw "Inf2Cat failed with exit code $LASTEXITCODE."
    }
}
else {
    Write-Warning "Inf2Cat was not found. The package will not have a catalog until the Windows Driver Kit is installed."
}

$cat = Join-Path $PackageDir "AppleKeyboardFilter.cat"
if (![string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    $signtool = Find-Tool "signtool.exe"
    if (!$signtool) {
        throw "signtool.exe was not found."
    }

    if (!(Test-Path $cat)) {
        throw "AppleKeyboardFilter.cat was not found. Run Inf2Cat before signing."
    }

    Write-Host "Signing driver catalog..."
    & $signtool sign /fd SHA256 /sha1 $CertificateThumbprint /tr $TimestampUrl /td SHA256 $cat
    if ($LASTEXITCODE -ne 0) {
        throw "signtool failed with exit code $LASTEXITCODE."
    }
}

$zipPath = Join-Path $ArtifactsRoot "AppleKeyboardFilterDriver.zip"
if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path $PackageDir -DestinationPath $zipPath -Force

Write-Host "Driver package: $PackageDir"
Write-Host "Driver package zip: $zipPath"
if (!(Test-Path $cat)) {
    Write-Warning "No driver catalog was produced. This package cannot be installed on normal Windows systems."
}
