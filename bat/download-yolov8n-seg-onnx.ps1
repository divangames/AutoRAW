#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot '_download-onnx.ps1')

$destDir = Join-Path $root 'models\subject'
$dest = Join-Path $destDir 'yolov8n-seg.onnx'

New-Item -ItemType Directory -Force -Path $destDir | Out-Null
if (Test-Path -LiteralPath $dest) {
    $len = (Get-Item -LiteralPath $dest).Length
    if ($len -ge 5MB) {
        Write-Host "Already exists: $dest ($([math]::Round($len / 1MB, 1)) MB)"
        exit 0
    }
    Remove-Item -LiteralPath $dest -Force
}

# Ultralytics/assets does NOT ship pre-built yolov8n-seg.onnx (404 on GitHub releases).
# Primary path: export from yolov8n-seg.pt via pip ultralytics.
Write-Host "Note: GitHub has no ready yolov8n-seg.onnx; exporting via ultralytics ..."
try {
    $bytes = Export-YoloV8SegOnnx -Destination $dest
    Write-Host "Saved: $dest ($([math]::Round($bytes / 1MB, 1)) MB)"
    exit 0
}
catch {
    Write-Warning $_.Exception.Message
}

# Optional mirrors (may work for some networks)
$urls = @(
    'https://github.com/ultralytics/assets/releases/download/v8.2.0/yolov8n-seg.onnx',
    'https://github.com/ultralytics/assets/releases/download/v8.3.0/yolov8n-seg.onnx'
)

foreach ($url in $urls) {
    try {
        Write-Host "Trying direct URL: $url"
        $bytes = Invoke-AutoRawFileDownload -Url $url -Destination $dest -MinBytes 5MB -MaxAttempts 2
        Write-Host "Saved: $dest ($([math]::Round($bytes / 1MB, 1)) MB)"
        exit 0
    }
    catch {
        Write-Warning $_.Exception.Message
        if (Test-Path -LiteralPath $dest) {
            Remove-Item -LiteralPath $dest -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Error @"
Failed to obtain yolov8n-seg.onnx.

1) Install Python 3 and run again:
   .\bat\download-yolov8n-seg-onnx.ps1

2) Or manually:
   py -3 -m pip install ultralytics onnx
   py -3 -c "from ultralytics import YOLO; YOLO('yolov8n-seg.pt').export(format='onnx', imgsz=640, simplify=True, opset=12)"
   Copy yolov8n-seg.onnx to: $dest
"@
