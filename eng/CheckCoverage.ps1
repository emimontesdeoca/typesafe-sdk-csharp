param(
    [string] $CoveragePath = ""
)

$ErrorActionPreference = "Stop"
if (-not $CoveragePath) {
    $coverageFile = Get-ChildItem (Join-Path $PSScriptRoot "..\tests\TypeSafe.Ai.Tests\TestResults") `
        -Recurse -Filter coverage.cobertura.xml -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime |
        Select-Object -Last 1
    if ($coverageFile) {
        $CoveragePath = $coverageFile.FullName
    }
}

if (-not (Test-Path $CoveragePath)) {
    throw "Coverage report not found: $CoveragePath"
}

[xml]$coverage = Get-Content -Raw $CoveragePath
$lineRate = [double]$coverage.coverage.'line-rate'
$branchRate = [double]$coverage.coverage.'branch-rate'
$minimumLineRate = 0.95
$minimumBranchRate = 0.90

Write-Output ("Coverage: line {0:P2}, branch {1:P2}" -f $lineRate, $branchRate)
if ($lineRate -lt $minimumLineRate -or $branchRate -lt $minimumBranchRate) {
    throw ("Coverage floor failed. Required line >= {0:P0}, branch >= {1:P0}." -f $minimumLineRate, $minimumBranchRate)
}