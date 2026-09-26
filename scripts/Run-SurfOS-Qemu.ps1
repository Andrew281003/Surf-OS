[CmdletBinding()]
param(
    [string] $DiskPath = (Join-Path $PSScriptRoot '..\artifacts\surfos-disk.raw'),
    [ValidateRange(33, 131072)]
    [int] $DiskSizeMb = 2048,
    [switch] $SkipBuild
)

$qemuDirectory = 'C:\Program Files\qemu'
$qemu = Join-Path $qemuDirectory 'qemu-system-x86_64.exe'
$qemuImg = Join-Path $qemuDirectory 'qemu-img.exe'
if (!(Test-Path -LiteralPath $qemu) -or !(Test-Path -LiteralPath $qemuImg)) {
    throw 'QEMU was not found under C:\Program Files\qemu.'
}

if (!$SkipBuild) {
    & (Join-Path $PSScriptRoot 'Build-SurfOS-Cosmos.ps1')
}

$iso = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\src\SurfOS.Cosmos\bin\Debug\net10.0\win-x64\cosmos\SurfOS.iso'))
if (!(Test-Path -LiteralPath $iso)) { throw "SurfOS ISO not found: $iso" }

$disk = [System.IO.Path]::GetFullPath($DiskPath)
$diskParent = Split-Path -Parent $disk
New-Item -ItemType Directory -Path $diskParent -Force | Out-Null
if (!(Test-Path -LiteralPath $disk)) {
    & $qemuImg create -f raw $disk "${DiskSizeMb}M"
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the QEMU disk.' }
    Write-Host "Created blank SurfOS disk: $disk"
}

$arguments = @(
    '-name', 'SurfOS',
    '-machine', 'pc',
    '-m', '256M',
    '-boot', 'd',
    '-cdrom', $iso,
    '-drive', "file=$disk,format=raw,if=ide,index=0",
    '-no-reboot'
)
Start-Process -FilePath $qemu -ArgumentList $arguments
