param(
    [string]$InstallDir = "$env:LOCALAPPDATA\MagicTrackpadBridge",
    [string]$TaskName = "MagicTrackpadBridge"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Recurse -Force "$RepoRoot\magictrackpad_bridge" $InstallDir
Copy-Item -Recurse -Force "$RepoRoot\scripts" $InstallDir
Copy-Item -Force "$RepoRoot\pyproject.toml" $InstallDir

$ConfigPath = Join-Path $HOME ".magictrackpad-bridge.json"
if (!(Test-Path $ConfigPath)) {
    Push-Location $RepoRoot
    try {
        python -m magictrackpad_bridge write-config --path $ConfigPath
    }
    finally {
        Pop-Location
    }
}

$Python = (Get-Command python).Source
$Action = New-ScheduledTaskAction -Execute $Python -Argument "-m magictrackpad_bridge run --config `"$ConfigPath`"" -WorkingDirectory $InstallDir
$Trigger = New-ScheduledTaskTrigger -AtLogOn
$Principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Highest
$Settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)

Register-ScheduledTask -TaskName $TaskName -Action $Action -Trigger $Trigger -Principal $Principal -Settings $Settings -Force | Out-Null
Start-ScheduledTask -TaskName $TaskName

Write-Host "Installed MagicTrackpadBridge scheduled task: $TaskName"
Write-Host "Config: $ConfigPath"

