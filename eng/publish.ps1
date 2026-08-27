[CmdletBinding()]
param(
    [ValidateSet("win-x64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")]
    [string[]] $RuntimeIdentifier = @("win-x64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"),
    [ValidatePattern("^[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$")]
    [string] $Version = "0.1.0",
    [ValidateSet("CompressedSingleFile", "TrimmedSingleFile", "NativeAot")]
    [string] $PublishMode = "TrimmedSingleFile",
    [switch] $NoRestore
)

$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$project = Join-Path $repoRoot "src/AtomPix.Desktop/AtomPix.Desktop.csproj"
$nugetConfig = Join-Path $repoRoot "NuGet.config"
$publishDirectoryName = if ($PublishMode -eq "NativeAot") { "publish-nativeaot" } else { "publish" }
$publishRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot ".artifacts/$publishDirectoryName"))
$packageRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot ".artifacts/packages"))
New-Item -ItemType Directory -Force -Path $publishRoot, $packageRoot | Out-Null

foreach ($rid in $RuntimeIdentifier) {
    $output = [System.IO.Path]::GetFullPath((Join-Path $publishRoot $rid))
    if (-not $output.StartsWith($publishRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to publish outside the artifact root: $output"
    }
    if (Test-Path -LiteralPath $output) {
        Remove-Item -LiteralPath $output -Recurse -Force
    }

    $arguments = @(
        "publish", $project,
        "--configuration", "Release",
        "--runtime", $rid,
        "--self-contained", "true",
        "--output", $output,
        "-p:RestoreConfigFile=$nugetConfig",
        "-p:Version=$Version",
        "-p:DebugType=None",
        "-p:DebugSymbols=false"
    )
    switch ($PublishMode) {
        "CompressedSingleFile" {
            $arguments += @(
                "-p:PublishSingleFile=true",
                "-p:IncludeNativeLibrariesForSelfExtract=true",
                "-p:EnableCompressionInSingleFile=true"
            )
        }
        "TrimmedSingleFile" {
            $arguments += @(
                "-p:PublishSingleFile=true",
                "-p:IncludeNativeLibrariesForSelfExtract=true",
                "-p:EnableCompressionInSingleFile=true",
                "-p:PublishTrimmed=true",
                "-p:TrimMode=partial"
            )
        }
        "NativeAot" {
            $arguments += @(
                "-p:PublishAot=true",
                "-p:IlcOptimizationPreference=Size",
                "-p:StripSymbols=true"
            )
        }
    }
    if ($NoRestore) { $arguments += "--no-restore" }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $rid." }

    $entryPoint = if ($rid -eq "win-x64") { "AtomPix.Desktop.exe" } else { "AtomPix.Desktop" }
    $entryPointPath = Join-Path $output $entryPoint
    if (-not (Test-Path -LiteralPath $entryPointPath -PathType Leaf)) {
        throw "Published entry point is missing: $entryPointPath"
    }
    Get-ChildItem -LiteralPath $output -Recurse -File -Filter "*.pdb" |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }

    $commit = "unknown"
    try {
        $resolvedCommit = (& git -C $repoRoot rev-parse HEAD 2>$null)
        if ($LASTEXITCODE -eq 0 -and $resolvedCommit) { $commit = $resolvedCommit.Trim() }
    } catch { }
    [ordered]@{
        product = "AtomPix"
        version = $Version
        runtimeIdentifier = $rid
        framework = "net10.0"
        selfContained = $true
        publishMode = $PublishMode
        singleFile = $PublishMode -ne "NativeAot"
        singleFileCompression = $PublishMode -ne "NativeAot"
        trimmed = $PublishMode -ne "CompressedSingleFile"
        trimMode = if ($PublishMode -eq "TrimmedSingleFile") { "partial" } elseif ($PublishMode -eq "NativeAot") { "full" } else { "none" }
        nativeAot = $PublishMode -eq "NativeAot"
        commit = $commit
        createdAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output "release-manifest.json") -Encoding utf8NoBOM

    $modeSuffix = if ($PublishMode -eq "NativeAot") { "-nativeaot" } else { "" }
    $baseName = "AtomPix-$Version-$rid$modeSuffix"
    if ($rid -eq "win-x64") {
        $archive = Join-Path $packageRoot "$baseName.zip"
        if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
        Compress-Archive -Path (Join-Path $output "*") -DestinationPath $archive -CompressionLevel Optimal
    } else {
        $archive = Join-Path $packageRoot "$baseName.tar.gz"
        if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
        & tar -C $output -czf $archive .
        if ($LASTEXITCODE -ne 0) { throw "Archive creation failed for $rid." }
    }

    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([System.IO.Path]::GetFileName($archive))" |
        Set-Content -LiteralPath "$archive.sha256" -Encoding ascii
    Write-Host "Created $archive"
}
