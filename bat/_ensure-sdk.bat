@echo off
REM No setlocal: PATH changes must remain visible to the caller (dev.cmd, *.cmd).
REM Do not gate on "dotnet --list-sdks" + findstr: UTF-8/BOM/locale can break that and block dev.

where dotnet >nul 2>&1
if errorlevel 1 (
  if exist "%ProgramFiles%\dotnet\dotnet.exe" set "PATH=%ProgramFiles%\dotnet;%PATH%"
)
where dotnet >nul 2>&1
if errorlevel 1 (
  if exist "%ProgramFiles(x86)%\dotnet\dotnet.exe" set "PATH=%ProgramFiles(x86)%\dotnet;%PATH%"
)
where dotnet >nul 2>&1
if errorlevel 1 (
  if exist "%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe" set "PATH=%LOCALAPPDATA%\Microsoft\dotnet;%PATH%"
)

where dotnet >nul 2>&1
if errorlevel 1 (
  echo/
  echo [AutoRAW] "dotnet" not found in PATH.
  echo Install .NET 8 SDK: https://get.dot.net/8
  echo If it is already installed, close this window and run dev.cmd again,
  echo or add to PATH: %%ProgramFiles%%\dotnet
  echo Then restart the terminal / PC.
  echo/
  exit /b 1
)

exit /b 0
