param(
    [switch]$All,
    [switch]$Json,
    [switch]$RequireReady,
    [switch]$RequireMicrosoftSigner
)

$ErrorActionPreference = "Stop"

function Get-DevicePropertyData {
    param(
        [string]$InstanceId,
        [string]$KeyName
    )

    try {
        return (Get-PnpDeviceProperty -InstanceId $InstanceId -KeyName $KeyName -ErrorAction Stop).Data
    }
    catch {
        return $null
    }
}

function Get-DeviceRegistryProperty {
    param(
        [string]$InstanceId,
        [string]$Name
    )

    $path = "HKLM:\SYSTEM\CurrentControlSet\Enum\$InstanceId"
    try {
        $item = Get-ItemProperty -LiteralPath $path -ErrorAction Stop
        return $item.$Name
    }
    catch {
        return $null
    }
}

function Convert-ToStringArray {
    param($Value)

    if ($null -eq $Value) {
        return @()
    }

    if ($Value -is [array]) {
        return @($Value | ForEach-Object { "$_" })
    }

    return @("$Value")
}

function Test-MagicKeyboardText {
    param([string]$Text)

    return $Text -match 'VID&0001004C_PID&(0320|0267|026C)' -or
        $Text -match 'VID_05AC&PID_(0321|0267|026C)'
}

function Test-FilterTargetInstanceId {
    param([string]$InstanceId)

    return $InstanceId -match '^BTHENUM\\\{00001124-0000-1000-8000-00805F9B34FB\}_VID&0001004C_PID&(0320|0267|026C)' -or
        $InstanceId -match '^USB\\VID_05AC&PID_(0321|0267|026C)&MI_01'
}

function Get-MagicKeyboardDevices {
    $devices = if ($All) { Get-PnpDevice } else { Get-PnpDevice -PresentOnly }
    $candidates = $devices | Where-Object {
        (Test-MagicKeyboardText $_.InstanceId) -or
        $_.FriendlyName -match 'Magic Keyboard|Apple Wireless Keyboard'
    }

    foreach ($device in $candidates) {
        $hardwareIds = Convert-ToStringArray (Get-DevicePropertyData $device.InstanceId "DEVPKEY_Device_HardwareIds")
        $text = (@($device.InstanceId) + $hardwareIds) -join "`n"
        if (!(Test-MagicKeyboardText $text)) {
            continue
        }

        $driverInfPath = Get-DevicePropertyData $device.InstanceId "DEVPKEY_Device_DriverInfPath"
        $driverProvider = Get-DevicePropertyData $device.InstanceId "DEVPKEY_Device_DriverProvider"
        $driverDesc = Get-DevicePropertyData $device.InstanceId "DEVPKEY_Device_DriverDesc"
        $service = Get-DevicePropertyData $device.InstanceId "DEVPKEY_Device_Service"
        $lowerFilters = Convert-ToStringArray (Get-DeviceRegistryProperty $device.InstanceId "LowerFilters")
        $upperFilters = Convert-ToStringArray (Get-DeviceRegistryProperty $device.InstanceId "UpperFilters")
        $isFilterTarget = Test-FilterTargetInstanceId $device.InstanceId
        $filterBound = ($lowerFilters -contains "AppleKeyboardFilter") -or
            ("$driverInfPath" -ieq "AppleKeyboardFilter.inf") -or
            ("$driverDesc" -match "Globe/Fn Filter")

        [pscustomobject]@{
            Status = $device.Status
            Class = $device.Class
            FriendlyName = $device.FriendlyName
            InstanceId = $device.InstanceId
            IsFilterTarget = $isFilterTarget
            FilterBound = $filterBound
            DriverInfPath = $driverInfPath
            DriverProvider = $driverProvider
            DriverDescription = $driverDesc
            Service = $service
            LowerFilters = $lowerFilters
            UpperFilters = $upperFilters
            HardwareIds = $hardwareIds
        }
    }
}

function Get-DriverStorePackages {
    $pnputil = Join-Path $env:SystemRoot "System32\pnputil.exe"
    $output = & $pnputil /enum-drivers 2>&1
    $packages = New-Object System.Collections.Generic.List[object]
    $current = @{}

    function Add-CurrentPackage {
        if ($current.Count -eq 0) {
            return
        }

        $packages.Add([pscustomobject]@{
            PublishedName = $current["Published Name"]
            OriginalName = $current["Original Name"]
            ProviderName = $current["Provider Name"]
            ClassName = $current["Class Name"]
            DriverVersion = $current["Driver Version"]
            SignerName = $current["Signer Name"]
            CatalogFile = $current["Catalog File"]
        })
    }

    foreach ($line in $output) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            Add-CurrentPackage
            $current = @{}
            continue
        }

        if ($line -match '^\s*([^:]+):\s*(.*?)\s*$') {
            $current[$matches[1].Trim()] = $matches[2].Trim()
        }
    }
    Add-CurrentPackage

    return @($packages | Where-Object {
        $_.OriginalName -ieq "AppleKeyboardFilter.inf" -or
        $_.ProviderName -eq "Apple Peripherals for Windows"
    })
}

$keyboardDevices = @(Get-MagicKeyboardDevices)
$filterTargets = @($keyboardDevices | Where-Object { $_.IsFilterTarget })
$driverStorePackages = @(Get-DriverStorePackages)
$filterInStore = $driverStorePackages.Count -gt 0
$filterBound = @($filterTargets | Where-Object { $_.FilterBound }).Count -gt 0
$microsoftSigned = @($driverStorePackages | Where-Object { $_.SignerName -match "Microsoft" }).Count -gt 0
$ready = $filterTargets.Count -gt 0 -and $filterInStore -and $filterBound
if ($RequireMicrosoftSigner) {
    $ready = $ready -and $microsoftSigned
}

$diagnosis = if ($filterTargets.Count -eq 0) {
    "No present Apple Magic Keyboard filter target was found."
}
elseif (!$filterInStore) {
    "Apple Keyboard Filter is not installed in the Windows driver store."
}
elseif (!$filterBound) {
    "Apple Keyboard Filter is in the driver store but is not bound to the Magic Keyboard target. Reconnect the keyboard or reboot after installing."
}
elseif ($RequireMicrosoftSigner -and !$microsoftSigned) {
    "Apple Keyboard Filter is bound, but the driver store package is not Microsoft-signed."
}
else {
    "Apple Keyboard Filter is installed and bound to a Magic Keyboard target."
}

$result = [pscustomobject]@{
    CapturedAt = [DateTimeOffset]::Now
    Ready = $ready
    Diagnosis = $diagnosis
    RequireMicrosoftSigner = [bool]$RequireMicrosoftSigner
    MagicKeyboardDevices = $keyboardDevices
    FilterDriverStorePackages = $driverStorePackages
}

if ($Json) {
    $result | ConvertTo-Json -Depth 8
}
else {
    Write-Host "Ready: $($result.Ready)"
    Write-Host "Diagnosis: $($result.Diagnosis)"
    Write-Host ""
    Write-Host "Magic Keyboard devices:"
    $keyboardDevices |
        Select-Object Status, Class, FriendlyName, IsFilterTarget, FilterBound, DriverInfPath, Service, InstanceId |
        Format-Table -AutoSize
    Write-Host ""
    Write-Host "Apple Keyboard Filter driver store packages:"
    $driverStorePackages |
        Select-Object PublishedName, OriginalName, ProviderName, DriverVersion, SignerName, CatalogFile |
        Format-Table -AutoSize
}

if ($RequireReady -and !$ready) {
    exit 2
}
