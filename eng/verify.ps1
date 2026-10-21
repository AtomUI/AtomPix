[CmdletBinding()]
param(
    [switch] $NoRestore,
    [switch] $SkipBuild,
    [switch] $SkipStress,
    [switch] $SkipAudit,
    [switch] $PublishCurrentPlatform
)

$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$solution = Join-Path $repoRoot "AtomPix.slnx"
$nugetConfig = Join-Path $repoRoot "NuGet.config"
$results = Join-Path $repoRoot ".artifacts/test-results"
New-Item -ItemType Directory -Force -Path $results | Out-Null

function Invoke-Checked([string] $description, [scriptblock] $action) {
    Write-Host "==> $description"
    & $action
    if ($LASTEXITCODE -ne 0) { throw "$description failed with exit code $LASTEXITCODE." }
}

function Assert-CityReleaseClosure {
    $cityOutput = Join-Path $repoRoot "external/AtomUI.City/output/bin/Release"
    $desktopOutput = Join-Path $repoRoot "src/AtomPix.Desktop/bin/Release/net10.0"
    $assemblies = @(
        "AtomUI.City.Core",
        "AtomUI.City.State",
        "AtomUI.City.Mvvm",
        "AtomUI.City.Routing",
        "AtomUI.City.Security",
        "AtomUI.City.Presentation",
        "AtomUI.City.Localization"
    )
    foreach ($assembly in $assemblies) {
        $source = Join-Path $cityOutput "$assembly/net10.0/$assembly.dll"
        $consumed = Join-Path $desktopOutput "$assembly.dll"
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw "City Release assembly is missing: $source"
        }
        if (-not (Test-Path -LiteralPath $consumed -PathType Leaf)) {
            throw "AtomPix Release closure is missing: $consumed"
        }
        $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
        $consumedHash = (Get-FileHash -LiteralPath $consumed -Algorithm SHA256).Hash
        if ($sourceHash -ne $consumedHash) {
            throw "AtomPix Release consumed a non-Release City assembly: $assembly"
        }
    }
}

Push-Location $repoRoot
try {
    if (-not $NoRestore) {
        Invoke-Checked "Restore" { dotnet restore $solution --configfile $nugetConfig }
    }
    if (-not $SkipBuild) {
        Invoke-Checked "Release build" { dotnet build $solution -c Release --no-restore }
        Invoke-Checked "City Release configuration closure" { Assert-CityReleaseClosure }
    }

    Invoke-Checked "Functional and UI automation" {
        dotnet test $solution -c Release --no-build --no-restore --filter "Category!=Stress" --logger "trx;LogFilePrefix=functional-" --results-directory $results
    }

    if (-not $SkipStress) {
        Invoke-Checked "Workflow, imaging and diagnostics stress tests" {
            dotnet test "tests/AtomPix.StressTests/AtomPix.StressTests.csproj" -c Release --no-build --no-restore --filter "Category=Stress" --logger "trx;LogFilePrefix=stress-" --results-directory $results
        }
        Invoke-Checked "AtomUI virtualization stress test" {
            dotnet test "tests/AtomPix.Desktop.UiTests/AtomPix.Desktop.UiTests.csproj" -c Release --no-build --no-restore --filter "Category=Stress" --logger "trx;LogFilePrefix=ui-stress-" --results-directory $results
        }
    }

    if (-not $SkipAudit) {
        Invoke-Checked "NuGet vulnerability audit" {
            dotnet list $solution package --vulnerable --include-transitive --no-restore
        }
    }

    Invoke-Checked "Whitespace integrity" { git diff --check }

    if ($PublishCurrentPlatform) {
        $rid = if ($IsWindows) { "win-x64" } elseif ($IsLinux) { "linux-x64" } elseif ($IsMacOS) { "osx-arm64" } else { throw "Unsupported platform." }
        # Runtime-specific assets are not guaranteed by a solution-level restore.
        & (Join-Path $PSScriptRoot "publish.ps1") -RuntimeIdentifier $rid
    }
} finally {
    Pop-Location
}
