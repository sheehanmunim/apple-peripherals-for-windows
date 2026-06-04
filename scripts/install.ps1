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
$Pythonw = Join-Path (Split-Path -Parent $Python) "pythonw.exe"
if (!(Test-Path $Pythonw)) {
    $Pythonw = $Python
}

$StartMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Magic Trackpad Bridge"
New-Item -ItemType Directory -Force -Path $StartMenuDir | Out-Null
$StartupDir = [Environment]::GetFolderPath("Startup")
$Shell = New-Object -ComObject WScript.Shell

$SettingsShortcut = $Shell.CreateShortcut((Join-Path $StartMenuDir "Magic Trackpad Settings.lnk"))
$SettingsShortcut.TargetPath = $Pythonw
$SettingsShortcut.Arguments = "-m magictrackpad_bridge settings --config `"$ConfigPath`""
$SettingsShortcut.WorkingDirectory = $InstallDir
$SettingsShortcut.Description = "Configure Apple Magic Trackpad gestures on Windows"
$SettingsShortcut.Save()

$RunShortcut = $Shell.CreateShortcut((Join-Path $StartMenuDir "Run Magic Trackpad Bridge.lnk"))
$RunShortcut.TargetPath = $Pythonw
$RunShortcut.Arguments = "-m magictrackpad_bridge run --config `"$ConfigPath`""
$RunShortcut.WorkingDirectory = $InstallDir
$RunShortcut.Description = "Start the Apple Magic Trackpad Windows bridge"
$RunShortcut.Save()

$TaskInstalled = $false
$StartupShortcutPath = Join-Path $StartupDir "Magic Trackpad Bridge.lnk"
try {
    $Action = New-ScheduledTaskAction -Execute $Pythonw -Argument "-m magictrackpad_bridge run --config `"$ConfigPath`"" -WorkingDirectory $InstallDir
    $Trigger = New-ScheduledTaskTrigger -AtLogOn
    $Principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited
    $Settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -RestartCount 10 -RestartInterval (New-TimeSpan -Minutes 1) -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Seconds 0)
    Register-ScheduledTask -TaskName $TaskName -Action $Action -Trigger $Trigger -Principal $Principal -Settings $Settings -Force | Out-Null
    Start-ScheduledTask -TaskName $TaskName
    $TaskInstalled = $true
}
catch {
    $StartupShortcut = $Shell.CreateShortcut($StartupShortcutPath)
    $StartupShortcut.TargetPath = $Pythonw
    $StartupShortcut.Arguments = "-m magictrackpad_bridge run --config `"$ConfigPath`""
    $StartupShortcut.WorkingDirectory = $InstallDir
    $StartupShortcut.Description = "Start Apple Magic Trackpad gestures at sign in"
    $StartupShortcut.Save()
    Start-Process -FilePath $Pythonw -ArgumentList "-m magictrackpad_bridge run --config `"$ConfigPath`"" -WorkingDirectory $InstallDir
    Write-Warning "Scheduled task was not registered; installed Startup shortcut and started the bridge for this session. $($_.Exception.Message)"
}

if ($TaskInstalled) {
    Write-Host "Installed MagicTrackpadBridge scheduled task: $TaskName"
    if (Test-Path $StartupShortcutPath) {
        Remove-Item -LiteralPath $StartupShortcutPath -Force
    }
}
else {
    Write-Host "Startup shortcut: $StartupShortcutPath"
}
Write-Host "Settings app shortcut: $StartMenuDir\Magic Trackpad Settings.lnk"
Write-Host "Config: $ConfigPath"
