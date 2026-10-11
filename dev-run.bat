@echo off
setlocal enabledelayedexpansion

cd /d "%~dp0"

echo ===================================================
echo [1/3] Checking for running instances of The Hub...
echo ===================================================
tasklist /FI "IMAGENAME eq TheHub.exe" 2>NUL | find /I /N "TheHub.exe">NUL
if "%ERRORLEVEL%"=="0" (
    echo [WARNING] TheHub.exe is running. Closing process to release file lock...
    taskkill /F /IM TheHub.exe >NUL 2>&1
    timeout /t 1 /nobreak >NUL
)

echo.
echo ===================================================
echo [2/3] Building solution (dotnet build)...
echo ===================================================
dotnet build TheHub.sln -c Debug
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] Build failed! Please review the errors above.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo ===================================================
echo [3/3] Launching TheHub from TheHub.App output...
echo ===================================================
set "APP_DIR=%~dp0TheHub.App\bin\Debug\net8.0-windows10.0.19041.0"
if exist "%APP_DIR%\TheHub.exe" (
    cd /d "%APP_DIR%"
    start "" "TheHub.exe"
    exit /b 0
) else (
    echo [ERROR] Executable not found at "%APP_DIR%\TheHub.exe"
    pause
    exit /b 1
)
