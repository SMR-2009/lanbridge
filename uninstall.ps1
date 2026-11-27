[CmdletBinding()]
param(
    [string]$InstallDir = "$env:LOCALAPPDATA\Programs\LanBridge",
    [switch]$RemoveData
)

$ErrorActionPreference = "Continue"
$failed = $false

if ([string]::IsNullOrWhiteSpace($InstallDir)) {
    Write-Warning "InstallDir cannot be empty."
    $failed = $true
}
else {
    $resolvedInstall = [IO.Path]::GetFullPath(
        [Environment]::ExpandEnvironmentVariables($InstallDir))
    $targetExe = Join-Path $resolvedInstall "LanBridge.exe"

    $root = [IO.Path]::GetPathRoot($resolvedInstall)
    if ([string]::Equals(
            $resolvedInstall.TrimEnd('\'),
            $root.TrimEnd('\'),
            [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $targetExe -PathType Leaf)) {
        Write-Warning "Refusing to remove $resolvedInstall because it does not contain LanBridge.exe."
        $failed = $true
    }
    else {
        Get-Process -Name "LanBridge" -ErrorAction SilentlyContinue | ForEach-Object {
            try {
                if ([string]::Equals($_.Path, $targetExe, [StringComparison]::OrdinalIgnoreCase)) {
                    Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
                }
            }
            catch {
            }
        }

        try {
            Remove-Item -LiteralPath $resolvedInstall -Recurse -Force -ErrorAction Stop
            Write-Host "Removed $resolvedInstall" -ForegroundColor DarkGray
        }
        catch {
            Write-Warning "Could not remove $resolvedInstall`: $($_.Exception.Message)"
            $failed = $true
        }
    }
}

$shortcutPath = Join-Path ([Environment]::GetFolderPath("Programs")) "LanBridge.lnk"
if (Test-Path -LiteralPath $shortcutPath) {
    try {
        Remove-Item -LiteralPath $shortcutPath -Force -ErrorAction Stop
        Write-Host "Removed the Start Menu shortcut." -ForegroundColor DarkGray
    }
    catch {
        Write-Warning "Could not remove the Start Menu shortcut: $($_.Exception.Message)"
        $failed = $true
    }
}

$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
Remove-ItemProperty -Path $runKey -Name "LanBridge" -ErrorAction SilentlyContinue

try {
    Start-Process -FilePath "netsh.exe" `
        -ArgumentList @("advfirewall", "firewall", "delete", "rule", "name=LanBridge") `
        -Verb RunAs -Wait -WindowStyle Hidden | Out-Null
    Write-Host "Removed the Windows Firewall rule." -ForegroundColor DarkGray
}
catch {
    Write-Warning "The firewall rule was not removed (permission declined or rule missing)."
}

if ($RemoveData) {
    $appData = Join-Path $env:APPDATA "LanBridge"
    if (Test-Path -LiteralPath $appData) {
        try {
            Remove-Item -LiteralPath $appData -Recurse -Force -ErrorAction Stop
            Write-Host "Removed LanBridge settings and logs." -ForegroundColor DarkGray
        }
        catch {
            Write-Warning "Could not remove LanBridge settings: $($_.Exception.Message)"
            $failed = $true
        }
    }
}

if ($failed) {
    Write-Host "LanBridge uninstall finished with warnings." -ForegroundColor Yellow
    return
}

Write-Host "LanBridge uninstalled." -ForegroundColor Green
Write-Host "Received files in your Downloads\LanBridge folder were left untouched." -ForegroundColor DarkGray
