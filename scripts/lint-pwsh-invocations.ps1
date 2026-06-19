#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Detect anti-patterns in bash -> PowerShell (.ps1) script invocations.

.DESCRIPTION
    PowerShell's `-File` CLI mode does NOT honor the POSIX `--` argument
    separator. Passing `--` as an argument surfaces as:

      "Parameter cannot be processed because the parameter name ''
       is ambiguous."

    This lint scans the repo for invocation anti-patterns so we catch the
    mistake at commit / CI time rather than during a rare hook branch.

    Error codes emitted:
      PWS001 - `pwsh|powershell[.exe] -File|-f <script> --` (the core bug)
      PWS002 - In-process `& <script>.ps1 --` inside scripts/tests/*.ps1
               (tests MUST exercise the same invocation path production uses;
               the in-process call operator masks CLI-binding bugs)
      PWS003 - A scripts/<name>.ps1 file invokes `pwsh|powershell[.exe]
               -NoProfile -File|-f scripts/<sibling>.ps1` via subprocess when it already
               runs inside a PowerShell host. Windows PowerShell 5.1 hosts
               may not have pwsh on PATH, and the subprocess boundary wastes
               startup time; dot-source a shared helper or use in-process
               `&` with a function that does not call `exit` instead.
               Opt-out per file: add a top-of-file comment marker
               `# lint-pwsh-invocations: allow-subprocess-pwsh` with a
               one-line rationale (e.g. "called script uses `exit` heavily;
               subprocess isolation required").
      PWS004 - `pwsh|powershell[.exe] -File|-f .githooks/<extensionless-hook>`.
               PowerShell -File targets must be .ps1 files on every supported
               host; run the extensionless hook directly or invoke the
               companion `.githooks/<hook>.ps1` implementation.

    Scanned paths:
      - *.sh
      - .githooks/*
      - .github/workflows/*.yml
      - scripts/**/*.ps1             (PWS003 only applies to top-level scripts/*.ps1)
      - package.json

    Multi-line invocation detection:
      Bash / YAML `run: |` blocks may split a `pwsh ... -File ... -- ...`
      invocation across physical lines using `\` continuations. We first scan
      each physical line, then compute a "logically joined" view — any line
      ending in a trailing `\` (ignoring trailing whitespace) is joined with
      the next line — and scan that view too. Violations found only on the
      joined view report the physical line number where the invocation STARTS.

    Excluded:
      - Lines inside PowerShell comment-based help blocks (open/close markers).
      - Lines beginning with a '#' comment character in .ps1, .sh, .yml, .yaml
        files. (Caveat: `#` inside quoted YAML strings is treated as a
        comment start; we accept this minor edge case because scanning for
        invocations inside a quoted YAML string is not a pattern we care
        about.)
      - This script itself (scripts/lint-pwsh-invocations.ps1) and the
        corresponding test script, which use the anti-pattern as fixture text.

.PARAMETER VerboseOutput
    Show per-file diagnostics including files that were scanned with no
    violations.

.EXAMPLE
    ./scripts/lint-pwsh-invocations.ps1
    Lint the whole repo.

.EXAMPLE
    ./scripts/lint-pwsh-invocations.ps1 -VerboseOutput
    Lint with verbose per-file output.
#>
param(
    [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Info($msg) {
    if ($VerboseOutput) { Write-Host "[lint-pwsh-invocations] $msg" -ForegroundColor Cyan }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$selfRel = 'scripts/lint-pwsh-invocations.ps1'
$selfTestRel = 'scripts/tests/test-lint-pwsh-invocations.ps1'

$pwshExecutablePattern = 'pwsh(?:\.exe)?|powershell(?:\.exe)?'
$pwshFileSwitchPattern = '-(?:File|f)\b'
$doubleDashTokenPattern = '(?:--|\\*"--\\*"|''--'')'

# PWS001: `pwsh|powershell[.exe] ... -File|-f <script> ... --` (end of token, or followed by whitespace).
# The intermediate-args groups before AND after `-File` accept ANY
# whitespace-separated tokens (flags, positionals, or quoted values). This is
# pragmatic — we want the regex to tolerate real-world invocations like
# `pwsh -NoProfile -File foo.ps1 positional -- arg` and `pwsh -File "path with spaces.ps1" "--" arg`.
# The script-path token accepts either a double-quoted string (possibly
# containing spaces), a single-quoted string, or a bare token.
$pws001Pattern = '(?:^|[\s;&|"''`(])(' + $pwshExecutablePattern + ')\b(?:\s+\S+)*?\s+' + $pwshFileSwitchPattern + '\s+(?:"[^"]+"|''[^'']+''|\S+)(?:\s+\S+)*?\s+' + $doubleDashTokenPattern + '(?=\s|$|")'
# PWS001-variant: array-indirection pwsh invocation. Catches the common
# bash pattern where the pwsh command line is stored in a PowerShell-named
# array and expanded with "${PWSH_CMD[@]}" or "${POWERSHELL_CMD[@]}". Example:
#   PWSH_CMD=(pwsh -NoProfile -File)
#   "${PWSH_CMD[@]}" foo.ps1 -- arg     # BUG — still hits PowerShell -File mode
# We match: a `"${NAME[@]}"` expansion followed later by a `.ps1` token and
# eventually a standalone `--`.
$pws001ArrayNamePattern = '[A-Z0-9_]*(?:PWSH|POWERSHELL)[A-Z0-9_]*'
$pws001ArrayPattern = '(\\*"\$\{(?:' + $pws001ArrayNamePattern + ')\[@\]\}\\*")\s+(?:\S+\s+)*?\S+\.ps1(?:\s+\S+)*?\s+' + $doubleDashTokenPattern + '(?=\s|$|")'
# PWS002: in-process `& <something> -- ...` inside test scripts. The `<something>`
# is either a literal *.ps1 path (quoted or unquoted) or a variable whose name
# ends with "Path" / "Script" or is obviously a script reference. We match the
# narrower common forms deliberately — `&` also appears in many legitimate
# contexts (Start-Job &, logical AND, etc.) so we over-index on call-style
# invocations that take `--` as the first argument.
$pws002Pattern = '(&)\s+(?:\([^)]*\.ps1[^)]*\)|\$[A-Za-z_][A-Za-z0-9_]*(?:Path|Script|Ps1|Cmd|Tool)?|["''][^"'']*\.ps1["'']|[^\s"'']+\.ps1)(?:\s+\S+)*?\s+' + $doubleDashTokenPattern + '(?=\s|$|")'

# PWS003: a scripts/<name>.ps1 file that invokes pwsh|powershell[.exe] -NoProfile
# -File|-f scripts/<sibling>.ps1 via subprocess. On Windows PowerShell 5.1 hosts
# this is a hard fail (no pwsh on PATH); even where it works, the subprocess
# boundary wastes startup time and drops the parent session's variables.
# Preferred alternatives: dot-source a shared helper module, or invoke an
# in-process function with `&` (when the callee is refactored to not call
# `exit`).
#
# Regex shape:
#   - Optional leading `&` call operator or line-start whitespace.
#   - pwsh OR powershell, with optional .exe, as a word.
#   - Any combination of intervening flags (greedy-nonconsuming).
#   - `-File` or `-f` followed by a script path whose first segment is `scripts/` OR
#     the fragment `$PSScriptRoot` (the canonical PS idiom for "this
#     script's directory" — which IS scripts/ when the caller IS
#     scripts/<name>.ps1).
#
# Double-quoted and single-quoted paths are both accepted. Bare tokens too.
# Anchors preceding the pwsh/powershell token. We deliberately REFUSE to match
# when the token sits inside a single or double-quoted string literal because
# Write-Host "... pwsh -NoProfile -File scripts/foo.ps1 ..." is help text, not
# an invocation. Accepted prefixes: start-of-line, whitespace, `&` call operator,
# `;` statement separator, `|` pipe, or `(` grouping. Explicitly NOT accepted:
# `"` or `'` (inside string literal).
#
# Additional guard: `$generateScript = Join-Path ... 'generate-doc-metadata.ps1'`
# style assignments where a `.ps1` path shows up in a quoted STRING but no
# `pwsh|powershell -File` precedes it — already excluded because we anchor on
# `pwsh|powershell`.
$pws003Pattern = '(?:^|[\s;&|`(])(' + $pwshExecutablePattern + ')\b(?:\s+-[A-Za-z][A-Za-z0-9]*(?:\s+\S+)?)*?\s+' + $pwshFileSwitchPattern + '\s+(?:"(?:[^"]*?[/\\])?(?:\$PSScriptRoot|scripts)[/\\][^"]+\.ps1"|''(?:[^'']*?[/\\])?(?:\$PSScriptRoot|scripts)[/\\][^'']+\.ps1''|(?:\S*[/\\])?(?:\$PSScriptRoot|scripts)[/\\]\S+\.ps1|\$\w+)'

# PWS004: direct PowerShell -File invocation of extensionless git hook
# entrypoints. Git hooks must be named without extensions, but PowerShell
# -File is not a portable launcher for those extensionless files. Use the hook
# executable directly, or use the .ps1 implementation path when debugging.
$hookEntryNames = 'pre-commit|pre-push|pre-merge-commit|post-rewrite'
$pathSeparatorPattern = '[/\\]+'
$escapedDoubleQuotePattern = '\\*"'
$pws004HookPathPattern = '(?:' + $escapedDoubleQuotePattern + '(?:[^"]*' + $pathSeparatorPattern + ')?\.githooks' + $pathSeparatorPattern + '(?:' + $hookEntryNames + ')' + $escapedDoubleQuotePattern + '|''(?:[^'']*' + $pathSeparatorPattern + ')?\.githooks' + $pathSeparatorPattern + '(?:' + $hookEntryNames + ')''|(?:\S*' + $pathSeparatorPattern + ')?\.githooks' + $pathSeparatorPattern + '(?:' + $hookEntryNames + '))'
$pws004HookTargetPattern = $pws004HookPathPattern + '(?=$|[\s;&,"''`)])'
$pws004Pattern = '(?:^|[\s;&|"''`(])(' + $pwshExecutablePattern + ')\b(?:\s+\S+)*?\s+' + $pwshFileSwitchPattern + '\s+' + $pws004HookTargetPattern
# PWS004-variant: bash array-indirection `pwsh -File` invocation targeting an
# extensionless hook. This mirrors the PWS001 array guard: if a PowerShell-named
# array is expanded as the command, do not let `.githooks/<hook>` pass as a
# positional target just because the actual `-File` switch lives in the array.
$pws004ArrayPattern = '(\\*"\$\{(?:' + $pws001ArrayNamePattern + ')\[@\]\}\\*")\s+(?:\S+\s+)*?' + $pws004HookTargetPattern
$pws004VariableAssignmentPattern = '(?:^|[;\s])(?:\[[^\]]+\]\s*)?\$(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*.*' + $pws004HookTargetPattern
$pws004JoinPathVariableAssignmentPattern = '(?:^|[;\s])(?:\[[^\]]+\]\s*)?\$(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*.*\bJoin-Path\b.*(?:["'']\.githooks["'']|["''][^"'']*[/\\]\.githooks["'']).*["''](?:' + $hookEntryNames + ')["''](?=$|[\s;&,)])'

# PWS003 opt-out marker: a single-line comment at the top of a scripts/<name>.ps1
# file that explicitly acknowledges the subprocess boundary. Must appear on its
# own line in the first 40 lines of the file. Rationale is required (callers
# should explain WHY subprocess isolation is needed — e.g. the called script
# uses `exit` heavily, or it must run in a fresh PS session).
$pws003AllowMarker = '^\s*#\s*lint-pwsh-invocations:\s*allow-subprocess-pwsh\s+\S+'

# Strips PowerShell string literals (double-quoted and single-quoted) from a
# line, replacing each literal with a same-length sequence of spaces so that
# column offsets of surrounding code are preserved. Used to suppress false
# positives where the pwsh/powershell token appears INSIDE a string (e.g.
# `Write-Host "  pwsh -NoProfile -File scripts/foo.ps1"`).
#
# Caveats:
#   - Does NOT attempt to parse here-strings (@" ... "@ / @' ... '@). Those
#     lines are skipped separately by Get-PowerShellHereStringMap.
#   - Does NOT model PowerShell escape semantics (``"` inside `"..."`) since
#     we simply want to mask the visible text. A mismatched quote on a line
#     leaves the tail unmasked; acceptable for our lint purposes.
function Hide-PowerShellStringLiterals {
    param([string]$Line)

    if ([string]::IsNullOrEmpty($Line)) {
        return $Line
    }

    $chars = $Line.ToCharArray()
    $n = $chars.Length
    $inDouble = $false
    $inSingle = $false
    for ($ci = 0; $ci -lt $n; $ci++) {
        $c = $chars[$ci]
        if ($inDouble) {
            if ($c -eq '"') {
                $inDouble = $false
                # The closing quote itself is not string content — leave it.
                continue
            }
            $chars[$ci] = ' '
            continue
        }
        if ($inSingle) {
            if ($c -eq "'") {
                $inSingle = $false
                continue
            }
            $chars[$ci] = ' '
            continue
        }
        if ($c -eq '"') { $inDouble = $true; continue }
        if ($c -eq "'") { $inSingle = $true; continue }
    }
    return -join $chars
}

function Test-IsIndexInsidePowerShellStringLiteral {
    param(
        [string]$Line,
        [int]$Index
    )

    if ([string]::IsNullOrEmpty($Line) -or $Index -le 0) {
        return $false
    }

    $chars = $Line.ToCharArray()
    $limit = [Math]::Min($Index, $chars.Length)
    $inDouble = $false
    $inSingle = $false
    for ($ci = 0; $ci -lt $limit; $ci++) {
        $c = $chars[$ci]
        if ($inDouble) {
            if ($c -eq '"') {
                $inDouble = $false
            }
            continue
        }
        if ($inSingle) {
            if ($c -eq "'") {
                $inSingle = $false
            }
            continue
        }
        if ($c -eq '"') {
            $inDouble = $true
            continue
        }
        if ($c -eq "'") {
            $inSingle = $true
        }
    }

    return ($inDouble -or $inSingle)
}

function Remove-PowerShellInlineComment {
    param([string]$Line)

    if ([string]::IsNullOrEmpty($Line)) {
        return $Line
    }

    $chars = $Line.ToCharArray()
    $inDouble = $false
    $inSingle = $false
    for ($ci = 0; $ci -lt $chars.Length; $ci++) {
        $c = $chars[$ci]
        if ($inDouble) {
            if ($c -eq '"') {
                $inDouble = $false
            }
            continue
        }
        if ($inSingle) {
            if ($c -eq "'") {
                $inSingle = $false
            }
            continue
        }
        if ($c -eq '"') {
            $inDouble = $true
            continue
        }
        if ($c -eq "'") {
            $inSingle = $true
            continue
        }
        if ($c -eq '#') {
            return $Line.Substring(0, $ci)
        }
    }

    return $Line
}

function Test-InvocationPattern {
    param(
        [string]$Line,
        [string]$Pattern,
        [bool]$IsPowerShell,
        [bool]$SuppressShellHelpText
    )

    if ([string]::IsNullOrEmpty($Line)) {
        return $false
    }

    $matches = [System.Text.RegularExpressions.Regex]::Matches(
        $Line,
        $Pattern,
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
    )
    foreach ($match in $matches) {
        $invocationGroup = $match.Groups[1]
        if (-not $invocationGroup.Success) {
            continue
        }
        if (-not $IsPowerShell) {
            $relativeFromInvocation = $Line.Substring($invocationGroup.Index)
            $fileSwitchMatch = [System.Text.RegularExpressions.Regex]::Match(
                $relativeFromInvocation,
                '\s-(?:File|f)\b',
                [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
            )
            if ($fileSwitchMatch.Success) {
                $fileSwitchIndex = $invocationGroup.Index + $fileSwitchMatch.Index
                $beforeFileSwitch = $Line.Substring($invocationGroup.Index, $fileSwitchIndex - $invocationGroup.Index)
                if (
                    $beforeFileSwitch -match '\s-(?:Command|c)(?=\s|$)' -and
                    (Test-IsIndexInsidePowerShellStringLiteral -Line $Line -Index $fileSwitchIndex) -and
                    $beforeFileSwitch -match '\s-(?:Command|c)\s+\\?["'']?\s*(?:Write-(?:Host|Output|Warning|Error|Verbose|Information)|echo|printf)\b' -and
                    $beforeFileSwitch -notmatch ';'
                ) {
                    continue
                }
            }
            $insideStringLiteral = Test-IsIndexInsidePowerShellStringLiteral -Line $Line -Index $invocationGroup.Index
            if ($insideStringLiteral) {
                $beforeInvocation = $Line.Substring(0, $invocationGroup.Index)
                if ($beforeInvocation -match '\s-(?:Command|c)(?=\s|$)') {
                    continue
                }
            }
            if (
                $SuppressShellHelpText -and
                $insideStringLiteral -and
                ($Line -match '^\s*(?:-\s+)?(?:run\s*:\s*)?(?:echo|printf)\b')
            ) {
                continue
            }
            return $true
        }
        if (-not (Test-IsIndexInsidePowerShellStringLiteral -Line $Line -Index $invocationGroup.Index)) {
            return $true
        }
    }

    return $false
}

function Get-RepoRelativePath {
    param([string]$FullPath)
    $normalized = $FullPath.Replace('\', '/')
    $root = $repoRoot.Replace('\', '/')
    if ($normalized.StartsWith($root + '/')) {
        return $normalized.Substring($root.Length + 1)
    }
    return $normalized
}

function Get-TargetFiles {
    $results = [System.Collections.Generic.List[string]]::new()

    # *.sh (recursive, but skip node_modules / site / .git)
    Get-ChildItem -Path $repoRoot -Recurse -File -Filter '*.sh' -ErrorAction SilentlyContinue |
        Where-Object {
            $rel = Get-RepoRelativePath $_.FullName
            $rel -notmatch '^(node_modules|site|\.git)/'
        } |
        ForEach-Object { $results.Add($_.FullName) | Out-Null }

    # .githooks/* (non-recursive files)
    $hooksDir = Join-Path $repoRoot '.githooks'
    if (Test-Path $hooksDir) {
        Get-ChildItem -Path $hooksDir -File -ErrorAction SilentlyContinue |
            ForEach-Object { $results.Add($_.FullName) | Out-Null }
    }

    # .github/workflows/*.yml
    $wfDir = Join-Path (Join-Path $repoRoot '.github') 'workflows'
    if (Test-Path $wfDir) {
        Get-ChildItem -Path $wfDir -File -Filter '*.yml' -ErrorAction SilentlyContinue |
            ForEach-Object { $results.Add($_.FullName) | Out-Null }
        Get-ChildItem -Path $wfDir -File -Filter '*.yaml' -ErrorAction SilentlyContinue |
            ForEach-Object { $results.Add($_.FullName) | Out-Null }
    }

    # scripts/**/*.ps1. PWS003 remains scoped below to top-level scripts/*.ps1,
    # but the other pwsh invocation checks should cover nested automation too.
    $scriptsDir = Join-Path $repoRoot 'scripts'
    if (Test-Path $scriptsDir) {
        Get-ChildItem -Path $scriptsDir -Recurse -File -Filter '*.ps1' -ErrorAction SilentlyContinue |
            ForEach-Object { $results.Add($_.FullName) | Out-Null }
    }

    # package.json
    $pkgJson = Join-Path $repoRoot 'package.json'
    if (Test-Path $pkgJson) { $results.Add($pkgJson) | Out-Null }

    return $results | Sort-Object -Unique
}

# Returns $true if the given line (inside a .ps1 file) is part of a comment-based
# help block. We track `<# ... #>` state across the whole file and also skip
# lines that contain a `.EXAMPLE`, `.SYNOPSIS`, `.DESCRIPTION`, etc. directive
# marker or the line immediately after one (heuristic, since CBH content lives
# inside the `<# ... #>` wrapper anyway — this is a second-level safety net for
# inline documentation).
#
# Why we keep a coarse per-line boolean instead of migrating to
# scripts/comment-stripping.ps1 (Get-CommentMaskedLines / Get-CommentRanges):
# this linter does PER-LINE regex scans across MIXED file types — bash with
# `\` continuations, YAML `run: >` folded block scalars, package.json, and
# .ps1 — each with bespoke join/folding semantics that comment-stripping
# does not model (heredocs, folded scalars, line-continuation joining are
# lexed line-by-line here). The byte-accurate column preservation that
# comment-stripping offers is unused by this linter (we report whole-line
# matches, not column ranges). Migrating would require porting every join
# pass to operate on a masked-text view AND reproducing or replacing the
# bespoke continuation semantics. The 36+ existing regression tests cover
# the present coarse map, so this stays line-based by design.
function Get-CommentBlockMap {
    param([string[]]$Lines)

    $map = New-Object bool[] $Lines.Length
    $inBlock = $false
    for ($i = 0; $i -lt $Lines.Length; $i++) {
        $line = $Lines[$i]
        if ($inBlock) {
            $map[$i] = $true
            if ($line -match '#>') {
                $inBlock = $false
            }
            continue
        }
        if ($line -match '<#') {
            $map[$i] = $true
            if (-not ($line -match '#>')) {
                $inBlock = $true
            }
            continue
        }
        # Full-line comment beginning with `#`.
        if ($line -match '^\s*#') {
            $map[$i] = $true
        }
    }
    return , $map
}

function Get-PowerShellHereStringMap {
    param([string[]]$Lines)

    $map = New-Object bool[] $Lines.Length
    $inHereString = $false
    $terminatorPattern = $null
    for ($i = 0; $i -lt $Lines.Length; $i++) {
        $line = $Lines[$i]
        if ($inHereString) {
            $map[$i] = $true
            if ($line -match $terminatorPattern) {
                $inHereString = $false
                $terminatorPattern = $null
            }
            continue
        }

        if ($line -match '@"\s*$') {
            $map[$i] = $true
            $inHereString = $true
            $terminatorPattern = '^\s*"@'
            continue
        }

        if ($line -match "@'\s*$") {
            $map[$i] = $true
            $inHereString = $true
            $terminatorPattern = "^\s*'@"
        }
    }
    return , $map
}

$targets = @(Get-TargetFiles)
Write-Info "Scanning $($targets.Count) file(s)"

$violations = [System.Collections.Generic.List[object]]::new()

foreach ($file in $targets) {
    $rel = Get-RepoRelativePath $file

    # Exclusions: this script itself, and its own test (test fixtures live in
    # tempdirs — the test script text intentionally contains the bad pattern as
    # a STRING to build fixtures, but never as an actual invocation).
    if ($rel -eq $selfRel) { continue }
    if ($rel -eq $selfTestRel) { continue }

    $lines = @()
    try {
        # Coerce to array so empty files (returns $null) don't crash under
        # StrictMode when we access .Length, and single-line-no-trailing-newline
        # files (returns a scalar String) don't cause the loop to iterate over
        # characters instead of lines (silently missing violations).
        $lines = @(Get-Content -LiteralPath $file -ErrorAction Stop)
    } catch {
        Write-Info "Skipping unreadable file: $rel"
        continue
    }

    $isPs1 = $file.EndsWith('.ps1', [System.StringComparison]::OrdinalIgnoreCase)
    $isSh = $file.EndsWith('.sh', [System.StringComparison]::OrdinalIgnoreCase)
    $isYaml = $file.EndsWith('.yml', [System.StringComparison]::OrdinalIgnoreCase) `
        -or $file.EndsWith('.yaml', [System.StringComparison]::OrdinalIgnoreCase)
    $isShellLike = $isSh -or ($rel -like '.githooks/*' -and -not $isPs1)
    $commentMap = $null
    $hereStringMap = $null
    if ($isPs1) {
        $commentMap = Get-CommentBlockMap -Lines $lines
        $hereStringMap = Get-PowerShellHereStringMap -Lines $lines
    }

    # PWS003 applies ONLY to top-level scripts/*.ps1 files. The lint script and
    # its own test are excluded above in the outer loop.
    $pws003Applies = $isPs1 -and ($rel -match '^scripts/[^/]+\.ps1$')

    # Detect the per-file allowlist marker for PWS003. Scan only the first
    # 40 physical lines — the marker is meant to be a top-of-file opt-out with
    # a one-line rationale, not buried inside the body of the script.
    $pws003Allowed = $false
    if ($pws003Applies) {
        $scanLimit = [Math]::Min(40, $lines.Length)
        $inPws003AllowHelpBlock = $false
        $inPws003AllowHereString = $false
        $pws003AllowHereStringTerminator = $null
        for ($m = 0; $m -lt $scanLimit; $m++) {
            $markerLine = $lines[$m]
            if ($inPws003AllowHereString) {
                if ($markerLine -match $pws003AllowHereStringTerminator) {
                    $inPws003AllowHereString = $false
                    $pws003AllowHereStringTerminator = $null
                }
                continue
            }
            if ($inPws003AllowHelpBlock) {
                if ($markerLine -match '#>') {
                    $inPws003AllowHelpBlock = $false
                }
                continue
            }
            if ($markerLine -match '@"') {
                if (-not ($markerLine -match '"@')) {
                    $inPws003AllowHereString = $true
                    $pws003AllowHereStringTerminator = '^\s*"@'
                }
                continue
            }
            if ($markerLine -match "@'") {
                if (-not ($markerLine -match "'@")) {
                    $inPws003AllowHereString = $true
                    $pws003AllowHereStringTerminator = "^\s*'@"
                }
                continue
            }
            if ($markerLine -match '<#') {
                if (-not ($markerLine -match '#>')) {
                    $inPws003AllowHelpBlock = $true
                }
                continue
            }
            if ($lines[$m] -match $pws003AllowMarker) {
                $pws003Allowed = $true
                break
            }
        }
    }

    $pws004HookVariableNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

    # Build a "logically joined" view that merges physical lines ending in `\`
    # with their successor(s). This catches bash/YAML-run multi-line pwsh
    # invocations that would otherwise slip past the per-line regex.
    #
    # joinedLines[j]      = concatenated content (with continuations collapsed
    #                       into a single space, per bash semantics)
    # joinedStartLines[j] = physical (1-based) line number where segment j
    #                       began — used for reporting.
    # joinedHasContinuation[j] = whether this joined entry was actually built
    #                       from 2+ physical lines (so we avoid double-reporting
    #                       violations that already matched on the raw line).
    #
    # Comment-line handling: bash does NOT continue a '#' comment across a
    # trailing `\` — the comment ends at the physical EOL. So when building the
    # join, we must NOT:
    #   (a) start a join group from a comment line (its `\` is a no-op), and
    #   (b) absorb a subsequent comment line as the continuation of a prior
    #       non-comment line (a comment line's contents are not part of the
    #       logical command — though bash would treat that as a syntax error,
    #       we simply terminate the join at the comment boundary).
    # Only .sh / .yml / .yaml files honor this skip; .ps1 and package.json
    # don't have bash-style `#` semantics so we leave them alone.
    $honorHashComments = $isSh -or $isYaml
    $joinedLines = [System.Collections.Generic.List[string]]::new()
    $joinedStartLines = [System.Collections.Generic.List[int]]::new()
    $joinedHasContinuation = [System.Collections.Generic.List[bool]]::new()
    $k = 0
    while ($k -lt $lines.Length) {
        $startLine = $k + 1
        $merged = $lines[$k]
        $hadContinuation = $false
        # If the start line is itself a comment and this file honors `#`
        # comments, do not join anything — record the physical line as-is so
        # the index advances correctly.
        $startIsComment = $honorHashComments -and ($merged -match '^\s*#')
        if (-not $startIsComment) {
            # `\` at end of line (possibly followed by trailing whitespace).
            while ($merged -match '\\\s*$' -and ($k + 1) -lt $lines.Length) {
                # If the NEXT line is a comment and we honor `#`, stop the
                # join at the comment boundary (bash would also stop there).
                if ($honorHashComments -and ($lines[$k + 1] -match '^\s*#')) {
                    break
                }
                $hadContinuation = $true
                # Strip the trailing backslash (and any trailing whitespace
                # before it) and replace with a single space before joining
                # the next physical line's content. This matches how
                # bash/YAML effectively sees it.
                $merged = ($merged -replace '\\\s*$', '') + ' ' + $lines[$k + 1]
                $k++
            }
        }
        $joinedLines.Add($merged) | Out-Null
        $joinedStartLines.Add($startLine) | Out-Null
        $joinedHasContinuation.Add($hadContinuation) | Out-Null
        $k++
    }

    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]
        $lineNum = $i + 1

        # Skip comment/help lines in .ps1 files.
        if ($isPs1 -and ($commentMap[$i] -or $hereStringMap[$i])) {
            continue
        }
        # Skip full-line comments in shell and YAML files. Note: a `#` inside
        # a quoted YAML string is also treated as a comment start here — see
        # the DESCRIPTION block for the accepted edge case.
        if (($isSh -or $isYaml) -and ($line -match '^\s*#')) {
            continue
        }

        $scanLine = if ($isPs1) { Remove-PowerShellInlineComment -Line $line } else { $line }

        if ($isPs1) {
            $assignmentMatches = [System.Text.RegularExpressions.Regex]::Matches(
                $scanLine,
                $pws004VariableAssignmentPattern,
                [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
            )
            foreach ($assignmentMatch in $assignmentMatches) {
                $nameGroup = $assignmentMatch.Groups['name']
                if ($nameGroup.Success -and -not (Test-IsIndexInsidePowerShellStringLiteral -Line $scanLine -Index $nameGroup.Index)) {
                    [void]$pws004HookVariableNames.Add($nameGroup.Value)
                }
            }
            $joinPathAssignmentMatches = [System.Text.RegularExpressions.Regex]::Matches(
                $scanLine,
                $pws004JoinPathVariableAssignmentPattern,
                [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
            )
            foreach ($assignmentMatch in $joinPathAssignmentMatches) {
                $nameGroup = $assignmentMatch.Groups['name']
                if ($nameGroup.Success -and -not (Test-IsIndexInsidePowerShellStringLiteral -Line $scanLine -Index $nameGroup.Index)) {
                    [void]$pws004HookVariableNames.Add($nameGroup.Value)
                }
            }
        }

        if (Test-InvocationPattern -Line $scanLine -Pattern $pws001Pattern -IsPowerShell:$isPs1 -SuppressShellHelpText:($isShellLike -or $isYaml)) {
            $violations.Add(@{
                Path = $rel
                Line = $lineNum
                Code = 'PWS001'
                Message = "pwsh/powershell -File invocation passes '--' as a separator; PowerShell -File does not honor POSIX '--' and will fail with 'parameter name '' is ambiguous'. Use explicit named params like -Paths instead."
                Content = $line.Trim()
            }) | Out-Null
            continue
        }

        if (Test-InvocationPattern -Line $scanLine -Pattern $pws001ArrayPattern -IsPowerShell:$isPs1 -SuppressShellHelpText:($isShellLike -or $isYaml)) {
            $violations.Add(@{
                Path = $rel
                Line = $lineNum
                Code = 'PWS001'
                Message = "pwsh/powershell invocation via bash array indirection (""`${NAME[@]}"") passes '--' as a separator; if the array expands to a `pwsh -File` command, PowerShell -File does not honor POSIX '--' and will fail with 'parameter name '' is ambiguous'. Use explicit named params like -Paths instead."
                Content = $line.Trim()
            }) | Out-Null
            continue
        }

        if (Test-InvocationPattern -Line $scanLine -Pattern $pws004ArrayPattern -IsPowerShell:$isPs1 -SuppressShellHelpText:($isShellLike -or $isYaml)) {
            $violations.Add(@{
                Path = $rel
                Line = $lineNum
                Code = 'PWS004'
                Message = "pwsh/powershell invocation via bash array indirection (""`${NAME[@]}"") targets an extensionless git hook. Invoke .githooks/<hook> directly through Git/shell, or use .githooks/<hook>.ps1 for PowerShell debugging."
                Content = $line.Trim()
            }) | Out-Null
            continue
        }

        if (Test-InvocationPattern -Line $scanLine -Pattern $pws004Pattern -IsPowerShell:$isPs1 -SuppressShellHelpText:($isShellLike -or $isYaml)) {
            $violations.Add(@{
                Path = $rel
                Line = $lineNum
                Code = 'PWS004'
                Message = "pwsh/powershell -File targets an extensionless git hook. Invoke .githooks/<hook> directly through Git/shell, or use .githooks/<hook>.ps1 for PowerShell debugging."
                Content = $line.Trim()
            }) | Out-Null
            continue
        }

        if ($isPs1 -and $pws004HookVariableNames.Count -gt 0) {
            $variableAlternation = (@($pws004HookVariableNames) | ForEach-Object { [regex]::Escape($_) }) -join '|'
            $pws004VariablePattern = '(?:^|[\s;&|"''`(])(' + $pwshExecutablePattern + ')\b(?:\s+\S+)*?\s+' + $pwshFileSwitchPattern + '\s+\$(?:' + $variableAlternation + ')(?=$|[\s;&,"''`)])'
            if (Test-InvocationPattern -Line $scanLine -Pattern $pws004VariablePattern -IsPowerShell:$true -SuppressShellHelpText:$false) {
                $violations.Add(@{
                    Path = $rel
                    Line = $lineNum
                    Code = 'PWS004'
                    Message = "pwsh/powershell -File targets a variable assigned to an extensionless git hook. Invoke .githooks/<hook> directly through Git/shell, or use .githooks/<hook>.ps1 for PowerShell debugging."
                    Content = $line.Trim()
                }) | Out-Null
                continue
            }
        }

        if (Test-InvocationPattern -Line $scanLine -Pattern $pws002Pattern -IsPowerShell:$isPs1 -SuppressShellHelpText:$false) {
            $isTest = $rel -like 'scripts/tests/*.ps1'
            if ($isTest) {
                $violations.Add(@{
                    Path = $rel
                    Line = $lineNum
                    Code = 'PWS002'
                    Message = "Test invokes .ps1 via in-process '&' with '--'; tests must exercise the same invocation path production uses ('pwsh -NoProfile -File ... -Paths ...'), otherwise CLI-binding bugs are masked."
                    Content = $line.Trim()
                }) | Out-Null
            }
        }

        if ($pws003Applies -and -not $pws003Allowed) {
            $pws003Candidate = Hide-PowerShellStringLiterals -Line $scanLine
            if ($pws003Candidate -match $pws003Pattern) {
                $violations.Add(@{
                    Path = $rel
                    Line = $lineNum
                    Code = 'PWS003'
                    Message = "scripts/*.ps1 invokes 'pwsh|powershell -NoProfile -File <sibling>.ps1' via subprocess. This fails on Windows PowerShell 5.1 hosts (no pwsh on PATH) and drops the parent session's state. Prefer dot-sourcing a shared helper module, or refactor the callee into an in-process function. If subprocess isolation is truly required, opt out with a top-of-file comment '# lint-pwsh-invocations: allow-subprocess-pwsh <rationale>'."
                    Content = $line.Trim()
                }) | Out-Null
            }
        }
    }

    # Second pass: logically-joined lines. Only consider entries actually built
    # from a continuation (otherwise we'd double-report plain single-line hits).
    for ($j = 0; $j -lt $joinedLines.Count; $j++) {
        if (-not $joinedHasContinuation[$j]) { continue }
        $joined = $joinedLines[$j]
        $startLine = $joinedStartLines[$j]
        $startIdx = $startLine - 1

        # Skip if the *physical* start line is a known comment.
        if ($isPs1 -and ($commentMap[$startIdx] -or $hereStringMap[$startIdx])) { continue }
        if (($isSh -or $isYaml) -and ($lines[$startIdx] -match '^\s*#')) { continue }

        if (Test-InvocationPattern -Line $joined -Pattern $pws001Pattern -IsPowerShell:$isPs1 -SuppressShellHelpText:($isShellLike -or $isYaml)) {
            $violations.Add(@{
                Path = $rel
                Line = $startLine
                Code = 'PWS001'
                Message = "pwsh/powershell -File invocation (multi-line with '\' continuation) passes '--' as a separator; PowerShell -File does not honor POSIX '--' and will fail with 'parameter name '' is ambiguous'. Use explicit named params like -Paths instead."
                Content = $joined.Trim()
            }) | Out-Null
            continue
        }

        if (Test-InvocationPattern -Line $joined -Pattern $pws001ArrayPattern -IsPowerShell:$isPs1 -SuppressShellHelpText:($isShellLike -or $isYaml)) {
            $violations.Add(@{
                Path = $rel
                Line = $startLine
                Code = 'PWS001'
                Message = "pwsh/powershell invocation via bash array indirection (""`${NAME[@]}"") (multi-line with '\' continuation) passes '--' as a separator; if the array expands to a `pwsh -File` command, PowerShell -File does not honor POSIX '--' and will fail with 'parameter name '' is ambiguous'. Use explicit named params like -Paths instead."
                Content = $joined.Trim()
            }) | Out-Null
            continue
        }

        if (Test-InvocationPattern -Line $joined -Pattern $pws004ArrayPattern -IsPowerShell:$isPs1 -SuppressShellHelpText:($isShellLike -or $isYaml)) {
            $violations.Add(@{
                Path = $rel
                Line = $startLine
                Code = 'PWS004'
                Message = "pwsh/powershell invocation via bash array indirection (""`${NAME[@]}"") targets an extensionless git hook (multi-line with '\' continuation). Invoke .githooks/<hook> directly through Git/shell, or use .githooks/<hook>.ps1 for PowerShell debugging."
                Content = $joined.Trim()
            }) | Out-Null
            continue
        }

        if (Test-InvocationPattern -Line $joined -Pattern $pws004Pattern -IsPowerShell:$isPs1 -SuppressShellHelpText:($isShellLike -or $isYaml)) {
            $violations.Add(@{
                Path = $rel
                Line = $startLine
                Code = 'PWS004'
                Message = "pwsh/powershell -File targets an extensionless git hook (multi-line with '\' continuation). Invoke .githooks/<hook> directly through Git/shell, or use .githooks/<hook>.ps1 for PowerShell debugging."
                Content = $joined.Trim()
            }) | Out-Null
            continue
        }

        if (Test-InvocationPattern -Line $joined -Pattern $pws002Pattern -IsPowerShell:$isPs1 -SuppressShellHelpText:$false) {
            $isTest = $rel -like 'scripts/tests/*.ps1'
            if ($isTest) {
                $violations.Add(@{
                    Path = $rel
                    Line = $startLine
                    Code = 'PWS002'
                    Message = "Test invokes .ps1 via in-process '&' with '--' (multi-line with '\' continuation); tests must exercise the same invocation path production uses ('pwsh -NoProfile -File ... -Paths ...'), otherwise CLI-binding bugs are masked."
                    Content = $joined.Trim()
                }) | Out-Null
            }
        }

        if ($pws003Applies -and -not $pws003Allowed) {
            $pws003JoinedCandidate = Hide-PowerShellStringLiterals -Line $joined
            if ($pws003JoinedCandidate -match $pws003Pattern) {
                $violations.Add(@{
                    Path = $rel
                    Line = $startLine
                    Code = 'PWS003'
                    Message = "scripts/*.ps1 invokes 'pwsh|powershell -NoProfile -File <sibling>.ps1' via subprocess (multi-line with '\' continuation). This fails on Windows PowerShell 5.1 hosts (no pwsh on PATH) and drops the parent session's state. Prefer dot-sourcing a shared helper module, or refactor the callee into an in-process function. If subprocess isolation is truly required, opt out with a top-of-file comment '# lint-pwsh-invocations: allow-subprocess-pwsh <rationale>'."
                    Content = $joined.Trim()
                }) | Out-Null
            }
        }
    }

    # Third pass (YAML-only): detect `run: >` folded block scalars that carry
    # a multi-line pwsh invocation WITHOUT `\` continuations. YAML folds the
    # scalar body into a single space-separated string before bash sees it, so
    # the entire block runs as one command — the `--` reaches pwsh.
    #
    # We intentionally do NOT fold `run: |` (literal block scalar). Under `|`,
    # each line is preserved as a separate command line; bash runs them
    # individually, and a bare `pwsh \n -NoProfile \n -File ... \n -- arg`
    # without `\` continuations would already be a bash syntax error. The
    # `run: |`-with-backslashes case is fully covered by the continuation pass
    # above, so folding `|` here would only produce spurious duplicate reports.
    #
    # Algorithm: for each physical line matching
    # `^(\s*)(?:-\s+)?run:\s*>[-+]?`, read subsequent lines that are MORE
    # indented than the `run:` key itself. Join them with single spaces and
    # apply PWS001. Report at the `run:` line number.
    if ($isYaml) {
        for ($i = 0; $i -lt $lines.Length; $i++) {
            $line = $lines[$i]
            # Skip comments.
            if ($line -match '^\s*#') { continue }
            if ($line -notmatch '^(?<indent>\s*)(?:-\s+)?run\s*:\s*>[-+]?\s*(#.*)?$') {
                continue
            }
            $keyIndent = $Matches['indent'].Length
            $bodyLines = [System.Collections.Generic.List[string]]::new()
            $j = $i + 1
            while ($j -lt $lines.Length) {
                $next = $lines[$j]
                # Blank lines are part of the scalar — preserve them as a space
                # in the join.
                if ($next -match '^\s*$') {
                    $bodyLines.Add('') | Out-Null
                    $j++
                    continue
                }
                # Detect indent: how many leading spaces before first non-ws?
                $nextIndent = ($next -replace '^(\s*).*$', '$1').Length
                if ($nextIndent -le $keyIndent) { break }
                # Skip block-internal comment lines (bash/YAML both ignore).
                if ($next -match '^\s*#') {
                    $j++
                    continue
                }
                $bodyLines.Add($next.TrimStart()) | Out-Null
                $j++
            }
            if ($bodyLines.Count -eq 0) { continue }
            # Join with single spaces — a close-enough approximation of YAML
            # folding semantics for regex-needle matching. We don't care about
            # paragraph boundaries or literal-block newline preservation since
            # we're just searching for the `-File <script> --` pattern.
            $blockJoined = ($bodyLines -join ' ') -replace '\s+', ' '
            $blockStartLine = $i + 1
            if (Test-InvocationPattern -Line $blockJoined -Pattern $pws001Pattern -IsPowerShell:$false -SuppressShellHelpText:$true) {
                $violations.Add(@{
                    Path = $rel
                    Line = $blockStartLine
                    Code = 'PWS001'
                    Message = "pwsh/powershell -File invocation inside YAML block scalar passes '--' as a separator; PowerShell -File does not honor POSIX '--' and will fail with 'parameter name '' is ambiguous'. Use explicit named params like -Paths instead."
                    Content = $blockJoined.Trim()
                }) | Out-Null
                continue
            }
            if (Test-InvocationPattern -Line $blockJoined -Pattern $pws001ArrayPattern -IsPowerShell:$false -SuppressShellHelpText:$true) {
                $violations.Add(@{
                    Path = $rel
                    Line = $blockStartLine
                    Code = 'PWS001'
                    Message = "pwsh/powershell invocation via bash array indirection (""`${NAME[@]}"") inside YAML block scalar passes '--' as a separator; if the array expands to a `pwsh -File` command, PowerShell -File does not honor POSIX '--' and will fail with 'parameter name '' is ambiguous'. Use explicit named params like -Paths instead."
                    Content = $blockJoined.Trim()
                }) | Out-Null
                continue
            }
            if (Test-InvocationPattern -Line $blockJoined -Pattern $pws004ArrayPattern -IsPowerShell:$false -SuppressShellHelpText:$true) {
                $violations.Add(@{
                    Path = $rel
                    Line = $blockStartLine
                    Code = 'PWS004'
                    Message = "pwsh/powershell invocation via bash array indirection (""`${NAME[@]}"") targets an extensionless git hook inside YAML block scalar. Invoke .githooks/<hook> directly through Git/shell, or use .githooks/<hook>.ps1 for PowerShell debugging."
                    Content = $blockJoined.Trim()
                }) | Out-Null
                continue
            }
            if (Test-InvocationPattern -Line $blockJoined -Pattern $pws004Pattern -IsPowerShell:$false -SuppressShellHelpText:$true) {
                $violations.Add(@{
                    Path = $rel
                    Line = $blockStartLine
                    Code = 'PWS004'
                    Message = "pwsh/powershell -File targets an extensionless git hook inside YAML block scalar. Invoke .githooks/<hook> directly through Git/shell, or use .githooks/<hook>.ps1 for PowerShell debugging."
                    Content = $blockJoined.Trim()
                }) | Out-Null
            }
        }
    }
}

if ($violations.Count -gt 0) {
    foreach ($v in $violations) {
        Write-Host ("{0}:{1}: {2} {3}" -f $v.Path, $v.Line, $v.Code, $v.Message) -ForegroundColor Red
        if ($VerboseOutput) {
            Write-Host ("    > {0}" -f $v.Content) -ForegroundColor DarkGray
        }
    }
    Write-Host ""
    Write-Host ("[lint-pwsh-invocations] {0} violation(s) found." -f $violations.Count) -ForegroundColor Red
    exit 1
}

if ($VerboseOutput) {
    Write-Host "[lint-pwsh-invocations] OK: No pwsh/powershell invocation anti-patterns detected." -ForegroundColor Green
}
exit 0
