#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot '_download-onnx.ps1')

$destDir = Join-Path $root 'models\subject'
$dest = Join-Path $destDir 'u2netp.onnx'
$url = 'https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2netp.onnx'

New-Item -ItemType Directory -Force -Path $destDir | Out-Null
if (Test-Path -LiteralPath $dest) {
    $len = (Get-Item -LiteralPath $dest).Length
    if ($len -ge 1MB) {
        Write-Host "Already exists: $dest ($([math]::Round($len / 1MB, 1)) MB)"
        exit 0
    }
    Remove-Item -LiteralPath $dest -Force
}

Write-Host "Downloading u2netp.onnx ..."
Write-Host "  $url"
$bytes = Invoke-AutoRawFileDownload -Url $url -Destination $dest -MinBytes 1MB
Write-Host "Saved: $dest ($([math]::Round($bytes / 1MB, 1)) MB)"
