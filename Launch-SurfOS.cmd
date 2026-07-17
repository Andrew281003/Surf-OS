@echo off
setlocal

set "ROOT=%~dp0"
set "APP=%ROOT%SurfOS2.exe"

if not exist "%APP%" (
    set "APP=%ROOT%bin\Debug\net9.0\SurfOS2.exe"
)

if not exist "%APP%" (
    echo Building SurfOS...
    dotnet build "%ROOT%SurfOS2.csproj" --nologo
    if errorlevel 1 (
        echo.
        echo SurfOS could not be built.
        pause
        exit /b 1
    )
)

"%APP%"
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
