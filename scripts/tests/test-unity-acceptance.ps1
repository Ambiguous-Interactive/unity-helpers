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
    'WALLSTOP_SENTINEL_INTERACTION_TOKEN', 'WALLSTOP_SENTINEL_INTERACTION_PROJECT',
    'WALLSTOP_SENTINEL_CAPTURE_TOKEN', 'WALLSTOP_SENTINEL_CAPTURE_PROJECT', 'WALLSTOP_SENTINEL_CAPTURE_OUTPUT',
    'WALLSTOP_PROTO_OWNER_CONFLICT_TOKEN', 'WALLSTOP_PROTO_OWNER_CONFLICT_PROJECT')
$originalEnvironment = @{}
foreach ($key in $environmentKeys) {
    $originalEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
}
$env:ACCEPTANCE_CONTROL_LOG = Join-Path $temporary 'calls.jsonl'
$env:WALLSTOP_SENTINEL_INTERACTION_TOKEN = 'original-token'
$env:WALLSTOP_SENTINEL_INTERACTION_PROJECT = 'original-project'
$env:WALLSTOP_PROTO_OWNER_CONFLICT_TOKEN = 'original-owner-token'
$env:WALLSTOP_PROTO_OWNER_CONFLICT_PROJECT = 'original-owner-project'
$env:WALLSTOP_SENTINEL_CAPTURE_TOKEN = 'original-capture-token'
$env:WALLSTOP_SENTINEL_CAPTURE_PROJECT = 'original-capture-project'
$env:WALLSTOP_SENTINEL_CAPTURE_OUTPUT = 'original-capture-output'
$checks = 0
$fixturePaths = @(
    'Tests/Editor/Validation/ValidationWorkspaceInteractionTests.cs',
    'Tests/Editor/Capture/SentinelSurfaceCaptureTests.cs',
    'Tests/Editor/Tools/WProtoSubtypeTagAssignerClassificationTests.cs',
    'Tests/Runtime/Serialization/WProtoCrossAssemblyTests.cs',
    'Tests/Runtime/Performance/IntMapPerformanceTests.cs'
)
try {
    Set-Content -LiteralPath (Join-Path $temporary 'assert-no-active-unity-editor.ps1') -Value @'
if ($env:ACCEPTANCE_CONTROL_ACTIVE -eq 'true') { throw 'Editor is already active.' }
'@
    Set-Content -LiteralPath (Join-Path $temporary 'run-ci-tests.ps1') -Value @'
param($UnityVersion, $RepoRoot, $ArtifactsPath, $ReleaseCodeOptimization, $ReleasePlayerBuild,
    $Il2CppCompilerConfiguration, $ProjectPath, $TestMode, $AssemblyNames, $TestFilter,
    $StandaloneScriptingBackend, $ManagedStrippingLevel, $EnableEditorGraphics)
if ($AssemblyNames -eq 'WallstopStudios.UnityHelpers.Tests.Editor.Validation') {
    if ($env:WALLSTOP_SENTINEL_INTERACTION_PROJECT -ne $ProjectPath -or
        [IO.File]::ReadAllText((Join-Path $ProjectPath '.sentinel-interaction-disposable')) -ne
        $env:WALLSTOP_SENTINEL_INTERACTION_TOKEN) { throw 'Disposable project markers disagree.' }
}
if ($AssemblyNames -eq 'WallstopStudios.UnityHelpers.Tests.Editor.Capture') {
    if (-not $EnableEditorGraphics -or $TestMode -ne 'editmode' -or
        $env:WALLSTOP_SENTINEL_CAPTURE_PROJECT -ne $ProjectPath -or
        $env:WALLSTOP_SENTINEL_CAPTURE_OUTPUT -ne (Join-Path $ArtifactsPath 'images') -or
        [IO.File]::ReadAllText((Join-Path $ProjectPath '.sentinel-capture-disposable')) -ne
        $env:WALLSTOP_SENTINEL_CAPTURE_TOKEN) { throw 'Capture graphics, output or project markers disagree.' }
}
if ($AssemblyNames -eq 'WallstopStudios.UnityHelpers.Tests.Editor.Tools') {
    if ($env:WALLSTOP_PROTO_OWNER_CONFLICT_PROJECT -ne $ProjectPath -or
        [IO.File]::ReadAllText((Join-Path $ProjectPath '.proto-owner-acceptance')) -ne
        $env:WALLSTOP_PROTO_OWNER_CONFLICT_TOKEN) { throw 'Owner project markers disagree.' }
    $sibling = Join-Path $ProjectPath 'Assets/SerializationAcceptance/Sibling/Sibling.asmdef'
    $definition = Get-Content -LiteralPath $sibling -Raw | ConvertFrom-Json
    if ($definition.name -ne 'WallstopStudios.UnityHelpers.Acceptance.Sibling' -or
        ($definition.references -join ',') -match 'Consumer|TestRunner|Tests') {
        throw 'The sibling must be independently compiled as an ordinary assembly.'
    }
}
if ($ManagedStrippingLevel -eq 'High') {
    $root = Join-Path $ProjectPath 'Assets/SerializationAcceptance'
    $assemblies = @(Get-ChildItem -LiteralPath $root -Filter '*.asmdef' -Recurse)
    if ($assemblies.Count -ne 2) { throw 'High stripping needs two ordinary model assemblies.' }
    foreach ($assembly in $assemblies) {
        $definition = Get-Content -LiteralPath $assembly.FullName -Raw | ConvertFrom-Json
        if ($definition.references -isnot [array]) { throw 'Assembly references must be a JSON array, including one reference.' }
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
    $graphics = $runner.Find({ param($node)
        $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
        $node.Left.Extent.Text -eq '$graphicsArgs'
    }, $true)
    $graphicsStatements = $graphics.Parent.Statements | Where-Object {
        ($_.GetType().Name -eq 'AssignmentStatementAst' -and $_.Left.Extent.Text -in @('$graphicsArgs', '$testArgs')) -or
        ($_.GetType().Name -eq 'IfStatementAst' -and $_.Clauses[0].Item1.Extent.Text -eq '$EnableEditorGraphics')
    }
    $ProjectPath = $temporary
    $testPlatform = 'EditMode'
    $resultsPath = Join-Path $temporary 'results.xml'
    $AssemblyNames = 'Capture.Tests'
    $categoryArgs = @()
    $filterArgs = @()
    $acceleratorArgs = @()
    foreach ($EnableEditorGraphics in @($false, $true)) {
        foreach ($statement in $graphicsStatements) { Invoke-Expression $statement.Extent.Text }
        $expected = if ($EnableEditorGraphics) { '-force-d3d11' } else { '-nographics' }
        $forbidden = if ($EnableEditorGraphics) { '-nographics' } else { '-force-d3d11' }
        if ($expected -notin $testArgs -or $forbidden -in $testArgs -or '-batchmode' -notin $testArgs) {
            throw 'Editor invocation lost explicit graphics selection or the normal headless default.'
        }
        $checks++
    }
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
        $expected = if ($selection -eq 'all') { 5 } elseif ($selection -in @('serialization', 'sentinel')) { 2 } else { 1 }
        if ($calls.Count -ne $expected) { throw "Wrong selected invocation count for $selection." }
        foreach ($call in $calls) {
            $fixtures = @($fixturePaths | Where-Object {
                [IO.Path]::GetFileNameWithoutExtension($_) -in ($call.TestFilter -split '\.')
            })
            if ($fixtures.Count -ne 1) { throw 'Acceptance must select one known source fixture.' }
            $fixture = Get-Item -LiteralPath (Join-Path $PSScriptRoot "../../$($fixtures[0])")
            $directory = $fixture.Directory
            $definitions = @()
            while ($null -ne $directory -and $definitions.Count -eq 0) {
                $definitions = @(Get-ChildItem -LiteralPath $directory.FullName -Filter '*.asmdef' -File)
                $directory = $directory.Parent
            }
            if ($definitions.Count -ne 1) { throw 'Acceptance fixture needs one nearest owning asmdef.' }
            $owner = Get-Content -LiteralPath $definitions[0].FullName -Raw | ConvertFrom-Json
            if ($call.AssemblyNames -ne $owner.name) {
                throw "Acceptance fixture $($fixture.Name) belongs to $($owner.name), not $($call.AssemblyNames)."
            }
            $checks++
            if (-not $call.ReleaseCodeOptimization -or -not $call.ReleasePlayerBuild -or
                $call.Il2CppCompilerConfiguration -ne 'Release' -or $call.TestFilter -notmatch '^WallstopStudios\.') {
                throw 'Acceptance lost Release configuration or exact test selection.'
            }
            if ($call.TestFilter -eq 'WallstopStudios.UnityHelpers.Tests.Serialization.WProtoCrossAssemblyTests' -and
                $call.ManagedStrippingLevel -ne 'High') { throw 'Serialization acceptance lost High stripping.' }
            if (Test-Path -LiteralPath $call.ProjectPath) { throw 'An owned acceptance project was left behind.' }
            if ($call.TestFilter -match 'ValidationWorkspaceInteractionTests' -and $call.AssemblyNames -ne 'WallstopStudios.UnityHelpers.Tests.Editor.Validation') {
                throw 'Sentinel filter must select its actual owning assembly.'
            }
            if ($call.TestMode -eq 'standalone' -and $call.StandaloneScriptingBackend -ne 'IL2CPP') {
                throw 'IntMap must use IL2CPP.'
            }
        }
        if ($env:WALLSTOP_SENTINEL_INTERACTION_TOKEN -ne 'original-token' -or
            $env:WALLSTOP_SENTINEL_INTERACTION_PROJECT -ne 'original-project' -or
            $env:WALLSTOP_SENTINEL_CAPTURE_TOKEN -ne 'original-capture-token' -or
            $env:WALLSTOP_SENTINEL_CAPTURE_PROJECT -ne 'original-capture-project' -or
            $env:WALLSTOP_SENTINEL_CAPTURE_OUTPUT -ne 'original-capture-output' -or
            $env:WALLSTOP_PROTO_OWNER_CONFLICT_TOKEN -ne 'original-owner-token' -or
            $env:WALLSTOP_PROTO_OWNER_CONFLICT_PROJECT -ne 'original-owner-project') { throw 'Sentinel marker environment leaked.' }
        $checks++
    }
    $env:ACCEPTANCE_CONTROL_FAIL = 'editmode'
    Remove-Item -LiteralPath $env:ACCEPTANCE_CONTROL_LOG
    $failed = $false
    try { & $subject -Acceptance all -UnityVersion '2021.3.45f1' -ArtifactsPath $temporary -TemporaryRoot $temporary -Repository (Join-Path $PSScriptRoot '../..') }
    catch { $failed = $_.Exception.Message.Contains('Injected native failure.') }
    if (-not $failed -or @(Get-Content -LiteralPath $env:ACCEPTANCE_CONTROL_LOG).Count -ne 5 -or
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

    $captureResults = Join-Path $temporary 'capture'
    $captureImages = Join-Path $captureResults 'images'
    New-Item -ItemType Directory -Path $captureImages -Force | Out-Null
    $captureName = 'WallstopStudios.UnityHelpers.Tests.Editor.Validation.SentinelSurfaceCaptureTests.CaptureBothActualEditorSkins'
    $captureXml = "<test-run><test-case fullname='$captureName' result='Passed' /></test-run>"
    $captureLog = @('dark', 'light') | ForEach-Object { "UH_SENTINEL_CAPTURE unity=2021.3.45f1 graphics=Direct3D11 skin=$_ images=6" }
    Set-Content -LiteralPath (Join-Path $captureResults 'results.xml') -Value $captureXml
    Set-Content -LiteralPath (Join-Path $captureResults 'unity.log') -Value $captureLog
    foreach ($skin in @('dark', 'light')) {
        foreach ($surface in @('after-issues', 'after-rules', 'after-builder', 'after-settings', 'after-builder-fix', 'after-graph', 'control-red', 'control-green')) {
            # Header controls exercise artifact inventory and dimensions. Native tests
            # independently decode the renderer output and check actual pixel controls.
            $dimensions = if ($surface.StartsWith('control-')) { '0000004000000040' } else { '00000500000002D0' }
            $header = [Convert]::FromHexString('89504E470D0A1A0A0000000D49484452' + $dimensions + '0802000000')
            [IO.File]::WriteAllBytes((Join-Path $captureImages "$surface-$skin.png"), $header)
        }
    }
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
    Set-Content -LiteralPath (Join-Path $results 'results.xml') -Value "<test-run><test-case fullname='$name' result='Passed' /></test-run>"
    $imagePath = Join-Path $captureImages 'after-graph-light.png'
    $imageHeader = [IO.File]::ReadAllBytes($imagePath)
    foreach ($shape in @('pass', 'skipped', 'missing case', 'missing skin', 'duplicate skin', 'wrong version', 'null graphics', 'wrong device', 'missing image', 'empty image', 'bad dimensions', 'bad signature')) {
        $xml = $captureXml
        $log = $captureLog -join [Environment]::NewLine
        [IO.File]::WriteAllBytes($imagePath, $imageHeader)
        switch ($shape) {
            'skipped' { $xml = $xml.Replace('Passed', 'Skipped') }
            'missing case' { $xml = '<test-run />' }
            'missing skin' { $log = $captureLog[0] }
            'duplicate skin' { $log = $log.Replace('skin=light', 'skin=dark') }
            'wrong version' { $log = $log.Replace('2021.3.45f1', '6000.5.2f1') }
            'null graphics' { $log = $log.Replace('Direct3D11', 'Null') }
            'wrong device' { $log = $log.Replace('Direct3D11', 'Direct3D12') }
            'missing image' { Remove-Item -LiteralPath $imagePath }
            'empty image' { [IO.File]::WriteAllBytes($imagePath, @()) }
            'bad dimensions' { $bad = $imageHeader.Clone(); $bad[19] = 1; [IO.File]::WriteAllBytes($imagePath, $bad) }
            'bad signature' { $bad = $imageHeader.Clone(); $bad[0] = 0; [IO.File]::WriteAllBytes($imagePath, $bad) }
        }
        Set-Content -LiteralPath (Join-Path $captureResults 'results.xml') -Value $xml
        Set-Content -LiteralPath (Join-Path $captureResults 'unity.log') -Value $log
        $failed = $false
        try {
            & (Join-Path $PSScriptRoot '../unity/verify-acceptance.ps1') -Acceptance sentinel -ArtifactsPath $temporary -Commit ('a' * 40) -UnityVersion '2021.3.45f1'
        } catch { $failed = $true }
        $summary = Get-Content -LiteralPath (Join-Path $temporary 'acceptance-summary.json') -Raw | ConvertFrom-Json
        $expectedStatus = if ($shape -eq 'pass') { 'passed' } else { 'failed' }
        if ($failed -ne ($shape -ne 'pass') -or $summary.capture -ne $expectedStatus -or $summary.sentinel -ne 'passed') {
            throw "Incorrect capture acceptance for $shape."
        }
        $checks++
    }
    Set-Content -LiteralPath (Join-Path $captureResults 'results.xml') -Value $captureXml
    Set-Content -LiteralPath (Join-Path $captureResults 'unity.log') -Value $captureLog
    [IO.File]::WriteAllBytes($imagePath, $imageHeader)
    $ownerResults = Join-Path $temporary 'owners'
    New-Item -ItemType Directory -Path $ownerResults | Out-Null
    $ownerName = 'WallstopStudios.UnityHelpers.Tests.Editor.Tools.WProtoSubtypeTagAssignerClassificationTests.NativeSiblingOwnersRefuseAssignmentAndThePlayerBuildGate'
    $serializationResults = Join-Path $temporary 'serialization'
    New-Item -ItemType Directory -Path $serializationResults | Out-Null
    $serializationNames = @(
        'AnExtendingAssemblyRoundTripsThroughPrecompiledMembersAndCollections',
        'ExtendingAPreviouslyMergeableBasePreservesItsConstructorSeed',
        'SeededBasePromotionKeepsInheritedMembersAndConsumerFields',
        'AConcreteSubtypeEntryPointUsesTheReplacementRootChain'
    ) | ForEach-Object { "WallstopStudios.UnityHelpers.Tests.Serialization.WProtoCrossAssemblyTests.$_" }
    foreach ($shape in @('pass', 'alias api', 'skipped', 'missing', 'duplicate', 'wrong name', 'fixture failure',
        'no config', 'duplicate config', 'disabled stripping', 'debug compiler', 'missing build', 'disabled build', 'debug build', 'missing owner', 'skipped owner', 'missing conflicts', 'one owner order', 'missing player', 'duplicate player', 'wrong commit', 'wrong version', 'editor')) {
        $body = ($serializationNames | ForEach-Object { "<test-case fullname='$_' result='Passed' />" }) -join ''
        $configure = 'UH perf config: backend=IL2CPP, api=NET_Standard, codeOpt=Release, il2cppConfig=Release, stripping=High, defines=[]'
        $build = 'UH player build config: backend=IL2CPP, stripping=High, development=False'
        $player = 'UH_SERIALIZATION_ACCEPTANCE commit=' + ('a' * 40) + ' unity=2021.3.45f1 backend=IL2CPP development=False cases=4 conflicts=2'
        $ownerBody = "<test-run><test-case fullname='$ownerName' result='Passed' /></test-run>"
        switch ($shape) {
            'missing owner' { $ownerBody = '<test-run />' }
            'skipped owner' { $ownerBody = $ownerBody.Replace('Passed', 'Skipped') }
            'missing conflicts' { $player = $player.Replace(' conflicts=2', '') }
            'one owner order' { $player = $player.Replace('conflicts=2', 'conflicts=1') }
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
        Set-Content -LiteralPath (Join-Path $ownerResults 'results.xml') -Value $ownerBody
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
    Set-Content -LiteralPath (Join-Path $ownerResults 'results.xml') -Value "<test-run><test-case fullname='$ownerName' result='Passed' /></test-run>"
    Set-Content -LiteralPath (Join-Path $serializationResults 'results.xml') -Value "<test-run>$body</test-run>"
    Set-Content -LiteralPath (Join-Path $serializationResults 'configure.log') -Value 'UH perf config: backend=IL2CPP, api=NET_Standard, codeOpt=Release, il2cppConfig=Release, stripping=High, defines=[]'
    Set-Content -LiteralPath (Join-Path $serializationResults 'unity.log') -Value 'UH player build config: backend=IL2CPP, stripping=High, development=False'
    Set-Content -LiteralPath (Join-Path $serializationResults 'player.log') -Value ('UH_SERIALIZATION_ACCEPTANCE commit=' + ('a' * 40) + ' unity=2021.3.45f1 backend=IL2CPP development=False cases=4 conflicts=2')
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
