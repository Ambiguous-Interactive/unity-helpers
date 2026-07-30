#!/usr/bin/env pwsh
# cspell:ignore Creds
# Contract tests for the fail-closed organization Unity lifecycle.
[CmdletBinding()]
param([switch]$VerboseOutput)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$workflowPath = Join-Path $repoRoot '.github/workflows/unity-tests.yml'
$benchmarksPath = Join-Path $repoRoot '.github/workflows/unity-benchmarks.yml'
$releasePath = Join-Path $repoRoot '.github/workflows/release.yml'
$runnerPath = Join-Path $repoRoot 'scripts/unity/run-ci-tests.ps1'

$classifierCommit = '1ec035504397eeff3f5c27059081d56ff7987802'
$lifecycleCommit = '08fc83e83fa4cae89c0177005b388585ffdb1d9a'
$returnCommit = '0ce3dce6cbe29af210432087e3b6d81509258063'
$actionPrefix = 'Ambiguous-Interactive/ambiguous-organization-build-lock/.github/actions'
$trustedClause = "github.event.pull_request.user.login != 'dependabot[bot]'"

function Write-Info {
    param([Parameter(Mandatory = $true)][string]$Message)
    if ($VerboseOutput) {
        Write-Host "[test-unity-workflow-matrix-contract] $Message" -ForegroundColor Cyan
    }
}

function Read-RequiredFile {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required contract file is missing: $Path"
    }
    return Get-Content -LiteralPath $Path -Raw
}

function Get-JobText {
    param(
        [Parameter(Mandatory = $true)][string]$Workflow,
        [Parameter(Mandatory = $true)][string]$JobName
    )

    $pattern = "(?ms)^  $([regex]::Escape($JobName)):\r?`n(?<body>.*?)(?=^  [a-zA-Z0-9_-]+:\r?`n|\z)"
    $match = [regex]::Match($Workflow, $pattern)
    if (-not $match.Success) {
        throw "Workflow job '$JobName' is missing."
    }
    return $match.Value
}

