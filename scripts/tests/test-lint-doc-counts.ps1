Param(
  [switch]$VerboseOutput
)

<#
.SYNOPSIS
    Test runner for lint-doc-counts.ps1

.DESCRIPTION
    lint-doc-counts.ps1 is a wrapper: it locates sync-doc-counts.ps1 and runs it in check mode,
    propagating the exit code. Both of those are rules, and neither could be made to report --
    the script resolved sync-doc-counts.ps1 from $PSScriptRoot with no way to hand it anything
    else, so a green run proved the documentation counts were in sync and proved nothing about
    the wrapper (#556, #562). It now takes -SyncScriptPath.

    Propagation is the rule worth pinning. If the wrapper ever swallowed a non-zero exit -- by
    catching, by ending on a Write-Host, by losing $LASTEXITCODE behind another command -- every
    drifted count in the repository would report as green, and the real sync script exits 1 for
    exactly that case.

    Green half:
    - a stub that exits 0 makes the wrapper exit 0 (the real counts are checked by `lint:repo`,
      so re-running them here would be a third invocation of a 26.7 s scan)

    Red halves:
    - a sync script path that does not exist
    - a stub that exits 1 (the drifted-counts case)
    - a stub that exits 42, so propagation is shown to pass the CODE through rather than
      collapsing every failure to 1
    - an unexpected positional argument

.PARAMETER VerboseOutput
    Show detailed output during test execution

.EXAMPLE
    ./scripts/tests/test-lint-doc-counts.ps1
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:TestsPassed = 0
$script:TestsFailed = 0
$script:FailedTests = @()

$repoRoot = (Get-Item $PSScriptRoot).Parent.Parent.FullName
$linter = Join-Path $repoRoot 'scripts/lint-doc-counts.ps1'
$workspace = Join-Path ([System.IO.Path]::GetTempPath()) ("lint-doc-counts-tests-" + [System.Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $workspace -Force | Out-Null

function Write-Info($msg) {
  if ($VerboseOutput) { Write-Host "[test-lint-doc-counts] $msg" -ForegroundColor Cyan }
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

function Invoke-Linter {
  param([string[]]$Arguments)

  $output = & pwsh -NoProfile -File $linter @Arguments 2>&1
  return [pscustomobject]@{
    ExitCode = $LASTEXITCODE
    Output   = ($output | Out-String)
  }
}

# A stub standing in for sync-doc-counts.ps1. It asserts it was handed -Check, so a wrapper that
# stopped passing check mode -- and started REWRITING the documentation instead of validating it --
# reports here rather than silently mutating a tree in CI.
function New-SyncStub {
  param([string]$Name, [int]$ExitCode)

  $path = Join-Path $workspace "$Name.ps1"
  Set-Content -LiteralPath $path -Value @"
param([switch]`$Check)
if (-not `$Check) {
  Write-Host 'STUB-ERROR: wrapper did not pass -Check'
  exit 99
}
Write-Host 'STUB-RAN'
exit $ExitCode
"@
  return $path
}

function Test-ExitCode {
  param([string]$TestName, [string[]]$Arguments, [int]$Expected, [string]$ExpectedMessage = '')

  $result = Invoke-Linter -Arguments $Arguments
  if ($result.ExitCode -ne $Expected) {
    Write-TestResult -TestName $TestName -Passed $false -Message "expected exit $Expected, got $($result.ExitCode): $($result.Output)"
    return
  }
  if ($ExpectedMessage -and $result.Output -notlike "*$ExpectedMessage*") {
    Write-TestResult -TestName $TestName -Passed $false -Message "exit code matched but output did not contain '$ExpectedMessage'. Got: $($result.Output)"
    return
  }
  Write-TestResult -TestName $TestName -Passed $true
}

Write-Host ''
Write-Host 'Running lint-doc-counts.ps1 self-tests' -ForegroundColor Cyan
Write-Host ''

try {
  Write-Info "Workspace: $workspace"

  # ── Green half ────────────────────────────────────────────────────────────
  # Deliberately NOT "run the wrapper with no arguments and expect 0". That spawns the real
  # sync-doc-counts.ps1 over the whole repository -- 26.7 s of this suite's wall clock -- to
  # re-answer a question `lint:repo` (check id `doc-counts`) and `validate:content` both already
  # ask. The stub below is the green half for THIS script's contract, which is the wrapper, not
  # the counts (#543).
  $ok = New-SyncStub -Name 'exit-zero' -ExitCode 0
  Test-ExitCode -TestName 'a sync script that exits 0 makes the wrapper exit 0' `
    -Arguments @('-SyncScriptPath', $ok) -Expected 0 -ExpectedMessage 'STUB-RAN'

  # ── Red halves ────────────────────────────────────────────────────────────
  Test-ExitCode -TestName 'a missing sync script is rejected' `
    -Arguments @('-SyncScriptPath', (Join-Path $workspace 'absent.ps1')) `
    -Expected 1 -ExpectedMessage 'sync-doc-counts.ps1 not found at'

  $drifted = New-SyncStub -Name 'exit-one' -ExitCode 1
  Test-ExitCode -TestName 'a drifted-count failure propagates as exit 1' `
    -Arguments @('-SyncScriptPath', $drifted) -Expected 1 -ExpectedMessage 'STUB-RAN'

  # 42 rather than 1, because "propagates" and "returns 1 on any failure" are different
  # contracts and only one of them is what CI reads.
  $odd = New-SyncStub -Name 'exit-forty-two' -ExitCode 42
  Test-ExitCode -TestName 'the sync script exit code is propagated, not collapsed' `
    -Arguments @('-SyncScriptPath', $odd) -Expected 42 -ExpectedMessage 'STUB-RAN'

  Test-ExitCode -TestName 'an unexpected positional argument is rejected' `
    -Arguments @('unexpected-positional') -Expected 1 -ExpectedMessage 'Unexpected arguments'
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
