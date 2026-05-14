@echo off
title Install .NET 8 SDK
echo This tries to install .NET 8 SDK using winget (Windows 10/11).
echo If winget is missing, open in browser: https://get.dot.net/8
echo.
where winget >nul 2>&1
if errorlevel 1 (
  echo winget not found. Install SDK manually from https://get.dot.net/8
  start https://get.dot.net/8
  pause
  exit /b 1
)

winget install --id Microsoft.DotNet.SDK.8 -e --accept-source-agreements --accept-package-agreements
echo.
echo Done. Close ALL terminals, reopen, then run Run-AutoRAW.cmd again.
pause
exit /b 0
