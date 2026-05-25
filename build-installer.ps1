#Requires -Version 5.1
<#
.SYNOPSIS
    Publish AutoRAW and compile a single Inno Setup installer (Russian UI).
.DESCRIPTION
    1. dotnet publish -> dist\publish\ (win-x64, framework-dependent)
    2. Copy setting, reference, zona, profiles, droplets (and droples if present) from repo root
    3. Read version from CHANGELOG.md (bat\Resolve-VersionFromChangelog.ps1) -> dist\changelog_version_assembly.txt + changelog_version_info.txt
    4. Run ISCC.exe with /DMyAppVersion /DMyAppVerFull on installer\AutoRAW.iss
    Requires Inno Setup 6 (ISCC) on the developer machine.
#>

$ErrorActionPreference = "Stop"

$RepoRoot   = $PSScriptRoot
$ProjFile   = Join-Path $RepoRoot "src\AutoRAW\AutoRAW.csproj"
$PublishDir = Join-Path $RepoRoot "dist\publish"
$IssFile    = Join-Path $RepoRoot "installer\AutoRAW.iss"
$DistVerAssembly = Join-Path $RepoRoot "dist\changelog_version_assembly.txt"
$DistVerInfo     = Join-Path $RepoRoot "dist\changelog_version_info.txt"

function Get-InstallerVersionPair {
    $changelog = Join-Path $RepoRoot "CHANGELOG.md"
    $resolve   = Join-Path $RepoRoot "bat\Resolve-VersionFromChangelog.ps1"
    if (-not (Test-Path -LiteralPath $changelog)) {
        throw "CHANGELOG.md not found at: $changelog"
    }
    if (-not (Test-Path -LiteralPath $resolve)) {
        throw "Version resolve script not found: $resolve"
    }
    $outDir = Split-Path -Parent $DistVerAssembly
    if (-not (Test-Path -LiteralPath $outDir)) {
        New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    }
    $psArgs = @(
        '-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass',
        '-File', $resolve,
        '-ChangelogPath', $changelog,
        '-OutAssemblyVersionFile', $DistVerAssembly,
        '-OutInformationalVersionFile', $DistVerInfo
    )
    & powershell.exe @psArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Resolve-VersionFromChangelog.ps1 failed, exit code $LASTEXITCODE"
    }
    return [pscustomobject]@{
        Assembly       = (Get-Content -LiteralPath $DistVerAssembly -TotalCount 1 -Encoding UTF8).Trim()
        Informational  = (Get-Content -LiteralPath $DistVerInfo -TotalCount 1 -Encoding UTF8).Trim()
    }
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

function Copy-RepoAssetFolder {
    param(
        [string] $FolderName
    )
    $src = Join-Path $RepoRoot $FolderName
    if (-not (Test-Path -LiteralPath $src)) { return }
    $dst = Join-Path $PublishDir $FolderName
    if (Test-Path -LiteralPath $dst) { Remove-Item -LiteralPath $dst -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $dst | Out-Null
    # robocopy: без *.psd (черновики в zona\operation — гигабайты, в рантайме не нужны)
    $rc = & robocopy.exe $src $dst /E /XF *.psd /R:1 /W:1 /NFL /NDL /NJH /NJS /nc /ns /np
    if ($LASTEXITCODE -ge 8) {
        throw "robocopy failed for $FolderName, exit code $LASTEXITCODE"
    }
}

Write-Host "[2/5] dotnet publish (Release, win-x64)..." -ForegroundColor Cyan
& dotnet publish $ProjFile `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:PublishSingleFile=false `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:SkipRepoAssetCopy=true `
    --output $PublishDir `
    --nologo

if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] Publish failed, exit code $LASTEXITCODE." -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "[3/5] Copying setting, reference, zona, profiles, droplets (without *.psd)..." -ForegroundColor Cyan
foreach ($folder in @("setting", "reference", "zona", "profiles", "droplets", "droples")) {
    if (Test-Path (Join-Path $RepoRoot $folder)) {
        Write-Host "  -> $folder" -ForegroundColor DarkGray
        Copy-RepoAssetFolder -FolderName $folder
    }
}

$modelsSrc = Join-Path $RepoRoot "models\subject"
$modelsDst = Join-Path $PublishDir "models\subject"
if (Test-Path -LiteralPath $modelsSrc) {
    New-Item -ItemType Directory -Force -Path $modelsDst | Out-Null
    Get-ChildItem -LiteralPath $modelsSrc -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $modelsDst $_.Name) -Force
    }
    Write-Host "  -> models\subject ($(( Get-ChildItem $modelsDst -Filter *.onnx ).Count) onnx)" -ForegroundColor DarkGray
}

Write-Host "[4/5] Locating Inno Setup (ISCC.exe)..." -ForegroundColor Cyan
$iscc = Find-IsccPath
if (-not $iscc) {
    Write-Host "[ERROR] Inno Setup 6 (ISCC.exe) not found." -ForegroundColor Red
    Write-Host "        Install: https://jrsoftware.org/isdl.php" -ForegroundColor Yellow
    Write-Host "        Or set env var INNO_SETUP_ISCC to full path of ISCC.exe." -ForegroundColor Yellow
    exit 2
}

$verPair = Get-InstallerVersionPair
$appVersionDisplay = $verPair.Informational
$appVersionFileInfo = $verPair.Assembly
Write-Host "  App version : $appVersionDisplay (VersionInfo $appVersionFileInfo)" -ForegroundColor DarkGray
Write-Host "  ISCC        : $iscc" -ForegroundColor DarkGray

Write-Host "[5/5] Compiling installer (bundled .NET SDK ~210 MB)..." -ForegroundColor Cyan
& $iscc @("/DMyAppVersion=$appVersionDisplay", "/DMyAppVerFull=$appVersionFileInfo", $IssFile)
if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] ISCC failed, exit code $LASTEXITCODE." -ForegroundColor Red
    exit $LASTEXITCODE
}

$setupExe = Join-Path $RepoRoot "dist\AutoRAW-Setup-$appVersionDisplay-ru.exe"

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
