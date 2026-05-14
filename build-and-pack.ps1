#Requires -Version 5.1
<#
.SYNOPSIS
    Build AutoRAW and pack into dist\AutoRAW\ for distribution.
.DESCRIPTION
    1. dotnet publish -> dist\AutoRAW\
    2. Copy setting, reference, zona folders (if present)
    3. Copy setup.bat
    Result: dist\AutoRAW\  - send this folder to the end user.
#>

$ErrorActionPreference = "Stop"

$RepoRoot = $PSScriptRoot
$ProjFile = Join-Path $RepoRoot "src\AutoRAW\AutoRAW.csproj"
$DistDir  = Join-Path $RepoRoot "dist\AutoRAW"

Write-Host "[1/4] Cleaning dist..." -ForegroundColor Cyan
if (Test-Path $DistDir) { Remove-Item $DistDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $DistDir | Out-Null

$autoRaw = Get-Process -Name "AutoRAW" -ErrorAction SilentlyContinue
if ($autoRaw) {
    Write-Host "[AutoRAW] Закройте запущенный AutoRAW.exe (иначе MSB3027 — файл занят при сборке)." -ForegroundColor Yellow
    Write-Host "          PID: $($autoRaw.Id -join ', ')" -ForegroundColor Yellow
    exit 1
}

Write-Host "[2/4] Building (dotnet publish)..." -ForegroundColor Cyan
& dotnet publish $ProjFile `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:PublishSingleFile=false `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    --output $DistDir `
    --nologo

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed with exit code $LASTEXITCODE." -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "[3/4] Copying assets..." -ForegroundColor Cyan
foreach ($folder in @("setting", "reference", "zona")) {
    $src = Join-Path $RepoRoot $folder
    if (Test-Path $src) {
        $dst = Join-Path $DistDir $folder
        Write-Host "  -> $folder" -ForegroundColor DarkGray
        if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
        Copy-Item -Path $src -Destination $dst -Recurse -Force
    }
}

Write-Host "[4/4] Copying setup.bat..." -ForegroundColor Cyan
$setupSrc = Join-Path $RepoRoot "setup.bat"
if (Test-Path $setupSrc) {
    Copy-Item $setupSrc -Destination $DistDir -Force
}

$items  = Get-ChildItem $DistDir -Recurse -File
$totalMb = [math]::Round(($items | Measure-Object -Property Length -Sum).Sum / 1MB, 1)

Write-Host ""
Write-Host "Done!" -ForegroundColor Green
Write-Host "  Output : $DistDir"
Write-Host "  Files  : $($items.Count)  ($totalMb MB total)"
Write-Host ""
Write-Host "  --> Send the  dist\AutoRAW\  folder to the end user." -ForegroundColor Yellow
Write-Host "  --> First run: setup.bat  (checks / installs .NET 8)" -ForegroundColor Yellow
Write-Host "  --> Then:      AutoRAW.exe directly" -ForegroundColor Yellow
