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
  param([string]$Root)

  $output = & pwsh -NoProfile -File $validator -RepoRoot $Root 2>&1
  return [pscustomobject]@{
    ExitCode = $LASTEXITCODE
    Output   = ConvertTo-Flat -Text ($output | Out-String)
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
  if ($result.ExitCode -eq 0) {
    Write-TestResult -TestName $TestName -Passed $false -Message 'validator accepted a hook set it must reject'
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

  # ── Green half ────────────────────────────────────────────────────────────
  Test-Accepts -TestName 'the repository hooks pass' -Root $repoRoot

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
