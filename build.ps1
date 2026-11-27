[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Version = "0.1.8"
)

$Version = $Version.TrimStart("v", "V")
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 0.1.8" }

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$artifacts = Join-Path $root "artifacts"

if (Test-Path $artifacts) {
    Remove-Item -Path $artifacts -Recurse -Force
}
New-Item -ItemType Directory -Path $artifacts | Out-Null

Push-Location $root
try {
    dotnet restore LanBridge.sln
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed" }

    dotnet build LanBridge.sln -c $Configuration --no-restore -warnaserror -p:Version=$Version
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

    dotnet test LanBridge.sln -c $Configuration --no-build
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed" }

    $exe = Join-Path $root "src\LanBridge\bin\$Configuration\net48\LanBridge.exe"
    if (-not (Test-Path $exe)) { throw "LanBridge.exe was not produced" }
    Copy-Item $exe (Join-Path $artifacts "LanBridge.exe")

    $exeConfig = "$exe.config"
    if (-not (Test-Path $exeConfig)) { throw "LanBridge.exe.config was not produced" }
    Copy-Item $exeConfig (Join-Path $artifacts "LanBridge.exe.config")

    foreach ($name in @("LICENSE", "README.md", "install.ps1", "uninstall.ps1")) {
        $source = Join-Path $root $name
        if (Test-Path $source) {
            Copy-Item $source (Join-Path $artifacts $name)
        }
    }

    $zip = Join-Path $artifacts "lanbridge-v$Version-win.zip"
    $hashNames = @("LanBridge.exe", "LanBridge.exe.config", "install.ps1", "uninstall.ps1")
    $lines = foreach ($name in $hashNames) {
        $path = Join-Path $artifacts $name
        if (-not (Test-Path $path)) { throw "$name is missing from artifacts" }
        $hash = (Get-FileHash -Path $path -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $name"
    }
    $lines | Set-Content -Path (Join-Path $artifacts "SHA256SUMS.txt") -Encoding ASCII

    $zipItems = Get-ChildItem -Path $artifacts -File |
        Where-Object { $_.Extension -ne ".zip" } |
        Select-Object -ExpandProperty FullName
    Compress-Archive -Path $zipItems -DestinationPath $zip -Force

    $zipHash = (Get-FileHash -Path $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    $lines + "$zipHash  lanbridge-v$Version-win.zip" |
        Set-Content -Path (Join-Path $artifacts "SHA256SUMS.txt") -Encoding ASCII

    Write-Host ""
    Write-Host "Artifacts written to $artifacts" -ForegroundColor Green
    Get-ChildItem -Path $artifacts | Select-Object Name, Length | Format-Table -AutoSize
}
finally {
    Pop-Location
}
