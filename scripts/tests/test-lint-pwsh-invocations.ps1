#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Test runner for lint-pwsh-invocations.ps1.

.DESCRIPTION
    Tests that lint-pwsh-invocations.ps1 correctly:
    - Exits 0 against a clean synthetic repo (negative fixtures only).
    - Exits 1 and emits PWS001 for a bash-level bad invocation in a .sh fixture.
    - Exits 1 and emits PWS001 for a bad invocation in a .yml workflow fixture.
    - Exits 1 and emits PWS001 for a bad invocation in a .githooks/* fixture.
    - Exits 1 and emits PWS002 for in-process bad invocation in tests/*.ps1 fixture.
    - Exits 1 and emits PWS004 for `pwsh -File .githooks/<extensionless-hook>`.
    - Does NOT flag the correct -Paths invocation pattern.
    - Does NOT flag the bad pattern when it only appears inside a PowerShell help
      comment block.
    - Includes a canary: a lone bad pattern in a .sh fixture MUST be caught.

.PARAMETER VerboseOutput
    Show verbose per-test diagnostics.

.EXAMPLE
    pwsh -NoProfile -File scripts/tests/test-lint-pwsh-invocations.ps1
    pwsh -NoProfile -File scripts/tests/test-lint-pwsh-invocations.ps1 -VerboseOutput
#>
param(
    [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:TestsPassed = 0
$script:TestsFailed = 0
$script:FailedTests = @()

function Write-Info($msg) {
    if ($VerboseOutput) { Write-Host "[test-lint-pwsh-invocations] $msg" -ForegroundColor Cyan }
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

$lintScriptPath = (Resolve-Path (Join-Path $PSScriptRoot '..' 'lint-pwsh-invocations.ps1')).Path

$tempBase = if ($env:TEMP) { $env:TEMP } elseif ($env:TMPDIR) { $env:TMPDIR } else { '/tmp' }
$tempRoot = Join-Path $tempBase "test-lint-pwsh-invocations-$(Get-Random)"

# Build a synthetic repo layout inside a tempdir and invoke the lint scoped to it.
# The lint resolves scan targets relative to $PSScriptRoot/.. (the repo root), so
# we must copy the lint script into the tempdir under the same layout.
function New-FixtureRoot {
    $root = Join-Path $tempRoot "repo-$(Get-Random)"
    New-Item -ItemType Directory -Path (Join-Path $root 'scripts/tests') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $root '.githooks') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $root '.github/workflows') -Force | Out-Null
    Copy-Item -LiteralPath $lintScriptPath -Destination (Join-Path $root 'scripts/lint-pwsh-invocations.ps1')
    return $root
}

function Invoke-LintInFixture {
    param([string]$FixtureRoot)
    $lintCopy = Join-Path $FixtureRoot 'scripts/lint-pwsh-invocations.ps1'
    $output = & pwsh -NoProfile -File $lintCopy *>&1
    $exitCode = $LASTEXITCODE
    return @{ ExitCode = $exitCode; Output = ($output | Out-String) }
}

try {
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

    Write-Host "Testing lint-pwsh-invocations.ps1..." -ForegroundColor White
    Write-Host "`n  Section: Negative (clean) fixtures" -ForegroundColor White

    # --- Pass_CleanRepo ---
    $root = New-FixtureRoot
    # A well-formed hook: uses -Paths (the correct pattern).
    Set-Content -LiteralPath (Join-Path $root '.githooks/pre-commit') -Value @'
#!/usr/bin/env bash
set -e
pwsh -NoProfile -File scripts/lint-dependabot.ps1 -Paths "${ARRAY[@]}"
'@
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_CleanRepo" ($result.ExitCode -eq 0) "Expected exit 0 on clean fixture. Exit: $($result.ExitCode). Output: $($result.Output)"

    Write-Host "`n  Section: Positive (should-fail) fixtures" -ForegroundColor White

    # --- Canary_LoneBadFileFlagInSh ---
    # The planted canary fixture: assert the lint catches `pwsh -File bad.ps1 --`.
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'canary.sh') -Value @'
#!/usr/bin/env bash
pwsh -File bad.ps1 -- arg
'@
    $result = Invoke-LintInFixture $root
    $hasCanary = $result.Output -match 'PWS001' -and $result.Output -match 'canary\.sh'
    Write-TestResult "Canary_LoneBadFileFlagInSh" ($result.ExitCode -ne 0 -and $hasCanary) "Expected exit != 0 and PWS001 on canary fixture. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_BadInvocationInShellScript ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/bad.sh') -Value @'
#!/usr/bin/env bash
set -e
pwsh -NoProfile -File scripts/lint-dependabot.ps1 -- "${FILES[@]}"
'@
    $result = Invoke-LintInFixture $root
    $hasPws001 = $result.Output -match 'PWS001' -and $result.Output -match 'scripts/bad\.sh'
    Write-TestResult "Fail_BadInvocationInShellScript" ($result.ExitCode -ne 0 -and $hasPws001) "Expected exit != 0 + PWS001. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_BadInvocationInWorkflow ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root '.github/workflows/bad.yml') -Value @'
name: Bad
on: [push]
jobs:
  bad:
    runs-on: ubuntu-latest
    steps:
      - run: pwsh -NoProfile -File scripts/thing.ps1 -- arg1 arg2
'@
    $result = Invoke-LintInFixture $root
    $hasPws001 = $result.Output -match 'PWS001' -and $result.Output -match 'bad\.yml'
    Write-TestResult "Fail_BadInvocationInWorkflow" ($result.ExitCode -ne 0 -and $hasPws001) "Expected exit != 0 + PWS001 in workflow. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_BadInvocationInGithooks ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root '.githooks/pre-commit') -Value @'
#!/usr/bin/env bash
set -e
if command -v pwsh >/dev/null 2>&1; then
  pwsh -NoProfile -File scripts/lint-dependabot.ps1 -- "${FILES[@]}"
fi
'@
    $result = Invoke-LintInFixture $root
    $hasPws001 = $result.Output -match 'PWS001' -and $result.Output -match 'pre-commit'
    Write-TestResult "Fail_BadInvocationInGithooks" ($result.ExitCode -ne 0 -and $hasPws001) "Expected exit != 0 + PWS001 in .githooks. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_BadInvocationPowershellCommand ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/bad-ps.sh') -Value @'
#!/usr/bin/env bash
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/lint-dependabot.ps1 -- arg
'@
    $result = Invoke-LintInFixture $root
    $hasPws001 = $result.Output -match 'PWS001' -and $result.Output -match 'bad-ps\.sh'
    Write-TestResult "Fail_BadInvocationPowershellCommand" ($result.ExitCode -ne 0 -and $hasPws001) "Expected exit != 0 + PWS001 for powershell.exe form. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_BadInvocationPwshExeShortFileAlias ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/bad-pwsh-exe-short-file.sh') -Value @'
#!/usr/bin/env bash
pwsh.exe -NoProfile -f scripts/lint-dependabot.ps1 -- arg
'@
    $result = Invoke-LintInFixture $root
    $hasPws001 = $result.Output -match 'PWS001' -and $result.Output -match 'bad-pwsh-exe-short-file\.sh'
    Write-TestResult "Fail_BadInvocationPwshExeShortFileAlias" ($result.ExitCode -ne 0 -and $hasPws001) "Expected exit != 0 + PWS001 for pwsh.exe -f form. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_BadInvocationAfterPwshCommand ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/bad-after-command.sh') -Value @'
#!/usr/bin/env bash
pwsh -NoProfile -Command 'Write-Host ok'; pwsh -NoProfile -File scripts/foo.ps1 -- arg
'@
    $result = Invoke-LintInFixture $root
    $hasPws001 = $result.Output -match 'PWS001' -and $result.Output -match 'bad-after-command\.sh'
    Write-TestResult "Fail_BadInvocationAfterPwshCommand" ($result.ExitCode -ne 0 -and $hasPws001) "Expected exit != 0 + PWS001 when real -File command follows pwsh -Command. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_BadInvocationInsidePwshCommand ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/bad-inside-command.sh') -Value @'
#!/usr/bin/env bash
pwsh -NoProfile -Command 'pwsh -NoProfile -File scripts/foo.ps1 -- arg'
'@
    $result = Invoke-LintInFixture $root
    $hasPws001 = $result.Output -match 'PWS001' -and $result.Output -match 'bad-inside-command\.sh'
    Write-TestResult "Fail_BadInvocationInsidePwshCommand" ($result.ExitCode -ne 0 -and $hasPws001) "Expected exit != 0 + PWS001 when bad -File invocation is inside pwsh -Command payload. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_BadInvocationAfterOutputInsidePwshCommand ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/bad-after-output-inside-command.sh') -Value @'
#!/usr/bin/env bash
pwsh -NoProfile -Command 'Write-Host ok; pwsh -NoProfile -File scripts/foo.ps1 -- arg'
'@
    $result = Invoke-LintInFixture $root
    $hasPws001 = $result.Output -match 'PWS001' -and $result.Output -match 'bad-after-output-inside-command\.sh'
    Write-TestResult "Fail_BadInvocationAfterOutputInsidePwshCommand" ($result.ExitCode -ne 0 -and $hasPws001) "Expected exit != 0 + PWS001 when bad -File invocation follows output inside pwsh -Command payload. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_BadInvocationQuotedDoubleDash ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/bad-quoted-double-dash.sh') -Value @'
#!/usr/bin/env bash
pwsh -NoProfile -File scripts/lint-dependabot.ps1 "--" arg
powershell.exe -NoProfile -ExecutionPolicy Bypass -f scripts/lint-dependabot.ps1 '--' arg
'@
    $result = Invoke-LintInFixture $root
    $hasPws001 = $result.Output -match 'PWS001' -and $result.Output -match 'bad-quoted-double-dash\.sh'
    Write-TestResult "Fail_BadInvocationQuotedDoubleDash" ($result.ExitCode -ne 0 -and $hasPws001) "Expected exit != 0 + PWS001 for quoted -- tokens under -File/-f. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_ExtensionlessHookFileTarget ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/bad-hook-file.sh') -Value @'
#!/usr/bin/env bash
pwsh -NoProfile -File .githooks/pre-commit
'@
    $result = Invoke-LintInFixture $root
    $hasPws004 = $result.Output -match 'PWS004' -and $result.Output -match 'bad-hook-file\.sh'
    Write-TestResult "Fail_ExtensionlessHookFileTarget" ($result.ExitCode -ne 0 -and $hasPws004) "Expected exit != 0 + PWS004 for extensionless git hook -File target. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_ExtensionlessHookFileTargetDotSlash ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/bad-hook-file-dotslash.sh') -Value @'
#!/usr/bin/env bash
pwsh -NoProfile -File ./.githooks/pre-push
'@
    $result = Invoke-LintInFixture $root
    $hasPws004 = $result.Output -match 'PWS004' -and $result.Output -match 'bad-hook-file-dotslash\.sh'
    Write-TestResult "Fail_ExtensionlessHookFileTargetDotSlash" ($result.ExitCode -ne 0 -and $hasPws004) "Expected exit != 0 + PWS004 for dot-slash extensionless hook target. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_ExtensionlessHookFileTargetPwshExeShortFileAlias ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/bad-hook-file-pwsh-exe-short-file.sh') -Value @'
#!/usr/bin/env bash
pwsh.exe -NoProfile -f .githooks/pre-push
'@
    $result = Invoke-LintInFixture $root
    $hasPws004 = $result.Output -match 'PWS004' -and $result.Output -match 'bad-hook-file-pwsh-exe-short-file\.sh'
    Write-TestResult "Fail_ExtensionlessHookFileTargetPwshExeShortFileAlias" ($result.ExitCode -ne 0 -and $hasPws004) "Expected exit != 0 + PWS004 for pwsh.exe -f extensionless hook target. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_ExtensionlessHookFileTargetAfterPwshCommand ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/bad-hook-after-command.sh') -Value @'
#!/usr/bin/env bash
pwsh -NoProfile -Command 'Write-Host ok'; pwsh -NoProfile -File .githooks/pre-commit
'@
    $result = Invoke-LintInFixture $root
    $hasPws004 = $result.Output -match 'PWS004' -and $result.Output -match 'bad-hook-after-command\.sh'
    Write-TestResult "Fail_ExtensionlessHookFileTargetAfterPwshCommand" ($result.ExitCode -ne 0 -and $hasPws004) "Expected exit != 0 + PWS004 when extensionless hook -File command follows pwsh -Command. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_ExtensionlessHookFileTargetInsidePwshCommand ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/bad-hook-inside-command.sh') -Value @'
#!/usr/bin/env bash
pwsh -NoProfile -Command 'pwsh -NoProfile -File .githooks/pre-commit'
'@
    $result = Invoke-LintInFixture $root
    $hasPws004 = $result.Output -match 'PWS004' -and $result.Output -match 'bad-hook-inside-command\.sh'
    Write-TestResult "Fail_ExtensionlessHookFileTargetInsidePwshCommand" ($result.ExitCode -ne 0 -and $hasPws004) "Expected exit != 0 + PWS004 when extensionless hook -File invocation is inside pwsh -Command payload. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_ExtensionlessHookFileTargetAfterOutputInsidePwshCommand ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/bad-hook-after-output-inside-command.sh') -Value @'
#!/usr/bin/env bash
pwsh -NoProfile -Command 'Write-Host ok; pwsh -NoProfile -File .githooks/pre-commit'
'@
    $result = Invoke-LintInFixture $root
    $hasPws004 = $result.Output -match 'PWS004' -and $result.Output -match 'bad-hook-after-output-inside-command\.sh'
    Write-TestResult "Fail_ExtensionlessHookFileTargetAfterOutputInsidePwshCommand" ($result.ExitCode -ne 0 -and $hasPws004) "Expected exit != 0 + PWS004 when extensionless hook -File invocation follows output inside pwsh -Command payload. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_ExtensionlessHookFileTargetRepoRoot ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/bad-hook-file-reporoot.sh') -Value @'
#!/usr/bin/env bash
pwsh -NoProfile -File "$REPO_ROOT/.githooks/pre-merge-commit"
'@
    $result = Invoke-LintInFixture $root
    $hasPws004 = $result.Output -match 'PWS004' -and $result.Output -match 'bad-hook-file-reporoot\.sh'
    Write-TestResult "Fail_ExtensionlessHookFileTargetRepoRoot" ($result.ExitCode -ne 0 -and $hasPws004) "Expected exit != 0 + PWS004 for repo-root-prefixed extensionless hook target. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_ExtensionlessHookFileTargetPowerShellQuotedArg ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/tests/test-bad-hook-file.ps1') -Value @'
param()
pwsh -NoProfile -File "$PSScriptRoot/../../.githooks/post-rewrite"
'@
    $result = Invoke-LintInFixture $root
    $hasPws004 = $result.Output -match 'PWS004' -and $result.Output -match 'test-bad-hook-file\.ps1'
    Write-TestResult "Fail_ExtensionlessHookFileTargetPowerShellQuotedArg" ($result.ExitCode -ne 0 -and $hasPws004) "Expected exit != 0 + PWS004 for quoted PowerShell extensionless hook target. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_ExtensionlessHookFileTargetNestedPowerShell ---
    $root = New-FixtureRoot
    New-Item -ItemType Directory -Path (Join-Path $root 'scripts/unity') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $root 'scripts/unity/bad-hook-file.ps1') -Value @'
param()
pwsh -NoProfile -File .githooks/pre-commit
'@
    $result = Invoke-LintInFixture $root
    $hasPws004 = $result.Output -match 'PWS004' -and $result.Output -match 'scripts/unity/bad-hook-file\.ps1'
    Write-TestResult "Fail_ExtensionlessHookFileTargetNestedPowerShell" ($result.ExitCode -ne 0 -and $hasPws004) "Expected exit != 0 + PWS004 in nested scripts/**/*.ps1. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_ExtensionlessHookFileTargetPackageJsonWindowsEscaped ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'package.json') -Value @'
{
  "scripts": {
    "bad": "pwsh -NoProfile -File .githooks\\pre-commit"
  }
}
'@
    $result = Invoke-LintInFixture $root
    $hasPws004 = $result.Output -match 'PWS004' -and $result.Output -match 'package\.json'
    Write-TestResult "Fail_ExtensionlessHookFileTargetPackageJsonWindowsEscaped" ($result.ExitCode -ne 0 -and $hasPws004) "Expected exit != 0 + PWS004 for JSON-escaped Windows hook separator. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_ExtensionlessHookFileTargetPackageJsonEscapedQuotedPath ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'package.json') -Value @'
{
  "scripts": {
    "bad": "powershell.exe -NoProfile -ExecutionPolicy Bypass -f \".githooks\\pre-commit\""
  }
}
'@
    $result = Invoke-LintInFixture $root
    $hasPws004 = $result.Output -match 'PWS004' -and $result.Output -match 'package\.json'
    Write-TestResult "Fail_ExtensionlessHookFileTargetPackageJsonEscapedQuotedPath" ($result.ExitCode -ne 0 -and $hasPws004) "Expected exit != 0 + PWS004 for JSON-escaped quoted Windows hook path. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_ExtensionlessHookFileTargetArrayIndirection ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/bad-hook-array.sh') -Value @'
