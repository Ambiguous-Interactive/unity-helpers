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
      PWS005 - A script declares an array parameter without BOTH a
               `[Parameter(ValueFromRemainingArguments = $true)]` sibling and
               `[CmdletBinding(PositionalBinding = $false)]`, so
               `pwsh -File <script> -Paths a b` binds only 'a' and puts 'b' on
               whichever parameter is positionally next -- silently.
      PWS006 - `Start-Process -ArgumentList <array|variable>`. Start-Process
               joins the array with spaces and adds NO quoting, so an argument
               containing a space is split into separate arguments. Use
               `System.Diagnostics.ProcessStartInfo.ArgumentList`, which quotes
               each element. Opt-out per site: a comment marker
               `# lint-pwsh-invocations: allow-start-process-argument-list <rationale>`
               on the invocation's line or in the comment block directly above.

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

# PWS006 opt-out marker. Per SITE rather than per file: a script may legitimately
# launch one fixed-switch installer and still be wrong to hand a path to another.
# Not anchored to the start of a line, so it may be a trailing comment on the
# invocation itself or a line of the comment block directly above it. A rationale
# is required, as it is for PWS003.
$pws006AllowMarker = '#\s*lint-pwsh-invocations:\s*allow-start-process-argument-list\s+\S+'

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

