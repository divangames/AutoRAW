#Requires -Version 5.1
<#
.SYNOPSIS
  Читает верхний заголовок ## [a.b.c.d.e] или ## [a.b.c] (до пяти компонентов) в CHANGELOG.md.
.DESCRIPTION
  Нормализует до 5 чисел (недостающие справа = 0). Пишет два файла:
  - Assembly: первые 4 компонента для MSBuild Version / FileVersion (например 0.7.6.0)
  - Informational: все 5 для AssemblyInformationalVersion и UI (например 0.7.6.0.0)
  Семантика позиций: 1 — тотальный релиз, 2 — крупные изменения, 3 — значительные,
  4 — простые изменения, 5 — мелкие / в основном фиксы.
#>
param(
    [Parameter(Mandatory)]
    [string] $ChangelogPath,

    [Parameter(Mandatory)]
    [string] $OutAssemblyVersionFile,

    [Parameter(Mandatory)]
    [string] $OutInformationalVersionFile
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
$m = [regex]::Match($core, '[-+]')
if ($m.Success) {
    $core = $core.Substring(0, $m.Index).TrimEnd('.')
}

$segments = $core.Split('.', [System.StringSplitOptions]::RemoveEmptyEntries)
if ($segments.Count -lt 1 -or $segments.Count -gt 5) {
    throw "Version in CHANGELOG must have 1..5 dot-separated segments: '$rawLine' (core: $core)"
}

$nums = New-Object System.Collections.Generic.List[int]
foreach ($seg in $segments) {
    $t = $seg.Trim()
    if ($t -notmatch '^\d+$') {
        throw "Invalid version segment (non-negative integer expected): '$seg' in '$rawLine'"
    }
    $n = [int]$t
    if ($n -lt 0 -or $n -gt 65535) {
        throw "Version segment out of range 0..65535: $n in '$rawLine'"
    }
    $nums.Add($n) | Out-Null
}

while ($nums.Count -lt 5) {
    $nums.Add(0) | Out-Null
}

$assemblyLine = "{0}.{1}.{2}.{3}" -f $nums[0], $nums[1], $nums[2], $nums[3]
$infoLine = "{0}.{1}.{2}.{3}.{4}" -f $nums[0], $nums[1], $nums[2], $nums[3], $nums[4]

foreach ($f in @($OutAssemblyVersionFile, $OutInformationalVersionFile)) {
    $dir = Split-Path -Parent $f
    if ($dir -and -not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
}

$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($OutAssemblyVersionFile, $assemblyLine + "`n", $utf8)
[System.IO.File]::WriteAllText($OutInformationalVersionFile, $infoLine + "`n", $utf8)