#!/usr/bin/env bash
PWSH_CMD=(pwsh -NoProfile -File)
"${PWSH_CMD[@]}" .githooks/pre-commit
'@
    $result = Invoke-LintInFixture $root
    $hasPws004 = $result.Output -match 'PWS004' -and $result.Output -match 'bad-hook-array\.sh'
    Write-TestResult "Fail_ExtensionlessHookFileTargetArrayIndirection" ($result.ExitCode -ne 0 -and $hasPws004) "Expected exit != 0 + PWS004 for PowerShell-named bash-array invocation targeting extensionless hook. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_ExtensionlessHookFileTargetVariableAssignment ---
    $root = New-FixtureRoot
    New-Item -ItemType Directory -Path (Join-Path $root 'scripts/unity') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $root 'scripts/unity/bad-hook-variable.ps1') -Value @'
param()
$hook = Join-Path $PSScriptRoot '../../.githooks/pre-commit'
pwsh -NoProfile -File $hook
'@
    $result = Invoke-LintInFixture $root
    $hasPws004 = $result.Output -match 'PWS004' -and $result.Output -match 'bad-hook-variable\.ps1'
    Write-TestResult "Fail_ExtensionlessHookFileTargetVariableAssignment" ($result.ExitCode -ne 0 -and $hasPws004) "Expected exit != 0 + PWS004 for variable assigned to extensionless hook path and passed to -File. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_ExtensionlessHookFileTargetJoinPathVariableAssignment ---
    $root = New-FixtureRoot
    New-Item -ItemType Directory -Path (Join-Path $root 'scripts/unity') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $root 'scripts/unity/bad-hook-joinpath-variable.ps1') -Value @'
