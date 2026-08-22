#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Fails when a ConcurrentDictionary field is written through its indexer.

.DESCRIPTION
    The shape this catches is a cache filled after a miss:

        if (!Cache.TryGetValue(key, out Value cached))
        {
            cached = Build(key);
            Cache[key] = cached;   // <-- not atomic
        }

    Two callers racing a first use each run Build, each stores, and each returns a different
    instance than the one that ends up cached. Where Build has a side effect -- a probe, a log, a
    registration -- that side effect happens twice; where callers compare instances by reference,
    they disagree. GetOrAdd (with the state-taking overload where the factory needs an argument,
    so the lambda stays static and no closure allocates) stores exactly one winner and hands it to
    every caller. TryAdd is the equivalent when the value is already computed and constant.

    A deliberate last-writer-wins overwrite is legitimate -- an explicit registration that must
    replace an inferred answer, for instance. Mark those with a trailing or preceding

        // concurrent-overwrite: <why this write must win>

    which documents the intent at the site and exempts the line here.

    Writes inside a `#if SINGLE_THREADED` branch are skipped: under that define the field is a
    plain Dictionary and the indexer is the only way to fill it. A sweep that does not track
    preprocessor state reports this problem four times bigger than it is -- 53 of the 71 raw hits
    when this was first swept were exactly that false positive.

.NOTES
    Source-based on purpose: it has to run on a plain ubuntu-latest with no Unity and no compiled
    assembly, and the shape is lexical.
#>
[CmdletBinding()]
param([switch]$VerboseOutput)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$scanRoots = @('Runtime', 'Editor', 'Styles')
$exemptionMarker = 'concurrent-overwrite:'

function Write-Info($message) {
    if ($VerboseOutput) { Write-Host "[lint-concurrent-cache-fill] $message" -ForegroundColor Cyan }
}

# Names of fields/locals whose declared type is ConcurrentDictionary. The generic argument list is
# matched across newlines because csharpier wraps long declarations onto several lines, and a
# single-line regex silently drops exactly the widest caches.
function Get-ConcurrentDictionaryNames {
    param([Parameter(Mandatory = $true)][string]$Text)

    $names = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $pattern = 'ConcurrentDictionary\s*<(?:[^<>]|<(?:[^<>]|<[^<>]*>)*>)*>\s+(\w+)'
    foreach ($match in [regex]::Matches($Text, $pattern, 'Singleline')) {
        [void]$names.Add($match.Groups[1].Value)
    }
    return , $names
}

# True while the current line sits inside a branch that is compiled when SINGLE_THREADED is
# defined. Tracks #if/#elif/#else/#endif nesting; $null means "this conditional says nothing about
# SINGLE_THREADED", which #else must preserve rather than invert.
function Test-SingleThreadedBranch {
    param([Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.ArrayList]$Stack)

    foreach ($entry in $Stack) {
        if ($entry -eq $true) { return $true }
    }
    return $false
}

function Get-DirectiveState {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Condition)

    if ($Condition -notmatch 'SINGLE_THREADED') { return $null }
    return ($Condition.Trim() -notlike '!*')
}

$files = @()
foreach ($scanRoot in $scanRoots) {
    $rootPath = Join-Path $repoRoot $scanRoot
    if (-not (Test-Path -LiteralPath $rootPath)) { continue }
    $files += @(Get-ChildItem -LiteralPath $rootPath -Recurse -File -Filter '*.cs' | ForEach-Object { $_.FullName })
}

$scanned = 0
$exempted = 0
$skippedSingleThreaded = 0
$failed = $false

foreach ($file in @($files | Sort-Object)) {
    $text = [System.IO.File]::ReadAllText($file)
    if ($text -notmatch 'ConcurrentDictionary') { continue }

    $names = Get-ConcurrentDictionaryNames -Text $text
    if ($names.Count -eq 0) { continue }
    $scanned++

    $relative = $file.Substring($repoRoot.Length).TrimStart('\', '/').Replace('\', '/')
    [string[]]$lines = [System.IO.File]::ReadAllLines($file)
    $stack = [System.Collections.ArrayList]::new()

    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]

        if ($line -match '^\s*#\s*(if|elif|else|endif)\b(.*)$') {
            $directive = $Matches[1]
            $condition = $Matches[2]
            switch ($directive) {
                'if' { [void]$stack.Add((Get-DirectiveState -Condition $condition)) }
                'elif' { if ($stack.Count -gt 0) { $stack[$stack.Count - 1] = Get-DirectiveState -Condition $condition } }
                'else' {
                    if ($stack.Count -gt 0) {
                        $current = $stack[$stack.Count - 1]
                        $stack[$stack.Count - 1] = if ($null -eq $current) { $null } else { -not $current }
                    }
                }
                'endif' { if ($stack.Count -gt 0) { $stack.RemoveAt($stack.Count - 1) } }
            }
            continue
        }

        foreach ($name in $names) {
            if ($line -notmatch ("(?<![\w.])" + [regex]::Escape($name) + "\s*\[[^\]]*\]\s*=(?!=)")) { continue }

            if (Test-SingleThreadedBranch -Stack $stack) {
                $skippedSingleThreaded++
                continue
            }

            $context = $line
            if ($i -gt 0) { $context = $lines[$i - 1] + "`n" + $context }
            if ($context -like "*$exemptionMarker*") {
                $exempted++
                continue
            }

            Write-Host "::error file=$relative,line=$($i + 1)::'$name' is a ConcurrentDictionary filled through its indexer. Two callers racing a first use each build a value and each returns one the cache does not hold. Use GetOrAdd (state-taking overload, static lambda) or TryAdd for an already-computed value. A deliberate overwrite must say why with a '// $exemptionMarker <reason>' comment."
            $failed = $true
        }
    }
}

Write-Info "Scanned $scanned file(s) declaring a ConcurrentDictionary; skipped $skippedSingleThreaded write(s) inside SINGLE_THREADED branches; $exempted deliberate overwrite(s) exempted."

if ($failed) {
    exit 1
}

Write-Host "[lint-concurrent-cache-fill] OK: every ConcurrentDictionary fill is atomic ($scanned file(s) scanned, $exempted exempted)." -ForegroundColor Green
