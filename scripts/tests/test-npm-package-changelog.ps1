Param(
  [switch]$VerboseOutput
)

<#
.SYNOPSIS
    Test runner for UPM inline changelog rendering contract validation

.DESCRIPTION
    Validates that the Unity package is correctly configured for the Unity
    Package Manager to RENDER the changelog inline (not just link to it).

    Unity's UPM renders CHANGELOG.md inline only when:
      1. CHANGELOG.md exists at the package root (next to package.json)
      2. CHANGELOG.md.meta companion exists (Unity asset tracking)
      3. changelogUrl is NOT present in package.json (its presence causes
         UPM to show an external link instead of inline rendering)
      4. CHANGELOG.md is included in the npm package (not excluded by .npmignore)
      5. CHANGELOG.md has a version entry matching the current package version

    References:
      - https://docs.unity3d.com/6000.3/Documentation/Manual/cus-changelog.html
      - https://docs.unity3d.com/6000.0/Documentation/Manual/cus-layout.html
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:TestsPassed = 0
$script:TestsFailed = 0

function Write-Info($msg) {
  if ($VerboseOutput) { Write-Host "[test-npm-package-changelog] $msg" -ForegroundColor Cyan }
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
  }
  else {
    Write-Host "  [FAIL] $TestName" -ForegroundColor Red
    if ($Message) {
      Write-Host "         $Message" -ForegroundColor Yellow
    }
    $script:TestsFailed++
  }
}

Write-Host "Testing UPM inline changelog rendering contract..." -ForegroundColor White

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$packageJsonPath = Join-Path $repoRoot 'package.json'
$changelogPath = Join-Path $repoRoot 'CHANGELOG.md'
$changelogMetaPath = Join-Path $repoRoot 'CHANGELOG.md.meta'
$npmignorePath = Join-Path $repoRoot '.npmignore'
Write-Info "Repo root: $repoRoot"

# ── Section: Required files ──────────────────────────────────────────────────

Write-Host ""
Write-Host "  Section: Required files" -ForegroundColor White

$packageJsonExists = Test-Path $packageJsonPath
Write-TestResult -TestName 'package.json exists' -Passed $packageJsonExists -Message "Missing: $packageJsonPath"

$changelogExists = Test-Path $changelogPath
Write-TestResult -TestName 'CHANGELOG.md exists at package root' -Passed $changelogExists -Message "Missing: $changelogPath"

$changelogMetaExists = Test-Path $changelogMetaPath
Write-TestResult -TestName 'CHANGELOG.md.meta companion exists' -Passed $changelogMetaExists -Message "Missing: $changelogMetaPath (required for Unity asset tracking)"

# ── Section: package.json contract ───────────────────────────────────────────

Write-Host ""
Write-Host "  Section: package.json contract" -ForegroundColor White

$rawContent = ''
$parsedJson = $null
$jsonParseSucceeded = $false

if ($packageJsonExists) {
  $rawContent = Get-Content -Path $packageJsonPath -Raw
  try {
    $parsedJson = $rawContent | ConvertFrom-Json
    $jsonParseSucceeded = $true
  }
  catch {
    $jsonParseSucceeded = $false
  }
}

Write-TestResult -TestName 'package.json is valid JSON' -Passed $jsonParseSucceeded

$versionValue = ''

if ($jsonParseSucceeded) {
  # changelogUrl must NOT be present - it causes UPM to show an external link
  # instead of rendering the local CHANGELOG.md inline
  $hasChangelogUrl = $null -ne $parsedJson.PSObject.Properties['changelogUrl']
  Write-TestResult -TestName 'changelogUrl is absent (required for inline rendering)' -Passed (-not $hasChangelogUrl) -Message "Remove changelogUrl from package.json; it overrides inline CHANGELOG.md rendering"

  $hasVersion = $null -ne $parsedJson.PSObject.Properties['version']
  Write-TestResult -TestName 'package.json contains version field' -Passed $hasVersion

  if ($hasVersion) {
    $versionValue = [string]$parsedJson.version
    Write-TestResult -TestName 'version field is non-empty' -Passed (-not [string]::IsNullOrWhiteSpace($versionValue))
  }
}

# Also check raw content for stray changelogUrl keys (catches duplicate/commented-out entries)
$changelogKeyMatches = [regex]::Matches($rawContent, '"changelogUrl"\s*:')
Write-TestResult -TestName 'no changelogUrl keys in package.json raw content' -Passed ($changelogKeyMatches.Count -eq 0) -Message "Found $($changelogKeyMatches.Count) changelogUrl key(s) in raw content"

# ── Section: npm pack inclusion ──────────────────────────────────────────────

Write-Host ""
Write-Host "  Section: npm pack inclusion" -ForegroundColor White