param()
$hook = Join-Path $PSScriptRoot '.githooks' 'pre-commit'
pwsh -NoProfile -File $hook
'@
    $result = Invoke-LintInFixture $root
    $hasPws004 = $result.Output -match 'PWS004' -and $result.Output -match 'bad-hook-joinpath-variable\.ps1'
    Write-TestResult "Fail_ExtensionlessHookFileTargetJoinPathVariableAssignment" ($result.ExitCode -ne 0 -and $hasPws004) "Expected exit != 0 + PWS004 for variable assigned by split Join-Path to extensionless hook path and passed to -File. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_ArrayIndirectionPackageJsonEscapedQuotedInvocation ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'package.json') -Value @'
{
  "scripts": {
    "bad": "\\\"${PWSH_CMD[@]}\\\" scripts/thing.ps1 -- arg"
  }
}
'@
    $result = Invoke-LintInFixture $root
    $hasPws001 = $result.Output -match 'PWS001' -and $result.Output -match 'package\.json'
    Write-TestResult "Fail_ArrayIndirectionPackageJsonEscapedQuotedInvocation" ($result.ExitCode -ne 0 -and $hasPws001) "Expected exit != 0 + PWS001 for JSON-escaped quoted bash-array invocation. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_BadInvocationInTestsInProcess ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/tests/test-foo.ps1') -Value @'
param()
$lint = "./scripts/lint-foo.ps1"
$output = & $lint -- $fixturePath *>&1
'@
    $result = Invoke-LintInFixture $root
    $hasPws002 = $result.Output -match 'PWS002' -and $result.Output -match 'test-foo\.ps1'
    Write-TestResult "Fail_BadInvocationInTestsInProcess" ($result.ExitCode -ne 0 -and $hasPws002) "Expected exit != 0 + PWS002 for in-process '& script --'. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_BadInvocationInTestsInProcessQuotedDoubleDash ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/tests/test-foo-quoted-dash.ps1') -Value @'
param()
$lint = "./scripts/lint-foo.ps1"
$output = & $lint '--' $fixturePath *>&1
'@
    $result = Invoke-LintInFixture $root
    $hasPws002 = $result.Output -match 'PWS002' -and $result.Output -match 'test-foo-quoted-dash\.ps1'
    Write-TestResult "Fail_BadInvocationInTestsInProcessQuotedDoubleDash" ($result.ExitCode -ne 0 -and $hasPws002) "Expected exit != 0 + PWS002 for in-process '& script ''--'''. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_BadInvocationInTestsInProcessDelayedDoubleDash ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/tests/test-foo-delayed-dash.ps1') -Value @'
param()
$script = "./scripts/lint-foo.ps1"
$output = & $script -Verbose '--' $fixturePath *>&1
'@
    $result = Invoke-LintInFixture $root
    $hasPws002 = $result.Output -match 'PWS002' -and $result.Output -match 'test-foo-delayed-dash\.ps1'
    Write-TestResult "Fail_BadInvocationInTestsInProcessDelayedDoubleDash" ($result.ExitCode -ne 0 -and $hasPws002) "Expected exit != 0 + PWS002 when '& script' passes options before quoted --. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_BadInvocationInTestsInProcessParenthesizedScript ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/tests/test-foo-parenthesized.ps1') -Value @'
