Param(
  [switch]$VerboseOutput
)

<#
.SYNOPSIS
    Test runner for validate-devcontainer-config.ps1

.DESCRIPTION
    The validator holds three things together that nothing else does: the devcontainer image
    namespace, the GHCR publish workflow's metadata and permissions, and the set of "[language]"
    formatter assignments that must keep step with the pre-commit hook. Run against this repository
    it exits 0 silently, which is evidence about the repository and none at all about whether the
    validator still reports (#556, #562). It now takes -RepoRoot, and this drives fixture trees
    through every rule.

    Each fixture is a copy of the real configuration with exactly one mutation, so the green half
    and every red half differ only by the thing under test -- and a rule added to the validator
    without a matching fixture fails here as a green half rather than passing vacuously.

    Green half:
    - this repository passes
    - a verbatim copy of it passes

    Red halves, one per rule that exits non-zero, each asserted on its own message:
    - a missing devcontainer.json, pre-commit hook, or publish workflow
    - a legacy ghcr.io/wallstop image reference
    - a devcontainer.json that stopped caching from the current image
    - a publish workflow with the wrong IMAGE_NAME
    - a publish workflow with no `packages: write`
    - a publish workflow with no image source label
    - a publish workflow that stopped running this validator
    - no workflow wired to run the validator on a publish-workflow change
    - a devcontainer.json missing a required "[language]" formatter entry

.PARAMETER VerboseOutput
    Show detailed output during test execution

.EXAMPLE
    ./scripts/tests/test-validate-devcontainer-config.ps1
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:TestsPassed = 0
$script:TestsFailed = 0
$script:FailedTests = @()

$repoRoot = (Get-Item $PSScriptRoot).Parent.Parent.FullName
$validator = Join-Path $repoRoot 'scripts/validate-devcontainer-config.ps1'
$workspace = Join-Path ([System.IO.Path]::GetTempPath()) ("validate-devcontainer-config-tests-" + [System.Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $workspace -Force | Out-Null

$publishWorkflow = '.github/workflows/build-publish-devcontainer.yml'

function Write-Info($msg) {
  if ($VerboseOutput) { Write-Host "[test-validate-devcontainer-config] $msg" -ForegroundColor Cyan }
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

# Write-Error renders through PowerShell's error formatter, which hard-wraps the message behind a
# "|" gutter. Matching the raw text therefore fails for a reason unrelated to the rule under test.
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

function New-ConfigFixture {
  param([string]$Name)

  $root = Join-Path $workspace $Name
  New-Item -ItemType Directory -Path (Join-Path $root '.devcontainer') -Force | Out-Null
  New-Item -ItemType Directory -Path (Join-Path $root '.githooks') -Force | Out-Null
  New-Item -ItemType Directory -Path (Join-Path $root '.github') -Force | Out-Null

  Copy-Item -LiteralPath (Join-Path $repoRoot '.devcontainer/devcontainer.json') -Destination (Join-Path $root '.devcontainer/devcontainer.json')
  Copy-Item -LiteralPath (Join-Path $repoRoot '.githooks/pre-commit') -Destination (Join-Path $root '.githooks/pre-commit')
  # Into .github, not into .github/workflows: Copy-Item -Recurse into an EXISTING directory nests
  # the source under it, producing .github/workflows/workflows and a fixture that fails the
  # missing-publish-workflow rule instead of the rule it was built for.
  Copy-Item -LiteralPath (Join-Path $repoRoot '.github/workflows') -Destination (Join-Path $root '.github') -Recurse -Force
  return $root
}

function Edit-FixtureFile {
  param([string]$Root, [string]$RelativePath, [string]$From, [string]$To)

  $path = Join-Path $Root $RelativePath
  $content = Get-Content -LiteralPath $path -Raw
  if (-not $content.Contains($From)) {
    throw "fixture cannot rewrite '$From' in $RelativePath because it is not there -- the validator's rule and this fixture have drifted apart"
  }
  Set-Content -LiteralPath $path -Value $content.Replace($From, $To) -NoNewline
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
    Write-TestResult -TestName $TestName -Passed $false -Message 'validator accepted a configuration it must reject'
    return
  }
  # .Contains, not -like: -like reads square brackets as a wildcard character class, and these
  # messages quote "[language]" entries.
  if (-not $result.Output.Contains((ConvertTo-Flat -Text $ExpectedMessage))) {
    Write-TestResult -TestName $TestName -Passed $false -Message "rejected, but not for the reason under test. Expected to contain '$ExpectedMessage'. Got: $($result.Output)"
    return
  }
  Write-TestResult -TestName $TestName -Passed $true
}

Write-Host ''
Write-Host 'Running validate-devcontainer-config.ps1 self-tests' -ForegroundColor Cyan
Write-Host ''

try {
  Write-Info "Workspace: $workspace"

  # ── Green half ────────────────────────────────────────────────────────────
  Test-Accepts -TestName 'this repository passes' -Root $repoRoot
  Test-Accepts -TestName 'a verbatim copy passes' -Root (New-ConfigFixture -Name 'baseline')

  # ── Red halves: missing inputs ────────────────────────────────────────────
  $missingCases = @(
    @{ Name = 'no-devcontainer'; Path = '.devcontainer/devcontainer.json'; Message = 'devcontainer.json not found at' },
    @{ Name = 'no-pre-commit'; Path = '.githooks/pre-commit'; Message = 'pre-commit hook not found at' },
    @{ Name = 'no-publish-workflow'; Path = $publishWorkflow; Message = 'Devcontainer publish workflow not found at' }
  )
  foreach ($case in $missingCases) {
    $root = New-ConfigFixture -Name $case.Name
    Remove-Item -LiteralPath (Join-Path $root $case.Path)
    Test-Rejects -TestName "a missing $($case.Path) is rejected" -Root $root -ExpectedMessage $case.Message
  }

  $noWorkflowDir = New-ConfigFixture -Name 'no-workflow-dir'
  # The publish workflow is checked before the directory, so the directory must go while that one
  # file stays -- otherwise this fixture reports the previous rule and covers nothing new.
  Move-Item -LiteralPath (Join-Path $noWorkflowDir $publishWorkflow) -Destination (Join-Path $noWorkflowDir '.github/build-publish-devcontainer.yml')
  Remove-Item -LiteralPath (Join-Path $noWorkflowDir '.github/workflows') -Recurse -Force
  New-Item -ItemType Directory -Path (Join-Path $noWorkflowDir '.github/workflows') -Force | Out-Null
  Move-Item -LiteralPath (Join-Path $noWorkflowDir '.github/build-publish-devcontainer.yml') -Destination (Join-Path $noWorkflowDir $publishWorkflow)
  Test-Rejects -TestName 'no workflow runs the validator on a publish-workflow change' -Root $noWorkflowDir `
    -ExpectedMessage 'A workflow must run validate-devcontainer-config.ps1'

  # ── Red halves: image namespace and GHCR metadata ─────────────────────────
  $legacy = New-ConfigFixture -Name 'legacy-image'
  Edit-FixtureFile -Root $legacy -RelativePath '.devcontainer/devcontainer.json' `
    -From 'ghcr.io/ambiguous-interactive/unity-helpers/devcontainer' -To 'ghcr.io/wallstop/unity-helpers/devcontainer'
  Test-Rejects -TestName 'a legacy image reference is rejected' -Root $legacy `
    -ExpectedMessage 'Devcontainer image references must use'

  $noCache = New-ConfigFixture -Name 'no-cache'
  Edit-FixtureFile -Root $noCache -RelativePath '.devcontainer/devcontainer.json' `
    -From 'ghcr.io/ambiguous-interactive/unity-helpers/devcontainer:buildcache' -To 'ghcr.io/example/other:buildcache'
  Test-Rejects -TestName 'a devcontainer that stopped caching from the current image is rejected' -Root $noCache `
    -ExpectedMessage 'must cache from'

  $wrongImageName = New-ConfigFixture -Name 'wrong-image-name'
  Edit-FixtureFile -Root $wrongImageName -RelativePath $publishWorkflow `
    -From 'IMAGE_NAME: ambiguous-interactive/unity-helpers/devcontainer' -To 'IMAGE_NAME: example/other'
  Test-Rejects -TestName 'a publish workflow with the wrong IMAGE_NAME is rejected' -Root $wrongImageName `
    -ExpectedMessage 'must publish IMAGE_NAME:'

  $noPermission = New-ConfigFixture -Name 'no-packages-write'
  Edit-FixtureFile -Root $noPermission -RelativePath $publishWorkflow -From 'packages: write' -To 'packages: read'
  Test-Rejects -TestName 'a publish workflow without packages: write is rejected' -Root $noPermission `
    -ExpectedMessage 'must grant packages: write'

  $noLabel = New-ConfigFixture -Name 'no-source-label'
  Edit-FixtureFile -Root $noLabel -RelativePath $publishWorkflow `
    -From 'org.opencontainers.image.source=' -To 'org.opencontainers.image.url='
  Test-Rejects -TestName 'a publish workflow without an image source label is rejected' -Root $noLabel `
    -ExpectedMessage 'must publish an org.opencontainers.image.source label'

  $noSelfValidation = New-ConfigFixture -Name 'no-self-validation'
  Edit-FixtureFile -Root $noSelfValidation -RelativePath $publishWorkflow `
    -From './scripts/validate-devcontainer-config.ps1 -VerboseOutput' -To './scripts/validate-devcontainer-config.ps1'
  Test-Rejects -TestName 'a publish workflow that stopped running this validator is rejected' -Root $noSelfValidation `
    -ExpectedMessage 'must run validate-devcontainer-config.ps1 before publishing'

  # ── Red half: the formatter-assignment contract ───────────────────────────
  $noFormatter = New-ConfigFixture -Name 'no-formatter'
  Edit-FixtureFile -Root $noFormatter -RelativePath '.devcontainer/devcontainer.json' `
    -From '"[shaderlab]"' -To '"[shaderlab-renamed-by-self-test]"'
  Test-Rejects -TestName 'a devcontainer missing a formatter assignment is rejected' -Root $noFormatter `
    -ExpectedMessage 'is missing 1 explicit formatter assignment'
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
