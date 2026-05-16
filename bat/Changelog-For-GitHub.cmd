@echo off
cd /d "%~dp0.."
chcp 65001 >nul
title AutoRAW - Changelog for GitHub

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Changelog-For-GitHub.ps1"
set "_ec=%errorlevel%"
if not "%_ec%"=="0" (
  echo.
  echo [ERROR] Script exited with code %_ec%
)
echo.
pause
exit /b %_ec%