param()
$output = & (Join-Path $PSScriptRoot '..' 'lint-foo.ps1') -- $fixturePath *>&1
'@
    $result = Invoke-LintInFixture $root
    $hasPws002 = $result.Output -match 'PWS002' -and $result.Output -match 'test-foo-parenthesized\.ps1'
    Write-TestResult "Fail_BadInvocationInTestsInProcessParenthesizedScript" ($result.ExitCode -ne 0 -and $hasPws002) "Expected exit != 0 + PWS002 for parenthesized in-process script target. Exit: $($result.ExitCode). Output: $($result.Output)"

    Write-Host "`n  Section: False-positive guards" -ForegroundColor White

    # --- Pass_CorrectPathsPattern ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/good.sh') -Value @'
#!/usr/bin/env bash
pwsh -NoProfile -File scripts/lint-dependabot.ps1 -Paths "${ARR[@]}"
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/lint-dependabot.ps1 -Paths "${ARR[@]}"
'@
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_CorrectPathsPattern" ($result.ExitCode -eq 0) "Expected exit 0 for correct -Paths invocation. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Pass_GithookPs1ImplementationFileTarget ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/good-hook-debug.sh') -Value @'
#!/usr/bin/env bash
pwsh -NoProfile -File .githooks/pre-commit.ps1
'@
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_GithookPs1ImplementationFileTarget" ($result.ExitCode -eq 0) "Expected exit 0 for .ps1 git hook implementation target. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Pass_GithookPs1ImplementationVariableTarget ---
    $root = New-FixtureRoot
    New-Item -ItemType Directory -Path (Join-Path $root 'scripts/unity') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $root 'scripts/unity/good-hook-variable.ps1') -Value @'
param()
$hook = Join-Path $PSScriptRoot '../../.githooks/pre-commit.ps1'
pwsh -NoProfile -File $hook
'@
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_GithookPs1ImplementationVariableTarget" ($result.ExitCode -eq 0) "Expected exit 0 for variable assigned to .ps1 hook implementation target. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Pass_GithookPs1ImplementationJoinPathVariableTarget ---
    $root = New-FixtureRoot
    New-Item -ItemType Directory -Path (Join-Path $root 'scripts/unity') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $root 'scripts/unity/good-hook-joinpath-variable.ps1') -Value @'
param()
$hook = Join-Path $PSScriptRoot '.githooks' 'pre-commit.ps1'
pwsh -NoProfile -File $hook
'@
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_GithookPs1ImplementationJoinPathVariableTarget" ($result.ExitCode -eq 0) "Expected exit 0 for variable assigned by split Join-Path to .ps1 hook implementation target. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Pass_GithookPs1ImplementationVariableTargetWithCommentMention ---
    $root = New-FixtureRoot
    New-Item -ItemType Directory -Path (Join-Path $root 'scripts/unity') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $root 'scripts/unity/good-hook-variable-comment.ps1') -Value @'
param()
$hook = Join-Path $PSScriptRoot '../../.githooks/pre-commit.ps1' # companion for .githooks/pre-commit
pwsh -NoProfile -File $hook
'@
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_GithookPs1ImplementationVariableTargetWithCommentMention" ($result.ExitCode -eq 0) "Expected exit 0 when .ps1 implementation variable line has trailing comment mentioning extensionless hook. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Pass_ExtensionlessHookMentionInPowerShellInlineComment ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/tests/test-hook-inline-comment.ps1') -Value @'
param()
$ok = $true # do not run pwsh -NoProfile -File .githooks/pre-commit
'@
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_ExtensionlessHookMentionInPowerShellInlineComment" ($result.ExitCode -eq 0) "Expected exit 0 when extensionless hook command is only a PowerShell inline comment. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Pass_ExtensionlessHookMentionInPowerShellString ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/tests/test-hook-help.ps1') -Value @'
param()
Write-Host "For debugging, do not run pwsh -NoProfile -File .githooks/pre-commit"
'@
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_ExtensionlessHookMentionInPowerShellString" ($result.ExitCode -eq 0) "Expected exit 0 when extensionless hook command is only PowerShell help text. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Pass_ExtensionlessHookMentionInShellEcho ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/help-text.sh') -Value @'
#!/usr/bin/env bash
echo "For debugging, do not run pwsh -NoProfile -File .githooks/pre-commit"
'@
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_ExtensionlessHookMentionInShellEcho" ($result.ExitCode -eq 0) "Expected exit 0 when extensionless hook command is only shell help text. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Pass_DoubleDashMentionInPowerShellString ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/tests/test-pws001-help.ps1') -Value @'
param()
Write-Host "Do not run pwsh -NoProfile -File scripts/foo.ps1 -- arg"
'@
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_DoubleDashMentionInPowerShellString" ($result.ExitCode -eq 0) "Expected exit 0 when PWS001 command text is only PowerShell help text. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Pass_DoubleDashMentionInShellEcho ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/pws001-help-text.sh') -Value @'
#!/usr/bin/env bash
echo "Do not run pwsh -NoProfile -File scripts/foo.ps1 -- arg"
'@
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_DoubleDashMentionInShellEcho" ($result.ExitCode -eq 0) "Expected exit 0 when PWS001 command text is only shell help text. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Pass_ArrayIndirectionMentionInPowerShellString ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/tests/test-pws001-array-help.ps1') -Value @'
param()
Write-Host 'Do not run "${PWSH_CMD[@]}" scripts/thing.ps1 -- arg'
'@
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_ArrayIndirectionMentionInPowerShellString" ($result.ExitCode -eq 0) "Expected exit 0 when array-indirection PWS001 text is only PowerShell help text. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Pass_ArrayIndirectionMentionInShellEcho ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/pws001-array-help-text.sh') -Value @'
#!/usr/bin/env bash
echo 'Do not run "${PWSH_CMD[@]}" scripts/thing.ps1 -- arg'
'@
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_ArrayIndirectionMentionInShellEcho" ($result.ExitCode -eq 0) "Expected exit 0 when array-indirection PWS001 text is only shell help text. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Pass_ArrayIndirectionMentionInWorkflowEcho ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root '.github/workflows/pws001-array-help.yml') -Value @'
name: Help Text
on: [push]
jobs:
  help:
    runs-on: ubuntu-latest
    steps:
      - run: echo 'Do not run "${PWSH_CMD[@]}" scripts/thing.ps1 -- arg'
'@
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_ArrayIndirectionMentionInWorkflowEcho" ($result.ExitCode -eq 0) "Expected exit 0 when array-indirection PWS001 text is only workflow echo help text. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Pass_InProcessDoubleDashMentionInPowerShellString ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/tests/test-pws002-help.ps1') -Value @'
param()
Write-Host "Do not run & scripts/foo.ps1 -- arg"
'@
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_InProcessDoubleDashMentionInPowerShellString" ($result.ExitCode -eq 0) "Expected exit 0 when PWS002 command text is only PowerShell help text. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Pass_InvocationMentionInPowerShellHereString ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/tests/test-here-string-help.ps1') -Value @'
param()
$help = @"
Do not run pwsh -NoProfile -File scripts/foo.ps1 -- arg
Do not run & scripts/foo.ps1 -- arg
Do not run pwsh -NoProfile -File .githooks/pre-commit
"@
'@
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_InvocationMentionInPowerShellHereString" ($result.ExitCode -eq 0) "Expected exit 0 when invocation text appears only in a PowerShell here-string. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Pass_InvocationMentionInPwshCommandHelpText ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/pwsh-command-help.sh') -Value @'
#!/usr/bin/env bash
pwsh -NoProfile -Command 'Write-Host "do not run pwsh -NoProfile -File scripts/foo.ps1 -- arg"'
pwsh -NoProfile -Command 'Write-Host "do not run pwsh -NoProfile -File .githooks/pre-commit"'
'@
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_InvocationMentionInPwshCommandHelpText" ($result.ExitCode -eq 0) "Expected exit 0 when invocation text appears only inside a pwsh -Command help string. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Pass_DoubleDashInCommentedHelp ---
    # Anti-pattern appears inside a <# ... #> help block -> must NOT be flagged.
    $root = New-FixtureRoot
    $cbh = @'
