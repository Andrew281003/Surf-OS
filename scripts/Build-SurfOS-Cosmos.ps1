[CmdletBinding()]
param()

$project = Join-Path $PSScriptRoot '..\src\SurfOS.Cosmos\SurfOS.Cosmos.csproj'
$projectDirectory = Split-Path -Parent $project

Push-Location $projectDirectory
try {
    dotnet restore $project
    if ($LASTEXITCODE -ne 0) { throw 'Cosmos restore failed.' }

    dotnet build $project --configuration Debug --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Cosmos ISO build failed.' }
} finally {
    Pop-Location
}

$iso = Join-Path $projectDirectory 'bin\Debug\net10.0\win-x64\cosmos\SurfOS.iso'
if (-not (Test-Path -LiteralPath $iso)) { throw 'Cosmos build completed without an ISO.' }
Write-Host "Bootable SurfOS ISO: $([System.IO.Path]::GetFullPath($iso))"
