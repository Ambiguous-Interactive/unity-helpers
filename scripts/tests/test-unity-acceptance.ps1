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
param($UnityVersion, $RepoRoot, $ArtifactsPath, $ReleaseCodeOptimization, $ReleasePlayerBuild,
    $Il2CppCompilerConfiguration, $ProjectPath, $TestMode, $AssemblyNames, $TestFilter,
    $StandaloneScriptingBackend, $ManagedStrippingLevel)
if ($TestMode -eq 'editmode') {
    if ($env:WALLSTOP_SENTINEL_INTERACTION_PROJECT -ne $ProjectPath -or
        [IO.File]::ReadAllText((Join-Path $ProjectPath '.sentinel-interaction-disposable')) -ne
        $env:WALLSTOP_SENTINEL_INTERACTION_TOKEN) { throw 'Disposable project markers disagree.' }
}
if ($ManagedStrippingLevel -eq 'High') {
    $root = Join-Path $ProjectPath 'Assets/SerializationAcceptance'
    $assemblies = @(Get-ChildItem -LiteralPath $root -Filter '*.asmdef' -Recurse)
    if ($assemblies.Count -ne 2) { throw 'High stripping needs two ordinary model assemblies.' }
    foreach ($assembly in $assemblies) {
        $definition = Get-Content -LiteralPath $assembly.FullName -Raw | ConvertFrom-Json
        if (-not $definition.autoReferenced -or ($definition.references -join ',') -match 'Tests|TestRunner') {
            throw 'Stripping subjects must not depend on test assemblies.'
        }
    }
    $consumer = Get-Content -LiteralPath (Join-Path $root 'Consumer/Contracts.cs') -Raw
    if ($consumer -match 'NUnit|\[Test|\[Category|Tests\.Core') { throw 'Ordinary consumer retained test framework roots.' }
    $driver = Get-Content -LiteralPath (Join-Path $root 'Consumer/SerializationAcceptance.cs') -Raw
    if ($driver -notmatch 'UH_SERIALIZATION_ACCEPTANCE commit=[0-9a-f]{40}') { throw 'Native driver is not bound to source.' }
}
$PSBoundParameters | ConvertTo-Json -Compress | Add-Content -LiteralPath $env:ACCEPTANCE_CONTROL_LOG
if ($env:ACCEPTANCE_CONTROL_FAIL -eq $TestMode) { throw 'Injected native failure.' }
if ($env:ACCEPTANCE_CONTROL_START_ACTIVE -eq 'true') { $env:ACCEPTANCE_CONTROL_ACTIVE = 'true' }
'@
    $tokens = $null
    $errors = $null
    $runner = [System.Management.Automation.Language.Parser]::ParseFile(
        (Join-Path $PSScriptRoot '../unity/run-ci-tests.ps1'), [ref]$tokens, [ref]$errors)
    if ($errors.Count) { throw 'Native runner does not parse.' }
    foreach ($function in @('New-ConfiguratorSource', 'Initialize-EphemeralProject')) {
        $definition = $runner.Find({ param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $function
        }, $true)
        Invoke-Expression $definition.Extent.Text
    }
    function Resolve-FullPath { param($Path) return [IO.Path]::GetFullPath($Path) }
    function New-ManifestJson { return '{}' }
    foreach ($level in @('Disabled', 'High')) {
        $project = Join-Path $temporary "configuration-$level"
        $null = Initialize-EphemeralProject -Root $temporary -Version '2021.3.45f1' -Mode editmode -Path $project -ManagedStrippingLevel $level
        $generated = Get-Content -LiteralPath (Join-Path $project 'Assets/Editor/UhCiTestConfigurator.cs') -Raw
        if (-not $generated.Contains("PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Standalone, ManagedStrippingLevel.$level);")) {
            throw 'Ephemeral project dropped the selected stripping level.'
        }
        if (-not $generated.Contains('stripping={PlayerSettings.GetManagedStrippingLevel(BuildTargetGroup.Standalone)}')) {
            throw 'Configurator must report the effective stripping level.'
        }
        $checks++
    }
    foreach ($selection in @('sentinel', 'intmap', 'serialization', 'all')) {
        Remove-Item -LiteralPath $env:ACCEPTANCE_CONTROL_LOG -ErrorAction SilentlyContinue
        & $subject -Acceptance $selection -UnityVersion '2021.3.45f1' -ArtifactsPath $temporary -TemporaryRoot $temporary -Repository (Join-Path $PSScriptRoot '../..')
        $calls = @(Get-Content -LiteralPath $env:ACCEPTANCE_CONTROL_LOG | ForEach-Object { $_ | ConvertFrom-Json })
        $expected = if ($selection -eq 'all') { 3 } else { 1 }
        if ($calls.Count -ne $expected) { throw "Wrong selected invocation count for $selection." }
        foreach ($call in $calls) {
            if (-not $call.ReleaseCodeOptimization -or -not $call.ReleasePlayerBuild -or
                $call.Il2CppCompilerConfiguration -ne 'Release' -or $call.TestFilter -notmatch '^WallstopStudios\.') {
                throw 'Acceptance lost Release configuration or exact test selection.'
            }
            if ($call.TestFilter -eq 'WallstopStudios.UnityHelpers.Tests.Serialization.WProtoCrossAssemblyTests' -and
                $call.ManagedStrippingLevel -ne 'High') { throw 'Serialization acceptance lost High stripping.' }
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
    try { & $subject -Acceptance all -UnityVersion '2021.3.45f1' -ArtifactsPath $temporary -TemporaryRoot $temporary -Repository (Join-Path $PSScriptRoot '../..') }
    catch { $failed = $_.Exception.Message.Contains('Injected native failure.') }
    if (-not $failed -or @(Get-Content -LiteralPath $env:ACCEPTANCE_CONTROL_LOG).Count -ne 3 -or
        $env:WALLSTOP_SENTINEL_INTERACTION_TOKEN -ne 'original-token') {
        throw 'Earlier failure must preserve later execution, fail the aggregate and restore marker state.'
    }
    $checks++
    $env:ACCEPTANCE_CONTROL_FAIL = ''
    $env:ACCEPTANCE_CONTROL_ACTIVE = 'true'
    Remove-Item -LiteralPath $env:ACCEPTANCE_CONTROL_LOG
    $failed = $false
    try { & $subject -Acceptance all -UnityVersion '2021.3.45f1' -ArtifactsPath $temporary -TemporaryRoot $temporary -Repository (Join-Path $PSScriptRoot '../..') }
    catch { $failed = $_.Exception.Message.Contains('Editor is already active.') }
    if (-not $failed -or (Test-Path -LiteralPath $env:ACCEPTANCE_CONTROL_LOG)) { throw 'An active editor must prevent every new launch.' }
    $checks++

    $env:ACCEPTANCE_CONTROL_ACTIVE = ''
    $env:ACCEPTANCE_CONTROL_START_ACTIVE = 'true'
    $failed = $false
    try { & $subject -Acceptance intmap -UnityVersion '2021.3.45f1' -ArtifactsPath $temporary -TemporaryRoot $temporary -Repository (Join-Path $PSScriptRoot '../..') }
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
    $serializationResults = Join-Path $temporary 'serialization'
    New-Item -ItemType Directory -Path $serializationResults | Out-Null
    $serializationNames = @(
        'AnExtendingAssemblyRoundTripsThroughPrecompiledMembersAndCollections',
        'ExtendingAPreviouslyMergeableBasePreservesItsConstructorSeed',
        'SeededBasePromotionKeepsInheritedMembersAndConsumerFields',
        'AConcreteSubtypeEntryPointUsesTheReplacementRootChain'
    ) | ForEach-Object { "WallstopStudios.UnityHelpers.Tests.Serialization.WProtoCrossAssemblyTests.$_" }
    foreach ($shape in @('pass', 'alias api', 'skipped', 'missing', 'duplicate', 'wrong name', 'fixture failure',
        'no config', 'duplicate config', 'disabled stripping', 'debug compiler', 'missing build', 'disabled build', 'debug build', 'missing player', 'duplicate player', 'wrong commit', 'wrong version', 'editor')) {
        $body = ($serializationNames | ForEach-Object { "<test-case fullname='$_' result='Passed' />" }) -join ''
        $configure = 'UH perf config: backend=IL2CPP, api=NET_Standard, codeOpt=Release, il2cppConfig=Release, stripping=High, defines=[]'
        $build = 'UH player build config: backend=IL2CPP, stripping=High, development=False'
        $player = 'UH_SERIALIZATION_ACCEPTANCE commit=' + ('a' * 40) + ' unity=2021.3.45f1 backend=IL2CPP development=False cases=4'
        switch ($shape) {
            'skipped' { $body = $body.Replace('Passed', 'Skipped') }
            'missing' { $body = $body -replace '<test-case[^>]+/>$', '' }
            'duplicate' { $body += $body }
            'wrong name' { $body = $body.Replace($serializationNames[0], 'Unrelated.Test') }
            'fixture failure' { $body += "<test-suite result='Failed'><failure /></test-suite>" }
            'alias api' { $configure = $configure.Replace('NET_Standard', 'NET_Standard_2_0') }
            'no config' { $configure = '' }
            'duplicate config' { $configure += "`n$configure" }
            'missing build' { $build = '' }
            'disabled build' { $build = $build.Replace('High', 'Disabled') }
            'debug build' { $build = $build.Replace('False', 'True') }
            'disabled stripping' { $configure = $configure.Replace('High', 'Disabled') }
            'debug compiler' { $configure = $configure.Replace('il2cppConfig=Release', 'il2cppConfig=Debug') }
            'missing player' { $player = '' }
            'duplicate player' { $player += "`n$player" }
            'wrong commit' { $player = $player.Replace(('a' * 40), ('b' * 40)) }
            'wrong version' { $player = $player.Replace('2021.3.45f1', '6000.5.2f1') }
            'editor' { $player = $player.Replace('IL2CPP', 'Mono') }
        }
        Set-Content -LiteralPath (Join-Path $serializationResults 'results.xml') -Value "<test-run>$body</test-run>"
        Set-Content -LiteralPath (Join-Path $serializationResults 'configure.log') -Value $configure
        Set-Content -LiteralPath (Join-Path $serializationResults 'unity.log') -Value $build
        Set-Content -LiteralPath (Join-Path $serializationResults 'player.log') -Value $player
        $failed = $false
        try {
            & (Join-Path $PSScriptRoot '../unity/verify-acceptance.ps1') -Acceptance serialization -ArtifactsPath $temporary -Commit ('a' * 40) -UnityVersion '2021.3.45f1'
        } catch { $failed = $true }
        if ($failed -ne ($shape -notin @('pass', 'alias api'))) { throw "Incorrect serialization acceptance for $shape." }
        $checks++
    }
    $body = ($serializationNames | ForEach-Object { "<test-case fullname='$_' result='Passed' />" }) -join ''
    Set-Content -LiteralPath (Join-Path $serializationResults 'results.xml') -Value "<test-run>$body</test-run>"
    Set-Content -LiteralPath (Join-Path $serializationResults 'configure.log') -Value 'UH perf config: backend=IL2CPP, api=NET_Standard, codeOpt=Release, il2cppConfig=Release, stripping=High, defines=[]'
    Set-Content -LiteralPath (Join-Path $serializationResults 'unity.log') -Value 'UH player build config: backend=IL2CPP, stripping=High, development=False'
    Set-Content -LiteralPath (Join-Path $serializationResults 'player.log') -Value ('UH_SERIALIZATION_ACCEPTANCE commit=' + ('a' * 40) + ' unity=2021.3.45f1 backend=IL2CPP development=False cases=4')
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