$npmCommand = Get-Command npm -ErrorAction SilentlyContinue
if ($null -eq $npmCommand) {
  Write-TestResult -TestName 'CHANGELOG.md included in npm pack output' -Passed $false -Message "npm command not found; unable to validate packed file list"
  Write-TestResult -TestName 'CHANGELOG.md.meta included in npm pack output' -Passed $false -Message "npm command not found; unable to validate packed file list"
}
else {
  Push-Location $repoRoot
  $packOutput = $null
  $packExitCode = 0
  try {
    $packOutput = & $npmCommand.Source pack --dry-run --json 2>&1
    $packExitCode = $LASTEXITCODE
  }
  finally {
    Pop-Location
  }

  $packOutputText = ($packOutput | Out-String).Trim()
  if ($packExitCode -ne 0) {
    Write-TestResult -TestName 'CHANGELOG.md included in npm pack output' -Passed $false -Message "npm pack --dry-run --json failed (exit $packExitCode): $packOutputText"
    Write-TestResult -TestName 'CHANGELOG.md.meta included in npm pack output' -Passed $false -Message "npm pack --dry-run --json failed (exit $packExitCode): $packOutputText"
  }
  else {
    $packParseSucceeded = $true
    $packEntries = @()
    $packedPaths = @()

    try {
      $packJson = $packOutputText | ConvertFrom-Json -ErrorAction Stop
      $packEntries = @($packJson)
      if (($packEntries.Count -gt 0) -and ($null -ne $packEntries[0].files)) {
        $packedPaths = @($packEntries[0].files | ForEach-Object { [string]$_.path })
      }
      else {
        $packParseSucceeded = $false
      }
    }
    catch {
      $packParseSucceeded = $false
    }

    if (-not $packParseSucceeded) {
      Write-TestResult -TestName 'CHANGELOG.md included in npm pack output' -Passed $false -Message "Unable to parse npm pack --dry-run --json output: $packOutputText"
      Write-TestResult -TestName 'CHANGELOG.md.meta included in npm pack output' -Passed $false -Message "Unable to parse npm pack --dry-run --json output: $packOutputText"
    }
    else {
      $hasPackedChangelog = $packedPaths -contains 'CHANGELOG.md'
      $hasPackedChangelogMeta = $packedPaths -contains 'CHANGELOG.md.meta'
      $packedFilesMessage = "Packed files ($($packedPaths.Count)): $($packedPaths -join ', ')"
      $changelogMessage = if ($hasPackedChangelog) { $packedFilesMessage } else { "Missing expected packed file: CHANGELOG.md. $packedFilesMessage" }
      $changelogMetaMessage = if ($hasPackedChangelogMeta) { $packedFilesMessage } else { "Missing expected packed file: CHANGELOG.md.meta. $packedFilesMessage" }
      Write-TestResult -TestName 'CHANGELOG.md included in npm pack output' -Passed $hasPackedChangelog -Message $changelogMessage
      Write-TestResult -TestName 'CHANGELOG.md.meta included in npm pack output' -Passed $hasPackedChangelogMeta -Message $changelogMetaMessage
    }
  }
}

# ── Section: CHANGELOG.md content ────────────────────────────────────────────

Write-Host ""
Write-Host "  Section: CHANGELOG.md content" -ForegroundColor White

if ($changelogExists -and (-not [string]::IsNullOrWhiteSpace($versionValue))) {
  $changelogRaw = Get-Content -Path $changelogPath -Raw
  $hasVersionHeader = $changelogRaw -match ("(?mi)^##\s+\[" + [regex]::Escape($versionValue) + "\](?:\s+-.*)?\s*$")
  $hasUnreleasedHeader = $changelogRaw -match '(?m)^##\s+\[Unreleased\]\s*$'
  Write-TestResult -TestName 'CHANGELOG.md has current version section or [Unreleased]' -Passed ($hasVersionHeader -or $hasUnreleasedHeader) -Message "Expected section: ## [$versionValue] or ## [Unreleased]"

  # Verify the changelog starts with a heading (Unity parser expects this)
  $firstLineArray = @(Get-Content -Path $changelogPath -TotalCount 1)
  $firstLine = ''
  if ($firstLineArray.Count -gt 0) {
    $firstLine = [string]$firstLineArray[0]
  }
  $startsWithHeading = $false
  if (-not [string]::IsNullOrWhiteSpace($firstLine)) {
    $startsWithHeading = $firstLine -match '^#\s+'
  }
  Write-TestResult -TestName 'CHANGELOG.md starts with markdown heading' -Passed $startsWithHeading -Message "First line should be a markdown heading (e.g., '# Changelog')"
}

# ── Section: package validator regressions ──────────────────────────────────

Write-Host ""
Write-Host "  Section: package validator regressions" -ForegroundColor White

$validatorPath = Join-Path $repoRoot 'scripts/validate-npm-package.ps1'
$caseOnlyCanaryPath = Join-Path $repoRoot 'docs/Index.md'
$caseOnlyCanaryRelativePath = 'docs/Index.md'

