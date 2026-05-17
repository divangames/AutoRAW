@echo off
chcp 65001 >nul 2>&1
cd /d "%~dp0"
title AutoRAW Presentation

:menu
echo.
echo  AutoRAW Presentation
echo    1  Start   — локальный сервер http://127.0.0.1:8765
echo    2  Deploy  — GitHub Pages (git push) или SSH (deploy.env)
echo    3  Open    — index.html в браузере (file://)
echo    0  Exit
echo.
set /p "_k=> "

if "%_k%"=="1" goto :start
if "%_k%"=="2" goto :deploy
if "%_k%"=="3" goto :open
if "%_k%"=="0" exit /b 0
goto :menu

:start
set "PORT=8765"
where py >nul 2>&1 && (set "_PY=py -3") || (set "_PY=python")
where %_PY% >nul 2>&1
if errorlevel 1 (
  echo [ОШИБКА] Нужен Python: py -3 или python в PATH.
  pause
  goto :menu
)
start "" "http://127.0.0.1:%PORT%/"
echo Сервер: http://127.0.0.1:%PORT%/  (Ctrl+C — остановка)
%_PY% -m http.server %PORT%
goto :menu

:deploy
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy.ps1"
echo.
pause
goto :menu

:open
start "" "%~dp0index.html"
goto :menu
