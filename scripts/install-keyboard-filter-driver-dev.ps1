param(
    [ValidateSet("current", "x64", "ARM64")]
    [string]$Platform = "current",
    [string]$DriverDir = "",
    [string]$DriverZip = "",
    [switch]$DownloadLatestArtifact,
    [string]$Repository = "sheehanmunim/apple-peripherals-for-windows",
    [string]$RunId = "",
    [string]$ArtifactRoot = "",
    [switch]$DownloadOnly,
    [switch]$SkipBuild,
    [string]$CertificateSubject = "CN=Apple Peripherals for Windows Test Driver",
    [switch]$EnableTestSigning,
    [switch]$NoInstall,
    [switch]$Elevate
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Quote-ProcessArgument {
    param([string]$Value)

    return '"' + ($Value -replace '"', '\"') + '"'
}

function Start-ElevatedSelf {
    $arguments = New-Object System.Collections.Generic.List[string]
    $arguments.Add("-ExecutionPolicy")
    $arguments.Add("Bypass")
    $arguments.Add("-File")
    $arguments.Add((Quote-ProcessArgument $PSCommandPath))
    $arguments.Add("-Platform")
    $arguments.Add($Platform)

    if (![string]::IsNullOrWhiteSpace($DriverDir)) {
        $arguments.Add("-DriverDir")
        $arguments.Add((Quote-ProcessArgument $DriverDir))
    }

    if (![string]::IsNullOrWhiteSpace($DriverZip)) {
        $arguments.Add("-DriverZip")
        $arguments.Add((Quote-ProcessArgument $DriverZip))
    }

    if ($DownloadLatestArtifact) {
        $arguments.Add("-DownloadLatestArtifact")
    }

    if (![string]::IsNullOrWhiteSpace($Repository)) {
        $arguments.Add("-Repository")
        $arguments.Add((Quote-ProcessArgument $Repository))
    }

    if (![string]::IsNullOrWhiteSpace($RunId)) {
        $arguments.Add("-RunId")
        $arguments.Add((Quote-ProcessArgument $RunId))
    }

    if (![string]::IsNullOrWhiteSpace($ArtifactRoot)) {
        $arguments.Add("-ArtifactRoot")
        $arguments.Add((Quote-ProcessArgument $ArtifactRoot))
    }

    if ($DownloadOnly) {
        $arguments.Add("-DownloadOnly")
    }

    if ($SkipBuild) {
        $arguments.Add("-SkipBuild")
    }

    if (![string]::IsNullOrWhiteSpace($CertificateSubject)) {
        $arguments.Add("-CertificateSubject")
        $arguments.Add((Quote-ProcessArgument $CertificateSubject))
    }

    if ($EnableTestSigning) {
        $arguments.Add("-EnableTestSigning")
    }

    if ($NoInstall) {
        $arguments.Add("-NoInstall")
    }

    Write-Host "Requesting Administrator approval to install the Apple Keyboard Filter driver..."
    try {
        $process = Start-Process -FilePath "powershell.exe" -ArgumentList $arguments -Verb RunAs -WindowStyle Normal -Wait -PassThru
        exit $process.ExitCode
    }
    catch [System.InvalidOperationException] {
        throw "Administrator approval was canceled. The Globe/Fn key cannot work until the Apple Keyboard Filter driver is installed from an elevated prompt."
    }
}

function Get-EffectivePlatform {
    if ($Platform -ne "current") {
        return $Platform
    }

    if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq [Runtime.InteropServices.Architecture]::Arm64) {
        return "ARM64"
    }

    return "x64"
}

function Get-TestSigningEnabled {
    $output = & bcdedit.exe /enum "{current}" 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "bcdedit /enum failed: $output"
    }

    return ($output -join "`n") -match '(?im)^\s*testsigning\s+(Yes|On|True)\s*$'
}

