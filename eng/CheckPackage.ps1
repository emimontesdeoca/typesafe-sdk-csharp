param(
    [string] $PackagePath = ""
)

$ErrorActionPreference = "Stop"
if (-not $PackagePath) {
    $PackagePath = Join-Path $PSScriptRoot "..\artifacts\package\TypeSafe.Ai.0.6.0.nupkg"
}
if (-not (Test-Path $PackagePath)) {
    throw "Package not found: $PackagePath"
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path $PackagePath))
try {
    $allowed = @(
        "TypeSafe.Ai.nuspec",
        "README.md",
        "LICENSE",
        "lib/net10.0/TypeSafe.Ai.dll",
        "lib/net10.0/TypeSafe.Ai.xml",
        "_rels/.rels",
        "[Content_Types].xml"
    )
    foreach ($entry in $archive.Entries) {
        $metadata = $entry.FullName.StartsWith("package/services/metadata/", [StringComparison]::Ordinal)
        if ($entry.FullName -notin $allowed -and -not $metadata) {
            throw "Unexpected package entry: $($entry.FullName)"
        }
    }
    Write-Output "Validated package contents: $PackagePath"
} finally {
    $archive.Dispose()
}