function Invoke-ValidatorExpectingUntrackedPayloadFailure {
  param(
    [string]$RelativePath,
    [string]$TestName
  )

  $canaryPath = Join-Path $repoRoot $RelativePath
  if (Test-Path -LiteralPath $canaryPath) {
    Write-TestResult -TestName $TestName -Passed $false -Message "Canary path already exists: $RelativePath"
    return
  }

  $validatorExitCode = 0
  $validatorOutput = $null
  try {
    $canaryDirectory = Split-Path -Parent $canaryPath
    if (-not [string]::IsNullOrWhiteSpace($canaryDirectory)) {
      New-Item -ItemType Directory -Path $canaryDirectory -Force | Out-Null
    }

    Set-Content -LiteralPath $canaryPath -Value "# Package Validator Canary`n" -NoNewline
    Push-Location $repoRoot
    try {
      $validatorOutput = & pwsh -NoProfile -File $validatorPath *>&1
      $validatorExitCode = $LASTEXITCODE
    }
    finally {
      Pop-Location
    }
  }
  finally {
    Remove-Item -LiteralPath $canaryPath -Force -ErrorAction SilentlyContinue
  }

  $validatorOutputText = ($validatorOutput | Out-String).Trim()
  $expectedMessage = "File in npm package but not tracked in git repo: $RelativePath"
  Write-TestResult `
    -TestName $TestName `
    -Passed ($validatorExitCode -ne 0 -and $validatorOutputText.Contains($expectedMessage)) `
    -Message "Expected validator to fail with '$expectedMessage'. Exit: $validatorExitCode. Output: $validatorOutputText"
}

function Invoke-ValidatorExpectingForbiddenRootArtifactFailure {
  param(
    [string]$RelativePath,
    [string]$TestName
  )

  $canaryPath = Join-Path $repoRoot $RelativePath
  if (Test-Path -LiteralPath $canaryPath) {
    Write-TestResult -TestName $TestName -Passed $false -Message "Canary path already exists: $RelativePath"
    return
  }

  $packageJsonOriginalContent = Get-Content -LiteralPath $packageJsonPath -Raw
  $validatorExitCode = 0
  $validatorOutput = $null
  try {
    $packageJsonObject = $packageJsonOriginalContent | ConvertFrom-Json
    $packageFiles = @($packageJsonObject.files | ForEach-Object { [string]$_ })
    if ($RelativePath -notin $packageFiles) {
      $packageFiles += $RelativePath
    }
    $packageJsonObject.files = $packageFiles
    Set-Content -LiteralPath $packageJsonPath -Value ($packageJsonObject | ConvertTo-Json -Depth 100) -NoNewline

    Set-Content -LiteralPath $canaryPath -Value "# Package Validator Forbidden Artifact Canary`n" -NoNewline
    Push-Location $repoRoot
    try {
      $validatorOutput = & pwsh -NoProfile -File $validatorPath *>&1
      $validatorExitCode = $LASTEXITCODE
    }
    finally {
      Pop-Location
    }
  }
  finally {
    Set-Content -LiteralPath $packageJsonPath -Value $packageJsonOriginalContent -NoNewline
    Remove-Item -LiteralPath $canaryPath -Force -ErrorAction SilentlyContinue
  }

  $validatorOutputText = ($validatorOutput | Out-String).Trim()
  $forbiddenMessage = "Forbidden release artifact included in npm package: $RelativePath"
  $duplicateMessages = @(
    "Unexpected top-level entry included in npm package: $RelativePath",
    "File in npm package but not tracked in git repo: $RelativePath"
  )
  $duplicateDiagnostics = @($duplicateMessages | Where-Object { $validatorOutputText.Contains($_) })
  Write-TestResult `
    -TestName $TestName `
    -Passed ($validatorExitCode -ne 0 -and $validatorOutputText.Contains($forbiddenMessage) -and $duplicateDiagnostics.Count -eq 0) `
    -Message "Expected validator to fail with '$forbiddenMessage' and without duplicate generic diagnostics. Duplicates: $($duplicateDiagnostics -join '; '). Exit: $validatorExitCode. Output: $validatorOutputText"
}

if (-not (Test-Path -LiteralPath $validatorPath)) {
  Write-TestResult -TestName 'validate-npm-package script exists' -Passed $false -Message "Missing validator: $validatorPath"
}
else {
  Invoke-ValidatorExpectingUntrackedPayloadFailure `
    -RelativePath 'docs/package-validator-untracked-payload-canary.md' `
    -TestName 'validate-npm-package rejects untracked payload files'

  Invoke-ValidatorExpectingForbiddenRootArtifactFailure `
    -RelativePath 'pr-description.md' `
    -TestName 'validate-npm-package reports forbidden root artifacts once'

  if (Test-Path -LiteralPath $caseOnlyCanaryPath) {
    Write-Info "Skipping case-only payload canary because $caseOnlyCanaryRelativePath resolves on this filesystem."
  }
  else {
    Invoke-ValidatorExpectingUntrackedPayloadFailure `
      -RelativePath $caseOnlyCanaryRelativePath `
      -TestName 'validate-npm-package rejects case-only untracked payload files'
  }
}

Write-Host ""
Write-Host "Results:" -ForegroundColor Magenta
Write-Host "  Passed: $script:TestsPassed"
Write-Host "  Failed: $script:TestsFailed"

if ($script:TestsFailed -gt 0) {
  exit 1
}

exit 0
