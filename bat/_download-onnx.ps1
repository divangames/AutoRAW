#Requires -Version 5.1
# ONNX download helper: retries, TLS, WebClient, BITS, curl, size check.

function Invoke-AutoRawFileDownload {
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [Parameter(Mandatory = $true)][string]$Destination,
        [int]$MinBytes = 5MB,
        [int]$MaxAttempts = 6
    )

    $destDir = Split-Path -Parent $Destination
    if ($destDir -and -not (Test-Path -LiteralPath $destDir)) {
        New-Item -ItemType Directory -Force -Path $destDir | Out-Null
    }

    if (Test-Path -LiteralPath $Destination) {
        $existing = (Get-Item -LiteralPath $Destination).Length
        if ($existing -ge $MinBytes) {
            return $existing
        }
        Remove-Item -LiteralPath $Destination -Force
    }

    $tmp = "$Destination.download"
    if (Test-Path -LiteralPath $tmp) {
        Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
    }

    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $ProgressPreference = 'SilentlyContinue'

    $lastError = $null
    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        try {
            if ($attempt -gt 1) {
                $delay = [Math]::Min(30, 2 * $attempt)
                Write-Host "Retry $attempt/$MaxAttempts in ${delay}s ..."
                Start-Sleep -Seconds $delay
            }

            if (Test-Path -LiteralPath $tmp) {
                Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
            }

            $wc = New-Object System.Net.WebClient
            $wc.Headers.Add('User-Agent', 'AutoRAW/1.0 (Windows; ONNX download)')
            $wc.DownloadFile($Url, $tmp)
            $wc.Dispose()

            $size = (Get-Item -LiteralPath $tmp).Length
            if ($size -lt $MinBytes) {
                throw "File too small ($size bytes), expected >= $MinBytes"
            }

            Move-Item -LiteralPath $tmp -Destination $Destination -Force
            return $size
        }
        catch {
            $lastError = $_
            Write-Warning "Attempt ${attempt}: $($_.Exception.Message)"
            if (Test-Path -LiteralPath $tmp) {
                Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
            }
        }
    }

    try {
        Write-Host "Trying BITS ..."
        if (Test-Path -LiteralPath $tmp) {
            Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
        }
        Start-BitsTransfer -Source $Url -Destination $tmp -DisplayName 'AutoRAW ONNX' -ErrorAction Stop
        $size = (Get-Item -LiteralPath $tmp).Length
        if ($size -ge $MinBytes) {
            Move-Item -LiteralPath $tmp -Destination $Destination -Force
            return $size
        }
        Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
    }
    catch {
        Write-Warning "BITS: $($_.Exception.Message)"
    }

    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($curl) {
        Write-Host "Trying curl.exe ..."
        if (Test-Path -LiteralPath $tmp) {
            Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
        }
        & curl.exe -fL --retry 5 --retry-delay 3 --connect-timeout 30 --max-time 900 `
            -A "AutoRAW/1.0" -o $tmp $Url
        if ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $tmp)) {
            $size = (Get-Item -LiteralPath $tmp).Length
            if ($size -ge $MinBytes) {
                Move-Item -LiteralPath $tmp -Destination $Destination -Force
                return $size
            }
        }
        if (Test-Path -LiteralPath $tmp) {
            Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
        }
    }

    $msg = "Download failed after $MaxAttempts attempts. Last error: $lastError`nURL: $Url`nSave manually to: $Destination"
    throw $msg
}

function Get-AutoRawPython {
    if (Get-Command py -ErrorAction SilentlyContinue) {
        $exe = & py -3 -c "import sys; print(sys.executable)" 2>$null
        if ($exe) {
            $exe = $exe.Trim()
            if (Test-Path -LiteralPath $exe) { return $exe }
        }
    }
    foreach ($name in @('python', 'python3')) {
        $c = Get-Command $name -ErrorAction SilentlyContinue
        if ($c) { return $c.Source }
    }
    return $null
}

function Export-YoloV8SegOnnx {
    param(
        [Parameter(Mandatory = $true)][string]$Destination
    )

    $python = Get-AutoRawPython
    if (-not $python) {
        throw "Python 3 not found. Install from python.org or run: py -3 -m pip install ultralytics"
    }

    Write-Host "Python: $python"
    Write-Host "Installing ultralytics (pip) if needed ..."
    & $python -m pip install --upgrade pip ultralytics onnx -q
    if ($LASTEXITCODE -ne 0) {
        throw "pip install ultralytics failed"
    }

    $workDir = Join-Path ([System.IO.Path]::GetTempPath()) "AutoRAW-yolov8n-seg-export"
    if (Test-Path -LiteralPath $workDir) {
        Remove-Item -LiteralPath $workDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    New-Item -ItemType Directory -Force -Path $workDir | Out-Null

    $pyFile = Join-Path $workDir 'export_yolov8n_seg.py'
    @'
from ultralytics import YOLO
m = YOLO("yolov8n-seg.pt")
m.export(format="onnx", imgsz=640, simplify=True, opset=12)
'@ | Set-Content -LiteralPath $pyFile -Encoding UTF8

    Write-Host "Export yolov8n-seg.pt -> ONNX (first run downloads weights) ..."
    Push-Location $workDir
    try {
        & $python $pyFile
        if ($LASTEXITCODE -ne 0) {
            throw "ultralytics export failed"
        }

        $candidates = @(
            (Join-Path $workDir 'yolov8n-seg.onnx'),
            (Join-Path $workDir 'yolov8n-seg\yolov8n-seg.onnx')
        )
        $exported = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if (-not $exported) {
            $exported = Get-ChildItem -LiteralPath $workDir -Filter 'yolov8n-seg.onnx' -Recurse -ErrorAction SilentlyContinue |
                Select-Object -First 1 -ExpandProperty FullName
        }
        if (-not $exported) {
            throw "ONNX file not found under $workDir"
        }

        $destDir = Split-Path -Parent $Destination
        if ($destDir) { New-Item -ItemType Directory -Force -Path $destDir | Out-Null }
        Copy-Item -LiteralPath $exported -Destination $Destination -Force
        return (Get-Item -LiteralPath $Destination).Length
    }
    finally {
        Pop-Location
    }
}
