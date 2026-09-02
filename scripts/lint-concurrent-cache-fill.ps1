#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Fails when a cache is filled non-atomically, or through a factory that allocates every call.

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
    replace an inferred answer, or a working factory that must replace one already cached and known
    to fail. Mark those with

        // concurrent-overwrite: <why this write must win>

    on the write line, or anywhere in the contiguous comment block immediately above it. The whole
    block is searched rather than one line, because a reason worth writing rarely fits on one and
    the marker should not dictate where the sentence breaks.

    Writes inside a `#if SINGLE_THREADED` branch are skipped: under that define the field is a
    plain Dictionary and the indexer is the only way to fill it. A sweep that does not track
    preprocessor state reports this problem four times bigger than it is -- 53 of the 71 raw hits
    when this was first swept were exactly that false positive.

    The second rule: a lambda passed to GetOrAdd / AddOrUpdate / GetOrCreateValue / GetValue must be
    `static`. A lambda that captures compiles to a display-class allocation plus a delegate
    allocation on **every call**, cache hit included; a non-capturing one is cached by the compiler
    in a static field and allocates once. Marking them `static` does not make them cheaper -- it
    makes the compiler **reject** the capture (CS8820), so the cheap case is the only one that
    compiles. Session 217 swept the tree this way and the compiler found five capturing factories in
    Runtime and two in Editor that nothing else would have reported.

    Not covered, because it is not lexically decidable: a method-group argument
    (`GetOrAdd(key, CreateThing)`) is indistinguishable from a local holding a delegate, and C# 9
    does not cache method-group conversions -- so that also allocates per call. Three such sites were
    found by hand in Serializer.cs and replaced with cached `static readonly` fields. If a fourth
    appears, only review will catch it.

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
$factoryCalls = @('GetOrAdd', 'AddOrUpdate', 'GetOrCreateValue', 'GetValue')

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

            # The write line plus the contiguous comment above it, in EITHER form. One line of
            # lookback would force a multi-line reason to break wherever puts the marker last, and
            # reading only `//` made the marker invisible the moment the #635 sweep rewrote a
            # two-line reason as a block -- which is exactly what happened to the two exemptions in
            # SerializableDictionaryPropertyDrawer.
            #
            # Only a line that is ENTIRELY a comment continues the walk. Accepting any line that
            # merely ENDS with `*/` was measured letting a trailing `foo(); /* trace */` open a
            # block the walk never closed, so the lookback ran to the top of the file and found a
            # marker belonging to a different method -- excusing a racy fill the linter used to
            # catch. A block's closing line in this repository's own form is `*/` on its own.
            $context = $line
            $insideBlockComment = $false
            for ($back = $i - 1; $back -ge 0; $back--) {
                $above = $lines[$back].Trim()
                if ($insideBlockComment) {
                    $context = $above + "`n" + $context
                    if ($above.StartsWith('/*')) { $insideBlockComment = $false }
                    continue
                }
                if ($above.StartsWith('/*') -and $above.EndsWith('*/')) {
                    $context = $above + "`n" + $context
                    continue
                }
                if ($above.StartsWith('*/')) {
                    $context = $above + "`n" + $context
                    $insideBlockComment = $true
                    continue
                }
                if (-not $above.StartsWith('//')) { break }
                $context = $above + "`n" + $context
            }
            if ($context -like "*$exemptionMarker*") {
                $exempted++
                continue
            }

            Write-Host "::error file=$relative,line=$($i + 1)::'$name' is a ConcurrentDictionary filled through its indexer. Two callers racing a first use each build a value and each returns one the cache does not hold. Use GetOrAdd (state-taking overload, static lambda) or TryAdd for an already-computed value. A deliberate overwrite must say why with a '$exemptionMarker <reason>' comment, in either the '//' or the '/* */' form."
            $failed = $true
        }
    }
}

