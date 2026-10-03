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
. (Join-Path $PSScriptRoot 'isolated-fixture-runspace.ps1')

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
# Independent fixtures overlap while retaining native Node/cspell execution.
$scenarios = @(
    @{
        # The single most important guard: the checked-in cspell.json already
        # covers every prefix the real lint scripts emit. A new lint-error-code
        # family shipped without a cspell entry breaks this before it can break
        # a pre-push hook.
        Name           = 'RealRepo.ValidatorPasses'
        UseRealRepo    = $true
        ExpectedExit   = 0
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
        ExpectedExit   = 0
    },
    @{
        # Explicitly flag the probe token in this fixture's cspell config. This
        # keeps the negative control deterministic as dictionaries evolve.
        Name           = 'Fixture.UnregisteredPrefix.Fails'
        Files          = [ordered]@{
            'scripts/lint-fake-novel.ps1' = @"
# Synthetic lint script emitting a novel XYZ-family code.
Write-Host "XYZ001: unregistered prefix should fail the validator"
"@
        }
        FlagWords      = @('XYZ001')
        ExpectedExit   = 1
        MustMatch      = @('\bXYZ\b', '"add_to_root_words":\s*\[\s*"XYZ"\s*\]', 'scripts/lint-fake-novel\.ps1:2\b')
    },
    @{
        # An empty lint-*.{ps1,js} glob is a repo-layout error, not a silent
        # pass: renaming the whole family must not disable the check.
        Name           = 'Fixture.NoLintScripts.FailsLoudly'
        Files          = [ordered]@{}
        ExpectedExit   = 1
        MustMatch      = @('No lint/test/hook files found', 'Repository layout may have changed')
    },
    @{
        Name           = 'Fixture.LintScriptWithNoCodes.Passes'
        Files          = [ordered]@{
            'scripts/lint-fake-silent.ps1' = @"
# Synthetic lint script with no lint codes at all (emits prose only).
Write-Host "All good."
"@
        }
        ExpectedExit   = 0
    },
    @{
        # ABC is in cspell's default dictionary, so ABC001 splits and is
        # accepted. This is a positive control for reading .js at all, and its
        # exit code is asserted along with the harvested prefix.
        Name           = 'Fixture.JsLintScriptIsScanned'
        Files          = [ordered]@{
            'scripts/lint-fake-js.js' = @"
// Synthetic JS lint script emitting a novel ABC-family code.
console.log('ABC001: unregistered prefix should fail the validator');
"@
        }
        ExpectedExit   = 0
        MustMatch      = @('Harvested 1 unique prefix\(es\): ABC')
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
        ExpectedExit   = 1
        MustMatch      = @('\bHOK\b', '\.githooks/pre-commit:2\b', '\.githooks/pre-commit:3\b')
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
        ExpectedExit   = 1
        MustMatch      = @('\bTST\b', 'scripts/tests/test-lint-novel\.ps1:1\b', 'scripts/tests/test-lint-novel\.ps1:3\b')
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
        ExpectedExit   = 0
        MustNotMatch   = @('(?m)^\s+SC\b', '(?m)^\s+MD\b')
    }
)

function Start-ValidatorCli {
    param([string]$ScriptPath, [string]$WorkingDirectory, [switch]$VerboseValidator)

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'pwsh'
    foreach ($argument in @('-NoProfile', '-File', $ScriptPath)) { [void]$startInfo.ArgumentList.Add($argument) }
    if ($VerboseValidator) { [void]$startInfo.ArgumentList.Add('-VerboseOutput') }
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false
    $process = [System.Diagnostics.Process]::Start($startInfo)
    try {
        return @{
            Process = $process
            StandardOutput = $process.StandardOutput.ReadToEndAsync()
            StandardError = $process.StandardError.ReadToEndAsync()
            Disposed = $false
        }
    }
    catch {
        try {
            if (-not $process.HasExited) { $process.Kill($true) }
            $process.WaitForExit()
        }
        finally { $process.Dispose() }
        throw
    }
}

function Stop-ValidatorCli {
    param([hashtable]$Fixture)

    if ($Fixture.Disposed) { return }
    try {
        if (-not $Fixture.Process.HasExited) { $Fixture.Process.Kill($true) }
        $Fixture.Process.WaitForExit()
    }
    finally {
        $Fixture.Process.Dispose()
        $Fixture.Disposed = $true
    }
}

function Complete-ValidatorCli {
    param([hashtable]$Fixture)

    try {
        $Fixture.Process.WaitForExit()
        return @{
            ExitCode = $Fixture.Process.ExitCode
            Output = $Fixture.StandardOutput.GetAwaiter().GetResult() + $Fixture.StandardError.GetAwaiter().GetResult()
        }
    }
    finally { Stop-ValidatorCli -Fixture $Fixture }
}

