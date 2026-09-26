[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $Path = (Join-Path $env:TEMP 'SurfOS-Test.vhdx'),
    [ValidateRange(1, 64)]
    [int] $SizeGb = 4
)

$resolvedParent = [System.IO.Path]::GetFullPath((Split-Path -Parent $Path))
$resolvedPath = Join-Path $resolvedParent (Split-Path -Leaf $Path)

New-Item -ItemType Directory -Path $resolvedParent -Force | Out-Null

if (Test-Path -LiteralPath $resolvedPath) {
    throw "The test disk already exists: $resolvedPath"
}

if ($PSCmdlet.ShouldProcess($resolvedPath, "Create a $SizeGb GB dynamic VHDX test disk")) {
    $sizeBytes = $SizeGb * 1GB
    New-VHD -Path $resolvedPath -SizeBytes $sizeBytes -Dynamic -ErrorAction Stop | Out-Null
    Write-Host "Created SurfOS test disk: $resolvedPath"
}
