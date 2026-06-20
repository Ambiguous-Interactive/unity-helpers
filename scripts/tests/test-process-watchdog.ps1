#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Test runner for scripts/unity/run-ci-tests.ps1's process-watchdog completion guard.

.DESCRIPTION
    Verifies the completion-sentinel behavior of Invoke-ProcessWithTreeKillTimeout
    (and its Set-CompletionGraceDeadline helper) that immunizes the -runTests editor
    passes against the Unity 2021.3 -batchmode shutdown deadlock (tests finish and
    write results.xml, then the editor hangs forever):

      1. A process that logs the completion sentinel and then HANGS is tree-killed
         within the grace window (NOT after the full wall-clock timeout), reports
         TimedOut=$false, and carries the exit code parsed from the sentinel
         ("Exiting with code N") for both a passing (0) and a failing (2) run.
      2. A process that exits normally without ever logging the sentinel returns its
         real exit code with TimedOut=$false (the guard is inert on the happy path).
      3. A process that hangs WITHOUT logging the sentinel still hits the plain
         wall-clock deadline -> TimedOut=$true, exit 124 (the backstop is intact).

    The real function bodies are extracted from run-ci-tests.ps1 via the PowerShell
    AST (the script is not dot-sourceable: it has a top-level param() + main flow), so
    this exercises the SHIPPING code, not a copy. Self-contained, deterministic,
    cross-platform (spawns short-lived pwsh child processes on loopback-free logic).

.PARAMETER VerboseOutput
    Show detailed output during test execution.

.EXAMPLE
    pwsh -NoProfile -File scripts/tests/test-process-watchdog.ps1
    pwsh -NoProfile -File scripts/tests/test-process-watchdog.ps1 -VerboseOutput
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
# main flow (top-level param() + execution make it non-dot-sourceable).
$tokens = $null
$errs = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($target, [ref]$tokens, [ref]$errs)
if ($errs -and $errs.Count -gt 0) {
    Write-Host "FATAL: run-ci-tests.ps1 has parse errors:"
    $errs | ForEach-Object { Write-Host "  $($_.Extent.StartLineNumber): $($_.Message)" }
    exit 1
}
foreach ($name in @(
    'ConvertTo-ProcessArgumentLine',
    'Set-CompletionGraceDeadline',
    'Invoke-ProcessWithTreeKillTimeout'
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

$sentinel = 'Test run completed\. Exiting with code \d+'
$pwshPath = (Get-Command pwsh).Source
$tmp = [System.IO.Path]::GetTempPath()
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

function Invoke-Watchdog {
    param([string]$ChildCommand, [int]$TimeoutSeconds, [int]$GraceSeconds, [string]$Label)
    $log = Join-Path $tmp ("uh-watchdog-{0}.log" -f $Label)
    return Invoke-ProcessWithTreeKillTimeout `
        -FilePath $pwshPath `
        -Arguments @('-NoProfile', '-Command', $ChildCommand) `
        -TimeoutSeconds $TimeoutSeconds `
        -LogPath $log `
        -Label $Label `
        -CompletionPattern $sentinel `
        -CompletionGraceSeconds $GraceSeconds
}

# 1. sentinel (code 0) then hang -> grace-killed fast, exit 0, not timed out.
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$r = Invoke-Watchdog -ChildCommand 'Write-Host "Test run completed. Exiting with code 0"; Start-Sleep -Seconds 90' -TimeoutSeconds 600 -GraceSeconds 2 -Label 'pass-then-hang'
$sw.Stop()
Assert-That "completion+hang is killed within the grace window (elapsed=$([int]$sw.Elapsed.TotalSeconds)s, far below the 600s timeout)" ($sw.Elapsed.TotalSeconds -lt 30)
Assert-That "completion+hang reports the parsed exit code 0" ($r.ExitCode -eq 0)
Assert-That "completion+hang is NOT treated as a timeout" (-not $r.TimedOut)

# 2. sentinel (code 2) then hang -> exit 2, not timed out (a failing run still fails).
$r = Invoke-Watchdog -ChildCommand 'Write-Host "Test run completed. Exiting with code 2 (Failed)."; Start-Sleep -Seconds 90' -TimeoutSeconds 600 -GraceSeconds 2 -Label 'fail-then-hang'
Assert-That "failing completion+hang reports the parsed exit code 2" ($r.ExitCode -eq 2)
Assert-That "failing completion+hang is NOT treated as a timeout" (-not $r.TimedOut)

# 3. normal exit, no sentinel -> real exit code, guard inert.
$r = Invoke-Watchdog -ChildCommand 'Write-Host "hello"; exit 0' -TimeoutSeconds 600 -GraceSeconds 2 -Label 'normal-exit'
Assert-That "normal exit reports the real exit code 0" ($r.ExitCode -eq 0)
Assert-That "normal exit is NOT treated as a timeout" (-not $r.TimedOut)

# 3b. sentinel logged but editor then EXITS CLEANLY within grace (the common non-hang
#     path on healthy Unity): the REAL editor exit code must win over the parsed
#     sentinel code, and it must not be treated as a kill/timeout. Child logs
#     "Exiting with code 0" then actually exits 5 -> expect 5.
$r = Invoke-Watchdog -ChildCommand 'Write-Host "Test run completed. Exiting with code 0"; Start-Sleep -Seconds 1; exit 5' -TimeoutSeconds 600 -GraceSeconds 30 -Label 'sentinel-then-clean-exit'
Assert-That "sentinel + clean exit returns the REAL editor exit code (5), not the parsed sentinel code (0)" ($r.ExitCode -eq 5)
Assert-That "sentinel + clean exit is NOT treated as a timeout" (-not $r.TimedOut)

# 4. hang with no sentinel -> wall-clock backstop fires (timeout, exit 124).
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$r = Invoke-Watchdog -ChildCommand 'Write-Host "working"; Start-Sleep -Seconds 90' -TimeoutSeconds 2 -GraceSeconds 600 -Label 'no-sentinel-timeout'
$sw.Stop()
Assert-That "sentinel-less hang hits the wall-clock backstop fast (elapsed=$([int]$sw.Elapsed.TotalSeconds)s)" ($sw.Elapsed.TotalSeconds -lt 30)
Assert-That "sentinel-less hang is reported as a timeout" ($r.TimedOut)
Assert-That "sentinel-less hang reports the timeout exit code 124" ($r.ExitCode -eq 124)

Write-Host ""
Write-Host "Process-watchdog tests: $passed passed, $failed failed."
if ($failed -gt 0) {
    exit 1
}
Write-Host "All process-watchdog tests passed."
exit 0