function Test-OverlappingFixtures {
    $parentLocation = (Get-Location).Path
    $releasePath = Join-Path $tempRoot 'release-controls'
    $controls = @()
    try {
        foreach ($label in @('first', 'second')) {
            $directory = New-FixtureRoot
            $path = Join-Path $directory 'scripts/overlap-control.ps1'
            $readyPath = Join-Path $directory 'ready'
            Set-Content -LiteralPath $path -Value @'
param($Label, $ReadyPath, $ReleasePath)
if (Get-Variable OverlapFixtureState -Scope Global -ErrorAction SilentlyContinue) { exit 7 }
$global:OverlapFixtureState = $Label
Set-Content -LiteralPath $ReadyPath -Value $Label
$deadline = [DateTime]::UtcNow.AddSeconds(20)
while (-not (Test-Path -LiteralPath $ReleasePath)) {
    if ([DateTime]::UtcNow -gt $deadline) { throw 'overlap control timed out' }
    Start-Sleep -Milliseconds 10
}
if ($global:OverlapFixtureState -cne $Label) { exit 7 }
Write-Host "label:$Label"
Write-Host "working:$((Get-Location).Path)"
Write-Host "script:$PSScriptRoot"
Write-Host "command:$PSCommandPath"
exit 0
'@
            $fixture = Start-IsolatedFixture -ScriptPath $path -WorkingDirectory $directory -Parameters @{
                Label = $label; ReadyPath = $readyPath; ReleasePath = $releasePath
            }
            $controls += @{ Label = $label; Directory = $directory; Path = $path; ReadyPath = $readyPath; Fixture = $fixture }
        }
        $deadline = [DateTime]::UtcNow.AddSeconds(10)
        while (@($controls | Where-Object { -not (Test-Path -LiteralPath $_.ReadyPath) }).Count -gt 0 -and [DateTime]::UtcNow -lt $deadline) {
            Start-Sleep -Milliseconds 10
        }
        $overlap = @($controls | Where-Object {
            (Test-Path -LiteralPath $_.ReadyPath) -and $_.Fixture.Runner.InvocationStateInfo.State -eq 'Running'
        }).Count -eq 2
        Write-TestResult 'Runspace.IndependentFixturesOverlap' $overlap
        Set-Content -LiteralPath $releasePath -Value 'release'
        foreach ($control in $controls) {
            $result = Complete-IsolatedFixture -Fixture $control.Fixture
            $expectedOutput = @(
                "label:$($control.Label)", "working:$($control.Directory)",
                "script:$(Split-Path -Parent $control.Path)", "command:$($control.Path)"
            ) -join [Environment]::NewLine
            Write-TestResult "Runspace.OverlappingGlobalLocationAndScriptPath[$($control.Label)]" (
                $result.ExitCode -eq 0 -and $result.Output -ceq $expectedOutput
            ) "exit $($result.ExitCode); output: $($result.Output)"
            Write-TestResult "Runspace.DisposedAfterCompletion[$($control.Label)]" (
                $control.Fixture.Disposed -and $control.Fixture.Runspace.RunspaceStateInfo.State -eq 'Closed'
            )
        }
        Write-TestResult 'Runspace.ParentLocationPreserved' ((Get-Location).Path -ceq $parentLocation)
    }
    finally {
        $cleanupErrors = @()
        foreach ($control in $controls) {
            try { Stop-IsolatedFixture -Fixture $control.Fixture }
            catch { $cleanupErrors += $_.Exception.Message }
        }
        if ($cleanupErrors.Count -gt 0) { throw ($cleanupErrors -join '; ') }
    }

    foreach ($control in @(
        @{ Name = 'ErrorExitZero'; Content = "Write-Error 'async-error' -ErrorAction Continue; exit 0"; Diagnostic = 'PowerShell error' },
        @{ Name = 'ErrorExitOne'; Content = "Write-Error 'async-error' -ErrorAction Continue; exit 1"; Diagnostic = 'PowerShell error' },
        @{ Name = 'Throw'; Content = "throw 'async-throw'; exit 0"; Diagnostic = 'async-throw' }
    )) {
        $path = Join-Path $tempRoot 'async-error-control.ps1'
        Set-Content -LiteralPath $path -Value $control.Content
        $fixture = Start-IsolatedFixture -ScriptPath $path -WorkingDirectory $tempRoot
        $rejected = $false
        try { $null = Complete-IsolatedFixture -Fixture $fixture }
        catch { $rejected = $_.Exception.Message.Contains($control.Diagnostic) }
        finally { Stop-IsolatedFixture -Fixture $fixture }
        Write-TestResult "Runspace.AsyncRejects$($control.Name)AndDisposes" (
            $rejected -and $fixture.Disposed -and $fixture.Runspace.RunspaceStateInfo.State -eq 'Closed'
        )
    }

    $path = Join-Path $tempRoot 'async-cancel-control.ps1'
    $readyPath = Join-Path $tempRoot 'cancel-ready'
    Set-Content -LiteralPath $path -Value @'
param($ReadyPath)
Set-Content -LiteralPath $ReadyPath -Value 'ready'
Start-Sleep -Seconds 20
exit 0
'@
    $fixture = Start-IsolatedFixture -ScriptPath $path -Parameters @{ ReadyPath = $readyPath }
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(10)
        while (-not (Test-Path -LiteralPath $readyPath) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 10 }
        $wasRunning = (Test-Path -LiteralPath $readyPath) -and $fixture.Runner.InvocationStateInfo.State -eq 'Running'
        Stop-IsolatedFixture -Fixture $fixture
        Write-TestResult 'Runspace.CancelsAndDisposesActiveFixture' (
            $wasRunning -and $fixture.Disposed -and $fixture.Runspace.RunspaceStateInfo.State -eq 'Closed'
        )
    }
    finally { Stop-IsolatedFixture -Fixture $fixture }
}

