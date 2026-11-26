[CmdletBinding()]
param(
    [string]$InstallDir = "$env:LOCALAPPDATA\Programs\LanBridge",
    [switch]$RemoveData
)

$ErrorActionPreference = "Continue"

Get-Process -Name "LanBridge" -ErrorAction SilentlyContinue | Stop-Process -Force

$shortcutPath = Join-Path ([Environment]::GetFolderPath("Programs")) "LanBridge.lnk"
if (Test-Path $shortcutPath) {
    Remove-Item -Path $shortcutPath -Force
    Write-Host "Removed the Start Menu shortcut." -ForegroundColor DarkGray
}

if (Test-Path $InstallDir) {
    Remove-Item -Path $InstallDir -Recurse -Force
    Write-Host "Removed $InstallDir" -ForegroundColor DarkGray
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
    if (Test-Path $appData) {
        Remove-Item -Path $appData -Recurse -Force
        Write-Host "Removed LanBridge settings and logs." -ForegroundColor DarkGray
    }
}

Write-Host "LanBridge uninstalled." -ForegroundColor Green
Write-Host "Received files in your Downloads\LanBridge folder were left untouched." -ForegroundColor DarkGray
