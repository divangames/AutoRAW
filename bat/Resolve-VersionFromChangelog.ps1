#Requires -Version 5.1
<#
.SYNOPSIS
  Читает верхний заголовок ## [x.y.z] в CHANGELOG.md и записывает нормализованную версию в один файл (одна строка).
.DESCRIPTION
  Используется MSBuild (AutoRAW.csproj) и сборкой установщика. Формат строки: Major.Minor.Build (например 0.5.3).
#>
param(
    [Parameter(Mandatory)]
    [string] $ChangelogPath,

    [Parameter(Mandatory)]
    [string] $OutVersionFile
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ChangelogPath)) {
    throw "CHANGELOG not found: $ChangelogPath"
}

$rawLine = $null
foreach ($hl in Get-Content -LiteralPath $ChangelogPath -Encoding UTF8) {
    if ($hl -match '^\s*## \[([^\]]+)\]\s*') {
        $rawLine = $Matches[1].Trim()
        break
    }
}

if ([string]::IsNullOrWhiteSpace($rawLine)) {
    throw "No '## [version]' heading found in CHANGELOG.md"
}

$core = $rawLine
$sep = $core.IndexOfAny(@('-', '+'))
if ($sep -ge 0) {
    $core = $core.Substring(0, $sep).TrimEnd('.')
}

$parsed = $null
if (-not [System.Version]::TryParse($core, [ref]$parsed)) {
    throw "Cannot parse version from CHANGELOG segment: $rawLine (core: $core)"
}

$v = $parsed
$line = $v.ToString(3)

$dir = Split-Path -Parent $OutVersionFile
if ($dir -and -not (Test-Path -LiteralPath $dir)) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}

$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($OutVersionFile, $line + "`n", $utf8)
