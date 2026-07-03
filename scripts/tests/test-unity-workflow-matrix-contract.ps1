#!/usr/bin/env pwsh
# Contract test: a job skipped by a job-level `if:` before matrix expansion must
# not use `matrix.*` in the job display name. GitHub renders those skipped names
# literally, which hides the actual gated job behind unresolved expressions.
[CmdletBinding()]
param([switch]$VerboseOutput)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Info($msg) {
    if ($VerboseOutput) { Write-Host "[test-unity-workflow-matrix-contract] $msg" -ForegroundColor Cyan }
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$workflowPath = Join-Path $repoRoot '.github/workflows/unity-tests.yml'

if (-not (Test-Path -LiteralPath $workflowPath)) {
    Write-Host "::error::Unity workflow not found: $workflowPath"
    exit 1
}

[string[]]$lines = Get-Content -LiteralPath $workflowPath
[string]$workflowContent = $lines -join "`n"
[bool]$failed = $false
[bool]$insideJobs = $false
$jobTexts = @{}

$hasPrCancelConcurrency = (
    $workflowContent.Contains('group: unity-tests-${{ github.event.pull_request.number || github.ref }}') -and
    $workflowContent.Contains('cancel-in-progress: ${{ github.event_name == ''pull_request'' }}')
)
if (-not $hasPrCancelConcurrency) {
    Write-Host "::error file=.github/workflows/unity-tests.yml::Unity Tests must cancel superseded pull_request runs so old iterations do not keep the organization Unity runner occupied."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked Unity Tests pull_request concurrency cancellation contract."
}

$slowReportBudgetCount = ([regex]::Matches($workflowContent, [regex]::Escape('-FixtureBudgetSeconds 120'))).Count
if ($slowReportBudgetCount -lt 3) {
    Write-Host "::error file=.github/workflows/unity-tests.yml::Unity slow-test reports must include a warn-only 120s fixture budget for main, standalone, and single-threaded legs."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked Unity slow-test warn-only fixture budget contract."
}

for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^jobs:\s*$') {
        $insideJobs = $true
        continue
    }

    if (-not $insideJobs) {
        continue
    }

    $jobMatch = [regex]::Match($lines[$i], '^  ([A-Za-z0-9_-]+):\s*$')
    if (-not $jobMatch.Success) { continue }

    $jobId = $jobMatch.Groups[1].Value
    $start = $i
    $end = $lines.Count
    for ($j = $i + 1; $j -lt $lines.Count; $j++) {
        if ($lines[$j] -match '^  [A-Za-z0-9_-]+:\s*$') {
            $end = $j
            break
        }
    }

    [string[]]$jobLines = @($lines[$start..($end - 1)])
    [string]$jobText = $jobLines -join "`n"
    $jobTexts[$jobId] = $jobText
    [bool]$hasJobIf = $jobText -match '(?m)^    if:\s*'
    [bool]$hasMatrixPresenceGate = $hasJobIf -and $jobText -match "matrix-include[^`n]+!=\s*'\[\]'"
    [bool]$hasDynamicMatrixInclude = $jobText -match 'fromJSON\(needs\.[^)]+\.outputs\.matrix-include'
    [string[]]$jobNameLines = @($jobLines | Where-Object { $_ -match '^    name:\s*' })

    foreach ($jobNameLine in $jobNameLines) {
        if ($hasMatrixPresenceGate -and $hasDynamicMatrixInclude -and $jobNameLine -match '\$\{\{\s*matrix\.') {
            Write-Host "::error file=.github/workflows/unity-tests.yml,line=$($start + 1)::Job '$jobId' has a job-level if, a needs-derived dynamic matrix, and a matrix expression in its job name. Use a static job name; keep matrix values in step names, artifacts, or action labels."
            $failed = $true
        }
    }

    if ($VerboseOutput) {
        Write-Info "Checked job '$jobId' (matrix-presence-gate=$hasMatrixPresenceGate, dynamic-matrix=$hasDynamicMatrixInclude, job-name-lines=$($jobNameLines.Count))."
    }

    $i = $end - 1
}

