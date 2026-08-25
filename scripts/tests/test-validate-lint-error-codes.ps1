#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Test runner for validate-lint-error-codes.ps1.

.DESCRIPTION
    Tests that validate-lint-error-codes.ps1 correctly:
    - Passes against the real repository (current cspell.json registers every
      lint-error-code prefix emitted by scripts/lint-*.{ps1,js},
      scripts/tests/test-lint-*.{ps1,js,sh}, or .githooks/*).
    - Fails with a deterministic exit code and a copy-pasteable JSON patch
      when a synthetic lint script introduces a novel, unregistered prefix.
    - Tolerates lint scripts that emit no lint codes at all.
    - Ignores prefix variants that cspell accepts via compound-word splitting
      (e.g. DEP, because "DEP" splits into common English fragments under the
      active cspell config).
    - Emits the violating sources (script:line) in its failure output.

.PARAMETER VerboseOutput
    Show verbose per-test diagnostics.

.EXAMPLE
    pwsh -NoProfile -File scripts/tests/test-validate-lint-error-codes.ps1
#>
param(
    [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# cspell:ignore HOK TST

$script:TestsPassed = 0
$script:TestsFailed = 0
$script:FailedTests = @()

function Write-Info($msg) {
    if ($VerboseOutput) { Write-Host "[test-validate-lint-error-codes] $msg" -ForegroundColor Cyan }
}

function Write-TestResult {
    param(
        [string]$TestName,
        [bool]$Passed,
        [string]$Message = ""
    )
    if ($Passed) {
        Write-Host "  [PASS] $TestName" -ForegroundColor Green
        $script:TestsPassed++
    } else {
        Write-Host "  [FAIL] $TestName" -ForegroundColor Red
        if ($Message) {
            Write-Host "         $Message" -ForegroundColor Yellow
        }
        $script:TestsFailed++
        $script:FailedTests += $TestName
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$validatorPath = Join-Path $repoRoot 'scripts/validate-lint-error-codes.ps1'

$tempBase = if ($env:TEMP) { $env:TEMP } elseif ($env:TMPDIR) { $env:TMPDIR } else { '/tmp' }
# The space is deliberate and load-bearing. `Start-Process -ArgumentList <array>` joins the array
# WITHOUT quoting, so a spaced path silently truncates at the space and pwsh answers its usage banner
# with exit 64 -- which reads as "the validator failed" and would keep the four failure-asserting
# scenarios green while measuring nothing. Every fixture lives under a spaced path so that regression
# cannot come back quietly.
$tempRoot = Join-Path $tempBase "test-validate-lint-error-codes $(Get-Random)"
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

# Build a synthetic repo that mimics the real layout: scripts/lint-*.ps1 files
# plus a cspell.json at the root. The validator resolves paths relative to
# $PSScriptRoot/..; copying the validator script under the same layout makes
# it scope its scan to our fixture.
function New-FixtureRoot {
    $root = Join-Path $tempRoot "repo-$(Get-Random)"
    New-Item -ItemType Directory -Path (Join-Path $root 'scripts') -Force | Out-Null
    # The extended harvester (P1-2) also scans scripts/tests/ and .githooks/.
    # Pre-create those dirs so individual tests can drop fixtures there without
    # repeating the boilerplate.
    New-Item -ItemType Directory -Path (Join-Path $root 'scripts/tests') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $root '.githooks') -Force | Out-Null
    Copy-Item -LiteralPath $validatorPath -Destination (Join-Path $root 'scripts/validate-lint-error-codes.ps1')
    Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts/run-node-bin.js') -Destination (Join-Path $root 'scripts/run-node-bin.js')
    # Seed a minimal-but-valid cspell.json matching the real config shape.
    # caseSensitive:false + minWordLength:3 mirror the real config so compound
    # splitting behavior is identical.
    $cspellSeed = @{
        '$schema'            = 'https://raw.githubusercontent.com/streetsidesoftware/cspell/main/cspell.schema.json'
        version              = '0.2'
        language             = 'en'
        files                = @()
        words                = @('UNH')
        flagWords            = @()
        minWordLength        = 3
        allowCompoundWords   = $true
        caseSensitive        = $false
    }
    $cspellSeed | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root 'cspell.json') -NoNewline

    # Share node_modules with the real repo so the repo-local cspell launcher works
    # inside the fixture CWD without re-downloading dependencies. A symlink is
    # safe here because the validator only reads node_modules — it never
    # writes. On platforms where symlink creation is forbidden (Windows non-
    # admin, some CI runners), fall back to a package.json that points npm at
    # the repo's node_modules directory via NODE_PATH. We prefer the symlink
    # path because Node package resolution follows symlinks transparently.
    $fixtureNodeModules = Join-Path $root 'node_modules'
    $realNodeModules = Join-Path $repoRoot 'node_modules'
    if (Test-Path -LiteralPath $realNodeModules) {
        try {
            New-Item -ItemType SymbolicLink -Path $fixtureNodeModules -Target $realNodeModules -ErrorAction Stop | Out-Null
        }
        catch {
            # Fallback: copy just the cspell bin + hoisted dirs. We keep this
            # narrow to avoid ballooning the fixture.
            Write-Info "Symlink creation failed ($_); falling back to copy."
            Copy-Item -LiteralPath $realNodeModules -Destination $fixtureNodeModules -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    return $root
}
# ── Scenarios ────────────────────────────────────────────────────────────────
# Every scenario is the same shape -- seed a fixture, run the validator in it,
# assert on the exit code and the output -- so they are declared as data and
# executed by one loop rather than written out nine times.
#
# The validator's cost is almost entirely cspell's start-up: measured on this
# devcontainer, `cspell --version` is 7.9 s against 8.5 s for real work, and the
# nine runs were 81.3 s of the contract suite's wall clock. They are independent
# processes over independent fixtures, so they are started together and waited
# for afterwards ([#540](https://github.com/Ambiguous-Interactive/unity-helpers/issues/540)).
$scenarios = @(
    @{
        # The single most important guard: the checked-in cspell.json already
        # covers every prefix the real lint scripts emit. A new lint-error-code
        # family shipped without a cspell entry breaks this before it can break
        # a pre-push hook.
        Name           = 'RealRepo.ValidatorPasses'
        UseRealRepo    = $true
        ExpectExitZero = $true
    },
    @{
        Name           = 'Fixture.OnlyKnownPrefix.Passes'
        Files          = [ordered]@{
            'scripts/lint-fake-good.ps1' = @"
# Synthetic lint script using only UNH (registered).
# UNH001 - example code
Write-Host "UNH001: something"
"@
        }
        ExpectExitZero = $true
    },
    @{
        # XYZ is never a real English word; cspell will not accept it via
        # compound splitting. A synthetic prefix guarantees the failure is for
        # the right reason (missing cspell registration), not a false positive.
        Name           = 'Fixture.UnregisteredPrefix.Fails'
        Files          = [ordered]@{
            'scripts/lint-fake-novel.ps1' = @"
# Synthetic lint script emitting a novel XYZ-family code.
Write-Host "XYZ001: unregistered prefix should fail the validator"
"@
        }
        ExpectExitZero = $false
        MustMatch      = @('\bXYZ\b', 'add_to_root_words', 'lint-fake-novel\.ps1:')
    },
    @{
        # An empty lint-*.{ps1,js} glob is a repo-layout error, not a silent
        # pass: renaming the whole family must not disable the check.
        Name           = 'Fixture.NoLintScripts.FailsLoudly'
        Files          = [ordered]@{}
        ExpectExitZero = $false
    },
    @{
        Name           = 'Fixture.LintScriptWithNoCodes.Passes'
        Files          = [ordered]@{
            'scripts/lint-fake-silent.ps1' = @"
# Synthetic lint script with no lint codes at all (emits prose only).
Write-Host "All good."
"@
        }
        ExpectExitZero = $true
    },
    @{
        # ABC is in cspell's default dictionary, so ABC001 splits and is
        # accepted. This is a positive control for reading .js at all, and its
        # exit code is deliberately not asserted.
        Name           = 'Fixture.JsLintScriptIsScanned'
        Files          = [ordered]@{
            'scripts/lint-fake-js.js' = @"
// Synthetic JS lint script emitting a novel ABC-family code.
console.log('ABC001: unregistered prefix should fail the validator');
"@
        }
        IgnoreExitCode = $true
        MustNotMatch   = @('FullyQualifiedErrorId')
    },
    @{
        # Hook error messages cite lint-error-code families. A novel prefix
        # emitted from a hook but never from a lint script must still be caught,
        # so the lint script here deliberately emits nothing.
        Name           = 'Fixture.PrefixOnlyInGithooks.Detected'
        Files          = [ordered]@{
            'scripts/lint-silent.ps1' = @"
# Silent lint script -- emits no codes.
Write-Host 'All good.'
"@
            '.githooks/pre-commit'    = @"
#!/usr/bin/env bash
# pre-commit emits HOK001 as a failure code -- unregistered with cspell.
echo 'HOK001: hook-emitted code that must be harvested'
"@
        }
        ExpectExitZero = $false
        MustMatch      = @('\bHOK\b', '\.githooks/pre-commit:')
    },
    @{
        # Test assertions reference error codes, so a code used only in tests
        # would otherwise look valid to cspell but be missing from the contract.
        Name           = 'Fixture.PrefixOnlyInTests.Detected'
        Files          = [ordered]@{
            'scripts/lint-silent.ps1'             = @"
# Silent lint script.
Write-Host 'silent'
"@
            'scripts/tests/test-lint-novel.ps1' = @"
# A test file that asserts TST001 is emitted -- the prefix is introduced here,
# not in any lint-*.ps1, and must still be flagged.
Write-Host 'TST001: test-only prefix'
"@
        }
        ExpectExitZero = $false
        MustMatch      = @('\bTST\b', 'test-lint-novel\.ps1:')
    },
    @{
        # `# shellcheck disable=SC2016` is a legitimate reference in our scripts.
        # The harvester must not demand cspell registration for an upstream
        # linter's family.
        Name           = 'Fixture.UpstreamRuleAllowlist.SkipsSCandMD'
        Files          = [ordered]@{
            'scripts/lint-upstream-refs.ps1' = @"
# Synthetic lint script that references SC2016 in a comment disable tag,
# and MD025 in a markdownlint rule reference. Neither should cause the
# validator to flag SC or MD as missing from cspell.
# shellcheck disable=SC2016
# See MD025 (markdownlint) upstream.
Write-Host 'nothing emitted'
"@
        }
        ExpectExitZero = $true
        MustNotMatch   = @('(?m)^\s+SC\b', '(?m)^\s+MD\b')
    }
)

# ── Phase 1: seed every fixture and start every validator ────────────────────
$running = @()
foreach ($scenario in $scenarios) {
    $useRealRepo = $scenario.Contains('UseRealRepo') -and $scenario.UseRealRepo
    if ($useRealRepo) {
        $workingDirectory = $repoRoot
        $scriptPath = $validatorPath
        $argumentList = @('-NoProfile', '-File', $scriptPath)
    }
    else {
        $workingDirectory = New-FixtureRoot
        foreach ($relativePath in $scenario.Files.Keys) {
            $destination = Join-Path $workingDirectory $relativePath
            Set-Content -LiteralPath $destination -Value $scenario.Files[$relativePath]
        }
        $scriptPath = Join-Path $workingDirectory 'scripts/validate-lint-error-codes.ps1'
        $argumentList = @('-NoProfile', '-File', $scriptPath, '-VerboseOutput')
    }

    Write-Info "Starting $($scenario.Name) in $workingDirectory"
    # ProcessStartInfo.ArgumentList escapes each argument individually; Start-Process's -ArgumentList
    # does not. Both streams are drained asynchronously from the moment the process starts, so a
    # scenario that fills a pipe cannot deadlock against the WaitForExit below.
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'pwsh'
    foreach ($argument in $argumentList) { [void]$startInfo.ArgumentList.Add($argument) }
    $startInfo.WorkingDirectory = $workingDirectory
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false
    $process = [System.Diagnostics.Process]::Start($startInfo)
    $running += @{
        Scenario       = $scenario
        Process        = $process
        StandardOutput = $process.StandardOutput.ReadToEndAsync()
        StandardError  = $process.StandardError.ReadToEndAsync()
    }
}

# ── Phase 2: wait, then assert in declaration order ──────────────────────────
foreach ($entry in $running) {
    $scenario = $entry.Scenario
    Write-Host "`n  Section: $($scenario.Name)" -ForegroundColor White
    try {
        $entry.Process.WaitForExit()
        $exitCode = $entry.Process.ExitCode
        $output = $entry.StandardOutput.GetAwaiter().GetResult() +
            $entry.StandardError.GetAwaiter().GetResult()

        $reasons = @()
        if (-not ($scenario.Contains('IgnoreExitCode') -and $scenario.IgnoreExitCode)) {
            $exitOk = if ($scenario.ExpectExitZero) { $exitCode -eq 0 } else { $exitCode -ne 0 }
            if (-not $exitOk) {
                $expectation = if ($scenario.ExpectExitZero) { 'zero' } else { 'non-zero' }
                $reasons += "expected $expectation exit, got $exitCode"
            }
        }
        if ($scenario.Contains('MustMatch')) {
            foreach ($pattern in $scenario.MustMatch) {
                if ($output -notmatch $pattern) { $reasons += "output did not match /$pattern/" }
            }
        }
        if ($scenario.Contains('MustNotMatch')) {
            foreach ($pattern in $scenario.MustNotMatch) {
                if ($output -match $pattern) { $reasons += "output unexpectedly matched /$pattern/" }
            }
        }

        Write-TestResult $scenario.Name ($reasons.Count -eq 0) "$($reasons -join '; '). Exit: $exitCode. Output: $output"
    }
    catch {
        Write-TestResult $scenario.Name $false "Exception: $_"
    }
}


# ── Cleanup ──────────────────────────────────────────────────────────────────
if (Test-Path -LiteralPath $tempRoot) {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}

# ── Summary ──────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "Results:" -ForegroundColor Magenta
Write-Host "  Passed: $script:TestsPassed"
Write-Host "  Failed: $script:TestsFailed"

if ($script:TestsFailed -gt 0) {
    Write-Host ""
    Write-Host "Failed tests:" -ForegroundColor Red
    foreach ($failedTest in $script:FailedTests) {
        Write-Host "  - $failedTest" -ForegroundColor Yellow
    }
    exit 1
}

exit 0
