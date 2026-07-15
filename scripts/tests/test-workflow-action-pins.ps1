#!/usr/bin/env pwsh
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$workflowRoot = Join-Path $repoRoot '.github/workflows'
$failures = [System.Collections.Generic.List[string]]::new()

foreach ($workflow in Get-ChildItem -LiteralPath $workflowRoot -File | Where-Object { $_.Extension -in @('.yml', '.yaml') }) {
    $lineNumber = 0
    foreach ($line in Get-Content -LiteralPath $workflow.FullName) {
        $lineNumber++
        if ($line -notmatch '^\s*uses:\s*(?<reference>\S+)') {
            continue
        }

        $reference = $Matches['reference']
        if ($reference.StartsWith('./', [StringComparison]::Ordinal) -or
            $reference.StartsWith('docker://', [StringComparison]::Ordinal)) {
            continue
        }

        if ($reference -notmatch '@[0-9a-f]{40}$') {
            $failures.Add("$($workflow.Name):$lineNumber uses a mutable action reference: $reference")
        }
    }
}

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) {
        Write-Host "::error::$failure"
    }
    exit 1
}

Write-Host 'All external workflow actions use immutable 40-character commit SHAs.'