if (-not $jobTexts.ContainsKey('unity-tests-single-threaded')) {
    Write-Host "::error file=.github/workflows/unity-tests.yml::Missing unity-tests-single-threaded job."
    $failed = $true
} else {
    $singleThreadedJob = $jobTexts['unity-tests-single-threaded']
    $requiredSingleThreadedContracts = @(
        @{
            Name = 'needs main Unity matrix'
            Pattern = '(?m)^      - unity-tests\s*$'
            Message = 'unity-tests-single-threaded must wait for unity-tests so same-workflow jobs do not contend for the org Unity lock.'
        },
        @{
            Name = 'needs standalone Unity tier'
            Pattern = '(?m)^      - unity-tests-standalone\s*$'
            Message = 'unity-tests-single-threaded must wait for unity-tests-standalone so same-workflow jobs do not contend for the org Unity lock after the fast tier.'
        },
        @{
            Name = 'uses always for skipped standalone'
            Pattern = 'always\(\)'
            Message = 'unity-tests-single-threaded must use always() so workflow_dispatch runs with a skipped standalone tier can still evaluate its result gate.'
        },
        @{
            Name = 'requires successful main Unity matrix'
            Pattern = "needs\.unity-tests\.result\s*==\s*'success'"
            Message = 'unity-tests-single-threaded must run only after unity-tests succeeds.'
        },
        @{
            Name = 'accepts skipped standalone tier'
            Pattern = "needs\.unity-tests-standalone\.result\s*==\s*'skipped'"
            Message = 'unity-tests-single-threaded must allow unity-tests-standalone to be skipped for single-mode dispatch pins.'
        }
    )

    foreach ($contract in $requiredSingleThreadedContracts) {
        if ($singleThreadedJob -notmatch $contract.Pattern) {
            Write-Host "::error file=.github/workflows/unity-tests.yml::Unity workflow contract failed ($($contract.Name)): $($contract.Message)"
            $failed = $true
        } elseif ($VerboseOutput) {
            Write-Info "Checked unity-tests-single-threaded contract '$($contract.Name)'."
        }
    }
}

if (-not $jobTexts.ContainsKey('unitypackage-smoke')) {
    Write-Host "::error file=.github/workflows/unity-tests.yml::Missing unitypackage-smoke job."
    $failed = $true
} else {
    $unitypackageSmokeJob = $jobTexts['unitypackage-smoke']
    $requiredUnitypackageSmokeContracts = @(
        @{
            Name = 'needs main Unity matrix'
            Pattern = '(?m)^      - unity-tests\s*$'
            Message = 'unitypackage-smoke must wait for unity-tests so package export smoke runs only after the standard matrix is green.'
        },
        @{
            Name = 'needs standalone Unity tier'
            Pattern = '(?m)^      - unity-tests-standalone\s*$'
            Message = 'unitypackage-smoke must wait for unity-tests-standalone so the export smoke does not race the standalone tier for the org Unity lock.'
        },
        @{
            Name = 'needs single-threaded Unity tier'
            Pattern = '(?m)^      - unity-tests-single-threaded\s*$'
            Message = 'unitypackage-smoke must wait for unity-tests-single-threaded so release payload smoke is the final Unity gate.'
        },
        @{
            Name = 'requires successful single-threaded Unity tier'
            Pattern = "needs\.unity-tests-single-threaded\.result\s*==\s*'success'"
            Message = 'unitypackage-smoke must run only after the single-threaded Unity tier succeeds.'
        },
        @{
            Name = 'runs the release exporter'
            Pattern = 'bash scripts/unity/export-unitypackage\.sh'
            Message = 'unitypackage-smoke must run scripts/unity/export-unitypackage.sh so Samples~ are staged as the release .unitypackage payload.'
        },
        @{
            Name = 'uses release Unity version'
            Pattern = [regex]::Escape('UNITY_VERSION="$(jq -r ''.release'' .github/unity-versions.json)"')
            Message = 'unitypackage-smoke must use the release Unity version source of truth.'
        },
        @{
            Name = 'uploads export diagnostics'
            Pattern = [regex]::Escape('unitypackage-smoke-diagnostics-${{ github.run_id }}-${{ github.run_attempt }}')
            Message = 'unitypackage-smoke must upload export diagnostics when the smoke export fails.'
        }
    )

    foreach ($contract in $requiredUnitypackageSmokeContracts) {
        if ($unitypackageSmokeJob -notmatch $contract.Pattern) {
            Write-Host "::error file=.github/workflows/unity-tests.yml::Unity workflow contract failed ($($contract.Name)): $($contract.Message)"
            $failed = $true
        } elseif ($VerboseOutput) {
            Write-Info "Checked unitypackage-smoke contract '$($contract.Name)'."
        }
    }
}

if ($failed) {
    exit 1
}

Write-Host "[test-unity-workflow-matrix-contract] OK: gated dynamic-matrix jobs do not use matrix expressions in job names." -ForegroundColor Green
exit 0
