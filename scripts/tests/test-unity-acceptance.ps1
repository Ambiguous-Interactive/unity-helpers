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
    'WALLSTOP_PROTO_OWNER_CONFLICT_TOKEN', 'WALLSTOP_PROTO_OWNER_CONFLICT_PROJECT', 'UH_NATIVE_PROBE_SOURCE_SHA256',
    'UH_EDITOR_TEST_TIMEOUT_SECONDS', 'UH_STANDALONE_BUILD_TIMEOUT_SECONDS', 'UH_STANDALONE_PLAYER_TIMEOUT_SECONDS', 'UH_PERF_COMMIT')
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
    'Tests/Runtime/Serialization/WProtoConsumerMigrationTests.cs',
    'Tests/Runtime/Performance/IntMapPerformanceTests.cs',
    'Tests/Runtime/Extensions/BorrowedBase64Tests.cs',
    'Tests/Runtime/Performance/BorrowedBufferPreflightTests.cs'
)
try {
    Set-Content -LiteralPath (Join-Path $temporary 'assert-no-active-unity-editor.ps1') -Value @'
if ($env:ACCEPTANCE_CONTROL_ACTIVE -eq 'true') { throw 'Editor is already active.' }
'@
    Set-Content -LiteralPath (Join-Path $temporary 'build-native-allocation-probe.ps1') -Value @'
param($ProjectPath, $ArtifactsPath, $Repository)
$sourceHash = (Get-FileHash -LiteralPath (Join-Path $Repository 'Tests/Acceptance~/NativeAllocation/native-allocation-probe.cpp') -Algorithm SHA256).Hash.ToLowerInvariant()
$directory = Join-Path $ArtifactsPath 'native-allocation-probe'
$null = New-Item -ItemType Directory -Force -Path $directory
@{ SourcePath = 'Tests/Acceptance~/NativeAllocation/native-allocation-probe.cpp'; SourceSha256 = $sourceHash; BinarySha256 = ('a' * 64) } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'build.json')
return $sourceHash
'@
    Set-Content -LiteralPath (Join-Path $temporary 'run-ci-tests.ps1') -Value @'
