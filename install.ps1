#Requires -Version 5.1
<#
.SYNOPSIS
    Installs LanBridge for the current Windows user.

.DESCRIPTION
    The documented one-liner runs this script with irm | iex, in the caller's
    PowerShell session. This script therefore never calls exit. Failures print
    one clear message and return.

.EXAMPLE
    irm https://github.com/SMR-2009/lanbridge/releases/latest/download/install.ps1 | iex

.EXAMPLE
    .\install.ps1 -SourceDir .\artifacts -NoShortcut -SkipLaunch
#>
[CmdletBinding()]
param(
    [string]$InstallDir = "$env:LOCALAPPDATA\Programs\LanBridge",
    [string]$SourceDir,
    [string]$Version,
    [switch]$NoShortcut,
    [switch]$NoFirewall,
    [switch]$NoStartup,
    [switch]$SkipLaunch,
    [switch]$Help
)

$script:InMemory = [string]::IsNullOrEmpty($PSScriptRoot)
$script:Failed = $false
$script:OriginalTls = [Net.ServicePointManager]::SecurityProtocol
$script:OriginalErrorAction = $ErrorActionPreference
$script:OriginalProgress = $ProgressPreference
$script:Temp = $null

function Write-Step {
    param([string]$Message)
    Write-Host "==> $Message" -ForegroundColor DarkGray
}

function Write-Fail {
    param([string]$Message)
    $script:Failed = $true
    Write-Host ""
    Write-Host "LanBridge install failed." -ForegroundColor Red
    Write-Host "  $Message" -ForegroundColor Red
    Write-Host ""
    Write-Host "Check the release page: https://github.com/SMR-2009/lanbridge/releases" -ForegroundColor DarkGray
    Write-Host "The script made no changes if this happened before the copy step." -ForegroundColor DarkGray
}

function Get-ClearError {
    param($ErrorRecord)
    if ($null -eq $ErrorRecord) { return "Unknown error." }

    $exception = $ErrorRecord.Exception
    if ($null -eq $exception) { return [string]$ErrorRecord }

    $message = $exception.Message
    if ($exception.InnerException -and -not [string]::IsNullOrWhiteSpace($exception.InnerException.Message)) {
        $message = $exception.InnerException.Message
    }

    if ($exception.Response) {
        try {
            $status = [int]$exception.Response.StatusCode
            if ($status -gt 0) { $message = "HTTP $status. $message" }
        }
        catch {
        }
    }

    return $message
}

function Show-Help {
    Write-Host @"
LanBridge installer

Usage:
  irm https://github.com/SMR-2009/lanbridge/releases/latest/download/install.ps1 | iex
  .\install.ps1 [-Version v0.1.4] [-InstallDir PATH] [-NoShortcut]
                [-NoFirewall] [-SkipLaunch] [-SourceDir PATH] [-Help]

  -SourceDir  Install from local release artifacts instead of GitHub.
  -NoFirewall Skip the one-time firewall rule; first run offers it.
  -NoStartup  Do not start LanBridge when this user signs in.
  -NoShortcut Skip the Start Menu shortcut.
  -SkipLaunch Do not launch LanBridge after installing.

The installer verifies SHA-256 before copying anything.
"@
}

function Get-DownloadBase {
    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        $tag = $Version.Trim()
        if ($tag -notlike "v*") { $tag = "v$tag" }
        return "https://github.com/SMR-2009/lanbridge/releases/download/$tag"
    }

    try {
        $release = Invoke-RestMethod `
            -Uri "https://api.github.com/repos/SMR-2009/lanbridge/releases/latest" `
            -Headers @{ "User-Agent" = "LanBridge-Installer"; "Accept" = "application/vnd.github+json" } `
            -TimeoutSec 30 `
            -ErrorAction Stop

        if ($release -and $release.tag_name) {
            $assetNames = @($release.assets | ForEach-Object { $_.name })
            if ($assetNames -contains "LanBridge.exe" -and $assetNames -contains "SHA256SUMS.txt") {
                return "https://github.com/SMR-2009/lanbridge/releases/download/$($release.tag_name)"
            }

            Write-Host "The latest release has no assets yet; trying the latest download path." -ForegroundColor DarkGray
        }
    }
    catch {
        Write-Host "Could not query the GitHub API; trying the latest download path." -ForegroundColor DarkGray
    }

    return "https://github.com/SMR-2009/lanbridge/releases/latest/download"
}

