#!/usr/bin/env pwsh
# Contract test: pull_request workflows should cancel superseded PR iterations.
[CmdletBinding()]
param([switch]$VerboseOutput)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Info {
  param([string]$Message)
  if ($VerboseOutput) {
    Write-Host "[test-pr-workflow-concurrency] $Message" -ForegroundColor Cyan
  }
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$workflowDir = Join-Path $repoRoot '.github/workflows'
$failed = $false

if (-not (Test-Path -LiteralPath $workflowDir)) {
  Write-Host "::error::.github/workflows directory not found."
  exit 1
}

$workflowFiles = @(Get-ChildItem -LiteralPath $workflowDir -Filter '*.yml' -File | Sort-Object Name)
foreach ($file in $workflowFiles) {
  $relativePath = ".github/workflows/$($file.Name)"
  $content = Get-Content -LiteralPath $file.FullName -Raw

  if ($content -notmatch '(?m)^  pull_request(_target)?:\s*$') {
    Write-Info "Skipping $relativePath because it has no pull_request trigger."
    continue
  }

  $concurrencyMatch = [regex]::Match($content, '(?ms)^concurrency:\s*\r?\n(?<body>.*?)(?=^[A-Za-z0-9_-]+:|\z)')
  if (-not $concurrencyMatch.Success) {
    Write-Host "::error file=$relativePath::pull_request workflows must define top-level concurrency so superseded PR iterations are cancelled."
    $failed = $true
    continue
  }

  $body = $concurrencyMatch.Groups['body'].Value
  $hasUsableGroup =
    $body -match '(?m)^  group:\s*.+' -and
    ($body.Contains('github.event.pull_request.number') -or $body.Contains('github.ref'))
  $cancelsPullRequests =
    $body -match "(?m)^  cancel-in-progress:\s*true\s*$" -or
    $body -match "(?m)^  cancel-in-progress:\s*\$\{\{\s*github\.event_name\s*==\s*'pull_request'\s*\}\}\s*$"

  if (-not $hasUsableGroup) {
    Write-Host "::error file=$relativePath::pull_request workflow concurrency must group by the PR number or Git ref."
    $failed = $true
  }

  if (-not $cancelsPullRequests) {
    Write-Host "::error file=$relativePath::pull_request workflow concurrency must cancel superseded pull_request runs."
    $failed = $true
  }

  if ($hasUsableGroup -and $cancelsPullRequests) {
    Write-Info "Checked $relativePath."
  }
}

if ($failed) {
  exit 1
}

Write-Host "[test-pr-workflow-concurrency] OK: pull_request workflows cancel superseded PR runs." -ForegroundColor Green
exit 0
