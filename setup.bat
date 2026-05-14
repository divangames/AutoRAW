@echo off
chcp 65001 >nul
title AutoRAW

echo.
echo  ===  AutoRAW — Установка и запуск  ===
echo.

:: Проверяем, что AutoRAW.exe рядом с этим bat-файлом
if not exist "%~dp0AutoRAW.exe" (
    echo [ОШИБКА] AutoRAW.exe не найден рядом с setup.bat.
    echo.
    echo  Убедитесь, что вы запускаете setup.bat
    echo  из папки dist\AutoRAW\, а не из другого места.
    echo.
    pause
    exit /b 1
)

:: Проверяем .NET 8 Desktop Runtime
set DOTNET_OK=0
dotnet --list-runtimes 2>nul | findstr /C:"Microsoft.WindowsDesktop.App 8." >nul 2>&1
if %errorlevel%==0 set DOTNET_OK=1

if %DOTNET_OK%==1 (
    echo [OK] .NET 8 Desktop Runtime установлен.
    goto LAUNCH
)

echo [!] .NET 8 Desktop Runtime не обнаружен.
echo     Необходимо загрузить и установить (~55 МБ).
echo.
choice /C YN /M "Скачать и установить .NET 8 Desktop Runtime?"
if %errorlevel%==2 (
    echo.
    echo Отменено. Для работы AutoRAW необходим .NET 8 Desktop Runtime.
    echo Загрузите вручную: https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

:: Скачиваем установщик
echo.
echo Загрузка установщика .NET 8 (~55 МБ)...
set TMP_INST=%TEMP%\dotnet8-windowsdesktop-runtime-x64.exe

powershell -NoProfile -NonInteractive -Command ^
    "$ProgressPreference='SilentlyContinue';" ^
    "try {" ^
    "  Invoke-WebRequest -Uri 'https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe' -OutFile '%TMP_INST%';" ^
    "  Write-Host 'Загрузка завершена.';" ^
    "} catch { Write-Host ('Ошибка: ' + $_.Exception.Message); exit 1 }"

if %errorlevel% neq 0 (
    echo.
    echo Не удалось загрузить. Установите вручную:
    echo   https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

:: Устанавливаем
echo Установка .NET 8 Desktop Runtime...
"%TMP_INST%" /install /quiet /norestart

if %errorlevel% neq 0 (
    echo.
    echo Установщик завершился с кодом %errorlevel%.
    echo Попробуйте запустить вручную: %TMP_INST%
    pause
    exit /b 1
)

echo [OK] .NET 8 Desktop Runtime успешно установлен.
del /f /q "%TMP_INST%" >nul 2>&1

:LAUNCH
echo.
echo Запуск AutoRAW...
start "" "%~dp0AutoRAW.exe"
exit /b 0