# Rule two: every lambda handed to a cache factory must be `static`, so a capture is a compile
# error rather than a per-call allocation nobody measures.
$lambdaChecked = 0
$callPattern = '\.\s*(?:' + ($factoryCalls -join '|') + ')\s*(?:<[^;{}()]*>)?\s*\('
foreach ($file in @($files | Sort-Object)) {
    $text = [System.IO.File]::ReadAllText($file)
    if ($text -notmatch $callPattern) { continue }

    $relative = $file.Substring($repoRoot.Length).TrimStart('\', '/').Replace('\', '/')
    foreach ($call in [regex]::Matches($text, $callPattern)) {
        $open = $text.IndexOf('(', $call.Index + $call.Length - 1)
        if ($open -lt 0) { continue }

        # Walk the balanced argument list, then test each top-level argument.
        $depth = 0
        $close = -1
        for ($i = $open; $i -lt $text.Length; $i++) {
            $c = $text[$i]
            if ($c -eq '(') { $depth++ }
            elseif ($c -eq ')') {
                $depth--
                if ($depth -eq 0) { $close = $i; break }
            }
        }
        if ($close -lt 0) { continue }

        # Every `=>` at paren-depth 0 inside the argument list is a top-level lambda argument.
        # Keying off the arrow rather than splitting on commas sidesteps generic argument lists --
        # `WallstopGenericPool<Dictionary<TKey, TValue>>` carries a comma that no comma-splitter
        # ignoring angle brackets can tell from an argument separator, and an earlier draft of this
        # rule reported four false positives in Buffers.cs for exactly that reason. Depth also
        # excludes a nested lambda such as `onRelease: set => set.Clear()`, which is an argument to
        # the value being constructed, not to the cache.
        $depth = 0
        for ($i = $open + 1; $i -lt $close; $i++) {
            $c = $text[$i]
            if ($c -eq '(' -or $c -eq '[' -or $c -eq '{') { $depth++; continue }
            if ($c -eq ')' -or $c -eq ']' -or $c -eq '}') { $depth--; continue }
            if ($depth -ne 0) { continue }
            if ($c -ne '=' -or $i + 1 -ge $close -or $text[$i + 1] -ne '>') { continue }

            $lambdaChecked++

            # Walk back over the parameter list: either a balanced `(...)` or a bare identifier.
            $j = $i - 1
            while ($j -ge 0 -and [char]::IsWhiteSpace($text[$j])) { $j-- }
            if ($j -ge 0 -and $text[$j] -eq ')') {
                $paramDepth = 0
                while ($j -ge 0) {
                    if ($text[$j] -eq ')') { $paramDepth++ }
                    elseif ($text[$j] -eq '(') {
                        $paramDepth--
                        if ($paramDepth -eq 0) { break }
                    }
                    $j--
                }
                $j--
            } else {
                while ($j -ge 0 -and ($text[$j] -match '[\w]')) { $j-- }
            }
            while ($j -ge 0 -and [char]::IsWhiteSpace($text[$j])) { $j-- }

            if ($j -ge 5 -and $text.Substring($j - 5, 6) -eq 'static') { continue }

            $line = ($text.Substring(0, $i) -split "`n").Count
            Write-Host "::error file=$relative,line=$line::A lambda handed to a cache factory must be 'static'. Without it a capture compiles to a display-class plus a delegate allocated on every call, cache hit included, and nothing reports it. Add 'static'; if the compiler then rejects it (CS8820) the capture was real -- pass the captured value through the state-taking overload instead."
            $failed = $true
        }
    }
}

Write-Info "Scanned $scanned file(s) declaring a ConcurrentDictionary; skipped $skippedSingleThreaded write(s) inside SINGLE_THREADED branches; $exempted deliberate overwrite(s) exempted; checked $lambdaChecked cache-factory lambda(s)."

if ($failed) {
    exit 1
}

Write-Host "[lint-concurrent-cache-fill] OK: every ConcurrentDictionary fill is atomic and every cache-factory lambda is static ($scanned file(s) scanned, $exempted exempted, $lambdaChecked lambda(s) checked)." -ForegroundColor Green