function Resolve-RepoPath {
    param([string]$Path)

    if ([IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $RepoRoot $Path
}

function Test-DriverPackageDir {
    param([string]$Path)

    return (Test-Path (Join-Path $Path "AppleKeyboardFilter.inf") -PathType Leaf) -and
        (Test-Path (Join-Path $Path "AppleKeyboardFilter.sys") -PathType Leaf) -and
        (Test-Path (Join-Path $Path "AppleKeyboardFilter.cat") -PathType Leaf)
}

function Find-ArchitectureDir {
    param(
        [string]$Root,
        [string]$ArchitectureFolder
    )

    if (Test-DriverPackageDir $Root) {
        return $Root
    }

    $match = Get-ChildItem -LiteralPath $Root -Directory -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ieq $ArchitectureFolder } |
        Where-Object { Test-DriverPackageDir $_.FullName } |
        Select-Object -First 1

    if (!$match) {
        throw "Could not find $ArchitectureFolder AppleKeyboardFilter.inf/sys/cat package files under $Root."
    }

    return $match.FullName
}

function Copy-DriverPackage {
    param(
        [string]$SourceDir,
        [string]$DestinationDir
    )

    New-Item -ItemType Directory -Force -Path $DestinationDir | Out-Null
    foreach ($fileName in @("AppleKeyboardFilter.inf", "AppleKeyboardFilter.sys", "AppleKeyboardFilter.cat")) {
        Copy-Item -LiteralPath (Join-Path $SourceDir $fileName) -Destination (Join-Path $DestinationDir $fileName) -Force
    }
}

function Expand-DriverZip {
    param(
        [string]$ZipPath,
        [string]$Destination
    )

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Expand-Archive -LiteralPath $ZipPath -DestinationPath $Destination -Force
}

function Get-LatestKeyboardDriverRunId {
    param([string]$RepositoryName)

    if (!(Get-Command gh -ErrorAction SilentlyContinue)) {
        throw "GitHub CLI 'gh' is required for -DownloadLatestArtifact. Install gh or pass -DriverDir/-DriverZip."
    }

    $json = & gh run list --repo $RepositoryName --workflow keyboard-driver.yml --status success --limit 1 --json databaseId 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "gh run list failed: $json"
    }

    $runs = $json | ConvertFrom-Json
    if (!$runs -or $runs.Count -lt 1) {
        throw "No successful keyboard-driver workflow run was found in $RepositoryName."
    }

    return [string]$runs[0].databaseId
}

