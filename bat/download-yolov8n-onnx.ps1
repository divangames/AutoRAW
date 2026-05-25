#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot '_download-onnx.ps1')

$destDir = Join-Path $root 'models\subject'
$dest = Join-Path $destDir 'yolov8n.onnx'

New-Item -ItemType Directory -Force -Path $destDir | Out-Null
if (Test-Path -LiteralPath $dest) {
    $len = (Get-Item -LiteralPath $dest).Length
    if ($len -ge 5MB) {
        Write-Host "Already exists: $dest ($([math]::Round($len / 1MB, 1)) MB)"
        exit 0
    }
    Remove-Item -LiteralPath $dest -Force
}

$python = Get-AutoRawPython
if ($python) {
    Write-Host "Export yolov8n.pt -> ONNX via ultralytics ..."
    & $python -m pip install --upgrade pip ultralytics onnx -q
    $workDir = Join-Path ([System.IO.Path]::GetTempPath()) "AutoRAW-yolov8n-export"
    if (Test-Path -LiteralPath $workDir) {
        Remove-Item -LiteralPath $workDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    New-Item -ItemType Directory -Force -Path $workDir | Out-Null
    Push-Location $workDir
    try {
        & $python -c "from ultralytics import YOLO; YOLO('yolov8n.pt').export(format='onnx', imgsz=640, simplify=True, opset=12)"
        if ($LASTEXITCODE -eq 0) {
            $exported = Get-ChildItem -LiteralPath $workDir -Filter 'yolov8n.onnx' -Recurse -ErrorAction SilentlyContinue |
                Select-Object -First 1 -ExpandProperty FullName
            if ($exported) {
                Copy-Item -LiteralPath $exported -Destination $dest -Force
                $len = (Get-Item -LiteralPath $dest).Length
                Write-Host "Saved: $dest ($([math]::Round($len / 1MB, 1)) MB)"
                exit 0
            }
        }
    }
    finally {
        Pop-Location
    }
}

$url = 'https://github.com/ultralytics/assets/releases/download/v8.2.0/yolov8n.onnx'
Write-Host "Trying direct URL (often 404): $url"
try {
    $bytes = Invoke-AutoRawFileDownload -Url $url -Destination $dest -MinBytes 5MB
    Write-Host "Saved: $dest ($([math]::Round($bytes / 1MB, 1)) MB)"
    exit 0
}
catch {
    Write-Error "Install Python 3 and re-run, or export manually: YOLO('yolov8n.pt').export(format='onnx')"
}
