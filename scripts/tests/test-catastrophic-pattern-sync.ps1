#!/usr/bin/env pwsh
# Contract test: the Unity catastrophic-pattern list is duplicated across three call
# sites -- scripts/unity/run-ci-tests.ps1, .github/actions/verify-unity-results/action.yml,
# and .github/actions/dump-unity-log-tail/action.yml. They MUST stay identical: a divergent
# scanner gives false confidence (e.g. a real failure surfaced in one summary but hidden in
# another). The "keep in sync by convention" comments already failed once -- the
# `Package [id] cannot be found` entry drifted out of both action files. This test extracts
# the @{ Label=...; Pattern=...; UseSimple=... } entries from all three files and fails with a
# diff on any drift, so the convention is enforced mechanically instead of by hope.
[CmdletBinding()]
param([switch]$VerboseOutput)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

$sources = [ordered]@{
    'run-ci-tests.ps1'      = Join-Path $repoRoot 'scripts/unity/run-ci-tests.ps1'
    'verify-unity-results'  = Join-Path $repoRoot '.github/actions/verify-unity-results/action.yml'
    'dump-unity-log-tail'   = Join-Path $repoRoot '.github/actions/dump-unity-log-tail/action.yml'
}

function Get-PatternEntries {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Catastrophic-pattern source not found: $Path"
    }

    # Each entry is a single line of the form:
    #   @{ Label = '...'; Pattern = '...'; UseSimple = $true }
    # Trimming normalizes the differing indentation across the three files so only the
    # entry content is compared.
    [string[]]$entries = Get-Content -LiteralPath $Path |
        Where-Object { $_ -match '@\{\s*Label\s*=.*Pattern\s*=.*UseSimple\s*=\s*\$(true|false)' } |
        ForEach-Object { $_.Trim() }

    return , $entries
}

[bool]$failed = $false
$entriesByName = [ordered]@{}

foreach ($name in $sources.Keys) {
    [string[]]$entries = Get-PatternEntries -Path $sources[$name]
    $entriesByName[$name] = $entries
    if ($VerboseOutput) {
        Write-Host "[$name] $($entries.Count) catastrophic pattern(s)"
    }
    if ($entries.Count -lt 1) {
        Write-Host "::error::No catastrophic-pattern entries found in $name ($($sources[$name]))."
        $failed = $true
    }
}

$referenceName = 'run-ci-tests.ps1'
[string[]]$reference = $entriesByName[$referenceName]

foreach ($name in $sources.Keys) {
    if ($name -eq $referenceName) {
        continue
    }

    [string[]]$current = $entriesByName[$name]

    [string[]]$missing = @($reference | Where-Object { $current -notcontains $_ })
    [string[]]$extra = @($current | Where-Object { $reference -notcontains $_ })

    if ($missing.Count -gt 0 -or $extra.Count -gt 0) {
        $failed = $true
        Write-Host "::error::Catastrophic-pattern drift between '$referenceName' and '$name'."
        foreach ($entry in $missing) {
            Write-Host "  MISSING from ${name}: $entry"
        }
        foreach ($entry in $extra) {
            Write-Host "  EXTRA in ${name} (not in $referenceName): $entry"
        }
    }
}

if ($failed) {
    Write-Host "::error::Catastrophic-pattern lists are out of sync. Update all three call sites identically."
    exit 1
}

Write-Host "Catastrophic-pattern lists are in sync across all $($sources.Count) call sites ($($reference.Count) patterns)."
exit 0
