[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$brandingRoot = Join-Path $repoRoot 'src/AtomPix.Desktop/Assets/Branding'
$assetsRoot = Join-Path $repoRoot 'assets'

function Get-BigEndianUInt32 {
    param(
        [Parameter(Mandatory)] [byte[]] $Bytes,
        [Parameter(Mandatory)] [int] $Offset
    )

    return [uint32](
        ([uint32]$Bytes[$Offset] -shl 24) -bor
        ([uint32]$Bytes[$Offset + 1] -shl 16) -bor
        ([uint32]$Bytes[$Offset + 2] -shl 8) -bor
        [uint32]$Bytes[$Offset + 3])
}

function Assert-SquarePng {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [int] $ExpectedSize
    )

    $bytes = [IO.File]::ReadAllBytes($Path)
    $signature = [byte[]](137, 80, 78, 71, 13, 10, 26, 10)
    $hasPngSignature = $bytes.Length -ge 24
    for ($index = 0; $hasPngSignature -and $index -lt $signature.Length; $index++) {
        $hasPngSignature = $bytes[$index] -eq $signature[$index]
    }
    if (-not $hasPngSignature) {
        throw "Not a valid PNG file: $Path"
    }

    $width = Get-BigEndianUInt32 -Bytes $bytes -Offset 16
    $height = Get-BigEndianUInt32 -Bytes $bytes -Offset 20
    if ($width -ne $ExpectedSize -or $height -ne $ExpectedSize) {
        throw "Expected a ${ExpectedSize}x${ExpectedSize} PNG, got ${width}x${height}: $Path"
    }
}

function Write-BigEndianUInt32 {
    param(
        [Parameter(Mandatory)] [IO.Stream] $Stream,
        [Parameter(Mandatory)] [uint32] $Value
    )

    $Stream.WriteByte([byte](($Value -shr 24) -band 0xff))
    $Stream.WriteByte([byte](($Value -shr 16) -band 0xff))
    $Stream.WriteByte([byte](($Value -shr 8) -band 0xff))
    $Stream.WriteByte([byte]($Value -band 0xff))
}

function Write-Ascii {
    param(
        [Parameter(Mandatory)] [IO.Stream] $Stream,
        [Parameter(Mandatory)] [string] $Value
    )

    $bytes = [Text.Encoding]::ASCII.GetBytes($Value)
    $Stream.Write($bytes, 0, $bytes.Length)
}

$sizes = @(16, 32, 48, 64, 128, 256, 512)
foreach ($size in $sizes + 1024) {
    Assert-SquarePng -Path (Join-Path $brandingRoot "AtomPix-$size.png") -ExpectedSize $size
}

$windowsIcon = Join-Path $brandingRoot 'AtomPix.ico'
$icoBytes = [IO.File]::ReadAllBytes($windowsIcon)
if ($icoBytes.Length -lt 6 -or [BitConverter]::ToUInt16($icoBytes, 0) -ne 0 -or [BitConverter]::ToUInt16($icoBytes, 2) -ne 1) {
    throw "Not a valid Windows ICO file: $windowsIcon"
}
$icoFrameCount = [BitConverter]::ToUInt16($icoBytes, 4)
if ($icoFrameCount -lt 2) {
    throw "The Windows application icon must contain multiple resolutions: $windowsIcon"
}
if ($icoBytes.Length -lt 6 + (16 * $icoFrameCount)) {
    throw "The Windows ICO directory is truncated: $windowsIcon"
}
$icoSizes = @(
    for ($frameIndex = 0; $frameIndex -lt $icoFrameCount; $frameIndex++) {
        $encodedWidth = $icoBytes[6 + (16 * $frameIndex)]
        if ($encodedWidth -eq 0) { 256 } else { [int]$encodedWidth }
    }
)
foreach ($requiredSize in @(16, 24, 32, 48, 64, 128, 256)) {
    if ($requiredSize -notin $icoSizes) {
        throw "The Windows ICO is missing its ${requiredSize}x${requiredSize} frame: $windowsIcon"
    }
}

$sourceRoot = Join-Path $assetsRoot 'source'
$macRoot = Join-Path $assetsRoot 'macos'
$linuxRoot = Join-Path $assetsRoot 'linux/icons/hicolor'
New-Item -ItemType Directory -Force $sourceRoot, $macRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $brandingRoot 'AtomPix-1024.png') -Destination (Join-Path $sourceRoot 'AtomPix-1024.png') -Force

foreach ($size in $sizes) {
    $target = Join-Path $linuxRoot "${size}x${size}/apps"
    New-Item -ItemType Directory -Force $target | Out-Null
    Copy-Item -LiteralPath (Join-Path $brandingRoot "AtomPix-$size.png") -Destination (Join-Path $target 'atompix.png') -Force
}

$icnsChunks = [ordered]@{
    icp4 = 16
    icp5 = 32
    icp6 = 64
    ic07 = 128
    ic08 = 256
    ic09 = 512
    ic10 = 1024
}
$chunkPayloads = @()
$icnsLength = 8
foreach ($entry in $icnsChunks.GetEnumerator()) {
    $payload = [IO.File]::ReadAllBytes((Join-Path $brandingRoot "AtomPix-$($entry.Value).png"))
    $chunkPayloads += [pscustomobject]@{ Type = $entry.Key; Bytes = $payload }
    $icnsLength += 8 + $payload.Length
}

$icnsPath = Join-Path $macRoot 'AtomPix.icns'
$stream = [IO.File]::Open($icnsPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    Write-Ascii -Stream $stream -Value 'icns'
    Write-BigEndianUInt32 -Stream $stream -Value $icnsLength
    foreach ($chunk in $chunkPayloads) {
        Write-Ascii -Stream $stream -Value $chunk.Type
        Write-BigEndianUInt32 -Stream $stream -Value (8 + $chunk.Bytes.Length)
        $stream.Write($chunk.Bytes, 0, $chunk.Bytes.Length)
    }
} finally {
    $stream.Dispose()
}

$generatedIcns = [IO.File]::ReadAllBytes($icnsPath)
if ([Text.Encoding]::ASCII.GetString($generatedIcns, 0, 4) -ne 'icns' -or
    (Get-BigEndianUInt32 -Bytes $generatedIcns -Offset 4) -ne $generatedIcns.Length) {
    throw "Generated macOS icon failed ICNS structural validation: $icnsPath"
}

Write-Host "Generated AtomPix platform icons."
Write-Host "Windows: $windowsIcon ($icoFrameCount embedded frames)"
Write-Host "macOS:  $icnsPath ($($icnsChunks.Count) PNG chunks)"
Write-Host "Linux:  $linuxRoot ($($sizes.Count) hicolor sizes)"
