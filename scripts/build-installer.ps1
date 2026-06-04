param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ArtifactsRoot = if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    Join-Path $RepoRoot "artifacts\installer"
}
else {
    if ([IO.Path]::IsPathRooted($OutputDir)) { $OutputDir } else { Join-Path $RepoRoot $OutputDir }
}

$ResolvedRepo = [IO.Path]::GetFullPath($RepoRoot)
$ResolvedArtifacts = [IO.Path]::GetFullPath($ArtifactsRoot)
if (!$ResolvedArtifacts.StartsWith($ResolvedRepo, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDir must be inside the repository."
}

$AppProject = Join-Path $RepoRoot "src\MagicTrackpad.App\MagicTrackpad.App.csproj"
$SetupProject = Join-Path $RepoRoot "src\ApplePeripherals.Setup\ApplePeripherals.Setup.csproj"
$PayloadDir = Join-Path $ArtifactsRoot "payload"
$AppPayloadDir = Join-Path $PayloadDir "app"
$SetupOutDir = Join-Path $ArtifactsRoot "setup"
$PayloadZip = Join-Path $ArtifactsRoot "ApplePeripheralsPayload.zip"
$InstallerName = "ApplePeripheralsSetup-$Runtime.exe"
$InstallerPath = Join-Path $ArtifactsRoot $InstallerName

if (Test-Path $ArtifactsRoot) {
    Remove-Item -LiteralPath $ArtifactsRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $AppPayloadDir | Out-Null
New-Item -ItemType Directory -Force -Path $SetupOutDir | Out-Null

Write-Host "Publishing app payload..."
dotnet publish $AppProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $AppPayloadDir

if (!(Test-Path (Join-Path $AppPayloadDir "MagicTrackpad.exe"))) {
    throw "MagicTrackpad.exe was not published to the app payload."
}

Write-Host "Compressing app payload..."
Compress-Archive -Path (Join-Path $AppPayloadDir "*") -DestinationPath $PayloadZip -Force

Write-Host "Publishing setup executable..."
dotnet publish $SetupProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PayloadZip="$PayloadZip" `
    -o $SetupOutDir

$BuiltInstaller = Join-Path $SetupOutDir "ApplePeripheralsSetup.exe"
if (!(Test-Path $BuiltInstaller)) {
    throw "ApplePeripheralsSetup.exe was not created."
}

Copy-Item -LiteralPath $BuiltInstaller -Destination $InstallerPath -Force

Write-Host "Installer created: $InstallerPath"
