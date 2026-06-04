param(
    [switch]$DryRun,
    [string]$Config = "$HOME\.magictrackpad-bridge.json"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ArgsList = @("-m", "magictrackpad_bridge", "run", "--config", $Config)
if ($DryRun) {
    $ArgsList += "--dry-run"
}

Push-Location $RepoRoot
try {
    python @ArgsList
}
finally {
    Pop-Location
}