# PWS005: a script that declares ANY [string[]] parameter must also declare a
# ValueFromRemainingArguments sibling. `pwsh -File <script> -Paths a b c` binds ONLY `a` and
# silently drops the rest, so a multi-file invocation lints one file and reports success -- the
# worst shape a gate can fail in, and the shape `.llm/context.md` documents for lint-tests.ps1.
# Found in six scripts at once (session 221); the pattern was already written down in
# .llm/skills/bash-pwsh-invocation.md and nothing enforced it.
#
# The rule originally matched the parameter NAMED `Paths`, which is not what makes -File binding
# drop arguments -- the array-ness is. Four more scripts carried the same latent defect under
# other names (#556).
#
# And a ValueFromRemainingArguments sibling ALONE does not fix it. With positional binding on --
# the default -- `pwsh -File s.ps1 -Versions a b` binds 'b' to whichever named parameter is
# positionally next and the catch-all never sees it. Measured: an ensure-editor.ps1 invocation
# silently put 'b' in -InstallRoot. So the script must ALSO declare
# [CmdletBinding(PositionalBinding = $false)], which is what routes every stray value to the
# catch-all. Requiring only the sibling institutionalised a non-fix.
# The PowerShell files this repository OWNS: everything under scripts/, plus the four
# .githooks/<hook>.ps1 implementations, which are scripts that take arguments like any other.
#
# Ignored files are dropped. CI checks out tracked content only, so a gate that scans a
# developer's gitignored scratch script reports a violation the repository cannot fix and CI
# can never see -- and this container has one (`scripts/run-unity*` is in .gitignore).
# Untracked-but-not-ignored files are still scanned: a new script you forgot to stage is
# exactly the one a rule should catch. `git` failing (a fixture root is not a repository)
# excludes nothing.
function Get-OwnedPowerShellScript {
    $candidates = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
    foreach ($scriptDirectory in @('scripts', '.githooks')) {
        $absolute = Join-Path $repoRoot $scriptDirectory
        if (-not (Test-Path -LiteralPath $absolute -PathType Container)) { continue }
        foreach ($file in (Get-ChildItem -LiteralPath $absolute -Filter '*.ps1' -Recurse -File)) {
            $candidates.Add($file) | Out-Null
        }
    }

    $ignored = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    try {
        $listed = & git -C $repoRoot ls-files --others --ignored --exclude-standard -- 'scripts' '.githooks' 2>$null
        if ($LASTEXITCODE -eq 0) {
            foreach ($ignoredPath in @($listed)) {
                if (-not [string]::IsNullOrWhiteSpace($ignoredPath)) {
                    $ignored.Add($ignoredPath.Replace('\', '/')) | Out-Null
                }
            }
        }
    } catch {
        # No git, or no repository. Scan everything rather than nothing.
    }

    $owned = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
    foreach ($candidate in $candidates) {
        $candidateRelative = [System.IO.Path]::GetRelativePath($repoRoot, $candidate.FullName).Replace('\', '/')
        if ($ignored.Contains($candidateRelative)) { continue }
        $owned.Add($candidate) | Out-Null
    }

    return $owned
}

foreach ($scriptPath in (Get-OwnedPowerShellScript)) {
    # '\' not '\\': in a PowerShell single-quoted string the latter is TWO backslashes, so a
    # Windows separator from GetRelativePath would pass through unchanged and the self-exclusion
    # against $selfRel would never match (Bugbot, PR #555).
    $rel = [System.IO.Path]::GetRelativePath($repoRoot, $scriptPath.FullName).Replace('\', '/')
    if ($rel -eq $selfRel -or $rel -eq $selfTestRel) { continue }

    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($scriptPath.FullName, [ref]$null, [ref]$parseErrors)
    # A script the parser cannot read is not a script this rule has cleared. Skipping it silently
    # is the same failure mode the rule exists to prevent, so report it instead.
    if ($parseErrors -and $parseErrors.Count -gt 0) {
        $violations.Add(@{
            Path = $rel
            Line = $parseErrors[0].Extent.StartLineNumber
            Code = 'PWS005'
            Message = "could not be parsed, so its parameter block was never checked: $($parseErrors[0].Message)"
            Content = $parseErrors[0].Extent.Text
        }) | Out-Null
        continue
    }
    # PWS006: `Start-Process -ArgumentList <array>` joins the array with spaces and adds no
    # quoting, so an argument containing a space is split into separate arguments. What makes it
    # worth a rule rather than three fixes is the failure SHAPE: pwsh answers its usage banner and
    # exits 64, so a caller asserting "the child failed" is satisfied by a child that never ran.
    # Measured on #571's harness -- reverting one converted site under a spaced fixture root
    # reddened six of nine scenarios and left three green (#572).
    #
    # Checked from the AST, not a regex: the parameter and its value have to be matched as ONE
    # thing, and matching them independently over the file is the mistake #556's CSS gate made.
    #
    # Deliberately narrow. Only the three shapes that can hold more than one argument are
    # reported -- an array literal, an @() expression, and a variable, whose contents this script
    # cannot know. A string literal is quoted by its author and is left alone.
    $startProcessAsts = $ast.FindAll(
        {
            param($node)
            $node -is [System.Management.Automation.Language.CommandAst]
        },
        $true
    )
    $scriptLines = $null
    foreach ($startProcessAst in $startProcessAsts) {
        $commandName = $startProcessAst.GetCommandName()
        if ([string]::IsNullOrEmpty($commandName)) { continue }
        # `saps` is the shipped alias. `start` is one too, but it is also an ordinary word, and a
        # rule that reports `start` reports things that are not Start-Process.
        if ($commandName -notmatch '^(?:Start-Process|saps)$') { continue }

        $elements = $startProcessAst.CommandElements
        for ($elementIndex = 1; $elementIndex -lt $elements.Count; $elementIndex++) {
            $element = $elements[$elementIndex]
            if ($element -isnot [System.Management.Automation.Language.CommandParameterAst]) { continue }
            # PowerShell binds any unambiguous prefix, so -Arg, -Args, -ArgumentL and
            # -ArgumentList are all the same parameter. Start-Process has no other -Arg* one.
            if ($element.ParameterName -notmatch '^(?i:arg)') { continue }

            # `-ArgumentList:$values` puts the value on the parameter; `-ArgumentList $values`
            # puts it in the next element.
            $argumentValue = $element.Argument
            if ($null -eq $argumentValue -and ($elementIndex + 1) -lt $elements.Count) {
                $argumentValue = $elements[$elementIndex + 1]
            }
            if ($null -eq $argumentValue) { continue }

            $isJoinedShape = (
                $argumentValue -is [System.Management.Automation.Language.ArrayLiteralAst] -or
                $argumentValue -is [System.Management.Automation.Language.ArrayExpressionAst] -or
                $argumentValue -is [System.Management.Automation.Language.VariableExpressionAst]
            )
            if (-not $isJoinedShape) { continue }

            $siteLine = $startProcessAst.Extent.StartLineNumber
            if ($null -eq $scriptLines) {
                $scriptLines = [System.IO.File]::ReadAllLines($scriptPath.FullName)
            }

            # The marker may be a trailing comment on the invocation itself, or a line of the
            # comment block directly above it. A blank line ends the block: a rationale three
            # paragraphs up is not attached to this call.
            $isAllowed = $false
            if ($siteLine -le $scriptLines.Length -and $scriptLines[$siteLine - 1] -match $pws006AllowMarker) {
                $isAllowed = $true
            }
            for ($above = $siteLine - 2; 0 -le $above -and -not $isAllowed; $above--) {
                $aboveLine = $scriptLines[$above]
                if ($aboveLine -notmatch '^\s*#') { break }
                if ($aboveLine -match $pws006AllowMarker) { $isAllowed = $true }
            }
            if ($isAllowed) { continue }

            $violations.Add(@{
                Path = $rel
                Line = $siteLine
                Code = 'PWS006'
                Message = "Start-Process -$($element.ParameterName) is handed an array or a variable, which it joins with spaces and does NOT quote, so any element containing a space is split into separate arguments -- and the failure is a non-zero exit with a usage banner, which reads exactly like the child running and failing. Use System.Diagnostics.ProcessStartInfo.ArgumentList, which quotes each element. The conversion is not always mechanical: Start-Process -Wait also waits on the child's descendants, which Process.WaitForExit() does not. If the arguments are genuinely fixed switches, opt out on this site with a comment '# lint-pwsh-invocations: allow-start-process-argument-list <rationale>'. See .llm/skills/bash-pwsh-invocation.md."
                Content = $startProcessAst.Extent.Text.Split("`n")[0].Trim()
            }) | Out-Null
            break
        }
    }

    # The SCRIPT's own param block, not a nested function's: a function parameter is bound in
    # process and never goes through -File argument binding.
    $paramBlock = $ast.ParamBlock
    if ($null -eq $paramBlock) { continue }

    $arrayParameters = [System.Collections.Generic.List[string]]::new()
    $catchesRemaining = $false
    foreach ($parameter in $paramBlock.Parameters) {
        $isRemaining = $false
        foreach ($attribute in $parameter.Attributes) {
            if ($attribute.Extent.Text -match 'ValueFromRemainingArguments') {
                $catchesRemaining = $true
                $isRemaining = $true
            }
        }
        # Any array type, not just [string[]]: an [int[]] or [object[]] parameter drops arguments
        # exactly the same way.
        if (-not $isRemaining -and $null -ne $parameter.StaticType -and $parameter.StaticType.IsArray) {
            $arrayParameters.Add($parameter.Name.VariablePath.UserPath) | Out-Null
        }
    }

    # PositionalBinding = $false is the half that actually routes stray values to the catch-all.
    # It lives on a [CmdletBinding()] attribute attached to the param block, not inside it.
    $disablesPositional = $false
    foreach ($attribute in $paramBlock.Attributes) {
        if ($attribute.Extent.Text -match 'PositionalBinding\s*=\s*\$false') {
            $disablesPositional = $true
        }
    }

    if ($arrayParameters.Count -gt 0 -and (-not $catchesRemaining -or -not $disablesPositional)) {
        $first = $arrayParameters[0]
        $missing = @()
        if (-not $catchesRemaining) { $missing += 'a [Parameter(ValueFromRemainingArguments = $true)] sibling' }
        if (-not $disablesPositional) { $missing += '[CmdletBinding(PositionalBinding = $false)]' }
        $violations.Add(@{
            Path = $rel
            Line = $paramBlock.Extent.StartLineNumber
            Code = 'PWS005'
            Message = "declares array parameter(s) $($arrayParameters -join ', ') but is missing $($missing -join ' and '), so ``pwsh -File $rel -$first a b`` binds only 'a' and puts 'b' on whichever parameter is positionally next -- silently. BOTH are required: the sibling catches the remainder, PositionalBinding = `$false is what sends it there. See .llm/skills/bash-pwsh-invocation.md."
            Content = $paramBlock.Extent.Text.Split("`n")[0].Trim()
        }) | Out-Null
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
