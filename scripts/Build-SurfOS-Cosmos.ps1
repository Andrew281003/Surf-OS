[CmdletBinding()]
param()

$project = Join-Path $PSScriptRoot '..\src\SurfOS.Cosmos\SurfOS.Cosmos.csproj'

dotnet restore $project
if ($LASTEXITCODE -ne 0) { throw 'Cosmos restore failed.' }

dotnet build $project --configuration Debug --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Cosmos ISO build failed.' }

$iso = Join-Path $PSScriptRoot '..\src\SurfOS.Cosmos\bin\cosmos\cosmos\Debug\net6.0\SurfOS.iso'
Write-Host "Bootable SurfOS ISO: $([System.IO.Path]::GetFullPath($iso))"
