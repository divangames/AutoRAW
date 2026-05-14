@echo off
title AutoRAW build
cd /d "%~dp0"
call "%~dp0bat\_ensure-sdk.bat"
if errorlevel 1 (
  pause
  exit /b 1
)
call "%~dp0bat\_warn-if-autoraw-running.bat"
dotnet build "src\AutoRAW\AutoRAW.csproj" -c Debug
echo.
pause
