@echo off
setlocal

set "ROOT=%~dp0"
set "RID=%~1"
if "%RID%"=="" set "RID=win-x64"
set "OUT=%ROOT%dist\SurfOS-%RID%"
set "ZIP=%ROOT%dist\SurfOS-%RID%.zip"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo The .NET SDK is required on the development PC to publish SurfOS.
    pause
    exit /b 1
)

echo Publishing a self-contained SurfOS build for %RID%...
dotnet publish "%ROOT%SurfOS2.csproj" ^
    --configuration Release ^
    --runtime %RID% ^
    --self-contained true ^
    --output "%OUT%" ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -p:DebugType=None ^
    --nologo

if errorlevel 1 (
    echo.
    echo SurfOS publishing failed.
    pause
    exit /b 1
)

powershell -NoProfile -Command "Compress-Archive -Path '%OUT%\*' -DestinationPath '%ZIP%' -Force"
if errorlevel 1 (
    echo SurfOS was published, but the ZIP could not be created.
    echo Folder: %OUT%
    pause
    exit /b 1
)

echo.
echo Self-contained package created:
echo %ZIP%
echo.
echo Copy and extract the entire ZIP on the other PC, then run Launch-SurfOS.cmd.
pause
