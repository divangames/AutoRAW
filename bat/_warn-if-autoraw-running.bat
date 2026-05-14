@echo off
REM Warn if AutoRAW.exe is running (MSB3027 / file in use during dotnet build).
tasklist /FI "IMAGENAME eq AutoRAW.exe" 2>nul | find /I "AutoRAW.exe" >nul
if errorlevel 1 exit /b 0
echo.
echo [AutoRAW] AutoRAW.exe is still running — close the app, then build again.
echo           RU: Zakonchite rabotu AutoRAW.exe, inache sborka daet oshibku MSB3027 ^(fajl zanjat^).
echo.
exit /b 0