param($UnityVersion, $RepoRoot, $ArtifactsPath, $ReleaseCodeOptimization, $ReleasePlayerBuild,
    $Il2CppCompilerConfiguration, $ProjectPath, $TestMode, $AssemblyNames, $TestFilter,
    $StandaloneScriptingBackend, $ManagedStrippingLevel, $EnableEditorGraphics, $TestCategory, $FrozenPlayerDeclarationPath, $AdditionalScriptingDefines, $StandaloneBuildCapture = 'None')
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
if ($ManagedStrippingLevel -eq 'High' -and $StandaloneBuildCapture -eq 'None') {
    $root = Join-Path $ProjectPath 'Assets/SerializationAcceptance'
    $assemblies = @(Get-ChildItem -LiteralPath $root -Filter '*.asmdef' -Recurse)
    if ($assemblies.Count -ne 3) { throw 'High stripping needs three ordinary model assemblies.' }
    foreach ($assembly in $assemblies) {
        $definition = Get-Content -LiteralPath $assembly.FullName -Raw | ConvertFrom-Json
        if ($definition.references -isnot [array]) { throw 'Assembly references must be a JSON array, including one reference.' }
        if (-not $definition.overrideReferences -or $definition.precompiledReferences.Count -ne 0) {
            throw 'Ordinary acceptance assemblies must not reference protobuf-net or NUnit.'
        }
        if (-not $definition.autoReferenced -or ($definition.references -join ',') -match 'Tests|TestRunner') {
            throw 'Stripping subjects must not depend on test assemblies.'
        }
    }
    $consumer = Get-Content -LiteralPath (Join-Path $root 'Consumer/Contracts.cs') -Raw
    if ($consumer -match 'NUnit|\[Test|\[Category|Tests\.Core') { throw 'Ordinary consumer retained test framework roots.' }
    $migrationDefinition = Get-Content -LiteralPath (Join-Path $root 'MigrationContracts/MigrationContracts.asmdef') -Raw | ConvertFrom-Json
    if (-not $migrationDefinition.overrideReferences -or $migrationDefinition.precompiledReferences.Count -ne 0 -or
        $migrationDefinition.defineConstraints.Count -ne 0) { throw 'Migration declarations must compile without fallback or test framework references.' }
    $migrationCases = Get-Content -LiteralPath (Join-Path $root 'Consumer/MigrationCases.cs') -Raw
    if ($migrationCases -match 'NUnit|\[Test|\[Category|Tests\.Serialization|#if !ENABLE_IL2CPP') { throw 'Migration acceptance retained test-only declarations.' }
    $driver = Get-Content -LiteralPath (Join-Path $root 'Consumer/SerializationAcceptance.cs') -Raw
    if ($driver -notmatch 'UH_SERIALIZATION_ACCEPTANCE commit=[0-9a-f]{40}') { throw 'Native driver is not bound to source.' }
}
if ($TestFilter -match 'BorrowedBufferPreflightTests') {
    $declaration = Get-Content -LiteralPath $FrozenPlayerDeclarationPath -Raw | ConvertFrom-Json
    if ($TestCategory -ne '' -or $TestMode -ne 'standalone' -or
        ($TestFilter -split ';').Count -ne 3 -or $declaration.SourceFiles.Count -lt 100 -or
        $declaration.CorpusFiles.Count -ne 1 -or $declaration.Backend -ne $StandaloneScriptingBackend -or
        $declaration.UnityVersion -ne $UnityVersion -or $declaration.SourceCommit -notmatch '^[0-9a-f]{40}$') {
        throw 'Borrowed qualification lost its source/corpus declaration or exact control selection.'
    }
    if ($declaration.CorpusFiles[0].Sha256 -cne 'd3933603fc240334440040824a7d56316a4e5bafc0124c611cffa6daf2ef8052') {
        throw 'Borrowed qualification uses a different corpus.'
    }
    $nativeSource = @($declaration.SourceFiles | Where-Object { $_.Path -ceq 'Tests/Acceptance~/NativeAllocation/native-allocation-probe.cpp' })
    if ($nativeSource.Count -ne 1 -or $nativeSource[0].Sha256 -cne $env:UH_NATIVE_PROBE_SOURCE_SHA256 -or
        ($AdditionalScriptingDefines -join ';') -cne 'UNITY_HELPERS_NATIVE_ALLOCATION_PROBE') {
        throw 'Borrowed qualification lost the native probe source or global define.'
    }
}
if ($null -ne $FrozenPlayerDeclarationPath -and $TestFilter -match 'CompareFrozenSevenFamilyCorpus$') {
    $declaration = Get-Content -LiteralPath $FrozenPlayerDeclarationPath -Raw | ConvertFrom-Json
    if ($declaration.CampaignKind -cne 'wproto-seven-families-v1' -or
        $declaration.RunId -cnotmatch '^wproto-[0-9a-f]{32}$' -or
        $declaration.Backend -cne $StandaloneScriptingBackend -or
        ($StandaloneBuildCapture -eq 'None' -and $StandaloneScriptingBackend -cne 'Mono2x') -or
        $declaration.UnityVersion -cne $UnityVersion -or
        $declaration.SourceCommit -cnotmatch '^[0-9a-f]{40}$' -or
        $env:UH_PERF_COMMIT -cne $declaration.SourceCommit -or
        $declaration.CanonicalCorpusPath -cne 'scripts/unity/wproto-corpus.v1.json' -or
        $declaration.CorpusFiles.Count -ne 1 -or
        $declaration.CorpusFiles[0].Path -cne $declaration.CanonicalCorpusPath) {
        throw 'WProto declaration identity differs from the requested campaign.'
    }
    $actualCorpusHash = (Get-FileHash -LiteralPath (Join-Path $RepoRoot $declaration.CanonicalCorpusPath) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($declaration.CorpusFiles[0].Sha256 -cne $actualCorpusHash) { throw 'WProto declaration lost the actual corpus bytes.' }
    foreach ($relative in @('Tests/Runtime/Performance/ProtoSerializationCorpusPerformanceTests.cs',
        'Tests/Runtime/Serialization/WProtoBenchmarkCorpus.cs', 'scripts/unity/wproto-corpus.v1.json',
        'Runtime/Analyzers/WallstopStudios.UnityHelpers.Proto.Generator.dll')) {
        $entries = @($declaration.SourceFiles | Where-Object { $_.Path -ceq $relative })
        if ($entries.Count -ne 1 -or $entries[0].Sha256 -cne
            (Get-FileHash -LiteralPath (Join-Path $RepoRoot $relative) -Algorithm SHA256).Hash.ToLowerInvariant()) {
            throw "WProto source census lost current input $relative."
        }
    }
    if ($env:UH_EDITOR_TEST_TIMEOUT_SECONDS -cne '900' -or
        $env:UH_STANDALONE_BUILD_TIMEOUT_SECONDS -cne '1800' -or
        $env:UH_STANDALONE_PLAYER_TIMEOUT_SECONDS -cne '3600') {
        throw 'WProto timeout envelope was not applied to the player invocation.'
    }
}
$PSBoundParameters | ConvertTo-Json -Compress | Add-Content -LiteralPath $env:ACCEPTANCE_CONTROL_LOG
if ($StandaloneBuildCapture -ne 'None') { throw 'Evidence-only Standalone build capture stops before player launch and cannot satisfy acceptance.' }
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
    foreach ($function in @('New-ConfiguratorSource', 'Set-EphemeralProjectContent', 'Initialize-EphemeralProject')) {
        $definition = $runner.Find({ param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $function
        }, $true)
        Invoke-Expression $definition.Extent.Text
    }
    function Resolve-FullPath { param($Path) return [IO.Path]::GetFullPath($Path) }
    function New-ManifestJson { return '{}' }
    $initializeCall = $runner.Find({ param($node)
        $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -ceq 'Initialize-EphemeralProject'
    }, $true)
    if ($null -eq $initializeCall) { throw 'The real runner must initialize its project.' }
    foreach ($campaignKind in @('ordinary', 'borrowed-buffer-preflight-v1', 'wproto-seven-families-v1')) {
        foreach ($backend in @('Mono2x', 'IL2CPP')) {
            $RepoRoot = $temporary
            $UnityVersion = '6000.6.0f1'
            $TestMode = 'editmode'
            $ProjectPath = Join-Path $temporary "managed-variant-$campaignKind-$backend"
            $IncludeComparisons = $false
            $IncludeIntegrations = $false
            $StandaloneScriptingBackend = $backend
            $Il2CppCompilerConfiguration = 'Release'
            $ManagedStrippingLevel = 'Disabled'
            $UseReleasePlayerBuild = $true
            $frozenPlayerDeclaration = if ($campaignKind -ceq 'ordinary') { $null } else { [pscustomobject]@{ CampaignKind = $campaignKind } }
            $project = Invoke-Expression $initializeCall.Extent.Text
            $generated = [IO.File]::ReadAllText((Join-Path $project 'Assets/Editor/UhCiTestConfigurator.cs'))
            $expected = if ($campaignKind -ceq 'wproto-seven-families-v1') { 'true' } else { 'false' }
            if (-not $generated.Contains("if ($expected || PlayerSettings.GetScriptingBackend(target) == ScriptingImplementation.Mono2x)")) {
                throw "The runner lost the WProto-only managed variant selection for $campaignKind/$backend."
            }
            $checks++
        }
    }
    foreach ($level in @('Disabled', 'High')) {
        $project = Join-Path $temporary "configuration-$level"
        $null = Initialize-EphemeralProject -Root $temporary -Version '2021.3.45f1' -Mode editmode -Path $project -ManagedStrippingLevel $level
        $generated = Get-Content -LiteralPath (Join-Path $project 'Assets/Editor/UhCiTestConfigurator.cs') -Raw
        if (-not $generated.Contains("PlayerSettings.SetManagedStrippingLevel(target, ManagedStrippingLevel.$level);")) {
            throw 'Ephemeral project dropped the selected stripping level.'
        }
        if (-not $generated.Contains('stripping={PlayerSettings.GetManagedStrippingLevel(target)}')) {
            throw 'Configurator must report the effective stripping level.'
        }
        $checks++
    }
    $testLinkerSource = Join-Path $temporary 'Tests/Runtime/Serialization/link.xml'
    $testLinkerDirectory = Split-Path -Parent $testLinkerSource
    $null = New-Item -ItemType Directory -Force -Path $testLinkerDirectory
    $testLinkerContent = '<linker><assembly fullname="Test.Reflection"><type fullname="Test.Contract" preserve="all" /></assembly></linker>'
    [IO.File]::WriteAllText($testLinkerSource, $testLinkerContent)
    [IO.File]::WriteAllText((Join-Path $temporary 'link.xml'), '<linker><assembly fullname="Broad.Root" preserve="all" /></linker>')
    $testLinkerProject = Join-Path $temporary 'test-linker-project'
    $null = Initialize-EphemeralProject -Root $temporary -Version '2021.3.45f1' -Mode editmode -Path $testLinkerProject
    $testLinkerTarget = Join-Path $testLinkerProject 'Assets/UhCiTestLinker/link.xml'
    if (-not (Test-Path -LiteralPath $testLinkerTarget -PathType Leaf) -or
        [IO.File]::ReadAllText($testLinkerTarget).TrimEnd() -cne $testLinkerContent) {
        throw 'Ephemeral test project must stage the specific test linker declaration in Assets.'
    }
    $checks++
    $toolingRoot = Join-Path $temporary 'different-tooling-root'
    $toolingLinkerDirectory = Join-Path $toolingRoot 'Tests/Runtime/Serialization'
    $null = New-Item -ItemType Directory -Force -Path $toolingLinkerDirectory
    [IO.File]::WriteAllText((Join-Path $toolingLinkerDirectory 'link.xml'), '<linker><assembly fullname="Wrong.Tooling" preserve="all" /></linker>')
    $splitRootProject = Join-Path $temporary 'split-root-project'
    $null = Initialize-EphemeralProject -Root $temporary -RepoRoot $toolingRoot -Version '2021.3.45f1' -Mode editmode -Path $splitRootProject
    if ([IO.File]::ReadAllText((Join-Path $splitRootProject 'Assets/UhCiTestLinker/link.xml')).TrimEnd() -cne $testLinkerContent) {
        throw 'Test linker declarations must follow the tested package root, not the tooling checkout.'
    }
    $checks++
    $testLinkerWriteTime = [DateTime]::SpecifyKind([DateTime]'2001-01-01T00:00:00', [DateTimeKind]::Utc)
    [IO.File]::SetLastWriteTimeUtc($testLinkerTarget, $testLinkerWriteTime)
    $null = Initialize-EphemeralProject -Root $temporary -Version '2021.3.45f1' -Mode editmode -Path $testLinkerProject
    if ([IO.File]::GetLastWriteTimeUtc($testLinkerTarget) -ne $testLinkerWriteTime) {
        throw 'Unchanged test linker declarations must not trigger an import.'
    }
    $checks++
    $updatedTestLinkerContent = $testLinkerContent.Replace('Test.Contract', 'Test.Replacement')
    [IO.File]::WriteAllText($testLinkerSource, $updatedTestLinkerContent)
    $null = Initialize-EphemeralProject -Root $temporary -Version '2021.3.45f1' -Mode editmode -Path $testLinkerProject
    if ([IO.File]::ReadAllText($testLinkerTarget).TrimEnd() -cne $updatedTestLinkerContent -or
        [IO.File]::GetLastWriteTimeUtc($testLinkerTarget) -eq $testLinkerWriteTime) {
        throw 'Changed test linker declarations must replace cached preservation.'
    }
    $checks++
    [IO.File]::WriteAllText("$testLinkerTarget.meta", 'stale generated metadata')
    Remove-Item -LiteralPath $testLinkerSource
    $null = Initialize-EphemeralProject -Root $temporary -Version '2021.3.45f1' -Mode editmode -Path $testLinkerProject
    if ((Test-Path -LiteralPath $testLinkerTarget) -or (Test-Path -LiteralPath "$testLinkerTarget.meta")) {
        throw 'Removed test linker declarations must not survive in the cached project.'
    }
    $checks++
    $idempotentProject = Join-Path $temporary 'idempotent-project'
    $null = Initialize-EphemeralProject -Root $temporary -Version '2021.3.45f1' -Mode editmode -Path $idempotentProject
    $seedPaths = @(
        (Join-Path $idempotentProject 'Packages/manifest.json'),
        (Join-Path $idempotentProject 'ProjectSettings/ProjectVersion.txt'),
        (Join-Path $idempotentProject 'ProjectSettings/EditorSettings.asset'),
        (Join-Path $idempotentProject 'Assets/Editor/UhCiTestConfigurator.cs')
    )
    $sentinelWriteTime = [DateTime]::SpecifyKind([DateTime]'2001-01-01T00:00:00', [DateTimeKind]::Utc)
    foreach ($seedPath in $seedPaths) {
        [IO.File]::SetLastWriteTimeUtc($seedPath, $sentinelWriteTime)
    }
    $null = Initialize-EphemeralProject -Root $temporary -Version '2021.3.45f1' -Mode editmode -Path $idempotentProject
    if (@($seedPaths | Where-Object { [IO.File]::GetLastWriteTimeUtc($_) -ne $sentinelWriteTime }).Count -ne 0) {
        throw 'Unchanged ephemeral project seeds must preserve their write times.'
    }
    Set-Content -LiteralPath $seedPaths[0] -Value '{"stale":true}'
    [IO.File]::SetLastWriteTimeUtc($seedPaths[0], $sentinelWriteTime)
    $null = Initialize-EphemeralProject -Root $temporary -Version '2021.3.45f1' -Mode editmode -Path $idempotentProject
    if ((Get-Content -LiteralPath $seedPaths[0] -Raw).Trim() -ne '{}' -or
        [IO.File]::GetLastWriteTimeUtc($seedPaths[0]) -eq $sentinelWriteTime -or
        @($seedPaths[1..3] | Where-Object { [IO.File]::GetLastWriteTimeUtc($_) -ne $sentinelWriteTime }).Count -ne 0) {
        throw 'Ephemeral project seeds must rewrite only changed content.'
    }
    $configurator = Get-Content -LiteralPath $seedPaths[3] -Raw
    $caseChangedConfigurator = $configurator.Replace('using ', 'Using ')
    if ($caseChangedConfigurator -ceq $configurator) { throw 'Case-only seed control did not change.' }
    Set-Content -LiteralPath $seedPaths[3] -Value $caseChangedConfigurator
    [IO.File]::SetLastWriteTimeUtc($seedPaths[3], $sentinelWriteTime)
    $null = Initialize-EphemeralProject -Root $temporary -Version '2021.3.45f1' -Mode editmode -Path $idempotentProject
    if ((Get-Content -LiteralPath $seedPaths[3] -Raw).TrimEnd() -cne $configurator.TrimEnd() -or
        [IO.File]::GetLastWriteTimeUtc($seedPaths[3]) -eq $sentinelWriteTime) {
        throw 'Ephemeral project seeds must repair case-only content drift.'
    }
    foreach ($seedPath in $seedPaths) {
        [IO.File]::SetLastWriteTimeUtc($seedPath, $sentinelWriteTime)
    }
    [IO.File]::WriteAllBytes($seedPaths[0], [byte[]]::new(0))
    [IO.File]::SetLastWriteTimeUtc($seedPaths[0], $sentinelWriteTime)
    $null = Initialize-EphemeralProject -Root $temporary -Version '2021.3.45f1' -Mode editmode -Path $idempotentProject
    if ((Get-Content -LiteralPath $seedPaths[0] -Raw).Trim() -ne '{}' -or
        [IO.File]::GetLastWriteTimeUtc($seedPaths[0]) -eq $sentinelWriteTime -or
        @($seedPaths[1..3] | Where-Object { [IO.File]::GetLastWriteTimeUtc($_) -ne $sentinelWriteTime }).Count -ne 0) {
        throw 'Ephemeral project seeds must rewrite only a truncated zero-byte seed.'
    }
    $checks++
    foreach ($selection in @('sentinel', 'intmap', 'serialization', 'borrowed', 'all')) {
        Remove-Item -LiteralPath $env:ACCEPTANCE_CONTROL_LOG -ErrorAction SilentlyContinue
        & $subject -Acceptance $selection -UnityVersion '2021.3.45f1' -ArtifactsPath $temporary -TemporaryRoot $temporary -Repository (Join-Path $PSScriptRoot '../..')
        $calls = @(Get-Content -LiteralPath $env:ACCEPTANCE_CONTROL_LOG | ForEach-Object { $_ | ConvertFrom-Json })
        $expected = if ($selection -eq 'all') { 5 } elseif ($selection -in @('serialization', 'sentinel')) { 2 } else { 1 }
        if ($calls.Count -ne $expected) { throw "Wrong selected invocation count for $selection." }
        foreach ($call in $calls) {
            $fixtures = @($fixturePaths | Where-Object {
                [IO.Path]::GetFileNameWithoutExtension($_) -in ($call.TestFilter -split '[.;]')
            })
            $expectedFixtures = if ($call.TestFilter -match 'WProtoConsumerMigrationTests|BorrowedBufferPreflightTests') { 2 } else { 1 }
            if ($fixtures.Count -ne $expectedFixtures) { throw 'Acceptance must select every known source fixture.' }
            foreach ($fixturePath in $fixtures) {
                $fixture = Get-Item -LiteralPath (Join-Path $PSScriptRoot "../../$fixturePath")
                $directory = $fixture.Directory
                $definitions = @()
                while ($null -ne $directory -and $definitions.Count -eq 0) {
                    $definitions = @(Get-ChildItem -LiteralPath $directory.FullName -Filter '*.asmdef' -File)
                    $directory = $directory.Parent
                }
                if ($definitions.Count -ne 1) { throw 'Acceptance fixture needs one nearest owning asmdef.' }
                $owner = Get-Content -LiteralPath $definitions[0].FullName -Raw | ConvertFrom-Json
                if ($owner.name -notin ($call.AssemblyNames -split ';')) {
                    throw "Acceptance fixture $($fixture.Name) belongs to $($owner.name), not $($call.AssemblyNames)."
                }
                $checks++
            }
            if (-not $call.ReleaseCodeOptimization -or -not $call.ReleasePlayerBuild -or
                $call.Il2CppCompilerConfiguration -ne 'Release' -or $call.TestFilter -notmatch '^WallstopStudios\.') {
                throw 'Acceptance lost Release configuration or exact test selection.'
            }
            if ($call.TestFilter -match 'WProtoCrossAssemblyTests' -and
                $call.ManagedStrippingLevel -ne 'High') { throw 'Serialization acceptance lost High stripping.' }
            if (Test-Path -LiteralPath $call.ProjectPath) { throw 'An owned acceptance project was left behind.' }
            if ($call.TestFilter -match 'ValidationWorkspaceInteractionTests' -and $call.AssemblyNames -ne 'WallstopStudios.UnityHelpers.Tests.Editor.Validation') {
                throw 'Sentinel filter must select its actual owning assembly.'
            }
            if ($call.TestMode -eq 'standalone' -and $call.StandaloneScriptingBackend -ne 'IL2CPP' -and
                -not ($selection -eq 'borrowed' -and $call.StandaloneScriptingBackend -eq 'Mono2x')) {
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
    $timeoutKeys = @('UH_EDITOR_TEST_TIMEOUT_SECONDS', 'UH_STANDALONE_BUILD_TIMEOUT_SECONDS', 'UH_STANDALONE_PLAYER_TIMEOUT_SECONDS')
    $expectedTimeouts = @('71', $null, '')
    for ($index = 0; $index -lt $timeoutKeys.Count; $index++) {
        if ($null -eq $expectedTimeouts[$index]) {
            Remove-Item -LiteralPath "Env:$($timeoutKeys[$index])" -ErrorAction SilentlyContinue
        } else {
            [Environment]::SetEnvironmentVariable($timeoutKeys[$index], $expectedTimeouts[$index], 'Process')
        }
    }
    if ($null -ne [Environment]::GetEnvironmentVariable($timeoutKeys[1], 'Process')) {
        throw 'The absent timeout restoration control must start with an absent environment variable.'
    }
    $emptyTimeout = [Environment]::GetEnvironmentVariable($timeoutKeys[2], 'Process')
    if ($null -ne $emptyTimeout -and $emptyTimeout -cne '') { throw 'The empty timeout control did not establish an empty or absent value.' }
    $expectedTimeouts[2] = $emptyTimeout
    $sourceCommit = (& git -C (Join-Path $PSScriptRoot '../..') rev-parse HEAD | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $sourceCommit -cnotmatch '^[0-9a-f]{40}$') { throw 'Cannot establish the control source revision.' }
    foreach ($control in @(
        @{ Version = '2021.3.45f1'; Commit = $null },
        @{ Version = '6000.6.0f1'; Commit = '' },
        @{ Version = '2021.3.45f1'; Commit = $sourceCommit }
    )) {
        $version = $control.Version
        if ($null -eq $control.Commit) {
            Remove-Item -LiteralPath 'Env:UH_PERF_COMMIT' -ErrorAction SilentlyContinue
        } else {
            [Environment]::SetEnvironmentVariable('UH_PERF_COMMIT', $control.Commit, 'Process')
        }
        $expectedCommit = [Environment]::GetEnvironmentVariable('UH_PERF_COMMIT', 'Process')
        if ($null -eq $control.Commit -and $null -ne $expectedCommit) { throw 'The absent commit control must start absent.' }
        if ($null -ne $control.Commit -and $control.Commit.Length -gt 0 -and $expectedCommit -cne $control.Commit) { throw 'The populated commit control was not established.' }
        if ($null -ne $control.Commit -and $control.Commit.Length -eq 0 -and $null -ne $expectedCommit -and $expectedCommit -cne '') { throw 'The empty commit control was not established.' }
        foreach ($injectFailure in @($false, $true)) {
            $directory = Join-Path $temporary ('wproto-control-' + [Guid]::NewGuid().ToString('N'))
            Clear-Content -LiteralPath $env:ACCEPTANCE_CONTROL_LOG
            $env:ACCEPTANCE_CONTROL_FAIL = if ($injectFailure) { 'standalone' } else { '' }
            $failure = $null
            try { & $subject -Acceptance wproto -UnityVersion $version -ArtifactsPath $directory -TemporaryRoot $temporary -Repository (Join-Path $PSScriptRoot '../..') }
            catch { $failure = $_.ToString() }
            if (($null -ne $failure) -ne $injectFailure -or
                ($injectFailure -and $failure -notmatch 'Injected native failure')) {
                throw "Unexpected WProto route result: $version / $injectFailure / $failure"
            }
            $calls = @(Get-Content -LiteralPath $env:ACCEPTANCE_CONTROL_LOG | ForEach-Object { $_ | ConvertFrom-Json })
            if ($calls.Count -ne 1) { throw 'WProto acceptance must invoke one observed player without automatic timing retries.' }
            $call = $calls[0]
            if ($call.TestMode -cne 'standalone' -or $call.StandaloneScriptingBackend -cne 'Mono2x' -or
                -not $call.ReleaseCodeOptimization -or -not $call.ReleasePlayerBuild -or
                $call.ManagedStrippingLevel -cne 'Disabled' -or
                ($call.AdditionalScriptingDefines -join ';') -cne 'WALLSTOP_PROTO' -or
                $call.AssemblyNames -cne 'WallstopStudios.UnityHelpers.Tests.Runtime.Performance' -or
                $call.TestFilter -cne 'WallstopStudios.UnityHelpers.Tests.Runtime.Performance.ProtoSerializationPerformanceTests.CompareFrozenSevenFamilyCorpus' -or
                $call.TestCategory -cne '' -or (Test-Path -LiteralPath $call.ProjectPath)) {
                throw 'WProto acceptance changed its exact release Mono corpus selection or leaked its owned project.'
            }
            for ($index = 0; $index -lt $timeoutKeys.Count; $index++) {
                $actualTimeout = [Environment]::GetEnvironmentVariable($timeoutKeys[$index], 'Process')
                if ($actualTimeout -cne $expectedTimeouts[$index]) {
                    $expectedDisplay = if ($null -eq $expectedTimeouts[$index]) { '<absent>' } else { "'$($expectedTimeouts[$index])'" }
                    $actualDisplay = if ($null -eq $actualTimeout) { '<absent>' } else { "'$actualTimeout'" }
                    throw "WProto acceptance did not restore $($timeoutKeys[$index]): expected $expectedDisplay, observed $actualDisplay."
                }
            }
            if ([Environment]::GetEnvironmentVariable('UH_PERF_COMMIT', 'Process') -cne $expectedCommit) {
                throw 'WProto acceptance did not restore the original commit environment value.'
            }
            $declarationHash = (Get-FileHash -LiteralPath $call.FrozenPlayerDeclarationPath -Algorithm SHA256).Hash
            Clear-Content -LiteralPath $env:ACCEPTANCE_CONTROL_LOG
            $env:ACCEPTANCE_CONTROL_FAIL = ''
            $refused = $false
            try { & $subject -Acceptance wproto -UnityVersion $version -ArtifactsPath $directory -TemporaryRoot $temporary -Repository (Join-Path $PSScriptRoot '../..') }
            catch { $refused = $true }
            if (-not $refused -or @(Get-Content -LiteralPath $env:ACCEPTANCE_CONTROL_LOG).Count -ne 0 -or
                (Get-FileHash -LiteralPath $call.FrozenPlayerDeclarationPath -Algorithm SHA256).Hash -cne $declarationHash) {
                throw 'WProto acceptance must retain an existing attempt and refuse its replacement before player invocation.'
            }
            if ([Environment]::GetEnvironmentVariable('UH_PERF_COMMIT', 'Process') -cne $expectedCommit) {
                throw 'Refusing an existing WProto attempt changed the commit environment value.'
            }
            $checks += 2
        }
    }
    $conflictingCommit = if ($sourceCommit -ceq ('0' * 40)) { '1' * 40 } else { '0' * 40 }
    $env:UH_PERF_COMMIT = $conflictingCommit
    $directory = Join-Path $temporary 'wproto-conflicting-commit'
    Clear-Content -LiteralPath $env:ACCEPTANCE_CONTROL_LOG
    $refused = $false
    try { & $subject -Acceptance wproto -UnityVersion '6000.6.0f1' -ArtifactsPath $directory -TemporaryRoot $temporary -Repository (Join-Path $PSScriptRoot '../..') }
    catch { $refused = $_.ToString() -match 'UH_PERF_COMMIT conflicts' }
    if (-not $refused -or @(Get-Content -LiteralPath $env:ACCEPTANCE_CONTROL_LOG).Count -ne 0 -or
        (Test-Path -LiteralPath (Join-Path $directory 'wproto/declaration.json')) -or
        [Environment]::GetEnvironmentVariable('UH_PERF_COMMIT', 'Process') -cne $conflictingCommit) {
        throw 'A conflicting WProto commit must be preserved and refused before declaration or player invocation.'
    }
    $checks++
    $env:UH_PERF_COMMIT = $sourceCommit
    $env:ACCEPTANCE_CONTROL_ACTIVE = 'false'
    $env:ACCEPTANCE_CONTROL_FAIL = ''
    foreach ($backend in @('Mono2x', 'IL2CPP', 'mono2x', 'il2cpp')) {
        $canonicalBackend = if ($backend -eq 'IL2CPP') { 'IL2CPP' } else { 'Mono2x' }
        $expectedStripping = if ($canonicalBackend -ceq 'IL2CPP') { 'High' } else { 'Disabled' }
        $directory = Join-Path $temporary ('wproto-build-capture-' + [Guid]::NewGuid().ToString('N'))
        Clear-Content -LiteralPath $env:ACCEPTANCE_CONTROL_LOG
        $failure = $null
        try {
            & $subject -Acceptance wproto -UnityVersion '6000.6.0f1' -ArtifactsPath $directory -TemporaryRoot $temporary -Repository (Join-Path $PSScriptRoot '../..') -StandaloneBuildCapture Direct -StandaloneBuildCaptureBackend $backend
        } catch { $failure = $_.ToString() }
        if ($null -eq $failure -or $failure -notmatch 'Evidence-only Standalone build capture stops before player launch') {
            throw "Diagnostic backend did not retain the explicit no-player stop: $backend / $failure"
        }
        $calls = @(Get-Content -LiteralPath $env:ACCEPTANCE_CONTROL_LOG | ForEach-Object { $_ | ConvertFrom-Json })
        if ($calls.Count -ne 1 -or $calls[0].StandaloneBuildCapture -cne 'Direct' -or
            $calls[0].StandaloneScriptingBackend -cne $canonicalBackend -or (Test-Path -LiteralPath $calls[0].ProjectPath)) {
            throw "Diagnostic backend changed, repeated the build, or leaked its owned project: $backend"
        }
        $declaration = Get-Content -LiteralPath $calls[0].FrozenPlayerDeclarationPath -Raw | ConvertFrom-Json
        if ($declaration.Backend -cne $canonicalBackend -or $calls[0].ManagedStrippingLevel -cne $expectedStripping -or
            -not $calls[0].ReleaseCodeOptimization -or -not $calls[0].ReleasePlayerBuild -or
            $calls[0].TestFilter -cne 'WallstopStudios.UnityHelpers.Tests.Runtime.Performance.ProtoSerializationPerformanceTests.CompareFrozenSevenFamilyCorpus') {
            throw "Diagnostic backend selection changed the declared source/settings/filter contract: backend=$backend, stripping=$($calls[0].ManagedStrippingLevel), expected=$expectedStripping."
        }
        $checks++
    }
    foreach ($selection in @('wproto', 'all', 'intmap')) {
        Clear-Content -LiteralPath $env:ACCEPTANCE_CONTROL_LOG
        $directory = Join-Path $temporary ('wproto-invalid-backend-' + [Guid]::NewGuid().ToString('N'))
        $failure = $null
        try { & $subject -Acceptance $selection -UnityVersion '6000.6.0f1' -ArtifactsPath $directory -TemporaryRoot $temporary -Repository (Join-Path $PSScriptRoot '../..') -StandaloneBuildCaptureBackend IL2CPP }
        catch { $failure = $_.ToString() }
        if ($null -eq $failure -or $failure -notmatch 'requires explicit evidence-only capture mode' -or
            @(Get-Content -LiteralPath $env:ACCEPTANCE_CONTROL_LOG).Count -ne 0 -or (Test-Path -LiteralPath $directory)) {
            throw 'IL2CPP diagnostic selection must be refused before any ordinary acceptance or project creation.'
        }
        $checks++
    }
    foreach ($version in @('2022.3.45f1', '6000.5.2f1', '6000.6.0f2')) {
        Clear-Content -LiteralPath $env:ACCEPTANCE_CONTROL_LOG
        $refused = $false
        try { & $subject -Acceptance wproto -UnityVersion $version -ArtifactsPath $temporary -TemporaryRoot $temporary -Repository (Join-Path $PSScriptRoot '../..') }
        catch { $refused = $_.ToString() -match 'pinned floor or latest' }
        if (-not $refused -or @(Get-Content -LiteralPath $env:ACCEPTANCE_CONTROL_LOG).Count -ne 0) {
            throw "Unsupported WProto player version was not refused before invocation: $version"
        }
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
    $serializationNames += @(
        'ClosedTargets.Int.Zero',
        'ClosedTargets.Int.One',
        'ClosedTargets.Int.NegativeOne',
        'ClosedTargets.Int.Minimum',
        'ClosedTargets.Int.Maximum',
        'ClosedTargets.String.Zero',
        'ClosedTargets.String.NegativeOne',
        'ClosedTargets.String.Minimum',
        'ClosedTargets.String.Maximum',
        'CombinedMigrationReadsRetainedGoldenBytes',
        'RuntimeDefaultsHaveGoldenBytesAndAChangedDefaultIsDetectable',
        'NullAndEmptyReferenceValuesKeepTheirWireDistinction'
    ) | ForEach-Object { "WallstopStudios.UnityHelpers.Tests.Serialization.WProtoConsumerMigrationTests.$_" }
    foreach ($shape in @('pass', 'alias api', 'skipped', 'missing', 'duplicate', 'wrong name', 'fixture failure',
        'no config', 'duplicate config', 'disabled stripping', 'debug compiler', 'missing build', 'disabled build', 'debug build', 'missing owner', 'skipped owner', 'missing conflicts', 'one owner order', 'missing migrations', 'incomplete migrations', 'missing player', 'duplicate player', 'wrong commit', 'wrong version', 'editor')) {
        $body = ($serializationNames | ForEach-Object { "<test-case fullname='$_' result='Passed' />" }) -join ''
        $configure = 'UH perf config: backend=IL2CPP, api=NET_Standard, codeOpt=Release, il2cppConfig=Release, stripping=High, defines=[]'
        $build = 'UH player build config: backend=IL2CPP, stripping=High, development=False'
        $player = 'UH_SERIALIZATION_ACCEPTANCE commit=' + ('a' * 40) + ' unity=2021.3.45f1 backend=IL2CPP development=False cases=4 conflicts=2 migrations=12'
        $ownerBody = "<test-run><test-case fullname='$ownerName' result='Passed' /></test-run>"
        switch ($shape) {
            'missing owner' { $ownerBody = '<test-run />' }
            'skipped owner' { $ownerBody = $ownerBody.Replace('Passed', 'Skipped') }
            'missing migrations' { $player = $player.Replace(' migrations=12', '') }
            'incomplete migrations' { $player = $player.Replace('migrations=12', 'migrations=11') }
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
    Set-Content -LiteralPath (Join-Path $serializationResults 'player.log') -Value ('UH_SERIALIZATION_ACCEPTANCE commit=' + ('a' * 40) + ' unity=2021.3.45f1 backend=IL2CPP development=False cases=4 conflicts=2 migrations=12')
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
    $env:ACCEPTANCE_CONTROL_ACTIVE = 'false'
    $env:ACCEPTANCE_CONTROL_START_ACTIVE = 'false'
    $env:ACCEPTANCE_CONTROL_FAIL = ''
    Remove-Item -LiteralPath (Join-Path $temporary 'borrowed') -Recurse -Force
    & $subject -Acceptance borrowed -UnityVersion '6000.6.0f1' -ArtifactsPath $temporary -TemporaryRoot $temporary -Repository (Join-Path $PSScriptRoot '../..')
    $latest = @(Get-Content -LiteralPath $env:ACCEPTANCE_CONTROL_LOG | ForEach-Object { $_ | ConvertFrom-Json })[-1]
    if ($latest.StandaloneScriptingBackend -ne 'IL2CPP' -or $latest.UnityVersion -ne '6000.6.0f1') {
        throw 'Latest borrowed qualification must use Release IL2CPP.'
    }
    $checks++
    $borrowedDirectory = Join-Path $temporary 'borrowed'
    $declarationPath = Join-Path $borrowedDirectory 'declaration.json'
    $declaration = Get-Content -LiteralPath $declarationPath -Raw | ConvertFrom-Json
    $reportDirectory = Join-Path $borrowedDirectory ('frozen-player-' + $declaration.RunId)
    $null = New-Item -ItemType Directory -Path $reportDirectory
    $reportPath = Join-Path $reportDirectory 'provenance.json'
    foreach ($shape in @('pass', 'wrong-commit', 'unstable-payload', 'no-invocation', 'failed-correctness', 'failed-join', 'inert-counter', 'rejected-clock', 'campaign-claim', 'duplicate-report')) {
        $record = @{ RuntimeIdentity = @{ Commit = $declaration.SourceCommit }; RuntimeReleasePlayerState = 'passed'; ClockState = 'passed'; AllocatedBytesState = 'passed' }
        $report = @{
            DeclarationSha256 = (Get-FileHash -LiteralPath $declarationPath -Algorithm SHA256).Hash.ToLowerInvariant()
            RunId = $declaration.RunId; RequestedUnityVersion = '6000.6.0f1'; RequestedBackend = 'IL2CPP'
            ProvenanceStableState = 'passed'; JointCorrectness = @{ State = 'passed' }; RuntimeRecordJoinState = 'passed'
            CorrectnessCorpusConsumptionState = 'passed'; LaunchAttempted = $true; InvocationObserved = $true
            ResultsValidated = $true; ExecutionError = $null; CampaignEligible = $false; AdoptionEligible = $false
            RuntimeJoin = @{ RawRecords = @(@{ Marker = 'BORROWED_PREFLIGHT'; Record = $record }) }
        }
        switch ($shape) {
            'wrong-commit' { $record.RuntimeIdentity.Commit = '0' * 40 }
            'unstable-payload' { $report.ProvenanceStableState = 'rejected' }
            'no-invocation' { $report.InvocationObserved = $false }
            'failed-correctness' { $report.JointCorrectness.State = 'rejected' }
            'failed-join' { $report.RuntimeRecordJoinState = 'rejected' }
            'inert-counter' { $record.AllocatedBytesState = 'rejected' }
            'rejected-clock' { $record.ClockState = 'rejected' }
            'campaign-claim' { $report.CampaignEligible = $true }
        }
        $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath
        if ($shape -eq 'duplicate-report') { Copy-Item -LiteralPath $reportPath -Destination (Join-Path $borrowedDirectory 'provenance.json') }
        $failed = $false
        try {
            & (Join-Path $PSScriptRoot '../unity/verify-acceptance.ps1') -Acceptance borrowed -ArtifactsPath $temporary -Commit $declaration.SourceCommit -UnityVersion '6000.6.0f1'
        } catch { $failed = $true }
        if ($failed -ne ($shape -ne 'pass')) { throw "Wrong borrowed channel qualification for $shape." }
        if ($shape -eq 'pass') {
            $summary = Get-Content -LiteralPath (Join-Path $temporary 'acceptance-summary.json') -Raw | ConvertFrom-Json
            if ($summary.ChannelQualification -ne 'passed' -or $summary.CampaignEligible -or $summary.AdoptionEligible) {
                throw 'Channel controls must not authorize candidate adoption.'
            }
        }
        $checks++
    }
    Remove-Item -LiteralPath (Join-Path $borrowedDirectory 'provenance.json')
    $nativeSource = @($declaration.SourceFiles | Where-Object { $_.Path -ceq 'Tests/Acceptance~/NativeAllocation/native-allocation-probe.cpp' })[0]
    foreach ($shape in @('pass', 'inert', 'preboxed-allocation', 'wrong-request', 'wrong-index', 'incomplete', 'invalid-values', 'wrong-scale', 'wrong-source', 'wrong-backend', 'wrong-binary', 'missing-payload', 'missing-window', 'string-count', 'string-boolean', 'missing-traversal', 'inert-traversal-control', 'allocating-value-view', 'zero-timing', 'duplicate-traversal')) {
        $windows = @()
        for ($index = 0; $index -lt 15; $index++) {
            $pattern = @(8192, 8192, 0, 0, 8192, 4096, 8192, 0, 8192, 4096, 8192, 0, 8192, 4096, 8192)
            $preboxed = $index -in @(1, 4, 8, 12)
            $count = if ($preboxed) { 0 } else { $pattern[$index] }
            $windows += @{
                ChronologicalIndex = $index; IsWarmup = $index -lt 3; Requested = $pattern[$index]; Preboxed = $preboxed
                Count = $count; LogicalObjectBytes = $count * 20; Completed = $true; ValuesVerified = $true
            }
        }
        $record = @{
            RuntimeIdentity = @{ Commit = $declaration.SourceCommit }; RuntimeReleasePlayerState = 'passed'; ClockState = 'passed'; AllocatedBytesState = 'rejected'
            NativeAllocationCountState = 'passed'; NativeAllocationSourceSha256 = $nativeSource.Sha256; NativeAllocationBackend = 3
            NativeAllocationError = $null; NativeAllocationObservations = $windows
            TraversalTimingObservations = @(); TraversalAllocationObservations = @()
        }
        foreach ($traversalShape in @('array', 'list', 'hash-set', 'dictionary')) {
            foreach ($size in @(1, 16, 4096)) {
                foreach ($repetition in @(0, 1, 2)) {
                    $record.TraversalTimingObservations += @{ Shape = $traversalShape; Size = $size; Repetition = $repetition
                        Calls = [int][Math]::Max(128, [Math]::Floor(131072 / $size)); ReferenceTicks = 100000; ValueTicks = 100001 }
                    $record.TraversalAllocationObservations += @{ Shape = $traversalShape; Size = $size; Repetition = $repetition
                        Calls = 128; ReferenceCount = 128; ValueCount = 0 }
                }
            }
        }
        $snapshots = @(0..2 | ForEach-Object {
            @{ State = 'passed'; PayloadFiles = @(@{ Path = 'Probe_Data/Plugins/x86_64/unity-helpers-allocation-probe.dll'; Sha256 = 'a' * 64 }) }
        })
        $report = @{
            DeclarationSha256 = (Get-FileHash -LiteralPath $declarationPath -Algorithm SHA256).Hash.ToLowerInvariant()
            RunId = $declaration.RunId; RequestedUnityVersion = '6000.6.0f1'; RequestedBackend = 'IL2CPP'
            ProvenanceStableState = 'passed'; JointCorrectness = @{ State = 'passed' }; RuntimeRecordJoinState = 'passed'
            CorrectnessCorpusConsumptionState = 'passed'; LaunchAttempted = $true; InvocationObserved = $true
            ResultsValidated = $true; ExecutionError = $null; CampaignEligible = $false; AdoptionEligible = $false
            RuntimeJoin = @{ RawRecords = @(@{ Marker = 'BORROWED_PREFLIGHT'; Record = $record }) }; Snapshots = $snapshots
        }
        switch ($shape) {
            'inert' { $windows[5].Count = 0 }
            'preboxed-allocation' { $windows[4].Count = 1 }
            'wrong-request' { $windows[5].Requested = 0 }
            'wrong-index' { $windows[5].ChronologicalIndex = 4 }
            'incomplete' { $windows[5].Completed = $false }
            'invalid-values' { $windows[5].ValuesVerified = $false }
            'wrong-scale' { $windows[6].LogicalObjectBytes = 1 + $windows[6].LogicalObjectBytes }
            'wrong-source' { $record.NativeAllocationSourceSha256 = '0' * 64 }
            'wrong-backend' { $record.NativeAllocationBackend = 1 }
            'wrong-binary' { $snapshots[1].PayloadFiles[0].Sha256 = '0' * 64 }
            'missing-payload' { $report.Snapshots = @($snapshots[0]) }
            'missing-window' { $record.NativeAllocationObservations = @($windows[0..13]) }
            'string-count' { $windows[5].Count = '4096' }
            'string-boolean' { $windows[5].ValuesVerified = 'true' }
            'missing-traversal' { $record.TraversalAllocationObservations = @() }
            'inert-traversal-control' { $record.TraversalAllocationObservations[0].ReferenceCount = 0 }
            'allocating-value-view' { $record.TraversalAllocationObservations[0].ValueCount = 1 }
            'zero-timing' { $record.TraversalTimingObservations[0].ValueTicks = 0 }
            'duplicate-traversal' { $record.TraversalAllocationObservations[1] = $record.TraversalAllocationObservations[0] }
        }
        $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $reportPath
        $failed = $false
        try {
            & (Join-Path $PSScriptRoot '../unity/verify-acceptance.ps1') -Acceptance borrowed -ArtifactsPath $temporary -Commit $declaration.SourceCommit -UnityVersion '6000.6.0f1'
        } catch { $failed = $true }
        if ($failed -ne ($shape -ne 'pass')) { throw "Wrong native allocation qualification for $shape." }
        if ($shape -eq 'pass') {
            $summary = Get-Content -LiteralPath (Join-Path $temporary 'acceptance-summary.json') -Raw | ConvertFrom-Json
            if ($summary.AllocationChannel -cne 'native-managed-object-counts' -or $summary.CampaignEligible -or $summary.AdoptionEligible) {
                throw 'Native counts must remain distinct from allocated bytes and adoption evidence.'
            }
        }
        $checks++
    }
    $verifierRepository = Join-Path $temporary 'wproto-verifier-controls'
    $verifierLibrary = Join-Path $verifierRepository 'scripts/unity/lib'
    $null = New-Item -ItemType Directory -Path $verifierLibrary -Force
    Set-Content -LiteralPath (Join-Path $verifierLibrary 'wproto-player-provenance.ps1') -Value @'
function Assert-WProtoPlayerProvenance {
    param($Repository, $Directory, $Commit, $UnityVersion)
    if ($Commit -cne ('a' * 40) -or $UnityVersion -cne '2021.3.45f1' -or
        [IO.Path]::GetFileName($Directory) -cne 'wproto') { throw 'Verifier delegation changed its identity arguments.' }
    if ((Get-Content -LiteralPath (Join-Path $Directory 'mode.txt') -Raw).Trim() -ceq 'invalid-provenance') {
        throw 'Injected provenance refusal.'
    }
    Set-Content -LiteralPath (Join-Path $Directory 'provenance-called') -Value 'observed'
    $validatedSummary = Join-Path $Directory 'validated-summary.json'
    & node (Join-Path $Repository 'scripts/unity/verify-wproto-corpus.js') `
        --log (Join-Path $Directory 'player.log') --commit $Commit --unity-version $UnityVersion --output $validatedSummary
    if ($LASTEXITCODE -ne 0) { throw 'Injected corpus record refusal.' }
    return Get-Content -LiteralPath $validatedSummary -Raw | ConvertFrom-Json
}
'@
    Set-Content -LiteralPath (Join-Path $verifierRepository 'scripts/unity/verify-wproto-corpus.js') -Value @'
const fs = require("node:fs");
const path = require("node:path");
const args = process.argv.slice(2);
const keys = ["--log", "--commit", "--unity-version", "--output"];
if (args.length !== keys.length * 2 || keys.some((key, index) => args[index * 2] !== key)) {
  throw new Error("Unexpected corpus verifier arguments");
}
const directory = path.dirname(args[1]);
if (args[3] !== "a".repeat(40) || args[5] !== "2021.3.45f1" ||
    args[7] !== path.join(directory, "validated-summary.json") ||
    !fs.existsSync(path.join(directory, "provenance-called"))) {
  throw new Error("Corpus verification lost identity or ran before provenance");
}
fs.appendFileSync(path.join(directory, "node-called"), "observed\n");
const mode = fs.readFileSync(path.join(directory, "mode.txt"), "utf8").trim();
if (mode === "invalid-records") {
  process.exitCode = 3;
} else {
  fs.writeFileSync(args[7], JSON.stringify({ strictRecordValidity: true,
    timingQualified: mode === "below-target", timingTwofoldTarget: false }));
}
'@
    foreach ($mode in @('unstable', 'below-target', 'invalid-provenance', 'invalid-records')) {
        $directory = Join-Path $verifierRepository "$mode/wproto"
        $null = New-Item -ItemType Directory -Path $directory -Force
        Set-Content -LiteralPath (Join-Path $directory 'mode.txt') -Value $mode
        Set-Content -LiteralPath (Join-Path $directory 'player.log') -Value 'fixture log'
        $summaryPath = Join-Path $directory 'corpus-summary.json'
        Set-Content -LiteralPath $summaryPath -Value '{"stale":true}'
        $failed = $false
        try {
            & (Join-Path $PSScriptRoot '../unity/verify-acceptance.ps1') -Acceptance wproto `
                -ArtifactsPath (Split-Path -Parent $directory) -Commit ('a' * 40) `
                -UnityVersion '2021.3.45f1' -Repository $verifierRepository
        } catch { $failed = $true }
        $expectedFailure = $mode -in @('invalid-provenance', 'invalid-records')
        if ($failed -ne $expectedFailure -or
            (Test-Path -LiteralPath (Join-Path $directory 'node-called')) -ne ($mode -ne 'invalid-provenance')) {
            throw "WProto verifier failed its provenance/record delegation policy: $mode"
        }
        if ($mode -ne 'invalid-provenance' -and
            @(Get-Content -LiteralPath (Join-Path $directory 'node-called')).Count -ne 1) {
            throw 'WProto verification must persist its single validated result without a second record pass.'
        }
        if ($expectedFailure) {
            if (Test-Path -LiteralPath $summaryPath) { throw 'Rejected WProto evidence retained a stale successful summary.' }
        } else {
            $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
            if (-not $summary.strictRecordValidity -or $summary.timingTwofoldTarget -or
                $summary.timingQualified -ne ($mode -eq 'below-target')) {
                throw 'WProto baseline verification changed the raw timing decision.'
            }
        }
        $checks++
    }
    Write-Host "$checks orchestration, exact XML and raw evidence integration controls passed."
} finally {
    foreach ($key in $environmentKeys) {
        if ($null -eq $originalEnvironment[$key]) {
            Remove-Item -LiteralPath "Env:$key" -ErrorAction SilentlyContinue
        } else {
            [Environment]::SetEnvironmentVariable($key, $originalEnvironment[$key], 'Process')
        }
    }
    Remove-Item -LiteralPath $temporary -Recurse -Force
}