<#
.SYNOPSIS
    Example invocation:
        pwsh -NoProfile -File scripts/thing.ps1 -- arg
#>
param([string]$Foo)
Write-Host "ok"
'@
    Set-Content -LiteralPath (Join-Path $root 'scripts/tests/test-doc.ps1') -Value $cbh
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_DoubleDashInCommentedHelp" ($result.ExitCode -eq 0) "Expected exit 0 when '--' appears only inside <# #>. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Pass_SelfAndSelfTestExempted ---
    # The lint script itself and its own test script intentionally mention the
    # anti-pattern as fixture content; they must NOT be flagged.
    $root = New-FixtureRoot
    # Drop a copy of this test (which builds fixture STRINGS containing "--")
    # into the synthetic repo. The lint must exempt scripts/tests/test-lint-pwsh-invocations.ps1.
    Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $root 'scripts/tests/test-lint-pwsh-invocations.ps1')
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_SelfAndSelfTestExempted" ($result.ExitCode -eq 0) "Expected exit 0 with only the self-test present. Exit: $($result.ExitCode). Output: $($result.Output)"

    Write-Host "`n  Section: Literal-byte canary" -ForegroundColor White

    # --- Canary_ExactOriginalBugLine ---
    # Byte-for-byte fixture: the exact shipped bug line from the pre-commit hook.
    # If the lint ever stops firing on THIS, the primary regression guard is dead.
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root '.githooks/pre-commit') -Value @'
#!/usr/bin/env bash
pwsh -NoProfile -File scripts/lint-dependabot.ps1 -- "${DEPENDABOT_FILES_ARRAY[@]}"
'@
    $result = Invoke-LintInFixture $root
    $hasCanary = $result.Output -match 'PWS001' -and $result.Output -match 'pre-commit'
    Write-TestResult "Canary_ExactOriginalBugLine" ($result.ExitCode -ne 0 -and $hasCanary) "Expected exit != 0 + PWS001 on byte-for-byte original bug line. Exit: $($result.ExitCode). Output: $($result.Output)"

    Write-Host "`n  Section: Multi-line continuation (M1)" -ForegroundColor White

    # --- Fail_MultilineBashContinuation ---
    # Bash script where the pwsh invocation spans 4 physical lines via `\`.
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/multiline.sh') -Value @'
#!/usr/bin/env bash
set -e
pwsh \
  -NoProfile \
  -File scripts/lint-dependabot.ps1 \
  -- "${ARR[@]}"
'@
    $result = Invoke-LintInFixture $root
    $hasPws001 = $result.Output -match 'PWS001' -and $result.Output -match 'multiline\.sh'
    Write-TestResult "Fail_MultilineBashContinuation" ($result.ExitCode -ne 0 -and $hasPws001) "Expected exit != 0 + PWS001 on 4-line '\\' continuation. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_MultilineYamlRunBlock ---
    # YAML workflow `run: |` block with `\` continuations.
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root '.github/workflows/multiline.yml') -Value @'
name: Multiline
on: [push]
jobs:
  bad:
    runs-on: ubuntu-latest
    steps:
      - run: |
          pwsh \
            -NoProfile \
            -File scripts/thing.ps1 \
            -- "${ARR[@]}"
'@
    $result = Invoke-LintInFixture $root
    $hasPws001 = $result.Output -match 'PWS001' -and $result.Output -match 'multiline\.yml'
    Write-TestResult "Fail_MultilineYamlRunBlock" ($result.ExitCode -ne 0 -and $hasPws001) "Expected exit != 0 + PWS001 on YAML run: block with '\\' continuation. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Pass_MultilineContinuationWithCorrectPattern ---
    # Multi-line `\` continuation using -Paths (the correct form) must NOT trigger.
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/multiline-good.sh') -Value @'
#!/usr/bin/env bash
pwsh \
  -NoProfile \
  -File scripts/lint-dependabot.ps1 \
  -Paths "${ARR[@]}"
'@
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_MultilineContinuationWithCorrectPattern" ($result.ExitCode -eq 0) "Expected exit 0 on multi-line '\\' continuation with -Paths. Exit: $($result.ExitCode). Output: $($result.Output)"

    Write-Host "`n  Section: Comment exclusions for .sh / .yml (M3)" -ForegroundColor White

    # --- Pass_ShCommentIgnored ---
    # A bash comment mentioning the anti-pattern must NOT be flagged.
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/commented.sh') -Value @'
#!/usr/bin/env bash
# historical bad pattern: pwsh -NoProfile -File scripts/lint-dependabot.ps1 -- "${ARR[@]}"
echo ok
'@
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_ShCommentIgnored" ($result.ExitCode -eq 0) "Expected exit 0 on bash comment mentioning the anti-pattern. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Pass_YamlCommentIgnored ---
    # A YAML comment mentioning the anti-pattern must NOT be flagged.
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root '.github/workflows/commented.yml') -Value @'
name: Commented
on: [push]
# historical: pwsh -NoProfile -File scripts/thing.ps1 -- arg
jobs:
  ok:
    runs-on: ubuntu-latest
    steps:
      - run: echo ok
'@
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_YamlCommentIgnored" ($result.ExitCode -eq 0) "Expected exit 0 on YAML comment mentioning the anti-pattern. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_YamlQuotedStringWithHash ---
    # Per documented tolerated limitation: `#` inside a quoted YAML string is
    # treated as a comment-start. We ASSERT that limitation here so it is loud
    # if it ever changes. This is the "scanned" direction — a line whose first
    # non-whitespace character is NOT `#` (so it is NOT a comment-start) but
    # which contains a quoted string ending with a `#` inside the quotes
    # followed by the anti-pattern MUST still be flagged. The tolerated case
    # is a full-line `^\s*#...` which we skip.
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root '.github/workflows/hashquoted.yml') -Value @'
name: HashQuoted
on: [push]
jobs:
  bad:
    runs-on: ubuntu-latest
    steps:
      - run: echo "see #1"; pwsh -NoProfile -File scripts/thing.ps1 -- arg
