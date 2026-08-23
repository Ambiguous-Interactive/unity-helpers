#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Enforce that every lint-error-code family emitted by any lint script,
    lint test, or git hook is registered with cspell.

.DESCRIPTION
    Our project uses short all-caps prefixes followed by a three-digit number as
    lint error codes (e.g. UNH001, DEP004, PWS002). A new lint script can emit
    such codes in prose that cspell scans (skill docs, CHANGELOG, hook error
    messages) — and unless the prefix is known to cspell, the spell checker
    flags it as an unknown word.

    This validator is the end-to-end contract: it walks
      * scripts/lint-*.{ps1,js}         (where prefixes are usually defined),
      * scripts/tests/test-lint-*.{ps1,js,sh}  (test assertions cite codes),
      * .githooks/*                     (hook error messages cite codes),
    harvests tokens that match the error-code shape ([A-Z]{2,}\d{3}), and
    for each unique prefix asserts that the prefix is known to cspell.
    "Known to cspell" means: running `cspell stdin` over the bare prefix
    token produces zero unknown-word issues.

    A small upstream-rule allowlist (MD/SC/CS/SA) prevents the validator
    from demanding cspell registration for third-party linter codes that
    appear in our comments — e.g. `# shellcheck disable=SC2016` references
    SC but we don't own that family.

    The cspell roundtrip is authoritative. It honors compound splitting,
    minWordLength, caseSensitive, every dictionary, and any future cspell
    config change — so the test does not need to know whether the prefix is
    registered as an explicit word, falls inside an English base word, or is
    covered by some language setting.

    On failure the script prints a copy-pasteable JSON patch for cspell.json
    so the fix is one paste away, and exits non-zero.

.PARAMETER VerboseOutput
    Show per-script diagnostics including the error codes harvested.

.PARAMETER NodeCommand
    Override the node command used to invoke the repo-local cspell launcher.

.EXAMPLE
    pwsh -NoProfile -File scripts/validate-lint-error-codes.ps1
#>
param(
    [switch]$VerboseOutput,
    [string]$NodeCommand = 'node'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# cspell:ignore NOVELPFX

function Write-Info($msg) {
    if ($VerboseOutput) { Write-Host "[validate-lint-error-codes] $msg" -ForegroundColor Cyan }
}

function Write-ErrorMsg($msg) {
    Write-Host "[validate-lint-error-codes] ERROR: $msg" -ForegroundColor Red
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $repoRoot
try {
    $scriptsDir = Join-Path $repoRoot 'scripts'

    # ── Harvest lint-error-code tokens ────────────────────────────────────────
    # Canonical shape: 2+ uppercase ASCII letters followed by exactly three
    # digits (e.g. UNH001, PWS002, NOVELPFX001). The upper bound is
    # intentionally unbounded — the cspell roundtrip is authoritative, so the
    # regex over-captures and lets cspell decide. Narrowing to {2,5} let
    # prefixes longer than 5 chars escape the contract entirely (the exact
    # fragility flagged in P0-3); narrowing to {3,} would miss real two-letter
    # families like `MD025` (markdownlint), which we also want to observe so
    # the upstream-rule allowlist below can explicitly ignore them.
    #
    # Over-capture WHY: this regex is deliberately promiscuous — it harvests
    # prefixes from comments, strings, and adjacent-context tokens too
    # (e.g. `SHA256` in a comment yields `SHA`). The cspell roundtrip is the
    # authoritative gate: anything cspell accepts (via compound splitting,
    # dictionaries, minWordLength, etc.) is silently filtered out. This is
    # intentional — do not narrow the regex to try to exclude comments.
    $codePattern = '\b([A-Z]{2,})([0-9]{3})\b'

    # Upstream-rule allowlist: families emitted by THIRD-PARTY linters that we
    # merely *reference* in our scripts (comments, shellcheck disable tags,
    # markdownlint rule IDs, etc.) but do not *own*. These are NOT our lint-
    # error-code families, so they must not be required to appear in cspell.
    # Any genuinely unknown prefix here still surfaces at cspell-lint time —
    # we simply decline to demand a cspell entry for something we never emit.
    #
    # WHY each entry:
    #   MD  — markdownlint rule IDs (MD001..MD041). Referenced in docs/skills.
    #   SC  — shellcheck codes (SC2016, SC2329...). Referenced in `# shellcheck
    #         disable=` tags across .sh and .yml run: blocks.
    #   CS  — actionlint C# sentinel (also compiler warnings like CS0168 in
    #         Roslyn output). Referenced in docs but never emitted locally.
    #   SA  — StyleCop.Analyzers rule IDs (SA1600 series). Referenced in
    #         .editorconfig and docs.
    # Keep this list CONSERVATIVE. When in doubt, omit — the cspell roundtrip
    # will accept the prefix if cspell accepts it, so false positives here
    # have zero downside beyond a slightly larger cspell.json.
    $upstreamRuleAllowlist = @('MD', 'SC', 'CS', 'SA')

    # Scan the surface where new prefixes are born OR referenced:
    #   scripts/lint-*.{ps1,js}                  — lint script authors
    #   scripts/tests/test-lint-*.{ps1,js,sh}    — test assertions cite codes
    #   .githooks/*                              — hook error messages cite codes
    # Extending beyond lint scripts closes the loophole where a new code
    # emitted ONLY in a hook's error message (e.g. UNH004 in the pre-commit
    # failure block) was never scanned, so the cspell contract could go stale
    # without detection.
    $lintScripts = @()
    $lintScripts += Get-ChildItem -Path $scriptsDir -Filter 'lint-*.ps1' -File -ErrorAction SilentlyContinue
    $lintScripts += Get-ChildItem -Path $scriptsDir -Filter 'lint-*.js' -File -ErrorAction SilentlyContinue
    $testsDir = Join-Path $scriptsDir 'tests'
    if (Test-Path -LiteralPath $testsDir -PathType Container) {
        $lintScripts += Get-ChildItem -Path $testsDir -Filter 'test-lint-*.ps1' -File -ErrorAction SilentlyContinue
        $lintScripts += Get-ChildItem -Path $testsDir -Filter 'test-lint-*.js' -File -ErrorAction SilentlyContinue
        $lintScripts += Get-ChildItem -Path $testsDir -Filter 'test-lint-*.sh' -File -ErrorAction SilentlyContinue
    }
    $githooksDir = Join-Path $repoRoot '.githooks'
    if (Test-Path -LiteralPath $githooksDir -PathType Container) {
        # Githooks have no extension; enumerate all files and filter to
        # readable text (exclude *.meta Unity stubs, symlinks, and the README).
        $lintScripts += Get-ChildItem -Path $githooksDir -File -ErrorAction SilentlyContinue | Where-Object {
            $_.Name -notlike '*.meta' -and $_.Name -ne 'README.md' -and $_.Name -ne '.gitattributes'
        }
    }

    if ($lintScripts.Count -eq 0) {
        Write-ErrorMsg "No lint/test/hook files found under scripts/lint-*.{ps1,js}, scripts/tests/test-lint-*.{ps1,js,sh}, or .githooks/*. Repository layout may have changed."
        exit 1
    }

    Write-Info "Scanning $($lintScripts.Count) lint/test/hook file(s) for error-code prefixes..."

    # prefix → list of "script:line" sources (for richer error output).
    # Note: avoid assigning to the automatic $matches variable by using
    # $regexMatches below (PSAvoidAssignmentToAutomaticVariable).
    $prefixSources = @{}

    foreach ($script in $lintScripts) {
        $relPath = $script.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
        $lineNumber = 0
        foreach ($line in Get-Content -LiteralPath $script.FullName) {
            $lineNumber++
            $regexMatches = [regex]::Matches($line, $codePattern)
            if ($regexMatches.Count -eq 0) { continue }
            foreach ($match in $regexMatches) {
                $prefix = $match.Groups[1].Value
                # Skip upstream-rule prefixes (MD/SC/CS/SA). These appear in
                # comments, shellcheck disable tags, and markdownlint rule
                # references — we do not own them and must not demand cspell
                # registration for them. See $upstreamRuleAllowlist above.
                if ($upstreamRuleAllowlist -contains $prefix) { continue }
                if (-not $prefixSources.ContainsKey($prefix)) {
                    $prefixSources[$prefix] = [System.Collections.Generic.List[string]]::new()
                }
                # Cap sources per prefix to keep failure output readable.
                if ($prefixSources[$prefix].Count -lt 5) {
                    $prefixSources[$prefix].Add("${relPath}:${lineNumber}") | Out-Null
                }
            }
        }
    }

    if ($prefixSources.Count -eq 0) {
        Write-Info 'No lint-error-code tokens found. Nothing to validate.'
        exit 0
    }

    $sortedPrefixes = @($prefixSources.Keys | Sort-Object)
    Write-Info "Harvested $($sortedPrefixes.Count) unique prefix(es): $($sortedPrefixes -join ', ')"

    # ── Verify cspell is reachable ────────────────────────────────────────────
    if (-not (Get-Command $NodeCommand -ErrorAction SilentlyContinue)) {
        Write-ErrorMsg "$NodeCommand is required to roundtrip prefixes through cspell. Install Node.js and run 'npm install'."
        exit 1
    }

    # There is deliberately NO `cspell --version` reachability probe here. Measured on this
    # devcontainer, `cspell --version` and `cspell stdin` over two words cost the SAME 8.13 s: it is
    # all process start-up and none of it is spelling. So the probe doubled the cost of every run of
    # this validator, and `scripts/tests/test-validate-lint-error-codes.ps1` runs it nine times.
    # It also proved nothing new -- run-node-bin.js already exits 127 with an actionable message when
    # the package or its bin entry is absent, and the roundtrip below surfaces that exit code.
    $nodeBinRunner = Join-Path $PSScriptRoot 'run-node-bin.js'

    # ── Roundtrip harvested prefixes through cspell ───────────────────────────
    # We feed "<PREFIX>001" (not just "<PREFIX>") because the real-world token
    # always carries the three-digit suffix. cspell's compound splitter treats
    # these as separable tokens, so if the prefix is known via ANY mechanism
    # (explicit word, dictionary, minWordLength exclusion, compound decomposition)
    # the roundtrip produces zero issues. This matches what cspell does when
    # scanning actual skill docs and CHANGELOG entries.
    #
    # The -Raw output from cspell --no-color is stable enough to parse:
    #   `<stdin>:<line>:<col> - Unknown word (<word>)`
    # We only care about the presence of "Unknown word" lines.
    $probeInputLines = @()
    foreach ($prefix in $sortedPrefixes) {
        $probe = "${prefix}001"
        $probeInputLines += $probe
    }

    $probeInput = ($probeInputLines -join "`n") + "`n"
    $output = $probeInput | & $NodeCommand $nodeBinRunner cspell stdin --no-progress --no-color 2>&1
    $exitCode = $LASTEXITCODE

    $unknownPrefixes = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($line in @($output)) {
        $regexMatch = [regex]::Match([string]$line, 'Unknown word \(([A-Z]{2,})(?:[0-9]{3})?\)')
        if ($regexMatch.Success) {
            $unknownPrefixes.Add($regexMatch.Groups[1].Value) | Out-Null
        }
    }

    if ($exitCode -ne 0 -and $unknownPrefixes.Count -eq 0) {
        # 127 is run-node-bin.js's "not installed" exit; name that case rather than reporting it as
        # unparseable output, because it is the one a contributor hits and the fix is one command.
        if ($exitCode -eq 127) {
            Write-ErrorMsg "cspell is not installed in this repository. Run 'npm install' on the same host that runs git hooks."
        }
        else {
            Write-ErrorMsg "cspell prefix roundtrip failed without parseable unknown-word output (exit $exitCode)."
        }
        Write-Host ($output | Out-String) -ForegroundColor DarkGray
        exit 1
    }

    $unknown = [System.Collections.Generic.List[object]]::new()
    foreach ($prefix in $sortedPrefixes) {
        $probe = "${prefix}001"
        $hasUnknown = $unknownPrefixes.Contains($prefix)
        Write-Info "  cspell roundtrip: $probe => batchExit=$exitCode, hasUnknown=$hasUnknown"

        if ($hasUnknown) {
            $unknown.Add([PSCustomObject]@{
                Prefix = $prefix
                Sources = $prefixSources[$prefix]
            }) | Out-Null
        }
    }

    if ($unknown.Count -eq 0) {
        Write-Host "[validate-lint-error-codes] OK: all $($sortedPrefixes.Count) lint-error-code prefix(es) pass cspell." -ForegroundColor Green
        exit 0
    }

    # ── Failure path: emit a copy-pasteable cspell.json patch ─────────────────
    Write-Host '' -ErrorAction SilentlyContinue
    Write-Host '=== Unregistered lint-error-code prefix(es) ===' -ForegroundColor Red
    Write-Host ''
    Write-Host 'Every `[A-Z]{2,}\d{3}` token emitted by scripts/lint-*.{ps1,js}, scripts/tests/test-lint-*, or .githooks/* must pass cspell.' -ForegroundColor Yellow
    Write-Host 'The following prefix(es) are flagged as unknown words:' -ForegroundColor Yellow
    Write-Host ''

    foreach ($entry in $unknown) {
        Write-Host ("  {0}  (e.g. {0}001)" -f $entry.Prefix) -ForegroundColor Red
        foreach ($source in $entry.Sources) {
            Write-Host ("    - {0}" -f $source) -ForegroundColor DarkGray
        }
    }

    $jsonPatch = [ordered]@{
        cspell_patch_instructions = @(
            'Open cspell.json and append each prefix below to the root "words" array'
            '(near the end of the file) as a new quoted string element.'
        )
        add_to_root_words = @($unknown | ForEach-Object { $_.Prefix })
    }

    Write-Host ''
    Write-Host 'Copy-pasteable patch (cspell.json -> "words"):' -ForegroundColor Cyan
    Write-Host ''
    $patchText = $jsonPatch | ConvertTo-Json -Depth 4
    Write-Host $patchText -ForegroundColor White
    Write-Host ''
    Write-Host 'After editing cspell.json, verify with:' -ForegroundColor Cyan
    Write-Host '  npm run lint:spelling:config' -ForegroundColor White
    Write-Host '  npm run lint:spelling' -ForegroundColor White
    Write-Host '  pwsh -NoProfile -File scripts/validate-lint-error-codes.ps1' -ForegroundColor White
    Write-Host ''

    exit 1
}
finally {
    Pop-Location
}
