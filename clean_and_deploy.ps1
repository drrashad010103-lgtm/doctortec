# =============================================================================
# DOCTOR TECH - Oracle Retail POS 12.0 (Green Edition)
# Automated Clean, Database Migration, Build & Launch Script
# =============================================================================
$ErrorActionPreference = "Continue"

Write-Host "=================================================================" -ForegroundColor DarkGreen
Write-Host "   DOCTOR TECH - Point-of-Service & ERP (Oracle 12.0 Green)     " -ForegroundColor Green
Write-Host "=================================================================" -ForegroundColor DarkGreen

# 0. Move to the directory where this script is located
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if ($scriptDir -and (Test-Path $scriptDir)) {
    Set-Location $scriptDir
}

# Detect .NET SDK executable
$dotnetCmd = $null
if (Test-Path "D:\dotnet\dotnet.exe") {
    $dotnetCmd = "D:\dotnet\dotnet.exe"
} elseif (Get-Command "dotnet" -ErrorAction SilentlyContinue) {
    $dotnetCmd = (Get-Command "dotnet").Source
} elseif (Test-Path "C:\Program Files\dotnet\dotnet.exe") {
    $dotnetCmd = "C:\Program Files\dotnet\dotnet.exe"
}

if (-not $dotnetCmd) {
    Write-Host "[ERROR] Could not find dotnet.exe in PATH, D:\dotnet, or C:\Program Files\dotnet." -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}
Write-Host "Using .NET SDK: $dotnetCmd" -ForegroundColor Cyan

Write-Host "`n=== [1/5] Stopping running instances & cleaning old artifacts ===" -ForegroundColor Green
Get-Process -Name "RsSales*" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

# Remove duplicate OpeningEntryForm.vb if present in both root and Definitions/
if ((Test-Path ".\src\RsSales.UI\OpeningEntryForm.vb") -and (Test-Path ".\src\RsSales.UI\Definitions\OpeningEntryForm.vb")) {
    Remove-Item ".\src\RsSales.UI\OpeningEntryForm.vb" -Force -ErrorAction SilentlyContinue
    Write-Host "Removed duplicate src\RsSales.UI\OpeningEntryForm.vb" -ForegroundColor Yellow
}

Get-ChildItem -Path . -Include bin,obj -Recurse -Directory -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "`n=== [2/5] Ensuring appsettings.json configuration ===" -ForegroundColor Green
if (-not (Test-Path ".\appsettings.json") -and (Test-Path ".\appsettings.example.json")) {
    Copy-Item ".\appsettings.example.json" ".\appsettings.json" -Force
    Write-Host "Created appsettings.json from appsettings.example.json" -ForegroundColor Yellow
}

Write-Host "`n=== [3/5] Restoring & Building Solution ===" -ForegroundColor Green
& $dotnetCmd build .\RsSales.slnx -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "`n[BUILD FAILED] Please review the compiler errors above." -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit $LASTEXITCODE
}

Write-Host "`n=== [4/5] Applying Database Migrations (if needed) ===" -ForegroundColor Green
try {
    & $dotnetCmd run --project .\src\RsSales.DbTool\RsSales.DbTool.vbproj -c Release --no-build -- migrate
} catch {
    Write-Host "Note: Database migration skipped or already up to date." -ForegroundColor Yellow
}

Write-Host "`n=== [5/5] Launching DOCTOR TECH Point-of-Service ===" -ForegroundColor Green
$uiExe = ".\src\RsSales.UI\bin\Release\net10.0-windows\RsSales.UI.exe"
if (Test-Path $uiExe) {
    Start-Process -FilePath $uiExe -WorkingDirectory (Get-Location).Path
    Write-Host "Launched $uiExe successfully!" -ForegroundColor Green
} else {
    & $dotnetCmd run --project .\src\RsSales.UI\RsSales.UI.vbproj -c Release --no-build
}