function Test-EnrollmentContract {
    param([Parameter(Mandatory = $true)][string]$Workflow)

    if ($Workflow -notmatch '(?m)^  pull_request:\s*$') { return $false }
    if ($Workflow -notmatch '(?m)^  push:\s*$') { return $false }
    if ($Workflow -match '(?m)^  schedule:\s*$') { return $false }
    if ($Workflow -notmatch '(?m)^  cancel-in-progress: false\s*$') { return $false }
    if ($Workflow -match 'github\.actor') { return $false }

    $classifier = Get-JobText -Workflow $Workflow -JobName 'change-classifier'
    if ($classifier -match '(?m)^    needs:') { return $false }
    if ($classifier -notmatch [regex]::Escape(
            "$actionPrefix/classify-unity-changes@$classifierCommit"
        )) { return $false }
    foreach ($input in @('event-name', 'base-sha', 'head-sha')) {
        if ($classifier -notmatch "(?m)^          ${input}:") { return $false }
    }

    $preflight = Get-JobText -Workflow $Workflow -JobName 'runner-preflight'
    if ($preflight -notmatch [regex]::Escape($trustedClause)) { return $false }
    if ($preflight -notmatch '(?m)^    name: Self-hosted runner registration preflight\s*$') {
        return $false
    }
    if ($preflight -notmatch '(?m)^      - name: Require a registered Windows Unity runner\s*$') {
        return $false
    }
    if ($preflight -notmatch [regex]::Escape(
            "$actionPrefix/check-unity-runner-availability@$lifecycleCommit"
        )) { return $false }
    if ($preflight -notmatch [regex]::Escape(
            'required-label-sets: ''[["self-hosted","Windows","RAM-64GB"]]'''
        )) { return $false }

    $unity = Get-JobText -Workflow $Workflow -JobName 'unity-validation'
    if ($unity -notmatch [regex]::Escape($trustedClause)) { return $false }
    if ($unity -notmatch '(?m)^    runs-on: \[self-hosted, Windows, RAM-64GB\]\s*$') {
        return $false
    }
    if ($unity -notmatch '(?m)^    timeout-minutes: 1200\s*$') { return $false }
    if ([regex]::Matches($unity, [regex]::Escape(
                "$actionPrefix/acquire-build-lock@$lifecycleCommit"
            )).Count -ne 1) { return $false }
    if ([regex]::Matches($unity, '-LicenseReturnOwner Central').Count -ne 3) {
        return $false
    }
    foreach ($mode in @('editmode', 'playmode', 'standalone')) {
        if ($unity -notmatch [regex]::Escape("-TestMode $mode")) { return $false }
    }
    foreach ($required in @(
            'require-resource-lifecycle: "true"',
            'minimum-release-cooldown-seconds: "1"',
            'holder-id-suffix: unity-helpers-ci'
        )) {
        if ($unity -notmatch [regex]::Escape($required)) { return $false }
    }

    $terminal = @(
        'Return Unity license',
        'Classify Unity cleanup evidence',
        'Release organization Unity lock',
        'Require confirmed Unity cleanup'
    )
    $terminalMarkers = @($terminal | ForEach-Object { "      - name: $_" })
    $terminalPositions = @()
    foreach ($marker in $terminalMarkers) {
        if ([regex]::Matches($unity, "(?m)^$([regex]::Escape($marker))\s*$").Count -ne 1) {
            return $false
        }
        $terminalPositions += $unity.IndexOf($marker, [StringComparison]::Ordinal)
    }
    for ($index = 1; $index -lt $terminalPositions.Count; $index++) {
        if ($terminalPositions[$index] -le $terminalPositions[$index - 1]) {
            return $false
        }
    }

    $terminalBlocks = @()
    for ($index = 0; $index -lt $terminalPositions.Count; $index++) {
        $start = $terminalPositions[$index]
        $end = if ($index + 1 -lt $terminalPositions.Count) {
            $terminalPositions[$index + 1]
        } else {
            $unity.Length
        }
        $block = $unity.Substring($start, $end - $start)
        if ([regex]::Matches($block, '(?m)^      - ').Count -ne 1) { return $false }
        if ($block -match '(?m)^        continue-on-error: true\s*$') { return $false }
        $terminalBlocks += $block
    }

    $terminalRequirements = @(
        @(
            'id: return-unity-license',
            "uses: $actionPrefix/return-unity-license@$returnCommit",
            "if: `${{ always() && steps.acquire-build-lock.outputs.acquired == 'true' }}",
            'unity-version: 6000.5.2f1',
            'tool-cache: ${{ runner.tool_cache }}'
        ),
        @(
            'id: classify-unity-cleanup',
            "uses: $actionPrefix/classify-unity-cleanup-evidence@$lifecycleCommit",
            "if: `${{ always() && steps.acquire-build-lock.outputs.acquired == 'true' }}",
            'return-log-path: ${{ steps.return-unity-license.outputs.return-log-path }}',
            'return-command-completed: ${{ steps.return-unity-license.outputs.return-command-completed }}',
            'return-exit-code: ${{ steps.return-unity-license.outputs.return-exit-code }}',
            'evidence-capture-complete: ${{ steps.return-unity-license.outputs.evidence-capture-complete }}',
            'return-log-digest: ${{ steps.return-unity-license.outputs.return-log-digest }}'
        ),
        @(
            'id: release-build-lock',
            "uses: $actionPrefix/release-build-lock@$lifecycleCommit",
            'if: ${{ always() }}',
            'resource-cleanup-status: ${{ steps.classify-unity-cleanup.outputs.resource-cleanup-status }}',
            'resource-health: ${{ steps.classify-unity-cleanup.outputs.resource-health }}',
            'resource-reason: ${{ steps.classify-unity-cleanup.outputs.resource-reason }}'
        ),
        @(
            "uses: $actionPrefix/require-confirmed-unity-cleanup@$lifecycleCommit",
            'if: ${{ always() }}',
            'acquired: ${{ steps.acquire-build-lock.outputs.acquired }}',
            'classification-complete: ${{ steps.classify-unity-cleanup.outputs.classification-complete }}',
            'cleanup-status: ${{ steps.classify-unity-cleanup.outputs.resource-cleanup-status }}',
            'cleanup-health: ${{ steps.classify-unity-cleanup.outputs.resource-health }}',
            'cleanup-reason: ${{ steps.classify-unity-cleanup.outputs.resource-reason }}',
            'release-outcome: ${{ steps.release-build-lock.outcome }}',
            'cleanup-result: ${{ steps.release-build-lock.outputs.cleanup-result }}',
            'released: ${{ steps.release-build-lock.outputs.released }}',
            'release-health: ${{ steps.release-build-lock.outputs.resource-health }}',
            'release-reason: ${{ steps.release-build-lock.outputs.resource-reason }}',
            'reservation-state: ${{ steps.release-build-lock.outputs.reservation-state }}',
            'reservation-id: ${{ steps.release-build-lock.outputs.reservation-id }}',
            'incident-id: ${{ steps.release-build-lock.outputs.incident-id }}'
        )
    )
    for ($index = 0; $index -lt $terminalRequirements.Count; $index++) {
        foreach ($required in $terminalRequirements[$index]) {
            if ($terminalBlocks[$index] -notmatch [regex]::Escape($required)) {
                return $false
            }
        }
    }
    if ($unity -notmatch '(?m)^          unity-version: 6000\.5\.2f1\s*$') {
        return $false
    }
    if ($unity -notmatch '(?m)^          tool-cache: \$\{\{ runner\.tool_cache \}\}\s*$') {
        return $false
    }
    $fallback = Get-JobText -Workflow $Workflow -JobName 'unity-lock-cleanup'
    if ($fallback -notmatch '(?m)^    if: >-\s*$') { return $false }
    foreach ($required in @(
            'always()',
            "needs.unity-validation.result != 'skipped'",
            $trustedClause,
            'holder-id: ${{ github.repository }}:${{ github.run_id }}:unity-validation:unity-helpers-ci',
            'resource-cleanup-status: unknown',
            'resource-health: healthy',
            'resource-reason: return-terminated'
        )) {
        if ($fallback -notmatch [regex]::Escape($required)) { return $false }
    }

    $aggregate = Get-JobText -Workflow $Workflow -JobName 'unity-ci-success'
    if ($aggregate -notmatch '(?m)^    name: Unity CI Success\s*$') { return $false }
    if ($aggregate -notmatch '(?m)^    if: \$\{\{ always\(\) \}\}\s*$') { return $false }
    if ($aggregate -notmatch [regex]::Escape(
            "$actionPrefix/require-unity-validation@$classifierCommit"
        )) { return $false }
    foreach ($input in @(
            'classifier-result',
            'unity-required',
            'trusted-revision',
            'preflight-result',
            'unity-result',
            'fallback-result',
            'fallback-cleanup-result'
        )) {
        if ($aggregate -notmatch "(?m)^          ${input}:") { return $false }
    }
    return $true
}

