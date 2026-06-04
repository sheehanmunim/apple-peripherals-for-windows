param(
    [string]$Config = "$HOME\.magictrackpad-bridge.json",
    [switch]$Installed
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ProjectPath = Join-Path $RepoRoot "src\MagicTrackpad.App\MagicTrackpad.App.csproj"
$InstalledExe = Join-Path $env:LOCALAPPDATA "MagicTrackpadBridge\app\MagicTrackpad.exe"
$ArgsList = @("--settings", "--config", $Config)

if ($Installed -and (Test-Path $InstalledExe)) {
    & $InstalledExe @ArgsList
}
else {
    dotnet run --project $ProjectPath -c Release -- @ArgsList
}
