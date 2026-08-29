#!/usr/bin/env pwsh
# Contract test: Unity activation and build-lock WRITER credentials may only be in scope for a job
# that actually licenses an editor.
#
# The central enrollment audit is source-free: it cannot read intent, only scope. A job with
# UNITY_SERIAL/UNITY_EMAIL/UNITY_PASSWORD/BUILD_LOCK_APP_ID/BUILD_LOCK_APP_PRIVATE_KEY in its
# environment is a job that could activate an editor and take the org Unity seat, so it is measured
# against the paid-serial contract: lock acquire, license return, cleanup gate, cleanup classifier,
# typed release, and the rest. A job that only computes a matrix can satisfy none of them, and every
# one of those contracts then reports a finding that no change to that job can ever clear.
#
# That is exactly what `matrix-config` did in unity-tests.yml and unity-benchmarks.yml: it read all
# five secrets purely to emit a `has-secrets` boolean for downstream `if:` conditions, and cost this
# repository 16 unfixable findings, 8 per workflow (#596). The fix moved the signal to
# runner-preflight, which already holds build-lock reader credentials for work it genuinely does.
#
# The durable half is this gate. It states the requirement -- credentials only where they are used
# -- rather than naming the two jobs that got it wrong, so the next probe written for the next
# convenient boolean fails here instead of in an audit nobody in this repository runs.
# See #596 and #322.
[CmdletBinding()]
param([switch]$VerboseOutput)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Info {
  param([string]$Message)
  if ($VerboseOutput) {
    Write-Host "[test-workflow-license-secret-scope] $Message" -ForegroundColor Cyan
  }
}

# The credentials whose mere presence in a job's scope makes that job license-capable. Reader
# credentials (BUILD_LOCK_READER_APP_ID / BUILD_LOCK_READER_APP_PRIVATE_KEY) are deliberately NOT
# here: they can only read the lock and the runner inventory, they cannot activate an editor or take
# the seat, and runner-preflight holds them to do exactly the work they exist for.
$script:LicensingSecrets = @(
  'UNITY_SERIAL',
  'UNITY_EMAIL',
  'UNITY_PASSWORD',
  'UNITY_LICENSING_SERVER',
  'BUILD_LOCK_APP_ID',
  'BUILD_LOCK_APP_PRIVATE_KEY'
)

# What makes a job entitled to hold them: it validates the license, returns it, or takes and releases
# the organization build lock. These are the steps the credentials are FOR, so a job containing one
# is a job whose paid-serial classification is correct and whose contracts are satisfiable.
$script:LicensedJobMarkers = @(
  './.github/actions/validate-unity-license',
  './.github/actions/return-unity-license',
  '/actions/acquire-build-lock@',
  '/actions/release-build-lock@'
)

# Slice a workflow into jobs. Job ids sit at two spaces under the top-level `jobs:` key and their
# properties at four, so a two-space `key:` line inside that block is a job header and nothing else
# is. Starting only after `^jobs:` keeps the identically-indented keys of `on:`, `permissions:` and
# `concurrency:` out of the result.
function Get-WorkflowJobs {
  param([string[]]$Lines)

  $jobs = [ordered]@{}
  $inJobs = $false
  $current = $null
  foreach ($line in $Lines) {
    if ($line -match '^jobs:\s*$') {
      $inJobs = $true
      $current = $null
      continue
    }
    if (-not $inJobs) {
      continue
    }
    if ($line -match '^\S') {
      $inJobs = $false
      $current = $null
      continue
    }
    if ($line -match '^  (?<id>[A-Za-z0-9_.-]+):\s*$') {
      $current = $Matches['id']
      $jobs[$current] = [System.Collections.Generic.List[string]]::new()
      continue
    }
    if ($null -ne $current) {
      $jobs[$current].Add($line)
    }
  }
  return $jobs
}

# Comments are stripped before the scan for the reason test-workflow-repository-guard.ps1 records:
# a comment is where the reason for a rule is written, and a rule that a comment can violate makes
# every explanation of the rule a new failure. The `secrets.` prefix is required as well, so a job
# that merely NAMES a credential in prose is not reported.
function Get-LicenseSecretScopeAudit {
  param([string]$Content)

  $violations = New-Object System.Collections.Generic.List[psobject]
  $licensedHolders = New-Object System.Collections.Generic.List[string]

  $jobs = Get-WorkflowJobs -Lines ($Content -split "\r?\n")
  foreach ($jobId in @($jobs.Keys)) {
    $body = (@($jobs[$jobId]) | ForEach-Object { $_ -replace '#.*$', '' }) -join "`n"

    $held = @($script:LicensingSecrets | Where-Object {
        $body -match ('secrets\.' + [regex]::Escape($_) + '\b')
      })
    if ($held.Count -eq 0) {
      continue
    }

    $licensed = $false
    foreach ($marker in $script:LicensedJobMarkers) {
      if ($body.Contains($marker)) {
        $licensed = $true
        break
      }
    }

    if ($licensed) {
      $licensedHolders.Add($jobId)
      continue
    }
    foreach ($secret in $held) {
      $violations.Add([pscustomobject]@{ Job = $jobId; Secret = $secret })
    }
  }

  return [pscustomobject]@{
    Violations = @($violations)
    LicensedHolders = @($licensedHolders)
  }
}

$failed = $false

