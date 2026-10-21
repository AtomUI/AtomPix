[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$desktopRoot = Join-Path $repoRoot "src/AtomPix.Desktop"
$localizationRoot = Join-Path $desktopRoot "Localization"
$expectedCatalogs = @(
    "Batch",
    "Browser",
    "Common",
    "Errors",
    "Home",
    "Settings",
    "Shell",
    "Tools"
)

$catalogFiles = @(Get-ChildItem -LiteralPath $localizationRoot -Recurse -Filter "Resources.resx")
$actualCatalogs = @($catalogFiles | ForEach-Object {
    $_.Directory.Parent.Name
} | Sort-Object -Unique)

if (Compare-Object -ReferenceObject $expectedCatalogs -DifferenceObject $actualCatalogs) {
    throw "Localization catalog set differs from the approved eight-catalog design."
}

$keyOwners = @{}
foreach ($catalogFile in $catalogFiles) {
    [xml] $document = Get-Content -LiteralPath $catalogFile.FullName -Raw
    foreach ($entry in @($document.root.data)) {
        $key = [string] $entry.name
        $value = [string] $entry.value
        if ([string]::IsNullOrWhiteSpace($key)) {
            throw "Localization key is empty in $($catalogFile.FullName)."
        }
        if ([string]::IsNullOrWhiteSpace($value)) {
            throw "Localization value is empty for '$key' in $($catalogFile.FullName)."
        }
        if ($keyOwners.ContainsKey($key)) {
            throw "Duplicate localization key '$key' in '$($keyOwners[$key])' and '$($catalogFile.FullName)'."
        }
        $keyOwners[$key] = $catalogFile.FullName
    }
}

$usagePattern = [regex]::new('\{loc:Loc\s+(?<key>[A-Za-z0-9_.-]+)\}')
$hanPattern = [regex]::new('[\u3400-\u4DBF\u4E00-\u9FFF]')
$usageKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$hardCodedLines = [System.Collections.Generic.List[string]]::new()

foreach ($axamlFile in Get-ChildItem -LiteralPath $desktopRoot -Recurse -Filter "*.axaml") {
    $text = Get-Content -LiteralPath $axamlFile.FullName -Raw
    foreach ($match in $usagePattern.Matches($text)) {
        [void] $usageKeys.Add($match.Groups["key"].Value)
    }

    $lineNumber = 0
    foreach ($line in Get-Content -LiteralPath $axamlFile.FullName) {
        $lineNumber++
        if ($hanPattern.IsMatch($line)) {
            $relativePath = [System.IO.Path]::GetRelativePath($repoRoot, $axamlFile.FullName)
            $hardCodedLines.Add("${relativePath}:${lineNumber}")
        }
    }
}

if ($hardCodedLines.Count -gt 0) {
    throw "Hard-coded Chinese text remains in AXAML: $($hardCodedLines -join ', ')."
}

$missingKeys = @($usageKeys | Where-Object { -not $keyOwners.ContainsKey($_) } | Sort-Object)
if ($missingKeys.Count -gt 0) {
    throw "AXAML references missing localization keys: $($missingKeys -join ', ')."
}

Write-Host "Localization integrity passed: $($catalogFiles.Count) catalogs, $($keyOwners.Count) unique keys, $($usageKeys.Count) AXAML keys, 0 hard-coded AXAML Chinese lines."
