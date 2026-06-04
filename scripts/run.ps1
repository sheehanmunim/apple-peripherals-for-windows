param(
    [switch]$DryRun,
    [double]$Seconds = 0,
    [string]$Config = "$HOME\.magictrackpad-bridge.json",
    [switch]$Installed
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ProjectPath = Join-Path $RepoRoot "src\MagicTrackpad.App\MagicTrackpad.App.csproj"
$InstalledExe = Join-Path $env:LOCALAPPDATA "ApplePeripheralsForWindows\app\MagicTrackpad.exe"
$LegacyInstalledExe = Join-Path $env:LOCALAPPDATA "MagicTrackpadBridge\app\MagicTrackpad.exe"
$ArgsList = @("--bridge", "--config", $Config)

if ($DryRun) {
    $ArgsList += "--dry-run"
}

if ($Seconds -gt 0) {
    $ArgsList += @("--seconds", $Seconds.ToString([Globalization.CultureInfo]::InvariantCulture))
}

if ($Installed -and !(Test-Path $InstalledExe) -and (Test-Path $LegacyInstalledExe)) {
    $InstalledExe = $LegacyInstalledExe
}

if ($Installed -and (Test-Path $InstalledExe)) {
    $ArgumentString = "--bridge --config `"$Config`""
    if ($DryRun) {
        $ArgumentString += " --dry-run"
    }
    if ($Seconds -gt 0) {
        $ArgumentString += " --seconds $($Seconds.ToString([Globalization.CultureInfo]::InvariantCulture))"
    }

    $Process = Start-Process -FilePath $InstalledExe -ArgumentList $ArgumentString -PassThru -Wait:($DryRun -or $Seconds -gt 0)
    if (($DryRun -or $Seconds -gt 0) -and $Process.ExitCode -ne 0) {
        exit $Process.ExitCode
    }
}
else {
    dotnet run --project $ProjectPath -c Release -- @ArgsList
}
