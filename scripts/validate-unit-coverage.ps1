[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateRange(0.0, 1.0)]
    [double]$MinimumLineRate = 0.90,

    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $repositoryRoot 'src\RMC.TotalRisk.Tests\RMC.TotalRisk.Tests.csproj'
$resultsDirectory = Join-Path $repositoryRoot 'TestResults\unit-coverage'
$coverageFile = Join-Path $resultsDirectory 'RMC.TotalRisk.cobertura.xml'

if (-not (Test-Path $resultsDirectory)) {
    New-Item -ItemType Directory -Path $resultsDirectory | Out-Null
}

if (Test-Path $coverageFile) {
    Remove-Item -LiteralPath $coverageFile
}

$testArguments = @(
    'test',
    $testProject,
    '-c',
    $Configuration,
    '--no-restore'
)

if ($NoBuild) {
    $testArguments += '--no-build'
}

$testArguments += @(
    '--',
    '--coverage',
    '--coverage-output',
    $coverageFile,
    '--coverage-output-format',
    'cobertura',
    '--no-progress'
)

& dotnet @testArguments
if ($LASTEXITCODE -ne 0) {
    throw "The unit test run failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path $coverageFile)) {
    throw "The coverage collector did not create '$coverageFile'."
}

[xml]$coverage = Get-Content -LiteralPath $coverageFile
$totalRiskPackage = @($coverage.coverage.packages.package) |
    Where-Object { $_.name -eq 'RMC.TotalRisk' } |
    Select-Object -First 1

if ($null -eq $totalRiskPackage) {
    throw 'The coverage report does not contain the RMC.TotalRisk package.'
}

$lineRate = [double]::Parse(
    [string]$totalRiskPackage.'line-rate',
    [Globalization.CultureInfo]::InvariantCulture)

$coveredLines = @(
    $totalRiskPackage.classes.class.lines.line |
        Where-Object { [int]$_.hits -gt 0 }
).Count
$validLines = @($totalRiskPackage.classes.class.lines.line).Count
$percentage = $lineRate * 100.0
$minimumPercentage = $MinimumLineRate * 100.0

$summary = 'RMC.TotalRisk unit-only line coverage: {0:N2}% ({1}/{2} lines).' -f $percentage, $coveredLines, $validLines
Write-Host $summary

if ($lineRate -le $MinimumLineRate) {
    throw ('RMC.TotalRisk unit-only line coverage must exceed {0:N2}%; measured {1:N2}%.' -f $minimumPercentage, $percentage)
}

Write-Host ('Unit-only line coverage exceeds the {0:N2}% gate.' -f $minimumPercentage)
