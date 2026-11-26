[CmdletBinding()]
param(
    [string]$InstallDir = "$env:LOCALAPPDATA\Programs\LanBridge",
    [string]$SourceDir,
    [switch]$NoShortcut,
    [switch]$NoFirewall,
    [switch]$SkipLaunch
)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$releaseBase = "https://github.com/SMR-2009/lanbridge/releases/latest/download"
$temp = Join-Path $env:TEMP ("lanbridge-install-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $temp | Out-Null

try {
    $exe = Join-Path $temp "LanBridge.exe"
    $sums = Join-Path $temp "SHA256SUMS.txt"

    if ($SourceDir) {
        $source = (Resolve-Path $SourceDir).Path
        Copy-Item (Join-Path $source "LanBridge.exe") $exe
        Copy-Item (Join-Path $source "SHA256SUMS.txt") $sums
    }
    else {
        Invoke-WebRequest -Uri "$releaseBase/LanBridge.exe" -OutFile $exe -UseBasicParsing
        Invoke-WebRequest -Uri "$releaseBase/SHA256SUMS.txt" -OutFile $sums -UseBasicParsing
    }

    $expected = $null
    foreach ($line in Get-Content -Path $sums) {
        if ($line -match '^([0-9a-fA-F]{64})\s+\*?(.+?)\s*$' -and $Matches[2].Trim() -eq "LanBridge.exe") {
            $expected = $Matches[1].ToLowerInvariant()
            break
        }
    }

    if (-not $expected) {
        throw "SHA256SUMS.txt does not list LanBridge.exe."
    }

    $actual = (Get-FileHash -Path $exe -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expected) {
        throw "Checksum mismatch. The download may be corrupted."
    }

    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    $target = Join-Path $InstallDir "LanBridge.exe"
    Copy-Item $exe $target -Force

    if (-not $NoShortcut) {
        $shortcutPath = Join-Path ([Environment]::GetFolderPath("Programs")) "LanBridge.lnk"
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = $target
        $shortcut.WorkingDirectory = $InstallDir
        $shortcut.IconLocation = "$target,0"
        $shortcut.Description = "LanBridge - secure local file transfer"
        $shortcut.Save()
        [Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null
        Write-Host "Start Menu shortcut created." -ForegroundColor DarkGray
    }

    if (-not $NoFirewall) {
        $existingRule = & netsh.exe advfirewall firewall show rule name=LanBridge 2>$null
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Windows Firewall rule already present." -ForegroundColor DarkGray
        }
        else {
            Write-Host "Adding the Windows Firewall rule (one UAC prompt)..." -ForegroundColor DarkGray
            try {
                $firewallArgs = @(
                    "advfirewall", "firewall", "add", "rule",
                    "name=LanBridge", "dir=in", "action=allow",
                    "program=`"$target`"", "enable=yes",
                    "profile=private,public", "remoteip=LocalSubnet"
                )
                $firewall = Start-Process -FilePath "netsh.exe" -ArgumentList $firewallArgs -Verb RunAs -Wait -PassThru -WindowStyle Hidden
                if ($firewall.ExitCode -eq 0) {
                    Write-Host "Firewall rule added." -ForegroundColor Green
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
        Write-Host "Skipped the firewall rule; first-run setup will offer it once." -ForegroundColor DarkGray
    }

    Write-Host "LanBridge installed to $InstallDir" -ForegroundColor Green

    if (-not $SkipLaunch) {
        Start-Process -FilePath $target
    }
}
finally {
    if (Test-Path $temp) {
        Remove-Item -Path $temp -Recurse -Force -ErrorAction SilentlyContinue
    }
}