$workflow = Read-RequiredFile -Path $workflowPath
$benchmarks = Read-RequiredFile -Path $benchmarksPath
$release = Read-RequiredFile -Path $releasePath
$runner = Read-RequiredFile -Path $runnerPath

if (-not (Test-EnrollmentContract -Workflow $workflow)) {
    throw 'The canonical Unity workflow does not satisfy the enrollment contract.'
}

$mutations = [ordered]@{
    'cancellation enabled' = {
        param($value)
        $value.Replace('cancel-in-progress: false', 'cancel-in-progress: true')
    }
    'actor trust substitution' = {
        param($value)
        $value.Replace('github.event.pull_request.user.login', 'github.actor')
    }
    'mutable classifier revision' = {
        param($value)
        $value.Replace($classifierCommit, 'main')
    }
    'unapproved return revision' = {
        param($value)
        $value.Replace($returnCommit, ('f' * 40))
    }
    'lifecycle ownership removed' = {
        param($value)
        $value.Replace('-LicenseReturnOwner Central', '-LicenseReturnOwner Local')
    }
    'cleanup suffix interleaved' = {
        param($value)
        $needle = '      - name: Classify Unity cleanup evidence'
        $injected = "      - name: Opaque cleanup`n        run: echo unsafe`n`n$needle"
        $value.Replace($needle, $injected)
    }
    'cleanup suffix reordered' = {
        param($value)
        $returnMarker = '      - name: Return Unity license'
        $classifyMarker = '      - name: Classify Unity cleanup evidence'
        $releaseMarker = '      - name: Release organization Unity lock'
        $returnStart = $value.IndexOf($returnMarker, [StringComparison]::Ordinal)
        $classifyStart = $value.IndexOf($classifyMarker, [StringComparison]::Ordinal)
        $releaseStart = $value.IndexOf($releaseMarker, [StringComparison]::Ordinal)
        $returnBlock = $value.Substring($returnStart, $classifyStart - $returnStart)
        $classifyBlock = $value.Substring($classifyStart, $releaseStart - $classifyStart)
        $value.Substring(0, $returnStart) +
            $classifyBlock +
            $returnBlock +
            $value.Substring($releaseStart)
    }
    'unnamed step appended after gate' = {
        param($value)
        [regex]::Replace(
            $value,
            '(?m)^  unity-lock-cleanup:',
            "      - run: echo unsafe`r`n`r`n  unity-lock-cleanup:",
            1
        )
    }
    'return continue-on-error enabled' = {
        param($value)
        $value.Replace(
            '        id: return-unity-license',
            "        id: return-unity-license`r`n        continue-on-error: true"
        )
    }
    'fallback identity widened' = {
        param($value)
        $value.Replace(
            ':unity-validation:unity-helpers-ci',
            ':${{ github.job }}:${{ strategy.job-index }}'
        )
    }
    'typed aggregate removed' = {
        param($value)
        $value.Replace('require-unity-validation', 'echo-validation')
    }
}

