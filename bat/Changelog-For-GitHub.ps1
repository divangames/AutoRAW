#Requires -Version 5.1
<#
.SYNOPSIS
  Extracts the top ## [a.b.c.d.e] section from CHANGELOG.md for pasting into a GitHub release description.
#>
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$changelogPath = Join-Path $repoRoot 'CHANGELOG.md'
if (-not (Test-Path -LiteralPath $changelogPath)) {
    Write-Host "[ERROR] CHANGELOG.md not found: $changelogPath" -ForegroundColor Red
    exit 1
}

$lines = Get-Content -LiteralPath $changelogPath -Encoding UTF8
$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^## \[') {
        $start = $i
        break
    }
}
if ($start -lt 0) {
    Write-Host '[ERROR] No ## [version] heading found in CHANGELOG.md' -ForegroundColor Red
    exit 1
}

$end = $lines.Count
for ($i = $start + 1; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^\s*---\s*$' -and ($i + 2) -lt $lines.Count -and [string]::IsNullOrWhiteSpace($lines[$i + 1]) -and ($lines[$i + 2] -match '^## \[')) {
        $end = $i
        break
    }
}

$section = (($lines[$start..($end - 1)] | ForEach-Object { $_ }) -join "`n").TrimEnd()
if ([string]::IsNullOrWhiteSpace($section)) {
    Write-Host '[ERROR] Extracted section is empty' -ForegroundColor Red
    exit 1
}

$distDir = Join-Path $repoRoot 'dist'
if (-not (Test-Path -LiteralPath $distDir)) {
    New-Item -ItemType Directory -Path $distDir | Out-Null
}
$outFile = Join-Path $distDir 'GitHub-RELEASE-NOTES.md'
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($outFile, $section + "`n", $utf8NoBom)

try {
    Set-Clipboard -Value $section
    $clipOk = $true
}
catch {
    $clipOk = $false
}

Write-Host ''
Write-Host 'Latest CHANGELOG section (paste into GitHub release):' -ForegroundColor Cyan
Write-Host '---'
Write-Host $section
Write-Host '---'
Write-Host ''
Write-Host "Saved: $outFile" -ForegroundColor Green
if ($clipOk) {
    Write-Host 'Copied to clipboard.' -ForegroundColor Green
}
else {
    Write-Host 'Could not copy to clipboard; copy text from the file or from the block above.' -ForegroundColor Yellow
}
Write-Host ''
