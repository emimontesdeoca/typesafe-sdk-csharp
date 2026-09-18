param(
    [string] $ExpectedVersion = "",
    [string] $Tag = ""
)

$ErrorActionPreference = "Stop"
$propsText = Get-Content -Raw (Join-Path $PSScriptRoot "..\Directory.Build.props")
$versionMatch = [regex]::Match($propsText, '<VersionPrefix[^>]*>(?<version>[^<]+)</VersionPrefix>')
$version = if ($ExpectedVersion) { $ExpectedVersion } else { $versionMatch.Groups["version"].Value.Trim() }
if (-not $version -or $version -notmatch '^\d+\.\d+\.\d+$') {
    throw "The version must be a stable x.y.z value."
}
if ($Tag) {
    if ($Tag -notmatch '^v(?<tagVersion>\d+\.\d+\.\d+)$') {
        throw "Release tags must use vX.Y.Z: $Tag"
    }
    if ($Matches.tagVersion -ne $version) {
        throw "Release tag $Tag does not match package version $version."
    }
}

$changelog = Get-Content -Raw (Join-Path $PSScriptRoot "..\docs\changelog.md")
$headingPattern = "(?m)^##\s+$([regex]::Escape($version))\s+-\s+(?<date>\d{4}-\d{2}-\d{2})\s*$"
$headingMatch = [regex]::Match($changelog, $headingPattern)
if (-not $headingMatch.Success) {
    throw "docs/changelog.md must contain a dated heading for version $version."
}
$parsedDate = [DateTime]::MinValue
if (-not [DateTime]::TryParseExact(
        $headingMatch.Groups["date"].Value,
        "yyyy-MM-dd",
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::None,
        [ref]$parsedDate)) {
    throw "The changelog date for version $version is not a valid ISO date."
}

$package = Join-Path $PSScriptRoot "..\artifacts\package\TypeSafe.Ai.$version.nupkg"
if (Test-Path $package) {
    Write-Output "Validated version $version and package $package"
} else {
    Write-Output "Validated version $version; package has not been packed yet."
}