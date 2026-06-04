param(
    [string]$InstallDir = "$env:LOCALAPPDATA\MagicTrackpadBridge",
    [string]$TaskName = "MagicTrackpadBridge"
)

$ErrorActionPreference = "Stop"

if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

if (Test-Path $InstallDir) {
    Remove-Item -LiteralPath $InstallDir -Recurse -Force
}

Write-Host "Uninstalled MagicTrackpadBridge."