'@
    $result = Invoke-LintInFixture $root
    $hasPws001 = $result.Output -match 'PWS001' -and $result.Output -match 'hashquoted\.yml'
    Write-TestResult "Fail_YamlQuotedStringWithHash" ($result.ExitCode -ne 0 -and $hasPws001) "Expected exit != 0 + PWS001 on YAML line where '#' is inside a quoted string but the line itself is not a comment. Exit: $($result.ExitCode). Output: $($result.Output)"

    Write-Host "`n  Section: Edge-case input files (C1, C2)" -ForegroundColor White

    # --- Pass_EmptyFile ---
    # An empty .sh file must NOT crash the linter under StrictMode. Prior to
    # the fix, `Get-Content` returned $null and accessing .Length threw
    # "property 'Length' cannot be found on this object".
    $root = New-FixtureRoot
    # Create a truly empty file (0 bytes).
    $emptyPath = Join-Path $root 'empty.sh'
    New-Item -ItemType File -Path $emptyPath -Force | Out-Null
    $result = Invoke-LintInFixture $root
    # Exit 0 + no crash message in output.
    $notCrashed = ($result.Output -notmatch 'ParentContainsErrorRecordException') `
        -and ($result.Output -notmatch "property 'Length' cannot be found")
    Write-TestResult "Pass_EmptyFile" ($result.ExitCode -eq 0 -and $notCrashed) "Expected exit 0 and no crash on empty .sh file. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_SingleLineNoTrailingNewline ---
    # A single-line .sh file containing the anti-pattern with NO trailing
    # newline must still be flagged. Prior to the fix, Get-Content returned
    # a scalar String in this case, the loop iterated over characters instead
    # of lines, no regex ever matched, and violations were silently missed.
    $root = New-FixtureRoot
    $singlePath = Join-Path $root 'single.sh'
    # Use .NET directly to guarantee no trailing newline (Set-Content adds one).
    [System.IO.File]::WriteAllBytes(
        $singlePath,
        [System.Text.Encoding]::UTF8.GetBytes('pwsh -NoProfile -File thing.ps1 -- arg')
    )
    $result = Invoke-LintInFixture $root
    $hasPws001 = $result.Output -match 'PWS001' -and $result.Output -match 'single\.sh'
    Write-TestResult "Fail_SingleLineNoTrailingNewline" ($result.ExitCode -ne 0 -and $hasPws001) "Expected exit != 0 + PWS001 on single-line .sh with no trailing newline. Exit: $($result.ExitCode). Output: $($result.Output)"

    Write-Host "`n  Section: Comment-line backslash bypass (M1a)" -ForegroundColor White

    # --- Fail_YamlCommentBackslashDoesNotAbsorb ---
    # A '# comment with trailing \' must NOT absorb the subsequent lines into
    # one logical command — bash does not continue comments across '\'. The
    # real bash command that follows (with its own '\' continuations reaching
    # `--`) must still be flagged.
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root '.github/workflows/commentcont.yml') -Value @'
name: CommentCont
on: [push]
jobs:
  bad:
    runs-on: ubuntu-latest
    steps:
      - run: |
          # comment-with-backslash \
          pwsh \
            -NoProfile \
            -File scripts/thing.ps1 \
            -- "${ARR[@]}"
'@
    $result = Invoke-LintInFixture $root
    $hasPws001 = $result.Output -match 'PWS001' -and $result.Output -match 'commentcont\.yml'
    Write-TestResult "Fail_YamlCommentBackslashDoesNotAbsorb" ($result.ExitCode -ne 0 -and $hasPws001) "Expected exit != 0 + PWS001 when a `# comment \\` precedes a multi-line pwsh invocation. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_ShCommentBackslashDoesNotAbsorb ---
    # Same as above but in a plain .sh file.
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/commentcont.sh') -Value @'
#!/usr/bin/env bash
set -e
# a comment ending in a backslash \
pwsh \
  -NoProfile \
  -File scripts/thing.ps1 \
  -- "${ARR[@]}"
'@
    $result = Invoke-LintInFixture $root
    $hasPws001 = $result.Output -match 'PWS001' -and $result.Output -match 'commentcont\.sh'
    Write-TestResult "Fail_ShCommentBackslashDoesNotAbsorb" ($result.ExitCode -ne 0 -and $hasPws001) "Expected exit != 0 + PWS001 when `# comment \\` precedes multi-line pwsh in .sh. Exit: $($result.ExitCode). Output: $($result.Output)"

    Write-Host "`n  Section: Regex tolerance — positional args & quoted script paths (M1b)" -ForegroundColor White

    # --- Fail_PositionalArgBetweenFileAndDashDash ---
    # `pwsh -NoProfile -File foo.ps1 positional -- arg` — a bare positional
    # between -File <script> and the `--` separator must still trigger PWS001.
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/positional.sh') -Value @'
#!/usr/bin/env bash
pwsh -NoProfile -File scripts/thing.ps1 positional -- arg
'@
    $result = Invoke-LintInFixture $root
    $hasPws001 = $result.Output -match 'PWS001' -and $result.Output -match 'positional\.sh'
    Write-TestResult "Fail_PositionalArgBetweenFileAndDashDash" ($result.ExitCode -ne 0 -and $hasPws001) "Expected exit != 0 + PWS001 when a bare positional arg sits between -File <script> and --. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_QuotedScriptPathWithSpaces ---
    # `pwsh -NoProfile -File "path with spaces.ps1" -- arg` — the quoted
    # script path contains a space; previously the regex used \S+ which
    # can't span the space. Must still trigger PWS001.
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/quotedpath.sh') -Value @'
#!/usr/bin/env bash
pwsh -NoProfile -File "path with spaces.ps1" -- arg
'@
    $result = Invoke-LintInFixture $root
    $hasPws001 = $result.Output -match 'PWS001' -and $result.Output -match 'quotedpath\.sh'
    Write-TestResult "Fail_QuotedScriptPathWithSpaces" ($result.ExitCode -ne 0 -and $hasPws001) "Expected exit != 0 + PWS001 on quoted script path with spaces. Exit: $($result.ExitCode). Output: $($result.Output)"

    Write-Host "`n  Section: YAML folded block scalar (M1c)" -ForegroundColor White

    # --- Fail_YamlFoldedRunBlock ---
    # `run: >` — YAML folded scalar; body has NO backslashes. YAML folds it to
    # a single space-joined command that bash runs as one pwsh invocation
    # with `--` reaching PowerShell. Must trigger PWS001.
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root '.github/workflows/folded.yml') -Value @'
name: Folded
on: [push]
jobs:
  bad:
    runs-on: ubuntu-latest
    steps:
      - run: >
          pwsh
          -NoProfile
          -File scripts/thing.ps1
          -- arg
'@
    $result = Invoke-LintInFixture $root
    $hasPws001 = $result.Output -match 'PWS001' -and $result.Output -match 'folded\.yml'
    Write-TestResult "Fail_YamlFoldedRunBlock" ($result.ExitCode -ne 0 -and $hasPws001) "Expected exit != 0 + PWS001 on YAML `run: >` folded block scalar. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Pass_YamlFoldedRunBlockCorrect ---
    # `run: >` with the correct `-Paths` form must NOT trigger.
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root '.github/workflows/folded-good.yml') -Value @'
name: FoldedGood
on: [push]
jobs:
  ok:
    runs-on: ubuntu-latest
    steps:
      - run: >
          pwsh
          -NoProfile
          -File scripts/thing.ps1
          -Paths arg