function Resolve-GitHubDriverArtifact {
    param(
        [string]$ArchitectureFolder,
        [string]$RepositoryName,
        [string]$WorkflowRunId,
        [string]$Root
    )

    if (![string]::IsNullOrWhiteSpace($DriverDir) -or ![string]::IsNullOrWhiteSpace($DriverZip)) {
        return $DriverDir
    }

    if (!(Get-Command gh -ErrorAction SilentlyContinue)) {
        throw "GitHub CLI 'gh' is required for -DownloadLatestArtifact. Install gh or pass -DriverDir/-DriverZip."
    }

    $artifactName = if ($ArchitectureFolder -eq "ARM64") {
        "AppleKeyboardFilterDriver-ARM64-unsigned"
    }
    else {
        "AppleKeyboardFilterDriver-AMD64-unsigned"
    }

    $run = if (![string]::IsNullOrWhiteSpace($WorkflowRunId)) {
        $WorkflowRunId
    }
    else {
        Get-LatestKeyboardDriverRunId -RepositoryName $RepositoryName
    }

    $downloadRoot = if ([string]::IsNullOrWhiteSpace($Root)) {
        Join-Path $RepoRoot "artifacts\keyboard-filter-latest"
    }
    elseif ([IO.Path]::IsPathRooted($Root)) {
        $Root
    }
    else {
        Join-Path $RepoRoot $Root
    }

    $artifactDir = Join-Path $downloadRoot $artifactName
    if (Test-Path $artifactDir) {
        Remove-Item -LiteralPath $artifactDir -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
    Write-Host "Downloading $artifactName from $RepositoryName workflow run $run..."
    & gh run download $run --repo $RepositoryName --name $artifactName --dir $artifactDir
    if ($LASTEXITCODE -ne 0) {
        throw "gh run download failed with exit code $LASTEXITCODE."
    }

    return Find-ArchitectureDir -Root $artifactDir -ArchitectureFolder $ArchitectureFolder
}

function Find-Tool {
    param([string]$Name)

    Get-ChildItem "C:\Program Files (x86)\Windows Kits" -Recurse -Filter $Name -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

function Sign-DriverCatalog {
    param(
        [string]$CatalogPath,
        [System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate
    )

    $signtool = Find-Tool "signtool.exe"
    if ($signtool) {
        & $signtool sign /fd SHA256 /sha1 $Certificate.Thumbprint $CatalogPath
        if ($LASTEXITCODE -ne 0) {
            throw "signtool failed with exit code $LASTEXITCODE."
        }
    }
    else {
        $signature = Set-AuthenticodeSignature -FilePath $CatalogPath -Certificate $Certificate -HashAlgorithm SHA256
        if ($signature.Status -ne "Valid") {
            throw "Set-AuthenticodeSignature failed for $CatalogPath. Status: $($signature.Status)"
        }
    }

    $verify = Get-AuthenticodeSignature -LiteralPath $CatalogPath
    if ($verify.Status -ne "Valid") {
        throw "Catalog signing did not produce a trusted signature. Status: $($verify.Status)"
    }
}

function Prepare-DriverPackage {
    param(
        [string]$ArchitectureFolder,
        [string]$WorkRoot
    )

    $packageRoot = Join-Path $WorkRoot "package"
    $destinationDir = Join-Path $packageRoot $ArchitectureFolder

    if (![string]::IsNullOrWhiteSpace($DriverZip)) {
        $zip = Resolve-RepoPath $DriverZip
        if (!(Test-Path $zip -PathType Leaf)) {
            throw "DriverZip was not found: $zip"
        }

        $extractRoot = Join-Path $WorkRoot "extract"
        Expand-DriverZip -ZipPath $zip -Destination $extractRoot
        Copy-DriverPackage -SourceDir (Find-ArchitectureDir -Root $extractRoot -ArchitectureFolder $ArchitectureFolder) -DestinationDir $destinationDir
        return $destinationDir
    }

    if (![string]::IsNullOrWhiteSpace($DriverDir)) {
        $dir = Resolve-RepoPath $DriverDir
        if (!(Test-Path $dir -PathType Container)) {
            throw "DriverDir was not found: $dir"
        }

        Copy-DriverPackage -SourceDir (Find-ArchitectureDir -Root $dir -ArchitectureFolder $ArchitectureFolder) -DestinationDir $destinationDir
        return $destinationDir
    }

    $defaultDir = Join-Path $RepoRoot "artifacts\keyboard-filter\$ArchitectureFolder"
    if ($SkipBuild) {
        if (!(Test-DriverPackageDir $defaultDir)) {
            throw "Default driver package was not found at $defaultDir."
        }

        Copy-DriverPackage -SourceDir $defaultDir -DestinationDir $destinationDir
        return $destinationDir
    }

    try {
        & (Join-Path $PSScriptRoot "build-keyboard-filter-driver.ps1") -Platform $effectivePlatform
        if ($LASTEXITCODE -ne 0) {
            throw "build-keyboard-filter-driver.ps1 failed with exit code $LASTEXITCODE."
        }
    }
    catch {
        if (!(Test-DriverPackageDir $defaultDir)) {
            throw
        }

        Write-Warning "Build from source failed; using existing package at $defaultDir. Error: $($_.Exception.Message)"
    }

    Copy-DriverPackage -SourceDir $defaultDir -DestinationDir $destinationDir
    return $destinationDir
}

function Enable-TestSigningIfRequested {
    if (Get-TestSigningEnabled) {
        return $false
    }

    if (!$EnableTestSigning) {
        throw "Windows test-signing mode is not enabled. Re-run with -EnableTestSigning from elevated PowerShell, then reboot before expecting the driver to load."
    }

    Write-Warning "Enabling Windows test-signing mode for local development. A reboot is required before the test-signed keyboard filter can load."
    $output = & bcdedit.exe /set testsigning on 2>&1
    if ($LASTEXITCODE -ne 0) {
        $message = $output -join "`n"
        if ($message -match "Secure Boot policy") {
            throw "Secure Boot is blocking Windows test-signing mode. Disable Secure Boot in UEFI/BIOS, reboot Windows, then run this installer again; or use a Microsoft-signed Apple Keyboard Filter driver package."
        }

        throw "bcdedit /set testsigning on failed: $message"
    }

    return $true
}

function Get-OrCreateCodeSigningCertificate {
    $existing = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -eq $CertificateSubject -and $_.NotAfter -gt [DateTime]::Now.AddDays(30) } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1
    if ($existing) {
        return $existing
    }

    Write-Host "Creating local test code-signing certificate: $CertificateSubject"
    return New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $CertificateSubject `
        -CertStoreLocation Cert:\CurrentUser\My `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -HashAlgorithm SHA256 `
        -NotAfter ([DateTime]::Now.AddYears(3))
}

function Trust-CertificateForLocalMachine {
    param([System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate)

    $tempCert = Join-Path $env:TEMP "AppleKeyboardFilterTest-$($Certificate.Thumbprint).cer"
    try {
        Export-Certificate -Cert $Certificate -FilePath $tempCert -Force | Out-Null
        Import-Certificate -FilePath $tempCert -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
        Import-Certificate -FilePath $tempCert -CertStoreLocation Cert:\LocalMachine\TrustedPublisher | Out-Null
    }
    finally {
        if (Test-Path $tempCert) {
            Remove-Item -LiteralPath $tempCert -Force
        }
    }
}

$effectivePlatform = Get-EffectivePlatform
$architectureFolder = if ($effectivePlatform -eq "ARM64") { "ARM64" } else { "AMD64" }

if ($DownloadLatestArtifact -and [string]::IsNullOrWhiteSpace($DriverDir) -and [string]::IsNullOrWhiteSpace($DriverZip)) {
    $DriverDir = Resolve-GitHubDriverArtifact -ArchitectureFolder $architectureFolder -RepositoryName $Repository -WorkflowRunId $RunId -Root $ArtifactRoot
    $SkipBuild = $true
}

if ($DownloadOnly) {
    if ([string]::IsNullOrWhiteSpace($DriverDir)) {
        throw "-DownloadOnly requires -DownloadLatestArtifact or -DriverDir."
    }

    Write-Host "Driver package ready: $DriverDir"
    exit 0
}

if (!(Test-Administrator)) {
    if ($Elevate) {
        Start-ElevatedSelf
    }

    throw "The development keyboard filter installer must run from elevated PowerShell."
}

$rebootRequired = Enable-TestSigningIfRequested
if ($rebootRequired) {
    Write-Warning "Windows test-signing mode was enabled. Reboot Windows, then run this command again to sign and install the Apple Keyboard Filter driver."
    exit 3010
}

$certificate = Get-OrCreateCodeSigningCertificate
Trust-CertificateForLocalMachine $certificate

$workRoot = Join-Path $env:TEMP "AppleKeyboardFilterDev-$([Guid]::NewGuid().ToString('N'))"
try {
    Write-Host "Preparing Apple Keyboard Filter driver package for $effectivePlatform..."
    $driverDir = Prepare-DriverPackage -ArchitectureFolder $architectureFolder -WorkRoot $workRoot
    Write-Host "Test-signing Apple Keyboard Filter catalog..."
    Sign-DriverCatalog -CatalogPath (Join-Path $driverDir "AppleKeyboardFilter.cat") -Certificate $certificate

    if (!$NoInstall) {
        Write-Host "Installing local test-signed Apple Keyboard Filter driver..."
        & (Join-Path $PSScriptRoot "install-keyboard-filter-driver.ps1") -DriverDir $driverDir -AllowTestSigned
        if ($LASTEXITCODE -ne 0) {
            throw "install-keyboard-filter-driver.ps1 failed with exit code $LASTEXITCODE."
        }
    }

    Write-Host ""
    & (Join-Path $PSScriptRoot "check-keyboard-filter-driver.ps1")
}
finally {
    if (Test-Path $workRoot) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force
    }
}

if (!$NoInstall) {
    Write-Warning "If the filter is not bound yet, reconnect the Magic Keyboard or reboot Windows."
}
