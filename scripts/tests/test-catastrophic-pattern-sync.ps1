#!/usr/bin/env pwsh
# Contract test: the Unity catastrophic-pattern list has ONE source of truth --
# scripts/unity/lib/catastrophic-patterns.ps1 (Get-CatastrophicPatterns). Three call sites
# consume it: scripts/unity/run-ci-tests.ps1, .github/actions/verify-unity-results/action.yml,
# and .github/actions/dump-unity-log-tail/action.yml.
#
# Previously each site held a byte-identical inline copy "kept in sync by convention"; the
# convention failed (the `Package [id] cannot be found` entry drifted out of both action files)
# and the long Label strings tripped yamllint line-length (>200). The copies have been replaced
# by a shared dot-sourced function. This test now enforces the stronger invariant:
#   1. the shared source exists and Get-CatastrophicPatterns returns well-formed entries, and
#   2. NO consumer re-introduces an inline @{ Label=...; Pattern=...; UseSimple=... } array, and
#   3. every consumer actually loads the shared source (dot-source + Get-CatastrophicPatterns).
# So drift is structurally impossible, not merely discouraged.
[CmdletBinding()]
param([switch]$VerboseOutput)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

$sharedSource = Join-Path $repoRoot 'scripts/unity/lib/catastrophic-patterns.ps1'

# Consumers that MUST delegate to the shared source (and never inline their own array).
$consumers = [ordered]@{
    'run-ci-tests.ps1'     = Join-Path $repoRoot 'scripts/unity/run-ci-tests.ps1'
    'verify-unity-results' = Join-Path $repoRoot '.github/actions/verify-unity-results/action.yml'
    'dump-unity-log-tail'  = Join-Path $repoRoot '.github/actions/dump-unity-log-tail/action.yml'
}

[bool]$failed = $false

# --- 1. Shared source exists and produces well-formed entries -----------------------------
if (-not (Test-Path -LiteralPath $sharedSource)) {
    Write-Host "::error::Shared catastrophic-pattern source not found: $sharedSource"
    exit 1
}

. $sharedSource
if (-not (Get-Command -Name 'Get-CatastrophicPatterns' -ErrorAction SilentlyContinue)) {
    Write-Host "::error::$sharedSource does not define Get-CatastrophicPatterns."
    exit 1
}

[object[]]$patterns = @(Get-CatastrophicPatterns)
if ($patterns.Count -lt 1) {
    Write-Host "::error::Get-CatastrophicPatterns returned no entries."
    $failed = $true
}
foreach ($entry in $patterns) {
    foreach ($key in @('Label', 'Pattern', 'UseSimple')) {
        if (-not $entry.ContainsKey($key)) {
            Write-Host "::error::Catastrophic-pattern entry missing '$key': $($entry | Out-String)"
            $failed = $true
        }
    }
}
if ($VerboseOutput) {
    Write-Host "[shared] Get-CatastrophicPatterns -> $($patterns.Count) pattern(s)"
}

# --- 2 + 3. Each consumer delegates to the shared source, with no inline copy --------------
# An inline copy is an entry line of the form @{ Label=...; Pattern=...; UseSimple=$bool }.
$inlineEntryRegex = '@\{\s*Label\s*=.*Pattern\s*=.*UseSimple\s*=\s*\$(true|false)'

foreach ($name in $consumers.Keys) {
    $path = $consumers[$name]
    if (-not (Test-Path -LiteralPath $path)) {
        Write-Host "::error::Catastrophic-pattern consumer not found: $path"
        $failed = $true
        continue
    }

    [string[]]$lines = Get-Content -LiteralPath $path
    [string[]]$inlineHits = @($lines | Where-Object { $_ -match $inlineEntryRegex })
    if ($inlineHits.Count -gt 0) {
        $failed = $true
        Write-Host "::error::'$name' contains $($inlineHits.Count) inline catastrophic-pattern entr(y/ies). Use Get-CatastrophicPatterns from scripts/unity/lib/catastrophic-patterns.ps1 instead of an inline array."
        foreach ($hit in $inlineHits) {
            Write-Host "  INLINE: $($hit.Trim())"
        }
    }

    [bool]$callsShared = @($lines | Where-Object { $_ -match 'Get-CatastrophicPatterns' }).Count -gt 0
    if (-not $callsShared) {
        $failed = $true
        Write-Host "::error::'$name' does not call Get-CatastrophicPatterns; it must load the shared catastrophic-pattern source."
    }

    if ($VerboseOutput) {
        Write-Host "[$name] inline=$($inlineHits.Count) callsShared=$callsShared"
    }
}

if ($failed) {
    Write-Host "::error::Catastrophic-pattern single-sourcing contract violated. See errors above."
    exit 1
}

Write-Host "Catastrophic-pattern single source OK: $($patterns.Count) patterns, $($consumers.Count) consumers delegate to scripts/unity/lib/catastrophic-patterns.ps1."
exit 0