# ---------------------------------------------------------------------------
# Red half. A scanner over a corpus that is clean by construction reports the same thing whether it
# checked everything or nothing (#556), so the detector is made to fire, and made to stay quiet on
# the two shapes it must not confuse with a violation.
# ---------------------------------------------------------------------------
$probeFixture = @'
jobs:
  matrix-config:
    runs-on: ubuntu-latest
    steps:
      - name: Check for required licensed workflow secrets
        id: check-secrets
        env:
          UNITY_SERIAL: ${{ secrets.UNITY_SERIAL }}
          BUILD_LOCK_APP_ID: ${{ secrets.BUILD_LOCK_APP_ID }}
        run: echo "has-secrets=true" >> "${GITHUB_OUTPUT}"
'@

$licensedFixture = @'
jobs:
  unity-tests:
    runs-on: [self-hosted, Windows]
    steps:
      - name: Validate Unity license secrets
        uses: ./.github/actions/validate-unity-license
        env:
          UNITY_SERIAL: ${{ secrets.UNITY_SERIAL }}
          UNITY_EMAIL: ${{ secrets.UNITY_EMAIL }}
          UNITY_PASSWORD: ${{ secrets.UNITY_PASSWORD }}
  matrix-config:
    runs-on: ubuntu-latest
    steps:
      - run: echo "no credentials in this scope"
'@

$prosefixture = @'
jobs:
  matrix-config:
    runs-on: ubuntu-latest
    steps:
      # This job used to read ${{ secrets.UNITY_SERIAL }} to emit a boolean; see #596.
      - run: echo "no credentials in this scope"
'@

$selfTests = @(
  @{
    Name = 'a probe-only job is reported'
    Ok = {
      $audit = Get-LicenseSecretScopeAudit -Content $probeFixture
      $audit.Violations.Count -eq 2 -and
      @($audit.Violations | Where-Object { $_.Job -ne 'matrix-config' }).Count -eq 0 -and
      $audit.LicensedHolders.Count -eq 0
    }
    Message = 'The detector must report every licensing credential held by a job that does no licensed work.'
  },
  @{
    Name = 'a job that does the licensed work is not reported'
    Ok = {
      $audit = Get-LicenseSecretScopeAudit -Content $licensedFixture
      $audit.Violations.Count -eq 0 -and
      $audit.LicensedHolders.Count -eq 1 -and
      $audit.LicensedHolders[0] -eq 'unity-tests'
    }
    Message = 'The detector must attribute credentials to the job that holds them and clear a job that validates the license, and must not spill one job''s markers onto the next.'
  },
  @{
    Name = 'a credential named only in a comment is not reported'
    Ok = {
      $audit = Get-LicenseSecretScopeAudit -Content $prosefixture
      $audit.Violations.Count -eq 0
    }
    Message = 'A comment explaining why a credential was removed must not read as the credential being present, or every explanation of this rule becomes a violation of it.'
  }
)

foreach ($selfTest in $selfTests) {
  if (& $selfTest.Ok) {
    Write-Info "Self-test passed: $($selfTest.Name)."
  }
  else {
    Write-Host "::error file=scripts/tests/test-workflow-license-secret-scope.ps1::Self-test '$($selfTest.Name)' failed. $($selfTest.Message)"
    $failed = $true
  }
}

# ---------------------------------------------------------------------------
# The real corpus.
# ---------------------------------------------------------------------------
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$workflowDir = Join-Path $repoRoot '.github/workflows'

if (-not (Test-Path -LiteralPath $workflowDir)) {
  Write-Host '::error::.github/workflows directory not found.'
  exit 1
}

$workflowFiles = @(Get-ChildItem -LiteralPath $workflowDir -Filter '*.yml' -File | Sort-Object Name)
if ($workflowFiles.Count -eq 0) {
  Write-Host '::error::No workflow files found to check.'
  exit 1
}

$totalLicensedHolders = 0
foreach ($file in $workflowFiles) {
  $relativePath = ".github/workflows/$($file.Name)"
  $audit = Get-LicenseSecretScopeAudit -Content (Get-Content -LiteralPath $file.FullName -Raw)
  $totalLicensedHolders += $audit.LicensedHolders.Count

  foreach ($violation in $audit.Violations) {
    Write-Host "::error file=$relativePath::Job '$($violation.Job)' puts secrets.$($violation.Secret) in scope but never validates the Unity license, returns it, or takes the organization build lock. A source-free audit cannot see that the credential is only being probed: it classifies the job as license-capable and then measures it against paid-serial contracts a hosted helper job can never satisfy (#596). Read the credential only in the job that uses it, and derive any 'are we licensed?' boolean from a job that already holds credentials for work it does."
    $failed = $true
  }

  if ($audit.Violations.Count -eq 0) {
    Write-Info "Checked $relativePath ($($audit.LicensedHolders.Count) licensed credential holder(s))."
  }
}

# An empty corpus and a clean corpus print the same thing. This repository licenses Unity in CI, so
# a scan that found no job legitimately holding these credentials found nothing at all -- the job
# slicing or the marker list has drifted, and the gate is passing vacuously.
if ($totalLicensedHolders -eq 0) {
  Write-Host "::error::No job in .github/workflows holds Unity activation or build-lock writer credentials at all, so this gate checked nothing. Either the licensed Unity jobs were removed, or Get-WorkflowJobs no longer slices the workflows correctly."
  $failed = $true
}

if ($failed) {
  exit 1
}

Write-Host "[test-workflow-license-secret-scope] OK: $($workflowFiles.Count) workflow(s); licensing credentials are scoped to the $totalLicensedHolders job(s) that use them." -ForegroundColor Green
exit 0
