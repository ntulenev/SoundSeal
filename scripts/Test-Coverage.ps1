#Requires -Version 7.0
[CmdletBinding()]
param(
    [string] $ResultsDirectory = (Join-Path $PSScriptRoot '../TestResults')
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$run = Join-Path $ResultsDirectory ([Guid]::NewGuid().ToString('N'))
$run = [IO.Directory]::CreateDirectory($run).FullName
$solution = Join-Path $root 'src/MeetingRecorder.slnx'
& dotnet test $solution -c Release --collect 'XPlat Code Coverage' --results-directory $run -- 'DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=json'
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Multiple test hosts can exercise the same assembly. Count each line and IL branch
# once per module, and mark it covered when any test host executed it.
$modules = [ordered]@{}
foreach ($name in 'MeetingRecorder.Domain', 'MeetingRecorder.Application', 'MeetingRecorder.Infrastructure', 'SoundSeal') {
    $modules[$name] = @{ Lines = @{}; Branches = @{} }
}

foreach ($file in Get-ChildItem -LiteralPath $run -Recurse -Filter 'coverage.json') {
    $report = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json -AsHashtable
    foreach ($assembly in $report.GetEnumerator()) {
        $name = [IO.Path]::GetFileNameWithoutExtension($assembly.Key)
        if (-not $modules.Contains($name)) { continue }
        $module = $modules[$name]
        foreach ($document in $assembly.Value.GetEnumerator()) {
            $path = [IO.Path]::GetRelativePath($root, $document.Key)
            foreach ($type in $document.Value.GetEnumerator()) {
                foreach ($method in $type.Value.GetEnumerator()) {
                    foreach ($line in $method.Value.Lines.GetEnumerator()) {
                        $key = "$path|$($line.Key)"
                        $module.Lines[$key] = $module.Lines[$key] -or ($line.Value -gt 0)
                    }
                    foreach ($branch in $method.Value.Branches) {
                        $key = "$path|$($type.Key)|$($method.Key)|$($branch.Offset)|$($branch.EndOffset)|$($branch.Path)|$($branch.Ordinal)"
                        $module.Branches[$key] = $module.Branches[$key] -or ($branch.Hits -gt 0)
                    }
                }
            }
        }
    }
}

function Get-CoverageRow($name, $coveredLines, $lines, $coveredBranches, $branches) {
    [pscustomobject]@{
        Module = $name
        Lines = "$coveredLines/$lines"
        'Line %' = [Math]::Round(100.0 * $coveredLines / $lines, 1)
        Branches = "$coveredBranches/$branches"
        'Branch %' = if ($branches) { [Math]::Round(100.0 * $coveredBranches / $branches, 1) }
    }
}

$lineCount = $branchCount = $lineHits = $branchHits = 0
$rows = foreach ($entry in $modules.GetEnumerator()) {
    $lines = $entry.Value.Lines
    $branches = $entry.Value.Branches
    if (-not $lines.Count) { throw "Missing coverage for $($entry.Key)." }
    $coveredLines = @($lines.Values -eq $true).Count
    $coveredBranches = @($branches.Values -eq $true).Count
    $lineCount += $lines.Count
    $branchCount += $branches.Count
    $lineHits += $coveredLines
    $branchHits += $coveredBranches
    Get-CoverageRow $entry.Key $coveredLines $lines.Count $coveredBranches $branches.Count
}
$rows += Get-CoverageRow 'Total' $lineHits $lineCount $branchHits $branchCount
$rows | Format-Table -AutoSize
"Coverlet reports: $run"