'@
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_YamlFoldedRunBlockCorrect" ($result.ExitCode -eq 0) "Expected exit 0 on `run: >` folded block with -Paths. Exit: $($result.ExitCode). Output: $($result.Output)"

    Write-Host "`n  Section: Bash array-indirection pwsh invocation (M1d)" -ForegroundColor White

    # --- Fail_ArrayIndirectionPwshInvocation ---
    # `PWSH_CMD=(pwsh -NoProfile -File); "${PWSH_CMD[@]}" foo.ps1 -- arg` —
    # the pwsh command is expanded from a bash array, so a naive regex on
    # 'pwsh' misses it. The array-variant pattern must still trigger PWS001.
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/array.sh') -Value @'
#!/usr/bin/env bash
set -e
PWSH_CMD=(pwsh -NoProfile -File)
"${PWSH_CMD[@]}" scripts/thing.ps1 -- arg
'@
    $result = Invoke-LintInFixture $root
    $hasPws001 = $result.Output -match 'PWS001' -and $result.Output -match 'array\.sh'
    Write-TestResult "Fail_ArrayIndirectionPwshInvocation" ($result.ExitCode -ne 0 -and $hasPws001) "Expected exit != 0 + PWS001 on array-indirection pwsh invocation with --. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Pass_ArrayIndirectionWithPaths ---
    # Same pattern but with `-Paths` instead of `--` must NOT trigger.
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/array-good.sh') -Value @'
#!/usr/bin/env bash
PWSH_CMD=(pwsh -NoProfile -File)
"${PWSH_CMD[@]}" scripts/thing.ps1 -Paths arg
'@
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_ArrayIndirectionWithPaths" ($result.ExitCode -eq 0) "Expected exit 0 on array-indirection with -Paths. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Pass_ArrayIndirectionNonPowerShellCommand ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/array-node.sh') -Value @'
#!/usr/bin/env bash
NODE_CMD=(node)
"${NODE_CMD[@]}" scripts/thing.ps1 -- arg
CMD=(node)
"${CMD[@]}" scripts/thing.ps1 -- arg
'@
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_ArrayIndirectionNonPowerShellCommand" ($result.ExitCode -eq 0) "Expected exit 0 for non-PowerShell command arrays. Exit: $($result.ExitCode). Output: $($result.Output)"

    Write-Host "`n  Section: PWS003 — subprocess pwsh from scripts/*.ps1" -ForegroundColor White

    # --- Fail_Pws003SubprocessPwshInScriptPs1 ---
    # A scripts/*.ps1 file invokes `& pwsh -NoProfile -File scripts/sibling.ps1`
    # for a sibling script. This fails on Windows PowerShell 5.1 (no pwsh on
    # PATH) and wastes startup time; must trigger PWS003.
    #
    # Fixture content deliberately includes a LASTEXITCODE check: the `& pwsh`
    # on the line immediately under inspection will otherwise be treated as
    # a shell-portability D1 violation (section D of test-shell-portability.sh
    # does not track here-string context). Also realistic — real subprocess
    # pwsh call sites should always inspect the child's exit code.
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/runs-sibling.ps1') -Value @'
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$sibling = Join-Path $PSScriptRoot 'sibling.ps1'
& pwsh -NoProfile -File $sibling
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
'@
    $result = Invoke-LintInFixture $root
    $hasPws003 = $result.Output -match 'PWS003' -and $result.Output -match 'runs-sibling\.ps1'
    Write-TestResult "Fail_Pws003SubprocessPwshInScriptPs1" ($result.ExitCode -ne 0 -and $hasPws003) "Expected exit != 0 + PWS003 when scripts/*.ps1 invokes pwsh -File for a sibling. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_Pws003SubprocessPwshWithLiteralSiblingPath ---
    # Same as above but the sibling path is written as a literal string
    # (scripts/sibling.ps1), not a variable. Must also trigger PWS003.
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/runs-literal.ps1') -Value @'
Set-StrictMode -Version Latest
& pwsh -NoProfile -File scripts/sibling.ps1
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
'@
    $result = Invoke-LintInFixture $root
    $hasPws003 = $result.Output -match 'PWS003' -and $result.Output -match 'runs-literal\.ps1'
    Write-TestResult "Fail_Pws003SubprocessPwshWithLiteralSiblingPath" ($result.ExitCode -ne 0 -and $hasPws003) "Expected exit != 0 + PWS003 on literal scripts/sibling.ps1 invocation. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Pass_Pws003AllowlistMarker ---
    # The top-of-file allowlist marker opts the file out of PWS003 with a
    # human-readable rationale. Must NOT trigger PWS003.
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/allowed.ps1') -Value @'
# lint-pwsh-invocations: allow-subprocess-pwsh child writes structured JSON to stdout; subprocess isolation required.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$sibling = Join-Path $PSScriptRoot 'sibling.ps1'
& pwsh -NoProfile -File $sibling
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
'@
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_Pws003AllowlistMarker" ($result.ExitCode -eq 0) "Expected exit 0 when the allowlist marker is present. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_Pws003AllowlistMarkerMissingRationale ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/bad-marker-no-rationale.ps1') -Value @'
# lint-pwsh-invocations: allow-subprocess-pwsh
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$sibling = Join-Path $PSScriptRoot 'sibling.ps1'
& pwsh -NoProfile -File $sibling
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
'@
    $result = Invoke-LintInFixture $root
    $hasPws003 = $result.Output -match 'PWS003' -and $result.Output -match 'bad-marker-no-rationale\.ps1'
    Write-TestResult "Fail_Pws003AllowlistMarkerMissingRationale" ($result.ExitCode -ne 0 -and $hasPws003) "Expected exit != 0 + PWS003 when the allowlist marker has no rationale. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_Pws003AllowlistMarkerInString ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/bad-marker-string.ps1') -Value @'
Set-StrictMode -Version Latest
Write-Host "lint-pwsh-invocations: allow-subprocess-pwsh not a real marker"
$sibling = Join-Path $PSScriptRoot 'sibling.ps1'
& pwsh -NoProfile -File $sibling
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
'@
    $result = Invoke-LintInFixture $root
    $hasPws003 = $result.Output -match 'PWS003' -and $result.Output -match 'bad-marker-string\.ps1'
    Write-TestResult "Fail_Pws003AllowlistMarkerInString" ($result.ExitCode -ne 0 -and $hasPws003) "Expected exit != 0 + PWS003 when marker text appears only in a string. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_Pws003AllowlistMarkerInlineComment ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/bad-marker-inline.ps1') -Value @'
Set-StrictMode -Version Latest # lint-pwsh-invocations: allow-subprocess-pwsh not a real marker
$sibling = Join-Path $PSScriptRoot 'sibling.ps1'
& pwsh -NoProfile -File $sibling
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
'@
    $result = Invoke-LintInFixture $root
    $hasPws003 = $result.Output -match 'PWS003' -and $result.Output -match 'bad-marker-inline\.ps1'
    Write-TestResult "Fail_Pws003AllowlistMarkerInlineComment" ($result.ExitCode -ne 0 -and $hasPws003) "Expected exit != 0 + PWS003 when marker text appears only in an inline comment. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_Pws003AllowlistMarkerInCommentBlock ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/bad-marker-help-block.ps1') -Value @'
<#
# lint-pwsh-invocations: allow-subprocess-pwsh not a real marker
#>
Set-StrictMode -Version Latest
$sibling = Join-Path $PSScriptRoot 'sibling.ps1'
& pwsh -NoProfile -File $sibling
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
'@
    $result = Invoke-LintInFixture $root
    $hasPws003 = $result.Output -match 'PWS003' -and $result.Output -match 'bad-marker-help-block\.ps1'
    Write-TestResult "Fail_Pws003AllowlistMarkerInCommentBlock" ($result.ExitCode -ne 0 -and $hasPws003) "Expected exit != 0 + PWS003 when marker text appears only in a comment-based help block. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_Pws003AllowlistMarkerInHereString ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/bad-marker-here-string.ps1') -Value @'
