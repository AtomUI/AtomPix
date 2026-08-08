[CmdletBinding()]
param(
    [ValidateSet("win-x64", "linux-x64", "osx-arm64")]
    [string] $RuntimeIdentifier,
    [ValidateRange(2, 60)]
    [int] $Seconds = 5
)

$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$publishRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot ".artifacts/publish"))
$directory = [System.IO.Path]::GetFullPath((Join-Path $publishRoot $RuntimeIdentifier))
if (-not $directory.StartsWith($publishRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to execute outside the publish artifact root."
}
$entryPoint = if ($RuntimeIdentifier -eq "win-x64") { "AtomPix.Desktop.exe" } else { "AtomPix.Desktop" }
$executable = Join-Path $directory $entryPoint
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw "Missing executable: $executable" }

$start = @{ FilePath = $executable; WorkingDirectory = $directory; PassThru = $true }
if ($IsWindows) { $start.WindowStyle = "Hidden" }
$process = Start-Process @start
try {
    if ($process.WaitForExit($Seconds * 1000)) {
        throw "AtomPix exited during the $Seconds-second startup smoke test with code $($process.ExitCode)."
    }
    Write-Host "AtomPix remained healthy for the $Seconds-second startup smoke window."
} finally {
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
    }
}
