Param(
  [switch]$VerboseOutput
)

<#
.SYNOPSIS
    Test runner for lint-changelog.ps1

.DESCRIPTION
    lint-changelog.ps1 was the one linter in scripts/ with no self-test that could make it report,
    and the reason was structural rather than an oversight: it read the repository's own
    CHANGELOG.md from $PSScriptRoot and there was no way to hand it anything else. A green run over
    a valid CHANGELOG proves the file is valid; it proves nothing about whether the linter still
    fires (#556). It now takes -ChangelogPath, and this drives a fixture through every rule that
    exits non-zero.

    Green half:
    - a well-formed changelog passes
    - warnings alone (missing date, non-standard change type) do not fail the run

    Red halves, one per error rule, each asserted on its OWN message rather than on the exit code
    alone -- a fixture that trips a different rule than the one it is named for would otherwise
    read as covered:
    - a missing changelog file
    - a malformed version header
    - an invalid date
    - a heading with no space after ## or ###
    - an empty bullet
    - an [Unreleased] entry over the rendered-length limit
    - a blank line splitting one [Unreleased] list in two
    - a missing [Unreleased] section
    - a version header above [Unreleased]
    - a duplicate version

.PARAMETER VerboseOutput
    Show detailed output during test execution

.EXAMPLE
    ./scripts/tests/test-lint-changelog.ps1
    ./scripts/tests/test-lint-changelog.ps1 -VerboseOutput
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:TestsPassed = 0
$script:TestsFailed = 0
$script:FailedTests = @()

$repoRoot = (Get-Item $PSScriptRoot).Parent.Parent.FullName
$linter = Join-Path $repoRoot 'scripts/lint-changelog.ps1'
$workspace = Join-Path ([System.IO.Path]::GetTempPath()) ("lint-changelog-tests-" + [System.Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $workspace -Force | Out-Null

function Write-Info($msg) {
  if ($VerboseOutput) { Write-Host "[test-lint-changelog] $msg" -ForegroundColor Cyan }
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
  param([string]$Path, [int]$MaxEntryLength = 300)

  $output = & pwsh -NoProfile -File $linter -ChangelogPath $Path -MaxEntryLength $MaxEntryLength 2>&1
  return [pscustomobject]@{
    ExitCode = $LASTEXITCODE
    Output   = ($output | Out-String)
  }
}

function New-Changelog {
  param([string]$Name, [string]$Body)

  $path = Join-Path $workspace "$Name.md"
  Set-Content -LiteralPath $path -Value $Body -Encoding utf8
  return $path
}

$validBody = @'
# Changelog

## [Unreleased]

### Added

- Bind the enabled relational component when a disabled one sorts first.

## [1.2.0] - 2026-08-01

### Fixed

- Stop a per-frame allocation in the animator parameter cache.

## [1.1.0] - 2026-07-01

### Added

- Initial release notes.
'@

function Test-Green {
  param([string]$TestName, [string]$Name, [string]$Body)

  $result = Invoke-Linter -Path (New-Changelog -Name $Name -Body $Body)
  Write-TestResult -TestName $TestName -Passed ($result.ExitCode -eq 0) -Message "exit $($result.ExitCode): $($result.Output)"
}

function Test-Red {
  param([string]$TestName, [string]$Name, [string]$Body, [string]$Expect, [int]$MaxEntryLength = 300)

  $result = Invoke-Linter -Path (New-Changelog -Name $Name -Body $Body) -MaxEntryLength $MaxEntryLength
  if ($result.ExitCode -eq 0) {
    Write-TestResult -TestName $TestName -Passed $false -Message "expected a non-zero exit, got 0"
    return
  }
  $matched = $result.Output -match [regex]::Escape($Expect)
  Write-TestResult -TestName $TestName -Passed $matched -Message "exited $($result.ExitCode) but not through '$Expect': $($result.Output)"
}

Write-Host 'Testing scripts/lint-changelog.ps1...'
Write-Host ''
Write-Host '  Section: green half'

Test-Green -TestName 'a well-formed changelog passes' -Name 'valid' -Body $validBody

Test-Green -TestName 'warnings alone do not fail the run' -Name 'warnings-only' -Body @'
# Changelog

## [Unreleased]

### Tweaked

- A non-standard change type is a warning, not an error.

## [1.0.0]

### Added

- A version with no date is a warning, not an error.
'@

Write-Host '  Section: red halves'

$missing = Join-Path $workspace 'does-not-exist.md'
$missingResult = Invoke-Linter -Path $missing
Write-TestResult -TestName 'a missing changelog fails' `
  -Passed (($missingResult.ExitCode -ne 0) -and ($missingResult.Output -match 'CHANGELOG.md not found')) `
  -Message "exit $($missingResult.ExitCode): $($missingResult.Output)"

Test-Red -TestName 'a malformed version header fails' -Name 'malformed-version' -Expect 'Malformed version header' -Body @'
# Changelog

## [Unreleased]

### Added

- An entry.

## [1.0.0] - not-a-date

### Added

- An entry.
'@

Test-Red -TestName 'an invalid date fails' -Name 'invalid-date' -Expect 'Invalid date format' -Body @'
# Changelog

## [Unreleased]

### Added

- An entry.

## [1.0.0] - 2026-13-45

### Added

- An entry.
'@

Test-Red -TestName 'a heading with no space after ### fails' -Name 'no-space-heading' -Expect 'missing space after' -Body @'
# Changelog

## [Unreleased]

###Added

- An entry.
'@

Test-Red -TestName 'an empty bullet fails' -Name 'empty-bullet' -Expect 'Empty bullet list item' -Body @'
# Changelog

## [Unreleased]

### Added

- An entry.
-
'@

Test-Red -TestName 'an over-length [Unreleased] entry fails' -Name 'too-long' -Expect 'over the 40 limit' -MaxEntryLength 40 -Body @'
# Changelog

## [Unreleased]

### Added

- This entry is comfortably longer than forty rendered characters and must be reported.
'@

Test-Red -TestName 'a blank line splitting one list fails' -Name 'split-list' -Expect 'Blank line between two entries' -Body @'
# Changelog

## [Unreleased]

### Added

- The first entry.

- The second entry, separated by a blank line.
'@

Test-Red -TestName 'a missing [Unreleased] section fails' -Name 'no-unreleased' -Expect "Missing required '## [Unreleased]' section" -Body @'
# Changelog

## [1.0.0] - 2026-08-01

### Added

- An entry.
'@

Test-Red -TestName 'a version above [Unreleased] fails' -Name 'version-first' -Expect 'appears before [Unreleased] section' -Body @'
# Changelog

## [1.0.0] - 2026-08-01

### Added

- An entry.

## [Unreleased]

### Added

- An entry.
'@

Test-Red -TestName 'a duplicate version fails' -Name 'duplicate-version' -Expect 'Duplicate version [1.0.0]' -Body @'
# Changelog

## [Unreleased]

### Added

- An entry.

## [1.0.0] - 2026-08-01

### Added

- An entry.

## [1.0.0] - 2026-07-01

### Added

- An entry.
'@

Remove-Item -LiteralPath $workspace -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ''
Write-Host "Passed: $($script:TestsPassed)  Failed: $($script:TestsFailed)"
if (0 -lt $script:TestsFailed) {
  Write-Host "Failed tests: $($script:FailedTests -join ', ')" -ForegroundColor Red
  exit 1
}
exit 0
