param(
    [string]$InstallDir = "$env:LOCALAPPDATA\ApplePeripheralsForWindows",
    [string]$TaskName = "ApplePeripheralsBridge"
)

$ErrorActionPreference = "Stop"

foreach ($task in @($TaskName, "MagicTrackpadBridge") | Select-Object -Unique) {
    if (Get-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue) {
        Stop-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue
        Unregister-ScheduledTask -TaskName $task -Confirm:$false
    }
}

Get-CimInstance Win32_Process |
    Where-Object {
        ($_.Name -in @("MagicTrackpad.exe", "python.exe", "pythonw.exe")) -and
        ($_.CommandLine -like "*ApplePeripheralsForWindows*" -or $_.CommandLine -like "*MagicTrackpadBridge*" -or $_.CommandLine -like "*magictrackpad_bridge*")
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

foreach ($dir in @(
    (Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Apple Peripherals for Windows"),
    (Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Magic Trackpad Bridge"),
    (Join-Path $env:LOCALAPPDATA "MagicTrackpadBridge")
)) {
    if ($dir -ne $InstallDir -and (Test-Path $dir)) {
        Remove-Item -LiteralPath $dir -Recurse -Force
    }
}

foreach ($shortcut in @(
    (Join-Path ([Environment]::GetFolderPath("Startup")) "Apple Peripherals.lnk"),
    (Join-Path ([Environment]::GetFolderPath("Startup")) "Magic Trackpad Bridge.lnk")
)) {
    if (Test-Path $shortcut) {
        Remove-Item -LiteralPath $shortcut -Force
    }
}

Write-Host "Uninstalled Apple Peripherals for Windows."
