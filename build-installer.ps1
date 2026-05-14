#Requires -Version 5.1
<#
.SYNOPSIS
    Publish AutoRAW and compile a single Inno Setup installer (Russian UI).
.DESCRIPTION
    1. dotnet publish -> dist\publish\ (win-x64, framework-dependent)
    2. Copy setting, reference, zona from repo root
    3. Run ISCC.exe on installer\AutoRAW.iss
    Requires Inno Setup 6 (ISCC) on the developer machine.
#>

$ErrorActionPreference = "Stop"

$RepoRoot   = $PSScriptRoot
$ProjFile   = Join-Path $RepoRoot "src\AutoRAW\AutoRAW.csproj"
$PublishDir = Join-Path $RepoRoot "dist\publish"
$IssFile    = Join-Path $RepoRoot "installer\AutoRAW.iss"

function Get-ProjectVersion {
    $text = Get-Content -LiteralPath $ProjFile -Raw -Encoding UTF8
    if ($text -match '<Version>\s*([^<]+)\s*</Version>') {
        return $Matches[1].Trim()
    }
    return "0.0.0.0"
}

function Find-IsccPath {
    if ($env:INNO_SETUP_ISCC -and (Test-Path -LiteralPath $env:INNO_SETUP_ISCC)) {
        return $env:INNO_SETUP_ISCC
    }
    $cmd = Get-Command "iscc.exe" -ErrorAction SilentlyContinue
    if (-not $cmd) { $cmd = Get-Command "iscc" -ErrorAction SilentlyContinue }
    if ($cmd -and $cmd.Source -and (Test-Path -LiteralPath $cmd.Source)) {
        return $cmd.Source
    }
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )
    foreach ($p in $candidates) {
        if ($p -and (Test-Path -LiteralPath $p)) { return $p }
    }
    return $null
}

$autoRaw = Get-Process -Name "AutoRAW" -ErrorAction SilentlyContinue
if ($autoRaw) {
    Write-Host "[WARN] AutoRAW.exe is running - close it first (MSB3027)." -ForegroundColor Yellow
    Write-Host "       PID: $($autoRaw.Id -join ', ')" -ForegroundColor Yellow
    exit 1
}

Write-Host "[1/5] Cleaning dist\publish..." -ForegroundColor Cyan
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $PublishDir | Out-Null

Write-Host "[2/5] dotnet publish (Release, win-x64)..." -ForegroundColor Cyan
& dotnet publish $ProjFile `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:PublishSingleFile=false `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    --output $PublishDir `
    --nologo

if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] Publish failed, exit code $LASTEXITCODE." -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "[3/5] Copying setting, reference, zona..." -ForegroundColor Cyan
foreach ($folder in @("setting", "reference", "zona")) {
    $src = Join-Path $RepoRoot $folder
    if (Test-Path $src) {
        $dst = Join-Path $PublishDir $folder
        Write-Host "  -> $folder" -ForegroundColor DarkGray
        if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
        Copy-Item -Path $src -Destination $dst -Recurse -Force
    }
}

Write-Host "[4/5] Locating Inno Setup (ISCC.exe)..." -ForegroundColor Cyan
$iscc = Find-IsccPath
if (-not $iscc) {
    Write-Host "[ERROR] Inno Setup 6 (ISCC.exe) not found." -ForegroundColor Red
    Write-Host "        Install: https://jrsoftware.org/isdl.php" -ForegroundColor Yellow
    Write-Host "        Or set env var INNO_SETUP_ISCC to full path of ISCC.exe." -ForegroundColor Yellow
    exit 2
}

$appVersion = Get-ProjectVersion
Write-Host "  App version : $appVersion" -ForegroundColor DarkGray
Write-Host "  ISCC        : $iscc" -ForegroundColor DarkGray

Write-Host "[5/5] Compiling installer (bundled .NET SDK ~210 MB)..." -ForegroundColor Cyan
& $iscc @("/DMyAppVersion=$appVersion", $IssFile)
if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] ISCC failed, exit code $LASTEXITCODE." -ForegroundColor Red
    exit $LASTEXITCODE
}

$appVersion = Get-ProjectVersion
$setupExe = Join-Path $RepoRoot "dist\AutoRAW-Setup-$appVersion-ru.exe"

Write-Host ""
Write-Host "Done." -ForegroundColor Green
if (Test-Path $setupExe) {
    $sizeMb = [math]::Round((Get-Item $setupExe).Length / 1048576, 1)
    Write-Host "  Installer : $setupExe ($sizeMb MB)" -ForegroundColor Green
} else {
    $found = Get-ChildItem (Join-Path $RepoRoot "dist") -Filter "AutoRAW-Setup-*.exe" -ErrorAction SilentlyContinue
    if ($found) {
        foreach ($f in $found) {
            $sizeMb = [math]::Round($f.Length / 1048576, 1)
            Write-Host "  Installer : $($f.FullName) ($sizeMb MB)" -ForegroundColor Green
        }
    } else {
        Write-Host "  Warning: installer .exe not found in dist\" -ForegroundColor Yellow
    }
}
