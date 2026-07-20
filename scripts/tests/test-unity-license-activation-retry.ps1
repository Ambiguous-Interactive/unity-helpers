#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Data-driven tests for the Unity serial-activation retry policy in
    scripts/unity/run-ci-tests.ps1 (issue #57).

.DESCRIPTION
    The organization build lock's release cooldown is deliberately near-zero, so a
    freshly acquired seat can hit Unity error 20111 ("maximum number of
    activations") while the previous holder's seat is still propagating as
    returned. Invoke-UnityLicenseActivate now retries that transient contention
    within a bounded wall-clock budget with jittered exponential backoff, while
    failing fast on permanent errors and preserving the 20111 evidence for the
    account-incident path when contention does NOT clear.

    This runner extracts the real function definitions from run-ci-tests.ps1 via
    the PowerShell AST (the script's top-level param()/execution make it
    non-dot-sourceable) and exercises them WITHOUT launching Unity:
      * the pure classifier / backoff / budget helpers via data-driven tables, and
      * the full retry loop via Invoke-UnityLicenseActivate's testability seam
        (-ActivationInvoker), a fake editor that is deterministic and needs no
        external process, so this runs on ubuntu-latest and Windows alike.
#>
[CmdletBinding()]
param(
    [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $PSScriptRoot
$target = Join-Path $scriptRoot 'unity/run-ci-tests.ps1'
if (-not (Test-Path -LiteralPath $target -PathType Leaf)) {
    Write-Host "FATAL: cannot find run-ci-tests.ps1 at $target"
    exit 1
}

# Pull the real function definitions out of run-ci-tests.ps1 without running its
# main flow (top-level param() + execution make it non-dot-sourceable). Same idiom
# as test-process-watchdog.ps1.
$tokens = $null
$errs = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($target, [ref]$tokens, [ref]$errs)
if ($errs -and $errs.Count -gt 0) {
    Write-Host "FATAL: run-ci-tests.ps1 has parse errors:"
    $errs | ForEach-Object { Write-Host "  $($_.Extent.StartLineNumber): $($_.Message)" }
    exit 1
}
foreach ($name in @(
    'Get-UnityActivationRetryBudgetSeconds',
    'Get-UnityActivationRetryDelaySeconds',
    'Get-UnityActivationFailureClass',
    'Write-CiNotice',
    'Invoke-UnityLicenseActivate'
)) {
    $fn = $ast.FindAll(
        {
            param($n)
            $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name
        },
        $true
    ) | Select-Object -First 1
    if (-not $fn) {
        Write-Host "FATAL: function '$name' not found in run-ci-tests.ps1"
        exit 1
    }
    Invoke-Expression $fn.Extent.Text
}

$passed = 0
$failed = 0

function Assert-That {
    param([string]$Description, [bool]$Condition)
    if ($Condition) {
        if ($VerboseOutput) { Write-Host "  PASS: $Description" }
        $script:passed++
    } else {
        Write-Host "  FAIL: $Description"
        $script:failed++
    }
}

# ---------------------------------------------------------------------------
# 1. Classifier (Get-UnityActivationFailureClass) — data-driven.
#    Real Unity licensing log fragments -> (Class, Reason). The boundary rows
#    (201110 / 120111 must NOT match 20111) protect the incident guard rails.
# ---------------------------------------------------------------------------
$classifierCases = @(
    @{ Name = 'exit 0 is success regardless of log'; ExitCode = 0; Log = 'anything at all'; Class = 'success'; Reason = 'activated' }
    @{ Name = '20111 numeric code -> retryable account-limit'; ExitCode = 1; Log = 'Licensing::Client Error: Code 20111'; Class = 'retryable'; Reason = 'account-limit-20111' }
    @{ Name = '20111 human string -> retryable account-limit'; ExitCode = 1; Log = 'serial has reached the maximum number of activations'; Class = 'retryable'; Reason = 'account-limit-20111' }
    @{ Name = '20111 human string is case-insensitive'; ExitCode = 1; Log = 'Maximum Number Of Activations reached'; Class = 'retryable'; Reason = 'account-limit-20111' }
    @{ Name = '20111 wins even with a nonzero-but-odd exit'; ExitCode = 137; Log = 'Error: Code 20111 reached'; Class = 'retryable'; Reason = 'account-limit-20111' }
    @{ Name = '201110 does NOT match 20111 (trailing digit guard)'; ExitCode = 1; Log = 'Error: Code 201110 something else'; Class = 'retryable'; Reason = 'unknown' }
    @{ Name = '120111 does NOT match 20111 (leading digit guard)'; ExitCode = 1; Log = 'Error: Code 120111 something else'; Class = 'retryable'; Reason = 'unknown' }
    @{ Name = '20113 numeric code -> hard serial-expired'; ExitCode = 1; Log = 'Licensing::Client Error: Code 20113'; Class = 'hard'; Reason = 'serial-expired-20113' }
    @{ Name = '20113 human string -> hard serial-expired'; ExitCode = 1; Log = 'the serial expired on 2026-01-01'; Class = 'hard'; Reason = 'serial-expired-20113' }
    @{ Name = '20111 takes precedence over a co-occurring 20113'; ExitCode = 1; Log = 'Code 20113 ... maximum number of activations'; Class = 'retryable'; Reason = 'account-limit-20111' }
    @{ Name = 'process kill (137) with empty log -> retryable unknown'; ExitCode = 137; Log = ''; Class = 'retryable'; Reason = 'unknown' }
    @{ Name = 'timeout sentinel (124) -> retryable unknown'; ExitCode = 124; Log = 'wall-clock timeout'; Class = 'retryable'; Reason = 'unknown' }
    @{ Name = 'network/token licensing error (20105) -> retryable unknown'; ExitCode = 1; Log = 'Licensing::Client Error: Code 20105 token unavailable'; Class = 'retryable'; Reason = 'unknown' }
    @{ Name = 'generic nonzero with no signal -> retryable unknown'; ExitCode = 1; Log = 'some unrelated failure'; Class = 'retryable'; Reason = 'unknown' }
    @{ Name = 'nonzero with null log is tolerated -> retryable unknown'; ExitCode = 1; Log = $null; Class = 'retryable'; Reason = 'unknown' }
)
foreach ($c in $classifierCases) {
    $result = Get-UnityActivationFailureClass -ExitCode $c.ExitCode -LogText $c.Log
    Assert-That "classify: $($c.Name) [Class]" ($result.Class -eq $c.Class)
    Assert-That "classify: $($c.Name) [Reason]" ($result.Reason -eq $c.Reason)
}

# ---------------------------------------------------------------------------
# 2. Backoff ceiling (Get-UnityActivationRetryDelaySeconds) — data-driven.
#    min(cap, base * 2^(N-1)) with base=5, cap=30 by default.
# ---------------------------------------------------------------------------
$backoffCases = @(
    @{ Attempt = 1; Expected = 5 }
    @{ Attempt = 2; Expected = 10 }
    @{ Attempt = 3; Expected = 20 }
    @{ Attempt = 4; Expected = 30 }   # 40 capped to 30
    @{ Attempt = 5; Expected = 30 }   # 80 capped to 30
    @{ Attempt = 50; Expected = 30 }  # no overflow, stays capped
    @{ Attempt = 0; Expected = 5 }    # clamped up to attempt 1
    @{ Attempt = -3; Expected = 5 }   # negative clamped up to attempt 1
)
foreach ($b in $backoffCases) {
    $d = Get-UnityActivationRetryDelaySeconds -Attempt $b.Attempt
    Assert-That "backoff: attempt $($b.Attempt) -> $($b.Expected)s" ($d -eq $b.Expected)
}
Assert-That "backoff: custom base/cap honored (attempt 3, base 2, cap 100 -> 8)" (
    (Get-UnityActivationRetryDelaySeconds -Attempt 3 -BaseSeconds 2 -CapSeconds 100) -eq 8
)

# ---------------------------------------------------------------------------
# 3. Budget env parsing (Get-UnityActivationRetryBudgetSeconds).
# ---------------------------------------------------------------------------
$savedBudgetEnv = $env:UH_ACTIVATION_RETRY_BUDGET_SECONDS
try {
    $env:UH_ACTIVATION_RETRY_BUDGET_SECONDS = $null
    Assert-That "budget: unset -> default 360" ((Get-UnityActivationRetryBudgetSeconds) -eq 360)
    Assert-That "budget: unset honors an explicit default" ((Get-UnityActivationRetryBudgetSeconds -Default 90) -eq 90)

    $env:UH_ACTIVATION_RETRY_BUDGET_SECONDS = '120'
    Assert-That "budget: valid override 120 -> 120" ((Get-UnityActivationRetryBudgetSeconds) -eq 120)

    $env:UH_ACTIVATION_RETRY_BUDGET_SECONDS = '0'
    Assert-That "budget: 0 is the explicit opt-out (single attempt)" ((Get-UnityActivationRetryBudgetSeconds) -eq 0)

    $env:UH_ACTIVATION_RETRY_BUDGET_SECONDS = '-5'
    Assert-That "budget: negative override ignored -> default 360" ((Get-UnityActivationRetryBudgetSeconds) -eq 360)

    $env:UH_ACTIVATION_RETRY_BUDGET_SECONDS = 'notanumber'
    Assert-That "budget: non-integer override ignored -> default 360" ((Get-UnityActivationRetryBudgetSeconds) -eq 360)
} finally {
    if ($null -eq $savedBudgetEnv) {
        Remove-Item Env:UH_ACTIVATION_RETRY_BUDGET_SECONDS -ErrorAction SilentlyContinue
    } else {
        $env:UH_ACTIVATION_RETRY_BUDGET_SECONDS = $savedBudgetEnv
    }
}

# ---------------------------------------------------------------------------
# 4. Retry loop (Invoke-UnityLicenseActivate) via the -ActivationInvoker seam.
#    The fake editor is deterministic, writes the log like Tee-Object (OVERWRITE
#    each attempt), and needs no external process. We assert OUTCOMES (attempt
#    count, throw/return, and the final-attempt log invariant), never timing.
# ---------------------------------------------------------------------------
$tmpRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("uh-activation-retry-{0}" -f ([System.Guid]::NewGuid().ToString('N')))
New-Item -ItemType Directory -Force -Path $tmpRoot | Out-Null

# Build a fake invoker from a per-attempt plan of tokens. Each token decides that
# attempt's exit code + the log line the fake writes (mirroring Unity's -logFile -
# stdout being Tee'd, OVERWRITING the log). $script:invokerAttempts records calls.
function New-FakeInvoker {
    param([string[]]$Plan)
    $state = [pscustomobject]@{ Count = 0 }
    $script:lastInvokerState = $state
    return {
        param($activateArgs, $logPath)
        $state.Count++
        $token = if ($state.Count -le $Plan.Count) { $Plan[$state.Count - 1] } else { $Plan[$Plan.Count - 1] }
        switch ($token) {
            '20111'   { $line = 'Licensing::Client Error: Code 20111 reached the maximum number of activations'; $code = 1 }
            'expired' { $line = 'Licensing::Client Error: Code 20113 serial expired'; $code = 1 }
            'fail'    { $line = 'generic activation failure with no known signal'; $code = 1 }
            default   { $line = 'License activated successfully'; $code = 0 }
        }
        # OVERWRITE the log to mirror `Tee-Object -FilePath` (no -Append): the file
        # must reflect ONLY this (final) attempt.
        Set-Content -LiteralPath $logPath -Value $line -Encoding UTF8
        return $code
    }.GetNewClosure()
}

function Invoke-FakeActivation {
    param([string[]]$Plan, [int]$BudgetSeconds, [string]$Label)
    $log = Join-Path $tmpRoot ("activate-{0}.log" -f $Label)
    $invoker = New-FakeInvoker -Plan $Plan
    $threw = $false
    $errMsg = ''
    try {
        # Distinctive secret sentinels so the credential-leak assertion is meaningful.
        Invoke-UnityLicenseActivate -EditorPath 'fake' `
            -Serial 'SECRET-SERIAL-ZZZ' -Email 'SECRET-EMAIL-ZZZ' -Password 'SECRET-PW-ZZZ' `
            -LogPath $log -RetryBudgetSeconds $BudgetSeconds -ActivationInvoker $invoker
    } catch {
        $threw = $true
        $errMsg = $_.Exception.Message
    }
    return [pscustomobject]@{
        Threw    = $threw
        Message  = $errMsg
        Attempts = $script:lastInvokerState.Count
        FinalLog = (Get-Content -LiteralPath $log -Raw)
    }
}

# 4a. Success on the first attempt: no retry, returns cleanly.
$r = Invoke-FakeActivation -Plan @('ok') -BudgetSeconds 60 -Label 'first-success'
Assert-That "loop: first-attempt success does not throw" (-not $r.Threw)
Assert-That "loop: first-attempt success makes exactly 1 attempt" ($r.Attempts -eq 1)

# 4b. Transient 20111 then success: retries and succeeds; final log is CLEAN (no
#     stale 20111 for the return-side classifier to mis-read as an incident).
$r = Invoke-FakeActivation -Plan @('20111', 'ok') -BudgetSeconds 60 -Label 'retry-then-success'
Assert-That "loop: 20111-then-success does not throw" (-not $r.Threw)
Assert-That "loop: 20111-then-success makes exactly 2 attempts" ($r.Attempts -eq 2)
Assert-That "loop: 20111-then-success leaves a CLEAN final log (no stale 20111)" ($r.FinalLog -notmatch '20111')

# 4c. Persistent 20111 exhausts the budget: throws, names the account-limit
#     reason, and the final log STILL contains 20111 so the existing return-side
#     classifier raises the account incident.
$r = Invoke-FakeActivation -Plan @('20111') -BudgetSeconds 2 -Label 'persistent-20111'
Assert-That "loop: persistent 20111 throws once the budget is exhausted" ($r.Threw)
Assert-That "loop: persistent 20111 makes at least 2 attempts before giving up" ($r.Attempts -ge 2)
Assert-That "loop: persistent 20111 throw names the account-limit reason" ($r.Message -match 'account-limit-20111')
Assert-That "loop: persistent 20111 preserves the 20111 evidence in the final log" ($r.FinalLog -match '20111')
Assert-That "loop: throw message never leaks the serial/email/password" ($r.Message -notmatch 'SECRET')
Assert-That "loop: throw message points at the non-uploaded activation log" ($r.Message -match 'activation log')

# 4d. Hard failure (serial expired) fails fast: exactly one attempt, no retry.
$r = Invoke-FakeActivation -Plan @('expired', 'ok') -BudgetSeconds 60 -Label 'hard-fast'
Assert-That "loop: hard serial-expired throws" ($r.Threw)
Assert-That "loop: hard serial-expired does NOT retry (exactly 1 attempt)" ($r.Attempts -eq 1)
Assert-That "loop: hard serial-expired names the not-retryable reason" ($r.Message -match 'serial-expired-20113')

# 4e. Budget 0 is the opt-out: a single attempt, legacy fail-fast on any nonzero.
$r = Invoke-FakeActivation -Plan @('20111', 'ok') -BudgetSeconds 0 -Label 'budget-zero'
Assert-That "loop: budget 0 makes exactly 1 attempt (no retry)" ($r.Attempts -eq 1)
Assert-That "loop: budget 0 throws on the single failing attempt" ($r.Threw)

# 4f. Budget 0 with an immediate success still succeeds in one attempt.
$r = Invoke-FakeActivation -Plan @('ok') -BudgetSeconds 0 -Label 'budget-zero-success'
Assert-That "loop: budget 0 success returns in exactly 1 attempt" (($r.Attempts -eq 1) -and (-not $r.Threw))

Remove-Item -LiteralPath $tmpRoot -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Unity license activation-retry tests: $passed passed, $failed failed."
if ($failed -gt 0) {
    exit 1
}
Write-Host "All Unity license activation-retry tests passed."
exit 0
