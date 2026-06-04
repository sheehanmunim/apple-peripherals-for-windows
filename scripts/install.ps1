param(
    [string]$InstallDir = "$env:LOCALAPPDATA\MagicTrackpadBridge",
    [string]$TaskName = "MagicTrackpadBridge",
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ProjectPath = Join-Path $RepoRoot "src\MagicTrackpad.App\MagicTrackpad.App.csproj"
$AppDir = Join-Path $InstallDir "app"
$ConfigPath = Join-Path $HOME ".magictrackpad-bridge.json"
$StartMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Magic Trackpad Bridge"
$StartupDir = [Environment]::GetFolderPath("Startup")
$StartupShortcutPath = Join-Path $StartupDir "Magic Trackpad Bridge.lnk"

function Stop-MagicTrackpadProcesses {
    Get-CimInstance Win32_Process |
        Where-Object {
            ($_.Name -in @("MagicTrackpad.exe", "python.exe", "pythonw.exe")) -and
            ($_.CommandLine -like "*MagicTrackpadBridge*" -or $_.CommandLine -like "*magictrackpad_bridge*")
        } |
        ForEach-Object {
            try {
                Stop-Process -Id $_.ProcessId -Force -ErrorAction Stop
            }
            catch {
                Write-Warning "Could not stop process $($_.ProcessId): $($_.Exception.Message)"
            }
        }
}

if (!(Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET 8 SDK is required to build and install MagicTrackpad.exe. Install Microsoft.DotNet.SDK.8, then run this script again."
}

Stop-MagicTrackpadProcesses

New-Item -ItemType Directory -Force -Path $AppDir | Out-Null
dotnet publish $ProjectPath -c Release -r $Runtime --self-contained false -o $AppDir

$ExePath = Join-Path $AppDir "MagicTrackpad.exe"
if (!(Test-Path $ExePath)) {
    throw "Publish completed but MagicTrackpad.exe was not found at $ExePath"
}

if (!(Test-Path $ConfigPath)) {
    $ConfigProcess = Start-Process -FilePath $ExePath -ArgumentList "--write-config --config `"$ConfigPath`"" -Wait -PassThru
    if ($ConfigProcess.ExitCode -ne 0) {
        throw "Could not create default config at $ConfigPath"
    }
}

New-Item -ItemType Directory -Force -Path $StartMenuDir | Out-Null
$Shell = New-Object -ComObject WScript.Shell

$SettingsShortcut = $Shell.CreateShortcut((Join-Path $StartMenuDir "Magic Trackpad Settings.lnk"))
$SettingsShortcut.TargetPath = $ExePath
$SettingsShortcut.Arguments = "--settings --config `"$ConfigPath`""
$SettingsShortcut.WorkingDirectory = $AppDir
$SettingsShortcut.Description = "Configure Apple Magic Trackpad gestures on Windows"
$SettingsShortcut.Save()

$RunShortcut = $Shell.CreateShortcut((Join-Path $StartMenuDir "Run Magic Trackpad Bridge.lnk"))
$RunShortcut.TargetPath = $ExePath
$RunShortcut.Arguments = "--bridge --config `"$ConfigPath`""
$RunShortcut.WorkingDirectory = $AppDir
$RunShortcut.Description = "Start the Apple Magic Trackpad Windows bridge"
$RunShortcut.Save()

$TaskInstalled = $false
try {
    $Action = New-ScheduledTaskAction -Execute $ExePath -Argument "--bridge --config `"$ConfigPath`"" -WorkingDirectory $AppDir
    $Trigger = New-ScheduledTaskTrigger -AtLogOn
    $Principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited
    $Settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -RestartCount 10 -RestartInterval (New-TimeSpan -Minutes 1) -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Seconds 0)
    Register-ScheduledTask -TaskName $TaskName -Action $Action -Trigger $Trigger -Principal $Principal -Settings $Settings -Force | Out-Null
    Start-ScheduledTask -TaskName $TaskName
    $TaskInstalled = $true
}
catch {
    $StartupShortcut = $Shell.CreateShortcut($StartupShortcutPath)
    $StartupShortcut.TargetPath = $ExePath
    $StartupShortcut.Arguments = "--bridge --config `"$ConfigPath`""
    $StartupShortcut.WorkingDirectory = $AppDir
    $StartupShortcut.Description = "Start Apple Magic Trackpad gestures at sign in"
    $StartupShortcut.Save()
    Start-Process -FilePath $ExePath -ArgumentList "--bridge --config `"$ConfigPath`"" -WorkingDirectory $AppDir
    Write-Warning "Scheduled task was not registered; installed Startup shortcut and started the bridge for this session. $($_.Exception.Message)"
}

if ($TaskInstalled) {
    if (Test-Path $StartupShortcutPath) {
        Remove-Item -LiteralPath $StartupShortcutPath -Force
    }
    Write-Host "Installed MagicTrackpadBridge scheduled task: $TaskName"
}
else {
    Write-Host "Startup shortcut: $StartupShortcutPath"
}

Write-Host "Settings app shortcut: $StartMenuDir\Magic Trackpad Settings.lnk"
Write-Host "Installed app: $ExePath"
Write-Host "Config: $ConfigPath"
