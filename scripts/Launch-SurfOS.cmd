@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "REPO=%SCRIPT_DIR%.."
set "PROJECT=%REPO%\src\SurfOS.Console\SurfOS.csproj"
set "APP=%SCRIPT_DIR%SurfOS.exe"

if not exist "%APP%" (
    set "APP=%SCRIPT_DIR%SurfOS2.exe"
)

if not exist "%APP%" (
    set "APP=%REPO%\src\SurfOS.Console\bin\Debug\net9.0\SurfOS.exe"
)

if not exist "%APP%" (
    echo Building SurfOS...
    dotnet build "%PROJECT%" --nologo
    if errorlevel 1 (
        echo.
        echo SurfOS could not be built.
        pause
        exit /b 1
    )
)

if not exist "%APP%" (
    echo.
    echo SurfOS was built, but the executable was not found:
    echo %APP%
    pause
    exit /b 1
)

for %%I in ("%APP%") do (
    set "APP=%%~fI"
    set "APP_DIR=%%~dpI"
)

where wt.exe >nul 2>nul
if not errorlevel 1 (
    start "" wt.exe -w new -M nt --title "SurfOS" --suppressApplicationTitle -d "%APP_DIR%" "%APP%"
    exit /b 0
)

echo Windows Terminal was not found. Falling back to the Windows console host.
start "SurfOS" /max /wait "%APP%"
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
    echo.
    echo SurfOS exited with error code %EXIT_CODE%.
    echo Startup log: %LOCALAPPDATA%\SurfOS\startup-crash.log
    echo Kernel log : %LOCALAPPDATA%\SurfOS\logs\kernel.log
    echo.
    pause
)

exit /b %EXIT_CODE%