function Save-Download {
    param([string]$Url, [string]$OutFile)

    try {
        Invoke-WebRequest `
            -Uri $Url `
            -OutFile $OutFile `
            -UseBasicParsing `
            -TimeoutSec 300 `
            -Headers @{ "User-Agent" = "LanBridge-Installer" } `
            -ErrorAction Stop
    }
    catch {
        $status = ""
        try {
            if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
                $status = " (HTTP " + [int]$_.Exception.Response.StatusCode + ")"
            }
        }
        catch {
        }

        throw "Could not download $Url$status. The release may not be published yet, or GitHub is unreachable."
    }
}

try {
    $ErrorActionPreference = "Stop"
    $ProgressPreference = "SilentlyContinue"

    if ($Help) {
        Show-Help
        return
    }

    if ($PSVersionTable.PSVersion.Major -lt 5) {
        throw "PowerShell 5.1 or newer is required."
    }

    try {
        [Net.ServicePointManager]::SecurityProtocol =
            [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    }
    catch {
        Write-Host "Could not explicitly enable TLS 1.2; continuing with the system default." -ForegroundColor DarkGray
    }

    if ([string]::IsNullOrWhiteSpace($InstallDir)) {
        throw "InstallDir cannot be empty."
    }

    $InstallDir = [Environment]::ExpandEnvironmentVariables($InstallDir)
    $script:Temp = Join-Path $env:TEMP ("lanbridge-install-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $script:Temp -Force | Out-Null

    $exe = Join-Path $script:Temp "LanBridge.exe"
    $sums = Join-Path $script:Temp "SHA256SUMS.txt"
    $config = Join-Path $script:Temp "LanBridge.exe.config"

    if ($SourceDir) {
        $source = (Resolve-Path -Path $SourceDir -ErrorAction Stop).Path
        $sourceExe = Join-Path $source "LanBridge.exe"
        $sourceSums = Join-Path $source "SHA256SUMS.txt"
        if (-not (Test-Path -LiteralPath $sourceExe)) {
            throw "SourceDir does not contain LanBridge.exe."
        }
        if (-not (Test-Path -LiteralPath $sourceSums)) {
            throw "SourceDir does not contain SHA256SUMS.txt."
        }

        Copy-Item -LiteralPath $sourceExe -Destination $exe -Force
        Copy-Item -LiteralPath $sourceSums -Destination $sums -Force
        $sourceConfig = Join-Path $source "LanBridge.exe.config"
        if (Test-Path -LiteralPath $sourceConfig) {
            Copy-Item -LiteralPath $sourceConfig -Destination $config -Force
        }
        Write-Step "Using local artifacts from $source"
    }
    else {
        $base = Get-DownloadBase
        Write-Step "Downloading LanBridge.exe"
        Save-Download -Url "$base/LanBridge.exe" -OutFile $exe
        Write-Step "Downloading SHA256SUMS.txt"
        Save-Download -Url "$base/SHA256SUMS.txt" -OutFile $sums

        try {
            Save-Download -Url "$base/LanBridge.exe.config" -OutFile $config
        }
        catch {
            Write-Warning "The optional runtime config was not downloaded; the app still runs with defaults."
            $config = $null
        }
    }

    $expectedHashes = @{}
    foreach ($line in (Get-Content -LiteralPath $sums -ErrorAction Stop)) {
        $parts = ($line.Trim() -split '\s+')
        if ($parts.Count -ge 2) {
            $name = $parts[1].TrimStart("*")
            $expectedHashes[$name.ToLowerInvariant()] = $parts[0].ToLowerInvariant()
        }
    }

    if (-not $expectedHashes.ContainsKey("lanbridge.exe")) {
        throw "SHA256SUMS.txt does not list LanBridge.exe."
    }

    foreach ($pair in @(
        @{ Name = "LanBridge.exe"; Path = $exe }
        @{ Name = "LanBridge.exe.config"; Path = $config }
    )) {
        if (-not $pair.Path -or -not (Test-Path -LiteralPath $pair.Path)) { continue }
        $key = $pair.Name.ToLowerInvariant()
        if (-not $expectedHashes.ContainsKey($key)) {
            throw "SHA256SUMS.txt does not list $($pair.Name)."
        }

        $actual = (Get-FileHash -LiteralPath $pair.Path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $expectedHashes[$key]) {
            throw "Checksum mismatch for $($pair.Name). The download may be corrupted or incomplete."
        }
    }

    if (Get-Process -Name "LanBridge" -ErrorAction SilentlyContinue) {
        throw "LanBridge is running. Quit it from the tray, then run the installer again."
    }

    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    $target = Join-Path $InstallDir "LanBridge.exe"
    Copy-Item -LiteralPath $exe -Destination $target -Force
    if ($config -and (Test-Path -LiteralPath $config)) {
        Copy-Item -LiteralPath $config -Destination ($target + ".config") -Force
    }
    Write-Step "Files copied to $InstallDir"

    if (-not $NoShortcut) {
        try {
            $shortcutPath = Join-Path ([Environment]::GetFolderPath("Programs")) "LanBridge.lnk"
            $shell = New-Object -ComObject WScript.Shell
            $shortcut = $shell.CreateShortcut($shortcutPath)
            $shortcut.TargetPath = $target
            $shortcut.WorkingDirectory = $InstallDir
            $shortcut.IconLocation = "$target,0"
            $shortcut.Description = "LanBridge - secure local file transfer"
            $shortcut.Save()
            [Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null
            Write-Step "Start Menu shortcut created"
        }
        catch {
            Write-Warning "The Start Menu shortcut could not be created. The app is still installed."
        }
    }

    if (-not $NoStartup) {
        try {
            $runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
            New-Item -Path $runKey -Force | Out-Null
            Set-ItemProperty -Path $runKey -Name "LanBridge" -Value "`"$target`" --tray"
            Write-Step "Start at sign-in enabled"
        }
        catch {
            Write-Warning "Could not enable start at sign-in. You can turn it on in Settings."
        }
    }
    else {
        Write-Step "Skipped start at sign-in"
    }

    if (-not $NoFirewall) {
        $existingRule = (& netsh.exe advfirewall firewall show rule name=LanBridge verbose 2>$null) -join "`n"
        if ($LASTEXITCODE -eq 0 -and $existingRule -match [regex]::Escape($target)) {
            Write-Step "Windows Firewall rule already present for this path"
        }
        else {
            Write-Step "Adding the Windows Firewall rule (one UAC prompt)"
            try {
                $firewallArgs = @(
                    "advfirewall", "firewall", "add", "rule",
                    "name=LanBridge", "dir=in", "action=allow",
                    "program=`"$target`"", "enable=yes",
                    "profile=private,public", "remoteip=LocalSubnet"
                )
                $firewall = Start-Process `
                    -FilePath "netsh.exe" `
                    -ArgumentList $firewallArgs `
                    -Verb RunAs `
                    -Wait `
                    -PassThru `
                    -WindowStyle Hidden

                if ($firewall.ExitCode -eq 0) {
                    Write-Step "Firewall rule added"
                }
                else {
                    Write-Warning "The firewall rule was not added. Use Settings -> Repair firewall rule later."
                }
            }
            catch {
                Write-Warning "The firewall rule was not added. Use Settings -> Repair firewall rule later."
            }
        }
    }
    else {
        Write-Step "Skipped the firewall rule; first run will offer it"
    }

    Write-Host ""
    Write-Host "LanBridge installed to $InstallDir" -ForegroundColor Green
    Write-Host "Reply with a file on the other laptop; the tray icon is the control surface." -ForegroundColor DarkGray

    if (-not $SkipLaunch) {
        try {
            Start-Process -FilePath $target
        }
        catch {
            Write-Warning "LanBridge was installed but could not be launched. Start it from the Start Menu."
        }
    }
}
catch {
    Write-Fail (Get-ClearError $_)
}
finally {
    try {
        [Net.ServicePointManager]::SecurityProtocol = $script:OriginalTls
    }
    catch {
    }

    $ErrorActionPreference = $script:OriginalErrorAction
    $ProgressPreference = $script:OriginalProgress

    if ($script:Temp -and (Test-Path -LiteralPath $script:Temp)) {
        Remove-Item -LiteralPath $script:Temp -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($script:Failed) {
    $global:LASTEXITCODE = 1
}
