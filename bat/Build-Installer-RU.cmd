@echo off
REM Build AutoRAW installer (Inno Setup 6, Russian UI).
REM Requires ISCC in PATH or default Inno Setup 6 installation.
cd /d "%~dp0.."

REM Check if AutoRAW is running and offer to kill it
set "_pid="
for /f "tokens=2" %%P in ('tasklist /fi "imagename eq AutoRAW.exe" /fo csv /nh 2^>nul ^| findstr /i "autoraw"') do (
  set "_pid=%%~P"
)

if not "%_pid%"=="" (
  echo.
  echo [WARN] AutoRAW.exe is running (PID: %_pid%)
  echo        The build will fail if the exe is locked (MSB3027).
  echo.
  set /p "_kill=Close AutoRAW automatically? [Y/N]: "
  if /i "%_kill%"=="Y" (
    taskkill /PID %_pid% /F >nul 2>&1
    echo        AutoRAW closed.
    timeout /t 1 /nobreak >nul
  ) else (
    echo        Close AutoRAW manually and run again.
    exit /b 1
  )
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\build-installer.ps1"
set ERR=%ERRORLEVEL%
if not "%ERR%"=="0" (
  echo.
  echo [ERROR] Installer build failed, exit code: %ERR%
  exit /b %ERR%
)
