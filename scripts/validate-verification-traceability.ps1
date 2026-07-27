param(
    [string]$DevRepository = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$matrixPath = Join-Path $repoRoot "docs/verification/legacy-traceability.csv"
$failures = New-Object System.Collections.Generic.List[string]

function Add-Failure([string]$Message) {
    [void]$failures.Add($Message)
}

if (-not (Test-Path -LiteralPath $matrixPath)) {
    throw "Missing verification traceability matrix: $matrixPath"
}

$rows = @(Import-Csv -LiteralPath $matrixPath)
if ($rows.Count -eq 0) {
    throw "The verification traceability matrix is empty."
}

$requiredColumns = @(
    "SourceFile",
    "SourceLine",
    "SourceMethod",
    "Disposition",
    "VerificationClass",
    "VerificationMethod",
    "ReportScenario",
    "Notes"
)
$actualColumns = @($rows[0].PSObject.Properties.Name)
foreach ($column in $requiredColumns) {
    if ($column -notin $actualColumns) {
        Add-Failure "Traceability matrix is missing required column '$column'."
    }
}

$allowedDispositions = @(
    "Covered",
    "ConsolidatedEquivalent",
    "Duplicate",
    "NonOraclePlaceholder",
    "NonOracleWorkbench",
    "BlockedMissingFeature",
    "BlockedExternalData",
    "Obsolete"
)
$targetedDispositions = @("Covered", "ConsolidatedEquivalent", "Duplicate")
$keys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($row in $rows) {
    $key = "$($row.SourceFile)|$($row.SourceMethod)"
    if (-not $keys.Add($key)) {
        Add-Failure "Duplicate legacy traceability row: $key"
    }

    if ([string]::IsNullOrWhiteSpace($row.SourceFile) -or
        [string]::IsNullOrWhiteSpace($row.SourceMethod) -or
        [string]::IsNullOrWhiteSpace($row.Disposition) -or
        [string]::IsNullOrWhiteSpace($row.Notes)) {
        Add-Failure "Traceability row '$key' has an unaccounted required field."
    }

    if ($row.Disposition -notin $allowedDispositions) {
        Add-Failure "Traceability row '$key' has unsupported disposition '$($row.Disposition)'."
    }

    if ($row.Disposition -in $targetedDispositions -and
        ([string]::IsNullOrWhiteSpace($row.VerificationClass) -or
         [string]::IsNullOrWhiteSpace($row.VerificationMethod))) {
        Add-Failure "Traceability row '$key' must name its current verification class and method."
    }
}

$verificationRoot = Join-Path $repoRoot "src/RMC.TotalRisk.Verification"
$currentPairs = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
Get-ChildItem -Path $verificationRoot -Recurse -Filter *.cs -File |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    ForEach-Object {
        $text = [System.IO.File]::ReadAllText($_.FullName)
        $classMatch = [regex]::Match($text, 'public\s+(?:sealed\s+)?class\s+(?<Name>[A-Za-z_][A-Za-z0-9_]*Verification)\b')
        if (-not $classMatch.Success) {
            return
        }

        $className = $classMatch.Groups["Name"].Value
        foreach ($methodMatch in [regex]::Matches(
            $text,
            'public\s+void\s+(?<Name>Test_[A-Za-z0-9_]+)\s*\(')) {
            [void]$currentPairs.Add("$className::$($methodMatch.Groups["Name"].Value)")
        }
    }

foreach ($row in $rows | Where-Object { $_.Disposition -in $targetedDispositions }) {
    $pair = "$($row.VerificationClass)::$($row.VerificationMethod)"
    if (-not $currentPairs.Contains($pair)) {
        Add-Failure "Traceability target does not exist: $pair (source $($row.SourceFile):$($row.SourceLine))."
    }
}

$coveredReportScenarios = [System.Collections.Generic.HashSet[int]]::new()
foreach ($row in $rows | Where-Object { -not [string]::IsNullOrWhiteSpace($_.ReportScenario) }) {
    foreach ($token in $row.ReportScenario.Split(';', [System.StringSplitOptions]::RemoveEmptyEntries)) {
        $scenario = 0
        if (-not [int]::TryParse($token.Trim(), [ref]$scenario) -or $scenario -lt 1 -or $scenario -gt 49) {
            Add-Failure "Invalid verification-report scenario '$token' on $($row.SourceFile)|$($row.SourceMethod)."
            continue
        }
        if ($row.Disposition -notin $targetedDispositions) {
            Add-Failure "Report scenario $scenario is attached to non-covered disposition '$($row.Disposition)'."
            continue
        }
        [void]$coveredReportScenarios.Add($scenario)
    }
}
foreach ($scenario in 1..49) {
    if (-not $coveredReportScenarios.Contains($scenario)) {
        Add-Failure "Verification report scenario $scenario is not traced to a current verification test."
    }
}

$fdaNfip = $rows | Where-Object { $_.SourceMethod -eq "Test_NFIP_Assurance_TOL_65_FDA" }
if ($null -eq $fdaNfip -or $fdaNfip.Disposition -ne "Obsolete") {
    Add-Failure "The obsolete FDA/NFIP source case must remain explicitly classified as Obsolete."
}

if ([string]::IsNullOrWhiteSpace($DevRepository)) {
    $DevRepository = Join-Path $repoRoot "..\RMC-TotalRisk-Dev\RMC-TotalRisk\Test_TotalRisk"
}
if (Test-Path -LiteralPath $DevRepository) {
    $DevRepository = (Resolve-Path -LiteralPath $DevRepository).Path.TrimEnd('\', '/')
    $legacyKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    Get-ChildItem -Path $DevRepository -Recurse -Filter *.vb -File | ForEach-Object {
        $relative = $_.FullName.Substring($DevRepository.Length).TrimStart('\', '/')
        foreach ($line in [System.IO.File]::ReadAllLines($_.FullName)) {
            $match = [regex]::Match($line, '\b(?:Public\s+)?Sub\s+(?<Name>Test_[A-Za-z0-9_]+)\s*\(')
            if ($match.Success) {
                [void]$legacyKeys.Add("$relative|$($match.Groups["Name"].Value)")
            }
        }
    }

    foreach ($legacyKey in $legacyKeys) {
        if (-not $keys.Contains($legacyKey)) {
            Add-Failure "Legacy test method is missing from the traceability matrix: $legacyKey"
        }
    }
    foreach ($key in $keys) {
        if (-not $legacyKeys.Contains($key)) {
            Add-Failure "Traceability row no longer exists in the legacy source: $key"
        }
    }
}

if ($failures.Count -gt 0) {
    throw ($failures -join [Environment]::NewLine)
}

Write-Host "Verification traceability validation passed: $($rows.Count) legacy methods and all 49 report scenarios are accounted for."
