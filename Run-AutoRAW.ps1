# UTF-8. From repo root: powershell -ExecutionPolicy Bypass -File .\Run-AutoRAW.ps1
$ErrorActionPreference = 'Continue'
$root = $PSScriptRoot
$log = Join-Path $env:TEMP ("AutoRAW-launch-{0}.log" -f ([guid]::NewGuid().ToString('N').Substring(0, 12)))
$logCopy = Join-Path $root 'AutoRAW-launch.log'

function Write-Log($msg) {
    $line = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $msg"
    Add-Content -LiteralPath $log -Value $line
    Write-Host $line
}

Set-Location -LiteralPath $root
"=== AutoRAW PowerShell launch ===" | Set-Content -LiteralPath $log
Write-Log "ROOT=$root"
Write-Log "TEMP_LOG=$log"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Log 'ERROR: dotnet not in PATH'
    Write-Host 'Install .NET 8 SDK: https://get.dot.net/8' -ForegroundColor Red
    Write-Host "Log: $log"
    Read-Host 'Press Enter'
    exit 1
}

$sdks = & dotnet --list-sdks 2>&1 | Out-String
Write-Log "dotnet --list-sdks:`n$sdks"
if ([string]::IsNullOrWhiteSpace($sdks)) {
    Write-Log 'ERROR: No SDK (empty dotnet --list-sdks)'
    Write-Host 'No .NET SDK. Install SDK (not only runtime): https://get.dot.net/8' -ForegroundColor Red
    Write-Host 'Or run: Install-DotNet8-SDK.cmd' -ForegroundColor Yellow
    Write-Host "Log: $log"
    Read-Host 'Press Enter'
    exit 1
}

$proj = Join-Path $root 'src\AutoRAW\AutoRAW.csproj'
Write-Log "dotnet run $proj"
& dotnet run --project $proj -c Debug --no-launch-profile 2>&1 | Tee-Object -FilePath $log -Append
$code = $LASTEXITCODE
Write-Log "exit code: $code"

try {
    Copy-Item -LiteralPath $log -Destination $logCopy -Force
    Write-Host "Also copied log to: $logCopy" -ForegroundColor DarkGray
} catch {
    Write-Host "Could not copy log to repo (close AutoRAW-launch.log in editor if open). Primary log: $log" -ForegroundColor Yellow
}

Write-Host "Log: $log"
Read-Host 'Press Enter'
exit $code