foreach ($mutation in $mutations.GetEnumerator()) {
    $candidate = & $mutation.Value $workflow
    if ($candidate -eq $workflow) {
        throw "Mutation '$($mutation.Key)' did not change its fixture."
    }
    if (Test-EnrollmentContract -Workflow $candidate) {
        throw "Unsafe mutation '$($mutation.Key)' passed the enrollment contract."
    }
}

foreach ($retired in @(
        @{ Name = 'benchmarks'; Content = $benchmarks },
        @{ Name = 'release export'; Content = $release }
    )) {
    if (
        $retired.Content -match 'UNITY_SERIAL|UNITY_EMAIL|UNITY_PASSWORD' -or
        $retired.Content -match 'acquire-build-lock|return-unity-license'
    ) {
        throw "$($retired.Name) retirement regained paid Unity credential reachability."
    }
}
if ($benchmarks -notmatch 'benchmark-retirement') {
    throw 'Benchmark retirement notice is missing.'
}
if ($benchmarks -notmatch '(?m)^          exit 1\s*$') {
    throw 'Benchmark retirement must fail closed instead of reporting false success.'
}
if ($release -notmatch 'Block release until container-owned cleanup is trusted') {
    throw 'Release fail-closed gate for container-owned cleanup is missing.'
}

foreach ($required in @(
        "[ValidateSet('Local', 'Central')]",
        "[string]`$LicenseReturnOwner = 'Local'",
        "`$hasLicenseCreds -and `$LicenseReturnOwner -eq 'Local'"
    )) {
    if ($runner -notmatch [regex]::Escape($required)) {
        throw "run-ci-tests.ps1 is missing central return ownership guard '$required'."
    }
}

Write-Info "Rejected $($mutations.Count) unsafe workflow mutations."
Write-Host 'Unity enrollment workflow contract passed.'
