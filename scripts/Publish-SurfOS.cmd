@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "ROOT=%SCRIPT_DIR%..\"
set "PROJECT=%ROOT%src\SurfOS.Console\SurfOS.csproj"
set "RID=%~1"
if "%RID%"=="" set "RID=win-x64"
if /I not "%RID%"=="win-x64" if /I not "%RID%"=="win-arm64" if /I not "%RID%"=="win-x86" (
    echo Unsupported runtime identifier: %RID%
    echo Supported values: win-x64, win-arm64, win-x86
    exit /b 2
)
set "OUT=%ROOT%dist\SurfOS-%RID%"
set "ZIP=%ROOT%dist\SurfOS-%RID%.zip"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo The .NET SDK is required on the development PC to publish SurfOS.
    pause
    exit /b 1
)

echo Publishing a self-contained SurfOS build for %RID%...
dotnet publish "%PROJECT%" ^
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

if exist "%OUT%\secrets.json" (
    echo Credential-like file found in publish output. Remove it manually before publishing.
    exit /b 1
)

powershell -NoProfile -Command "$content = @('SurfOS', '', 'Recommended: launch SurfOS using SurfOS.exe.', '', 'If the UI is broken, the application does not launch, or something is not working as expected, close SurfOS and launch it using Launch-SurfOS.cmd instead of the executable.'); Set-Content -LiteralPath '%OUT%\README.MD' -Value $content -Encoding UTF8"
if errorlevel 1 (
    echo SurfOS was published, but README.MD could not be created.
    echo Folder: %OUT%
    pause
    exit /b 1
)

powershell -NoProfile -Command "Add-Type -AssemblyName System.IO.Compression.FileSystem; if (Test-Path -LiteralPath '%ZIP%') { Remove-Item -LiteralPath '%ZIP%' -Force }; [System.IO.Compression.ZipFile]::CreateFromDirectory('%OUT%', '%ZIP%', [System.IO.Compression.CompressionLevel]::Optimal, $false)"
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
echo Copy and extract the entire ZIP on the other PC, then run SurfOS.exe.
echo If it does not work as expected, use Launch-SurfOS.cmd instead.
pause
