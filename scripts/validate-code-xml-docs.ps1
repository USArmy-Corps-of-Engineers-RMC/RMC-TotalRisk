param(
    [string]$Configuration = "Debug",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$failures = New-Object System.Collections.Generic.List[string]

function Get-RelativePath([string]$Path) {
    $root = (Resolve-Path $repoRoot).Path.TrimEnd('\', '/')
    $resolved = (Resolve-Path $Path).Path
    if ($resolved.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $resolved.Substring($root.Length).TrimStart('\', '/').Replace("\", "/")
    }

    return $resolved.Replace("\", "/")
}

function Add-Failure([string]$Message) {
    [void]$failures.Add($Message)
}

$codeRoots = @(
    "src/RMC.TotalRisk",
    "src/RMC.TotalRisk.Tests",
    "src/RMC.TotalRisk.Verification"
)

$sourceFiles = @(foreach ($root in $codeRoots) {
    $path = Join-Path $repoRoot $root
    if (Test-Path $path) {
        Get-ChildItem -Path $path -Recurse -Filter *.cs -File |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
    }
})

$exactRootNamespace = [regex]'^\s*namespace\s+RMC\.TotalRisk\s*(?:[;{]\s*)?$'
$rootUsing = [regex]'^\s*using\s+RMC\.TotalRisk\s*;'
$legacyFlatNamespace = [regex]'^\s*(?:namespace|using)\s+TotalRisk\s*[;{]?\s*$'
$bestFitReference = [regex]'\bRMC\.BestFit\b'
$uiFrameworkReference = [regex]'\bSystem\.Windows\b'
$sqliteReference = [regex]'SQLite'
$cultureLessTryParse = [regex]'double\.TryParse\((?![^)]*InvariantCulture)'
$cultureLessG17 = [regex]'\.ToString\("G17"\)'

if ($sourceFiles.Count -gt 0) {
    $combinedPattern = '^\s*namespace\s+RMC\.TotalRisk\s*(?:[;{]\s*)?$|^\s*using\s+RMC\.TotalRisk\s*;|^\s*(?:namespace|using)\s+TotalRisk\s*[;{]?\s*$|\bRMC\.BestFit\b|\bSystem\.Windows\b|SQLite|double\.TryParse\(|\.ToString\("G17"\)'
    $matches = Select-String -Path ($sourceFiles | Select-Object -ExpandProperty FullName) -Pattern $combinedPattern

    foreach ($match in $matches) {
        $line = $match.Line
        $relative = Get-RelativePath $match.Path
        $lineNumber = $match.LineNumber

        if ($exactRootNamespace.IsMatch($line)) {
            Add-Failure "${relative}:$lineNumber uses the bare root namespace RMC.TotalRisk; use a concrete namespace (e.g., RMC.TotalRisk.Models)."
        }
        elseif ($rootUsing.IsMatch($line)) {
            Add-Failure "${relative}:$lineNumber imports the bare root namespace RMC.TotalRisk; use the concrete namespace instead."
        }
        elseif ($legacyFlatNamespace.IsMatch($line)) {
            Add-Failure "${relative}:$lineNumber references the legacy flat TotalRisk namespace; porting must rename all namespaces to RMC.TotalRisk.*."
        }
        elseif ($bestFitReference.IsMatch($line)) {
            Add-Failure "${relative}:$lineNumber references RMC.BestFit; the model library depends only on RMC.Numerics (BestFit results are imported as Numerics artifacts)."
        }
        elseif ($uiFrameworkReference.IsMatch($line)) {
            Add-Failure "${relative}:$lineNumber references System.Windows; the model library is headless (no UI frameworks)."
        }
        elseif ($sqliteReference.IsMatch($line)) {
            Add-Failure "${relative}:$lineNumber references SQLite; persistence is a caller concern (XElement round-trip only in the model library)."
        }
        elseif ($cultureLessTryParse.IsMatch($line)) {
            Add-Failure "${relative}:$lineNumber parses a double without CultureInfo.InvariantCulture; serialization must be culture-invariant."
        }
        elseif ($cultureLessG17.IsMatch($line)) {
            Add-Failure "${relative}:$lineNumber formats G17 without CultureInfo.InvariantCulture; use ToString(""G17"", CultureInfo.InvariantCulture)."
        }
    }
}

function Test-HasXmlDocumentation($lines, [int]$index) {
    $j = $index - 1
    while ($j -ge 0 -and [string]::IsNullOrWhiteSpace($lines[$j])) {
        $j--
    }

    if ($j -ge 0 -and $lines[$j].Trim().EndsWith("]")) {
        $depth = 0
        while ($j -ge 0) {
            $trimmed = $lines[$j].Trim()
            if ($trimmed.EndsWith("]")) {
                $depth++
            }

            if ($trimmed.StartsWith("[")) {
                $depth--
            }

            $j--
            while ($j -ge 0 -and
                ([string]::IsNullOrWhiteSpace($lines[$j]) -or
                ($lines[$j].TrimStart().StartsWith("//") -and -not $lines[$j].TrimStart().StartsWith("///")))) {
                $j--
            }

            if ($depth -le 0 -and ($j -lt 0 -or -not $lines[$j].Trim().EndsWith("]"))) {
                break
            }
        }
    }

    return ($j -ge 0 -and $lines[$j].TrimStart().StartsWith("///"))
}

$documentedMemberFiles = @($sourceFiles | Where-Object {
    $_.Name -notmatch '\.(Designer|g|g\.i)\.cs$'
})
$typeDeclaration = [regex]'^[\s]*(?:public|internal|private|protected|file)[\s]+(?:(?:abstract|sealed|static|partial|readonly|unsafe|new|ref)[\s]+)*(class|struct|interface|enum|record)[\s]+[A-Za-z_][A-Za-z0-9_]*'
$methodDeclaration = [regex]'^[\s]*(?:public|internal|private|protected)[\s]+(?:(?:static|virtual|override|abstract|async|extern|unsafe|sealed|new|partial)[\s]+)*(?!class\b|struct\b|interface\b|enum\b|record\b|delegate\b|event\b)(?:[A-Za-z_][A-Za-z0-9_<>\[\],.?]*(?:[\s]+|[\*&]+[\s]*))+[A-Za-z_][A-Za-z0-9_]*[\s]*(?:<[^>]+>)?[\s]*\('

foreach ($file in $documentedMemberFiles) {
    $lines = [System.IO.File]::ReadAllLines($file.FullName)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if (($typeDeclaration.IsMatch($line) -or $methodDeclaration.IsMatch($line)) -and
            -not (Test-HasXmlDocumentation $lines $i)) {
            $relative = Get-RelativePath $file.FullName
            Add-Failure "${relative}:$($i + 1) is missing XML documentation for a class or method declaration."
        }
    }
}

if (-not $SkipBuild) {
    $projects = @(
        "src/RMC.TotalRisk/RMC.TotalRisk.csproj",
        "src/RMC.TotalRisk.Tests/RMC.TotalRisk.Tests.csproj",
        "src/RMC.TotalRisk.Verification/RMC.TotalRisk.Verification.csproj"
    )

    foreach ($project in $projects) {
        $projectPath = Join-Path $repoRoot $project
        if (-not (Test-Path $projectPath)) {
            Add-Failure "Missing project: $project"
            continue
        }

        $args = @(
            "build",
            $projectPath,
            "-c",
            $Configuration,
            "--no-restore",
            "--no-dependencies",
            "-p:EnforceXmlDocumentation=true",
            "-p:UseSharedCompilation=false",
            "-v:minimal"
        )

        & dotnet @args
        if ($LASTEXITCODE -ne 0) {
            Add-Failure "XML documentation build failed for $project."
        }
    }
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host "Code namespace and XML documentation validation passed."
