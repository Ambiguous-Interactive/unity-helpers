#!/usr/bin/env pwsh
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('unity-acceptance-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
$subject = Join-Path $temporary 'run-acceptance.ps1'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../unity/run-acceptance.ps1') -Destination $subject
$environmentKeys = @('ACCEPTANCE_CONTROL_LOG', 'ACCEPTANCE_CONTROL_ACTIVE', 'ACCEPTANCE_CONTROL_FAIL', 'ACCEPTANCE_CONTROL_START_ACTIVE',
    'WALLSTOP_SENTINEL_INTERACTION_TOKEN', 'WALLSTOP_SENTINEL_INTERACTION_PROJECT')
$originalEnvironment = @{}
foreach ($key in $environmentKeys) {
    $originalEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
}
$env:ACCEPTANCE_CONTROL_LOG = Join-Path $temporary 'calls.jsonl'
$env:WALLSTOP_SENTINEL_INTERACTION_TOKEN = 'original-token'
$env:WALLSTOP_SENTINEL_INTERACTION_PROJECT = 'original-project'
$checks = 0
try {
    Set-Content -LiteralPath (Join-Path $temporary 'assert-no-active-unity-editor.ps1') -Value @'
if ($env:ACCEPTANCE_CONTROL_ACTIVE -eq 'true') { throw 'Editor is already active.' }
'@
    Set-Content -LiteralPath (Join-Path $temporary 'run-ci-tests.ps1') -Value @'
param($UnityVersion, $ArtifactsPath, $ReleaseCodeOptimization, $ReleasePlayerBuild,
    $Il2CppCompilerConfiguration, $ProjectPath, $TestMode, $AssemblyNames, $TestFilter,
    $StandaloneScriptingBackend)
if ($TestMode -eq 'editmode') {
    if ($env:WALLSTOP_SENTINEL_INTERACTION_PROJECT -ne $ProjectPath -or
        [IO.File]::ReadAllText((Join-Path $ProjectPath '.sentinel-interaction-disposable')) -ne
        $env:WALLSTOP_SENTINEL_INTERACTION_TOKEN) { throw 'Disposable project markers disagree.' }
}
$PSBoundParameters | ConvertTo-Json -Compress | Add-Content -LiteralPath $env:ACCEPTANCE_CONTROL_LOG
if ($env:ACCEPTANCE_CONTROL_FAIL -eq $TestMode) { throw 'Injected native failure.' }
if ($env:ACCEPTANCE_CONTROL_START_ACTIVE -eq 'true') { $env:ACCEPTANCE_CONTROL_ACTIVE = 'true' }
'@
    foreach ($selection in @('sentinel', 'intmap', 'all')) {
        Remove-Item -LiteralPath $env:ACCEPTANCE_CONTROL_LOG -ErrorAction SilentlyContinue
        & $subject -Acceptance $selection -UnityVersion '2021.3.45f1' -ArtifactsPath $temporary -TemporaryRoot $temporary
        $calls = @(Get-Content -LiteralPath $env:ACCEPTANCE_CONTROL_LOG | ForEach-Object { $_ | ConvertFrom-Json })
        $expected = if ($selection -eq 'all') { 2 } else { 1 }
        if ($calls.Count -ne $expected) { throw "Wrong selected invocation count for $selection." }
        foreach ($call in $calls) {
            if (-not $call.ReleaseCodeOptimization -or -not $call.ReleasePlayerBuild -or
                $call.Il2CppCompilerConfiguration -ne 'Release' -or $call.TestFilter -notmatch '^WallstopStudios\.') {
                throw 'Acceptance lost Release configuration or exact test selection.'
            }
            if (Test-Path -LiteralPath $call.ProjectPath) { throw 'An owned acceptance project was left behind.' }
            if ($call.TestMode -eq 'editmode' -and $call.AssemblyNames -ne 'WallstopStudios.UnityHelpers.Tests.Editor.Validation') {
                throw 'Sentinel filter must select its actual owning assembly.'
            }
            if ($call.TestMode -eq 'standalone' -and $call.StandaloneScriptingBackend -ne 'IL2CPP') {
                throw 'IntMap must use IL2CPP.'
            }
        }
        if ($env:WALLSTOP_SENTINEL_INTERACTION_TOKEN -ne 'original-token' -or
            $env:WALLSTOP_SENTINEL_INTERACTION_PROJECT -ne 'original-project') { throw 'Sentinel marker environment leaked.' }
        $checks++
    }
    $env:ACCEPTANCE_CONTROL_FAIL = 'editmode'
    Remove-Item -LiteralPath $env:ACCEPTANCE_CONTROL_LOG
    $failed = $false
    try { & $subject -Acceptance all -UnityVersion '2021.3.45f1' -ArtifactsPath $temporary -TemporaryRoot $temporary }
    catch { $failed = $_.Exception.Message.Contains('Injected native failure.') }
    if (-not $failed -or @(Get-Content -LiteralPath $env:ACCEPTANCE_CONTROL_LOG).Count -ne 2 -or
        $env:WALLSTOP_SENTINEL_INTERACTION_TOKEN -ne 'original-token') {
        throw 'Earlier failure must preserve later execution, fail the aggregate and restore marker state.'
    }
    $checks++
    $env:ACCEPTANCE_CONTROL_FAIL = ''
    $env:ACCEPTANCE_CONTROL_ACTIVE = 'true'
    Remove-Item -LiteralPath $env:ACCEPTANCE_CONTROL_LOG
    $failed = $false
    try { & $subject -Acceptance all -UnityVersion '2021.3.45f1' -ArtifactsPath $temporary -TemporaryRoot $temporary }
    catch { $failed = $_.Exception.Message.Contains('Editor is already active.') }
    if (-not $failed -or (Test-Path -LiteralPath $env:ACCEPTANCE_CONTROL_LOG)) { throw 'An active editor must prevent every new launch.' }
    $checks++

    $env:ACCEPTANCE_CONTROL_ACTIVE = ''
    $env:ACCEPTANCE_CONTROL_START_ACTIVE = 'true'
    $failed = $false
    try { & $subject -Acceptance intmap -UnityVersion '2021.3.45f1' -ArtifactsPath $temporary -TemporaryRoot $temporary }
    catch { $failed = $_.Exception.Message.Contains('cleanup: Editor is already active.') }
    $retained = Get-Content -LiteralPath $env:ACCEPTANCE_CONTROL_LOG | ConvertFrom-Json
    if (-not $failed -or -not (Test-Path -LiteralPath $retained.ProjectPath)) {
        throw 'Cleanup must retain the owned project if an editor remains active.'
    }
    $checks++

    $results = Join-Path $temporary 'sentinel'
    New-Item -ItemType Directory -Path $results | Out-Null
    $name = 'WallstopStudios.UnityHelpers.Tests.Editor.Validation.ValidationWorkspaceInteractionTests.NativePanelCallbacksRetainDraftAndPersistSettings'
    foreach ($shape in @('pass', 'skipped', 'wrong name', 'duplicate', 'fixture failure', 'empty')) {
        $body = "<test-case fullname='$name' result='Passed' />"
        switch ($shape) {
            'skipped' { $body = $body.Replace('Passed', 'Skipped') }
            'wrong name' { $body = $body.Replace($name, 'Unrelated.Test') }
            'duplicate' { $body += $body }
            'fixture failure' { $body += "<test-suite result='Failed'><failure /></test-suite>" }
            'empty' { $body = '' }
        }
        Set-Content -LiteralPath (Join-Path $results 'results.xml') -Value "<test-run>$body</test-run>"
        $failed = $false
        try {
            & (Join-Path $PSScriptRoot '../unity/verify-acceptance.ps1') -Acceptance sentinel -ArtifactsPath $temporary -Commit ('a' * 40) -UnityVersion '2021.3.45f1'
        } catch { $failed = $true }
        if ($failed -ne ($shape -ne 'pass')) { throw "Incorrect XML acceptance for $shape." }
        $checks++
    }
    $intmapResults = Join-Path $temporary 'intmap'
    New-Item -ItemType Directory -Path $intmapResults | Out-Null
    $intmapName = 'WallstopStudios.UnityHelpers.Tests.Runtime.Performance.IntMapPerformanceTests.IntMapLookupsComparedAgainstDictionary'
    foreach ($control in @(
        @{ Selection = 'intmap'; NativeResult = 'Skipped'; Stable = $false },
        @{ Selection = 'all'; NativeResult = 'Skipped'; Stable = $false },
        @{ Selection = 'intmap'; NativeResult = 'Passed'; Stable = $false },
        @{ Selection = 'all'; NativeResult = 'Passed'; Stable = $true }
    )) {
        # Every four-cycle bootstrap batch has the same paired ratio. The alternating
        # durations give exact independent summaries and truthful unstable evidence.
        $reference = @(0..31 | ForEach-Object { if (-not $control.Stable -and $_ % 2) { 24.0 } else { 20.0 } })
        $candidate = @($reference | ForEach-Object { $_ / 1.6 })
        $spread = if ($control.Stable) { 0.0 } else { 0.2 }
        $referenceSummary = if ($control.Stable) {
            @{ Median = 20.0; P95 = 20.0; MedianAbsoluteDeviation = 0.0 }
        } else { @{ Median = 22.0; P95 = 24.0; MedianAbsoluteDeviation = 2.0 } }
        $subjectSummary = if ($control.Stable) {
            @{ Median = 12.5; P95 = 12.5; MedianAbsoluteDeviation = 0.0 }
        } else { @{ Median = 13.75; P95 = 15.0; MedianAbsoluteDeviation = 1.25 } }
        $raw = @{
            EnvironmentMetadata = @{
                commit = 'a' * 40; unityVersion = '2021.3.45f1'; backend = 'IL2CPP'
                isEditor = 'False'; buildConfiguration = 'Release'
            }
            ReferenceMilliseconds = $reference; SubjectMilliseconds = $candidate
            PairedLogRatios = @(0..31 | ForEach-Object { [Math]::Log($reference[$_]) - [Math]::Log($candidate[$_]) })
            Comparison = @{ Ratio = 1.6; ReferenceSpread = $spread; SubjectSpread = $spread; Cycles = 32 }
            ReferenceSummary = $referenceSummary; SubjectSummary = $subjectSummary
            RatioLower95 = 1.6; RatioUpper95 = 1.6; HasSufficientTiming = $true
            Iterations = 1; Seed = -1640531527
            ReferenceWarmupMilliseconds = 100; SubjectWarmupMilliseconds = 100
            ReferenceWarmupExecutions = 3; SubjectWarmupExecutions = 3
        } | ConvertTo-Json -Depth 12 -Compress
        $lines = foreach ($entries in @(1000, 10000)) {
            foreach ($misses in @(0, 50)) { "INTMAP_PAIRED_SAMPLES $entries $misses $raw" }
        }
        Set-Content -LiteralPath (Join-Path $intmapResults 'player.log') -Value $lines
        Set-Content -LiteralPath (Join-Path $intmapResults 'results.xml') -Value "<test-run><test-case fullname='$intmapName' result='$($control.NativeResult)' /></test-run>"
        Set-Content -LiteralPath (Join-Path $results 'results.xml') -Value "<test-run><test-case fullname='$name' result='Passed' /></test-run>"
        $summaryPath = Join-Path $temporary 'acceptance-summary.json'
        Remove-Item -LiteralPath $summaryPath -ErrorAction SilentlyContinue
        $failed = $false
        try {
            & (Join-Path $PSScriptRoot '../unity/verify-acceptance.ps1') -Acceptance $control.Selection -ArtifactsPath $temporary -Commit ('a' * 40) -UnityVersion '2021.3.45f1'
        } catch { $failed = $true }
        if ($failed -ne ($control.NativeResult -ne 'Passed')) {
            throw 'Native verification must reject skipped XML while valid unstable timings remain report-only.'
        }
        $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
        $expectedDecision = if ($control.Stable) { 'meets-hit-margin' } else { 'inconclusive' }
        if ($summary.decision -ne $expectedDecision -or
            ($control.Selection -eq 'all' -and $summary.sentinel -ne 'passed')) {
            throw 'Combined verification lost the raw timing decision or the successful Sentinel callback result.'
        }
        $checks++
    }
    Write-Host "$checks orchestration, exact XML and raw evidence integration controls passed."
} finally {
    foreach ($key in $environmentKeys) {
        [Environment]::SetEnvironmentVariable($key, $originalEnvironment[$key], 'Process')
    }
    Remove-Item -LiteralPath $temporary -Recurse -Force
}