$running = @()
$results = @{}
try {
    Test-IsolatedFixtureHarness
    Test-OverlappingFixtures

    foreach ($scenario in $scenarios) {
        $useRealRepo = $scenario.Contains('UseRealRepo') -and $scenario.UseRealRepo
        if ($useRealRepo) {
            $workingDirectory = $repoRoot
            $scriptPath = $validatorPath
        }
        else {
            $workingDirectory = New-FixtureRoot
            if ($scenario.Contains('FlagWords')) {
                $fixtureCspellPath = Join-Path $workingDirectory 'cspell.json'
                $fixtureCspell = Get-Content -LiteralPath $fixtureCspellPath -Raw | ConvertFrom-Json
                $fixtureCspell.flagWords = @($scenario.FlagWords)
                $fixtureCspell | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $fixtureCspellPath -NoNewline
            }
            foreach ($relativePath in $scenario.Files.Keys) {
                $destination = Join-Path $workingDirectory $relativePath
                Set-Content -LiteralPath $destination -Value $scenario.Files[$relativePath]
            }
            $scriptPath = Join-Path $workingDirectory 'scripts/validate-lint-error-codes.ps1'
        }

        Write-Info "Starting $($scenario.Name) in $workingDirectory"
        $fixture = if ($useRealRepo) {
            Start-ValidatorCli -ScriptPath $scriptPath -WorkingDirectory $workingDirectory
        } else {
            Start-IsolatedFixture -ScriptPath $scriptPath -WorkingDirectory $workingDirectory -Parameters @{ VerboseOutput = $true }
        }
        $running += @{ Scenario = $scenario; Fixture = $fixture; UseCli = $useRealRepo; CompareWith = '' }

        if ($scenario.Name -in @('Fixture.OnlyKnownPrefix.Passes', 'Fixture.UnregisteredPrefix.Fails')) {
            $cliFixture = Start-ValidatorCli -ScriptPath $scriptPath -WorkingDirectory $workingDirectory -VerboseValidator
            $running += @{
                Scenario = @{ Name = "CLI.SpacedPathVerboseParity[$($scenario.Name)]"; ExpectedExit = $scenario.ExpectedExit }
                Fixture = $cliFixture; UseCli = $true; CompareWith = $scenario.Name
            }
        }
    }

    foreach ($entry in $running) {
        $scenario = $entry.Scenario
        Write-Host "`n  Section: $($scenario.Name)" -ForegroundColor White
        try {
            $result = if ($entry.UseCli) { Complete-ValidatorCli -Fixture $entry.Fixture }
                else { Complete-IsolatedFixture -Fixture $entry.Fixture }
            $results[$scenario.Name] = $result
            $reasons = @()
            if ($result.ExitCode -ne $scenario.ExpectedExit) { $reasons += "expected exit $($scenario.ExpectedExit), got $($result.ExitCode)" }
            if ($scenario.Contains('MustMatch')) {
                foreach ($pattern in $scenario.MustMatch) {
                    if ($result.Output -notmatch $pattern) { $reasons += "output did not match /$pattern/" }
                }
            }
            if ($scenario.Contains('MustNotMatch')) {
                foreach ($pattern in $scenario.MustNotMatch) {
                    if ($result.Output -match $pattern) { $reasons += "output unexpectedly matched /$pattern/" }
                }
            }
            if ($entry.CompareWith) {
                if (-not $results.ContainsKey($entry.CompareWith)) { $reasons += 'runspace parity result is absent' }
                else {
                    $runspaceResult = $results[$entry.CompareWith]
                    if ($runspaceResult.ExitCode -ne $result.ExitCode -or $runspaceResult.Output.TrimEnd() -cne $result.Output.TrimEnd()) {
                        $reasons += 'CLI exit or complete output differs from runspace'
                    }
                }
            }
            Write-TestResult $scenario.Name ($reasons.Count -eq 0) "$($reasons -join '; '). Exit: $($result.ExitCode). Output: $($result.Output)"
        }
        catch { Write-TestResult $scenario.Name $false "Exception: $_" }
    }
}
catch { Write-TestResult 'Harness.SetupOrControlFailure' $false "Exception: $_" }
finally {
    foreach ($entry in $running) {
        try {
            if ($entry.UseCli) { Stop-ValidatorCli -Fixture $entry.Fixture }
            else { Stop-IsolatedFixture -Fixture $entry.Fixture }
        }
        catch { Write-TestResult "Harness.Cleanup[$($entry.Scenario.Name)]" $false "Exception: $_" }
    }
    if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
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
