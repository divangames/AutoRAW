@echo off
title AutoRAW
cd /d "%~dp0"
call "%~dp0bat\_ensure-sdk.bat"
if errorlevel 1 (
  pause
  exit /b 1
)
dotnet run --project "src\AutoRAW\AutoRAW.csproj" -c Debug --no-launch-profile
echo.
pause