$help = @"
# lint-pwsh-invocations: allow-subprocess-pwsh not a real marker
"@
Set-StrictMode -Version Latest
$sibling = Join-Path $PSScriptRoot 'sibling.ps1'
& pwsh -NoProfile -File $sibling
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
'@
    $result = Invoke-LintInFixture $root
    $hasPws003 = $result.Output -match 'PWS003' -and $result.Output -match 'bad-marker-here-string\.ps1'
    Write-TestResult "Fail_Pws003AllowlistMarkerInHereString" ($result.ExitCode -ne 0 -and $hasPws003) "Expected exit != 0 + PWS003 when marker text appears only in a here-string. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_Pws003AllowlistMarkerMalformedUnderscores ---
    # Negative test for the allowlist marker contract: the canonical form is
    # `allow-subprocess-pwsh` with hyphens. A typo'd `allow_subprocess_pwsh`
    # (underscores) must NOT exempt the file — otherwise silent typos in
    # future markers would become invisible escape hatches. The violation
    # must still be flagged as PWS003.
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/bad-marker-underscores.ps1') -Value @'
# lint-pwsh-invocations: allow_subprocess_pwsh child writes structured JSON to stdout; subprocess isolation required.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$sibling = Join-Path $PSScriptRoot 'sibling.ps1'
& pwsh -NoProfile -File $sibling
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
'@
    $result = Invoke-LintInFixture $root
    $hasPws003 = $result.Output -match 'PWS003' -and $result.Output -match 'bad-marker-underscores\.ps1'
    Write-TestResult "Fail_Pws003AllowlistMarkerMalformedUnderscores" ($result.ExitCode -ne 0 -and $hasPws003) "Expected exit != 0 + PWS003 when the allowlist marker uses underscores instead of hyphens. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_Pws003AllowlistMarkerSingularKey ---
    # Another typo variant: `lint-pwsh-invocation:` (singular) instead of the
    # canonical plural `lint-pwsh-invocations:`. Must not exempt the file.
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/bad-marker-singular.ps1') -Value @'
# lint-pwsh-invocation: allow-subprocess-pwsh child writes structured JSON to stdout; subprocess isolation required.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$sibling = Join-Path $PSScriptRoot 'sibling.ps1'
& pwsh -NoProfile -File $sibling
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
'@
    $result = Invoke-LintInFixture $root
    $hasPws003 = $result.Output -match 'PWS003' -and $result.Output -match 'bad-marker-singular\.ps1'
    Write-TestResult "Fail_Pws003AllowlistMarkerSingularKey" ($result.ExitCode -ne 0 -and $hasPws003) "Expected exit != 0 + PWS003 when the allowlist key is singular 'lint-pwsh-invocation' instead of plural. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Pass_Pws003TestsDirExempt ---
    # scripts/tests/*.ps1 is NOT scanned for PWS003 (tests need subprocess
    # isolation to exercise CLI-binding semantics — the very thing PWS002 guards).
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/tests/test-thing.ps1') -Value @'
Set-StrictMode -Version Latest
$script = Join-Path $PSScriptRoot '..' 'foo.ps1'
$output = & pwsh -NoProfile -File $script -Paths bar *>&1
$exitCode = $LASTEXITCODE
'@
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_Pws003TestsDirExempt" ($result.ExitCode -eq 0) "Expected exit 0 — scripts/tests/*.ps1 is exempt from PWS003. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Pass_Pws003StringLiteralNotFlagged ---
    # pwsh invocation text appearing INSIDE a double-quoted or single-quoted
    # string literal (e.g. Write-Host help text) must NOT trigger PWS003.
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/help-text.ps1') -Value @'
Set-StrictMode -Version Latest
Write-Host "  pwsh -NoProfile -File scripts/thing.ps1"
Write-Host '    pwsh -NoProfile -File scripts/other.ps1'
$example = "Run: pwsh -NoProfile -File scripts/yet-another.ps1"
'@
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_Pws003StringLiteralNotFlagged" ($result.ExitCode -eq 0) "Expected exit 0 — pwsh inside a quoted string literal must not trigger PWS003. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Pass_Pws003InlineCommentNotFlagged ---
    # pwsh invocation text appearing inside an inline comment must NOT trigger PWS003.
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/pws003-inline-comment.ps1') -Value @'
param()
Set-StrictMode -Version Latest # historical: pwsh -NoProfile -File scripts/sibling.ps1
'@
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_Pws003InlineCommentNotFlagged" ($result.ExitCode -eq 0) "Expected exit 0 — pwsh inside an inline comment must not trigger PWS003. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Pass_Pws003CommentBlockExempt ---
    # PowerShell comment-based-help blocks that DOCUMENT the anti-pattern
    # must NOT trigger PWS003 (they're documentation, not an invocation).
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/docs.ps1') -Value @'
<#
.SYNOPSIS
    Example invocation:
        pwsh -NoProfile -File scripts/thing.ps1
#>
param()
Write-Host "ok"
'@
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_Pws003CommentBlockExempt" ($result.ExitCode -eq 0) "Expected exit 0 — <# #> help block documenting the anti-pattern must not trigger PWS003. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Pass_Pws003PowerShellCommandAlsoCovered ---
    # `powershell` (not just `pwsh`) — Windows PowerShell 5.1 form — must
    # also trigger PWS003 when invoked from a scripts/*.ps1 file.
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/runs-psexe.ps1') -Value @'
Set-StrictMode -Version Latest
& powershell -NoProfile -ExecutionPolicy Bypass -File scripts/sibling.ps1
'@
    $result = Invoke-LintInFixture $root
    $hasPws003 = $result.Output -match 'PWS003' -and $result.Output -match 'runs-psexe\.ps1'
    Write-TestResult "Fail_Pws003PowerShellCommandAlsoCovered" ($result.ExitCode -ne 0 -and $hasPws003) "Expected exit != 0 + PWS003 for 'powershell -File' form. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_Pws003PowerShellExeShortFileAliasCovered ---
    $root = New-FixtureRoot
    Set-Content -LiteralPath (Join-Path $root 'scripts/runs-psexe-short-file.ps1') -Value @'
Set-StrictMode -Version Latest
& powershell.exe -NoProfile -ExecutionPolicy Bypass -f scripts/sibling.ps1
'@
    $result = Invoke-LintInFixture $root
    $hasPws003 = $result.Output -match 'PWS003' -and $result.Output -match 'runs-psexe-short-file\.ps1'
    Write-TestResult "Fail_Pws003PowerShellExeShortFileAliasCovered" ($result.ExitCode -ne 0 -and $hasPws003) "Expected exit != 0 + PWS003 for 'powershell.exe -f' form. Exit: $($result.ExitCode). Output: $($result.Output)"

} finally {
    Remove-Item -Recurse -Force $tempRoot -ErrorAction SilentlyContinue
}

# ── Summary ──────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host ("Tests passed: {0}" -f $script:TestsPassed) -ForegroundColor Green
Write-Host ("Tests failed: {0}" -f $script:TestsFailed) -ForegroundColor $(if ($script:TestsFailed -gt 0) { "Red" } else { "Green" })
if ($script:FailedTests.Count -gt 0) {
    Write-Host "Failed tests:" -ForegroundColor Red
    foreach ($t in $script:FailedTests) {
        Write-Host "  - $t" -ForegroundColor Red
    }
}

exit $script:TestsFailed
