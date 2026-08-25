Param(
  [switch]$VerboseOutput
)

<#
.SYNOPSIS
    Test runner for lint-unity-test-modules.ps1

.DESCRIPTION
    The linter's whole value is that it FIRES: a manifest declaring a package id that does not
    exist (the classic being com.unity.modules.grid) kills every Unity leg half an hour later with
    no obviously-named cause. A green run over the repository's own valid manifest proves the
    manifest is valid and proves nothing about whether the linter still reports (#556, #562).

    It already takes -Path, so no production change was needed here -- only the fixtures.

    Green half:
    - the repository's real manifest passes
    - a minimal well-formed fixture passes, with and without an _evidence map

    Red halves, one per rule that exits non-zero, each asserted on its OWN message so a fixture
    that trips a neighbouring rule cannot read as covering this one:
    - a missing manifest
    - a manifest that is not valid JSON
    - a manifest with no 'modules' object
    - a manifest declaring zero modules
    - a module with an empty version
    - com.unity.modules.grid, the alias that has a real replacement
    - an unknown built-in module short name (a typo)
    - an id that is neither a built-in module nor a bundled package
    - a module with no _evidence entry
    - an _evidence entry with no module

.PARAMETER VerboseOutput
    Show detailed output during test execution

.EXAMPLE
    ./scripts/tests/test-lint-unity-test-modules.ps1
    ./scripts/tests/test-lint-unity-test-modules.ps1 -VerboseOutput
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:TestsPassed = 0
$script:TestsFailed = 0
$script:FailedTests = @()

$repoRoot = (Get-Item $PSScriptRoot).Parent.Parent.FullName
$linter = Join-Path $repoRoot 'scripts/lint-unity-test-modules.ps1'
$realManifest = Join-Path $repoRoot '.github/unity-test-project-modules.json'
$workspace = Join-Path ([System.IO.Path]::GetTempPath()) ("lint-unity-test-modules-tests-" + [System.Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $workspace -Force | Out-Null

function Write-Info($msg) {
  if ($VerboseOutput) { Write-Host "[test-lint-unity-test-modules] $msg" -ForegroundColor Cyan }
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
  param([string]$Path)

  $output = & pwsh -NoProfile -File $linter -Path $Path 2>&1
  return [pscustomobject]@{
    ExitCode = $LASTEXITCODE
    Output   = ($output | Out-String)
  }
}

function New-Manifest {
  param([Parameter(Mandatory = $true)][string]$Name, [Parameter(Mandatory = $true)][string]$Content)

  $path = Join-Path $workspace "$Name.json"
  Set-Content -LiteralPath $path -Value $Content -NoNewline
  return $path
}

function Test-Accepts {
  param([string]$TestName, [string]$Path)

  $result = Invoke-Linter -Path $Path
  if ($result.ExitCode -eq 0) {
    Write-TestResult -TestName $TestName -Passed $true
  }
  else {
    Write-TestResult -TestName $TestName -Passed $false -Message "exit $($result.ExitCode): $($result.Output)"
  }
}

function Test-Rejects {
  param([string]$TestName, [string]$Path, [string]$ExpectedMessage)

  $result = Invoke-Linter -Path $Path
  if ($result.ExitCode -eq 0) {
    Write-TestResult -TestName $TestName -Passed $false -Message 'linter accepted a manifest it must reject'
    return
  }
  # .Contains, not -like: -like reads square brackets as a wildcard character class.
  if (-not $result.Output.Contains($ExpectedMessage)) {
    Write-TestResult -TestName $TestName -Passed $false -Message "rejected, but not for the reason under test. Expected to contain '$ExpectedMessage'. Got: $($result.Output)"
    return
  }
  Write-TestResult -TestName $TestName -Passed $true
}

Write-Host ''
Write-Host 'Running lint-unity-test-modules.ps1 self-tests' -ForegroundColor Cyan
Write-Host ''

try {
  Write-Info "Workspace: $workspace"

  # ── Green half ────────────────────────────────────────────────────────────
  Test-Accepts -TestName 'the repository manifest passes' -Path $realManifest

  $minimal = New-Manifest -Name 'minimal' -Content @'
{
  "modules": {
    "com.unity.modules.physics2d": "1.0.0",
    "com.unity.ugui": "1.0.0"
  },
  "_evidence": {
    "com.unity.modules.physics2d": "Collider2D in the relational component fixtures.",
    "com.unity.ugui": "UnityEngine.UI.Image in the visuals fixtures."
  }
}
'@
  Test-Accepts -TestName 'a well-formed fixture passes' -Path $minimal

  $noEvidence = New-Manifest -Name 'no-evidence' -Content @'
{
  "modules": {
    "com.unity.modules.animation": "1.0.0"
  }
}
'@
  Test-Accepts -TestName 'an absent _evidence map is not an error' -Path $noEvidence

  # ── Red halves ────────────────────────────────────────────────────────────
  Test-Rejects -TestName 'a missing manifest is rejected' `
    -Path (Join-Path $workspace 'does-not-exist.json') `
    -ExpectedMessage 'modules manifest not found'

  $badJson = New-Manifest -Name 'bad-json' -Content '{ "modules": { '
  Test-Rejects -TestName 'invalid JSON is rejected' -Path $badJson -ExpectedMessage 'is not valid JSON'

  $noModules = New-Manifest -Name 'no-modules' -Content '{ "_evidence": {} }'
  Test-Rejects -TestName 'a missing modules object is rejected' -Path $noModules `
    -ExpectedMessage "is missing a 'modules' object"

  $emptyModules = New-Manifest -Name 'empty-modules' -Content '{ "modules": {} }'
  Test-Rejects -TestName 'a manifest declaring no modules is rejected' -Path $emptyModules `
    -ExpectedMessage 'declares no modules'

  $emptyVersion = New-Manifest -Name 'empty-version' -Content @'
{
  "modules": {
    "com.unity.modules.animation": ""
  },
  "_evidence": {
    "com.unity.modules.animation": "Animator in the relational fixtures."
  }
}
'@
  Test-Rejects -TestName 'an empty version is rejected' -Path $emptyVersion `
    -ExpectedMessage 'has an empty version'

  # The id this linter was written for: it looks real, and Unity has no such package.
  $gridAlias = New-Manifest -Name 'grid-alias' -Content @'
{
  "modules": {
    "com.unity.modules.grid": "1.0.0"
  },
  "_evidence": {
    "com.unity.modules.grid": "GridLayout in the tilemap fixtures."
  }
}
'@
  Test-Rejects -TestName 'com.unity.modules.grid is rejected with its real replacement' -Path $gridAlias `
    -ExpectedMessage "Use 'com.unity.modules.tilemap' instead"

  $typo = New-Manifest -Name 'typo' -Content @'
{
  "modules": {
    "com.unity.modules.physisc2d": "1.0.0"
  },
  "_evidence": {
    "com.unity.modules.physisc2d": "Collider2D."
  }
}
'@
  Test-Rejects -TestName 'a typo in a built-in module short name is rejected' -Path $typo `
    -ExpectedMessage 'is not a known Unity built-in module'

  $foreignPackage = New-Manifest -Name 'foreign-package' -Content @'
{
  "modules": {
    "com.unity.textmeshpro": "3.0.6"
  },
  "_evidence": {
    "com.unity.textmeshpro": "Not a built-in module."
  }
}
'@
  Test-Rejects -TestName 'a non-module, non-bundled package is rejected' -Path $foreignPackage `
    -ExpectedMessage 'is not allowed here'

  $missingEvidence = New-Manifest -Name 'missing-evidence' -Content @'
{
  "modules": {
    "com.unity.modules.animation": "1.0.0",
    "com.unity.modules.audio": "1.0.0"
  },
  "_evidence": {
    "com.unity.modules.animation": "Animator in the relational fixtures."
  }
}
'@
  Test-Rejects -TestName 'a module with no _evidence entry is rejected' -Path $missingEvidence `
    -ExpectedMessage 'has no matching _evidence entry'

  $staleEvidence = New-Manifest -Name 'stale-evidence' -Content @'
{
  "modules": {
    "com.unity.modules.animation": "1.0.0"
  },
  "_evidence": {
    "com.unity.modules.animation": "Animator in the relational fixtures.",
    "com.unity.modules.audio": "Removed when the audio fixtures moved."
  }
}
'@
  Test-Rejects -TestName 'a stale _evidence entry is rejected' -Path $staleEvidence `
    -ExpectedMessage 'but it is not in the modules list'
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
