#!/usr/bin/env pwsh
# Contract test: a workflow may not gate itself on a hard-coded repository name.
#
# `if: github.repository == 'owner/name'` is a way of writing "not a fork" that stops being true
# when the canonical remote moves. It did: issues and pull requests for this package live in
# Ambiguous-Interactive/unity-helpers, and Sync Issue Template Versions was guarded on
# wallstop/unity-helpers, so it reported `skipped` on every run in the repository whose issue forms
# it edits -- for months, with nothing red to notice. `github.event.repository.fork == false` states
# the actual requirement and needs no maintenance the next time the remote moves.
[CmdletBinding()]
param([switch]$VerboseOutput)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Info {
  param([string]$Message)
  if ($VerboseOutput) {
    Write-Host "[test-workflow-repository-guard] $Message" -ForegroundColor Cyan
  }
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$workflowDir = Join-Path $repoRoot '.github/workflows'
$failed = $false

if (-not (Test-Path -LiteralPath $workflowDir)) {
  Write-Host '::error::.github/workflows directory not found.'
  exit 1
}

$workflowFiles = @(Get-ChildItem -LiteralPath $workflowDir -Filter '*.yml' -File | Sort-Object Name)
if ($workflowFiles.Count -eq 0) {
  Write-Host '::error::No workflow files found to check.'
  exit 1
}

foreach ($file in $workflowFiles) {
  $relativePath = ".github/workflows/$($file.Name)"
  $lines = Get-Content -LiteralPath $file.FullName

  for ($index = 0; $index -lt $lines.Count; $index++) {
    $line = $lines[$index]

    # Comments are where the reason for the guard is written, so they must not be mistaken for the
    # guard itself -- this contract exists because a fix was invisible, and a check satisfied by a
    # comment would make the next one invisible too.
    $code = ($line -replace '#.*$', '')
    if ($code -notmatch 'github\.repository\s*[!=]=') {
      continue
    }

    Write-Host "::error file=$relativePath,line=$($index + 1)::A workflow must not gate on a hard-coded repository name. Use ``if: github.event.repository.fork == false`` instead: it states the requirement (not a fork) rather than one repository's name, which goes inert the moment the canonical remote moves."
    $failed = $true
  }

  if (-not $failed) {
    Write-Info "Checked $relativePath."
  }
}

if ($failed) {
  exit 1
}

Write-Host "[test-workflow-repository-guard] OK: $($workflowFiles.Count) workflow(s) gate on fork status rather than a repository name." -ForegroundColor Green
exit 0
