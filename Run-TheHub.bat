@echo off
setlocal

set "DEBUG_DIR=%~dp0TheHub.App\bin\Debug\net8.0-windows10.0.19041.0"
set "RELEASE_DIR=%~dp0TheHub.App\bin\Release\net8.0-windows10.0.19041.0"

if exist "%DEBUG_DIR%\TheHub.exe" (
    cd /d "%DEBUG_DIR%"
    start "" "TheHub.exe"
    exit /b 0
)

if exist "%RELEASE_DIR%\TheHub.exe" (
    cd /d "%RELEASE_DIR%"
    start "" "TheHub.exe"
    exit /b 0
)

echo [ERROR] TheHub.exe was not found in TheHub.App build output!
echo Please run 'dotnet build' first.
pause
exit /b 1
