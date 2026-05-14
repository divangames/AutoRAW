@echo off
cd /d "%~dp0"
title AutoRAW

call "%~dp0bat\_ensure-sdk.bat"
if errorlevel 1 (
  echo.
  echo For a detailed log try:  powershell -ExecutionPolicy Bypass -File .\Run-AutoRAW.ps1
  echo.
  pause
  exit /b 1
)

setlocal EnableExtensions
:menu
echo.
echo  AutoRAW  ---  root: %CD%
echo    1  Build Debug   (WinExe, no console window)
echo    2  Run
echo    3  Build Debug, then Run
echo    4  Build Release (for distribution)
echo    5  Build Installer.exe
echo    0  Exit
set /p "_k=> "
if "%_k%"=="1" goto :do_build
if "%_k%"=="2" goto :do_run
if "%_k%"=="3" goto :do_both
if "%_k%"=="4" goto :do_build_rel
if "%_k%"=="5" goto :do_installer
if "%_k%"=="0" exit /b 0
goto :menu

:do_build
call "%~dp0bat\_warn-if-autoraw-running.bat"
dotnet build "src\AutoRAW\AutoRAW.csproj" -c Debug
echo.
pause
goto :menu

:do_build_rel
call "%~dp0bat\_warn-if-autoraw-running.bat"
dotnet build "src\AutoRAW\AutoRAW.csproj" -c Release
echo.
pause
goto :menu

:do_installer
call "%~dp0bat\Build-Installer-RU.cmd"
echo.
pause
goto :menu

:do_run
dotnet run --project "src\AutoRAW\AutoRAW.csproj" -c Debug --no-launch-profile
echo.
pause
goto :menu

:do_both
call "%~dp0bat\_warn-if-autoraw-running.bat"
dotnet build "src\AutoRAW\AutoRAW.csproj" -c Debug
if errorlevel 1 (
  echo BUILD FAILED
  pause
  goto :menu
)
dotnet run --project "src\AutoRAW\AutoRAW.csproj" -c Debug --no-launch-profile
echo.
pause
goto :menu
