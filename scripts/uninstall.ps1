param(
    [string]$InstallDir = "$env:LOCALAPPDATA\MagicTrackpadBridge",
    [string]$TaskName = "MagicTrackpadBridge"
)

$ErrorActionPreference = "Stop"

if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

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

if (Test-Path $InstallDir) {
    Remove-Item -LiteralPath $InstallDir -Recurse -Force
}

$StartMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Magic Trackpad Bridge"
if (Test-Path $StartMenuDir) {
    Remove-Item -LiteralPath $StartMenuDir -Recurse -Force
}

$StartupShortcut = Join-Path ([Environment]::GetFolderPath("Startup")) "Magic Trackpad Bridge.lnk"
if (Test-Path $StartupShortcut) {
    Remove-Item -LiteralPath $StartupShortcut -Force
}

Write-Host "Uninstalled MagicTrackpadBridge."
