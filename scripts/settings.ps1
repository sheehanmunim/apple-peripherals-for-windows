param(
    [string]$Config = "$HOME\.magictrackpad-bridge.json"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot

Push-Location $RepoRoot
try {
    python -m magictrackpad_bridge settings --config $Config
}
finally {
    Pop-Location
}

