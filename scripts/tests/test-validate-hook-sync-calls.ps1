Param(
  [switch]$VerboseOutput
)

<#
.SYNOPSIS
    Test runner for validate-hook-sync-calls.ps1

.DESCRIPTION
    The validator reads three hook implementations and asserts each still contains the calls and
    patterns it must. Run against the repository's own .githooks it prints nothing and exits 0,
    which is evidence about the hooks and no evidence about the validator (#556, #562). It now
    takes -RepoRoot, and this drives a fixture tree through every rule that exits non-zero.

    The fixture hooks are built from the REAL ones, so a rule added to the validator without a
    matching fixture change fails here as a green half rather than passing vacuously.

    Green half:
    - the repository's own hooks pass

    Red halves, one per rule that exits non-zero, each asserted on its own message:
    - a missing pre-commit.ps1
    - a pre-commit.ps1 that dropped a required sync script call
    - a missing pre-push.ps1
    - a pre-push.ps1 that dropped each of the four changed-file detection patterns
    - a missing pre-merge-commit.ps1
    - a pre-merge-commit.ps1 that dropped each of the three delegation patterns
    - a pre-merge-commit.ps1 that reintroduced a forbidden second-startup pattern

.PARAMETER VerboseOutput
    Show detailed output during test execution

.EXAMPLE
    ./scripts/tests/test-validate-hook-sync-calls.ps1
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:TestsPassed = 0
$script:TestsFailed = 0
$script:FailedTests = @()

$repoRoot = (Get-Item $PSScriptRoot).Parent.Parent.FullName
$validator = Join-Path $repoRoot 'scripts/validate-hook-sync-calls.ps1'
$realHooks = Join-Path $repoRoot '.githooks'
$workspace = Join-Path ([System.IO.Path]::GetTempPath()) ("validate-hook-sync-calls-tests-" + [System.Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $workspace -Force | Out-Null

function Write-Info($msg) {
  if ($VerboseOutput) { Write-Host "[test-validate-hook-sync-calls] $msg" -ForegroundColor Cyan }
}

function Write-TestResult {
  param([string]$TestName, [bool]$Passed, [string]$Message = '')

  if ($Passed) {
    Write-Host "  [PASS] $TestName" -ForegroundColor Green
    $script:TestsPassed++
  }
  else {
    Write-Host "  [FAIL] $TestName" -ForegroundColor Red
    if ($Message) { Write-Host "         $Message" -ForegroundColor DarkGray }
    $script:TestsFailed++
    $script:FailedTests += $TestName
  }
}

# Write-Error renders through PowerShell's error formatter, which hard-wraps the message across
# lines behind a "|" gutter. A raw substring match against the sentence the validator wrote
# therefore fails for a reason that has nothing to do with the rule under test -- and a self-test
# that reports for the wrong reason is the failure mode this whole exercise is about.
function ConvertTo-Flat {
  param([string]$Text)

  $flat = $Text -replace "`e\[[0-9;]*m", ''
  $flat = $flat -replace '\r?\n\s*\|\s*', ' '
  $flat = $flat -replace '\s+', ' '
  return $flat
}

function Invoke-Validator {
  param([string]$Root, [string]$ScriptPath = $validator, [switch]$Cli)

  if ($Cli) {
    $output = & pwsh -NoProfile -File $ScriptPath -RepoRoot $Root 2>&1
    return [pscustomobject]@{
      ExitCode = $LASTEXITCODE
      Output   = ConvertTo-Flat -Text ($output | Out-String)
    }
  }

  $runner = [System.Management.Automation.PowerShell]::Create()
  try {
    $null = $runner.AddCommand($ScriptPath).AddParameter('RepoRoot', $Root)
    $failure = $null
    $result = @()
    try { $result = $runner.Invoke() }
    catch { $failure = $_.Exception }
    $output = [System.Collections.Generic.List[string]]::new()
    foreach ($item in $result) { $output.Add($item.ToString()) }
    foreach ($item in $runner.Streams.Information) { $output.Add($item.MessageData.ToString()) }
    foreach ($item in $runner.Streams.Warning) { $output.Add($item.Message) }
    foreach ($item in $runner.Streams.Error) { $output.Add($item.ToString()) }
    if ($null -ne $failure) {
      $output.Add($failure.Message)
      return [pscustomobject]@{
        ExitCode = 1
        Output   = ConvertTo-Flat -Text ($output -join [Environment]::NewLine)
      }
    }
    $exitCode = $runner.Runspace.SessionStateProxy.GetVariable('LASTEXITCODE')
    if ($null -eq $exitCode) {
      throw "Validator returned without an exit code. $($output -join [Environment]::NewLine)"
    }
    if ($runner.Streams.Error.Count -gt 0 -and [int]$exitCode -eq 0) {
      throw "Validator emitted a PowerShell error with exit 0. $($output -join [Environment]::NewLine)"
    }
    return [pscustomobject]@{
      ExitCode = [int]$exitCode
      Output   = ConvertTo-Flat -Text ($output -join [Environment]::NewLine)
    }
  }
  finally {
    $runner.Dispose()
  }
}

# Each fixture is the real hook set, then one mutation. Copying rather than authoring keeps the
# fixtures honest: they satisfy every rule the validator has today, including any added after
# this file was written.
function New-HookFixture {
  param([string]$Name)

  $root = Join-Path $workspace $Name
  $hooks = Join-Path $root '.githooks'
  New-Item -ItemType Directory -Path $hooks -Force | Out-Null
  foreach ($hook in @('pre-commit.ps1', 'pre-push.ps1', 'pre-merge-commit.ps1')) {
    Copy-Item -LiteralPath (Join-Path $realHooks $hook) -Destination (Join-Path $hooks $hook)
  }
  return $root
}

# Case-INSENSITIVE, because the validator's own -notmatch is: `-LocalSha $localSha` satisfies its
# requirement for "localSha" twice over. A case-sensitive removal leaves the parameter name behind,
# the validator still finds it, and the red half reports green while having removed nothing. The
# removal has to delete exactly what the rule looks for, not what it appears to look for.
function Remove-HookText {
  param([string]$Root, [string]$Hook, [string]$Text)

  $path = Join-Path (Join-Path $Root '.githooks') $Hook
  $content = Get-Content -LiteralPath $path -Raw
  $pattern = [regex]::Escape($Text)
  if (-not [regex]::IsMatch($content, $pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
    throw "fixture cannot remove '$Text' from $Hook because it is not there -- the validator's rule and this fixture have drifted apart"
  }
  $stripped = [regex]::Replace($content, $pattern, 'REMOVED-BY-SELF-TEST', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
  Set-Content -LiteralPath $path -Value $stripped -NoNewline
}

function Test-Accepts {
  param([string]$TestName, [string]$Root)

  $result = Invoke-Validator -Root $Root
  if ($result.ExitCode -eq 0) {
    Write-TestResult -TestName $TestName -Passed $true
  }
  else {
    Write-TestResult -TestName $TestName -Passed $false -Message "exit $($result.ExitCode): $($result.Output)"
  }
}

function Test-Rejects {
  param([string]$TestName, [string]$Root, [string]$ExpectedMessage)

  $result = Invoke-Validator -Root $Root
  if ($result.ExitCode -ne 1) {
    Write-TestResult -TestName $TestName -Passed $false -Message "expected exit 1, received exit $($result.ExitCode)"
    return
  }
  # .Contains, not -like: -like reads square brackets as a wildcard character class, and the
  # expected messages here quote patterns such as [Console]::In.ReadToEnd.
  if (-not $result.Output.Contains((ConvertTo-Flat -Text $ExpectedMessage))) {
    Write-TestResult -TestName $TestName -Passed $false -Message "rejected, but not for the reason under test. Expected to contain '$ExpectedMessage'. Got: $($result.Output)"
    return
  }
  Write-TestResult -TestName $TestName -Passed $true
}

Write-Host ''
Write-Host 'Running validate-hook-sync-calls.ps1 self-tests' -ForegroundColor Cyan
Write-Host ''

try {
  Write-Info "Workspace: $workspace"

  $harnessScript = Join-Path $workspace 'harness.ps1'
  Set-Content -LiteralPath $harnessScript -Value "param([string]`$RepoRoot) Write-Host 'harness-zero'; exit 0"
  $harnessResult = Invoke-Validator -Root $repoRoot -ScriptPath $harnessScript
  Write-TestResult -TestName 'runspace preserves exit 0 and output' `
    -Passed ($harnessResult.ExitCode -eq 0 -and $harnessResult.Output.Contains('harness-zero'))
  Set-Content -LiteralPath $harnessScript -Value "param([string]`$RepoRoot) Write-Host 'harness-one'; exit 1"
  $harnessResult = Invoke-Validator -Root $repoRoot -ScriptPath $harnessScript
  Write-TestResult -TestName 'runspace preserves exit 1 and output' `
    -Passed ($harnessResult.ExitCode -eq 1 -and $harnessResult.Output.Contains('harness-one'))
  Set-Content -LiteralPath $harnessScript -Value "param([string]`$RepoRoot) Write-Warning 'harness-warning'; exit 0"
  $harnessResult = Invoke-Validator -Root $repoRoot -ScriptPath $harnessScript
  Write-TestResult -TestName 'runspace preserves warning output' `
    -Passed ($harnessResult.ExitCode -eq 0 -and $harnessResult.Output.Contains('harness-warning'))
  Set-Content -LiteralPath $harnessScript -Value "param([string]`$RepoRoot) `$global:FixtureState = 'dirty'; exit 0"
  $null = Invoke-Validator -Root $repoRoot -ScriptPath $harnessScript
  Set-Content -LiteralPath $harnessScript -Value "param([string]`$RepoRoot) if (Get-Variable -Name FixtureState -Scope Global -ErrorAction SilentlyContinue) { exit 1 }; exit 0"
  $harnessResult = Invoke-Validator -Root $repoRoot -ScriptPath $harnessScript
  Write-TestResult -TestName 'runspace starts with fresh global state' -Passed ($harnessResult.ExitCode -eq 0)
  Set-Content -LiteralPath $harnessScript -Value "param([string]`$RepoRoot) Write-Host 'harness-missing-exit'"
  $missingExitFailed = $false
  try { $null = Invoke-Validator -Root $repoRoot -ScriptPath $harnessScript }
  catch { $missingExitFailed = $_.Exception.Message.Contains('without an exit code') }
  Write-TestResult -TestName 'runspace rejects a missing exit' -Passed $missingExitFailed
  Set-Content -LiteralPath $harnessScript -Value "param([string]`$RepoRoot) `$ErrorActionPreference = 'Stop'; Write-Error 'harness-error'"
  $harnessResult = Invoke-Validator -Root $repoRoot -ScriptPath $harnessScript
  Write-TestResult -TestName 'runspace classifies a terminating diagnostic as exit 1' `
    -Passed ($harnessResult.ExitCode -eq 1 -and $harnessResult.Output.Contains('harness-error'))
  Set-Content -LiteralPath $harnessScript -Value "param([string]`$RepoRoot) Write-Error 'harness-error'; exit 0"
  $errorFailed = $false
  try { $null = Invoke-Validator -Root $repoRoot -ScriptPath $harnessScript }
  catch { $errorFailed = $_.Exception.Message.Contains('PowerShell error with exit 0') }
  Write-TestResult -TestName 'runspace rejects an error with exit 0' -Passed $errorFailed

  # ── Green half ────────────────────────────────────────────────────────────
  $realResult = Invoke-Validator -Root $repoRoot -Cli
  Write-TestResult -TestName 'the repository hooks pass through pwsh -File' `
    -Passed ($realResult.ExitCode -eq 0) -Message "exit $($realResult.ExitCode): $($realResult.Output)"

  $baseline = New-HookFixture -Name 'baseline'
  Test-Accepts -TestName 'a verbatim copy of the hooks passes' -Root $baseline

  # ── Red halves: pre-commit ────────────────────────────────────────────────
  $noPreCommit = New-HookFixture -Name 'no-pre-commit'
  Remove-Item -LiteralPath (Join-Path $noPreCommit '.githooks/pre-commit.ps1')
  Test-Rejects -TestName 'a missing pre-commit.ps1 is rejected' -Root $noPreCommit `
    -ExpectedMessage 'pre-commit PowerShell implementation not found'

  foreach ($sync in @('scripts/sync-banner-version.ps1', 'scripts/sync-issue-template-versions.ps1')) {
    $name = 'no-' + ($sync -replace '[^a-zA-Z0-9]', '-')
    $root = New-HookFixture -Name $name
    Remove-HookText -Root $root -Hook 'pre-commit.ps1' -Text $sync
    Test-Rejects -TestName "a pre-commit that dropped $sync is rejected" -Root $root `
      -ExpectedMessage 'missing 1 required sync script call'
  }

  # ── Red halves: pre-push ──────────────────────────────────────────────────
  $noPrePush = New-HookFixture -Name 'no-pre-push'
  Remove-Item -LiteralPath (Join-Path $noPrePush '.githooks/pre-push.ps1')
  Test-Rejects -TestName 'a missing pre-push.ps1 is rejected' -Root $noPrePush `
    -ExpectedMessage 'pre-push PowerShell implementation not found'

  foreach ($pattern in @('[Console]::In.ReadToEnd', 'localSha', 'remoteSha', 'allChanged')) {
    $name = 'no-prepush-' + ($pattern -replace '[^a-zA-Z0-9]', '-')
    $root = New-HookFixture -Name $name
    Remove-HookText -Root $root -Hook 'pre-push.ps1' -Text $pattern
    Test-Rejects -TestName "a pre-push that dropped '$pattern' is rejected" -Root $root `
      -ExpectedMessage 'The hook must read stdin to detect changed files'
  }

  # ── Red halves: pre-merge-commit ──────────────────────────────────────────
  $noMerge = New-HookFixture -Name 'no-pre-merge-commit'
  Remove-Item -LiteralPath (Join-Path $noMerge '.githooks/pre-merge-commit.ps1')
  Test-Rejects -TestName 'a missing pre-merge-commit.ps1 is rejected' -Root $noMerge `
    -ExpectedMessage 'Merge commits will bypass pre-commit validation'

  foreach ($pattern in @('& $preCommit @HookArgs', 'exit $LASTEXITCODE')) {
    $name = 'no-merge-' + ($pattern -replace '[^a-zA-Z0-9]', '-')
    $root = New-HookFixture -Name $name
    Remove-HookText -Root $root -Hook 'pre-merge-commit.ps1' -Text $pattern
    Test-Rejects -TestName "a pre-merge-commit that dropped '$pattern' is rejected" -Root $root `
      -ExpectedMessage 'Without delegation, merge commits bypass pre-commit validation'
  }

  # A hook that spawns a second PowerShell is the regression this forbids: it passes every
  # "is it wired up" assertion and costs a process start-up on every merge.
  $secondStartup = New-HookFixture -Name 'second-startup'
  $mergePath = Join-Path $secondStartup '.githooks/pre-merge-commit.ps1'
  Add-Content -LiteralPath $mergePath -Value "`n`$pwshPath = (Get-Process -Id `$PID).Path"
  Test-Rejects -TestName 'a reintroduced second-startup pattern is rejected' -Root $secondStartup `
    -ExpectedMessage 'must delegate to pre-commit.ps1 in-process'
}
finally {
  Remove-Item -LiteralPath $workspace -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host "Passed: $script:TestsPassed" -ForegroundColor Green
Write-Host "Failed: $script:TestsFailed" -ForegroundColor $(if ($script:TestsFailed -gt 0) { 'Red' } else { 'Green' })

if ($script:TestsFailed -gt 0) {
  Write-Host ''
  Write-Host 'Failed tests:' -ForegroundColor Red
  foreach ($name in $script:FailedTests) {
    Write-Host "  - $name" -ForegroundColor Red
  }
  exit 1
}

exit 0
