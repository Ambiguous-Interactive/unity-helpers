#requires -Version 7.0
[CmdletBinding()]
param([string]$ControlsPath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../unity/lib/il2cpp-conversion-inputs.ps1')
$usesTemporaryDirectory = [string]::IsNullOrWhiteSpace($ControlsPath)
$root = if ($usesTemporaryDirectory) {
    Join-Path ([IO.Path]::GetTempPath()) ('nullable-converter-controls-' + [Guid]::NewGuid().ToString('N'))
} else {
    [IO.Path]::GetFullPath($ControlsPath)
}
if (Test-Path $root) { throw 'Refusing existing parser control directory' }
New-Item -ItemType Directory -Path $root | Out-Null
try {
    $results = [Collections.Generic.List[object]]::new()
    function Require {
        param([bool]$Valid, [string]$Name)
        if (-not $Valid) { throw "Parser control failed: $Name" }
        $results.Add(@{ control = $Name; passed = $true })
    }
    # Dependency recipes are synthetic inputs; the literal floor manifest retains observed package resolution.
    $templateJson = '{"dependencies":{"com.wallstop-studios.unity-helpers":"file:__CANDIDATE_PACKAGE__","com.unity.modules.jsonserialize":"1.0.0"}}'
    $configurationJson = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../../.github/unity-test-project-modules.json') -Raw
    $repositoryInput = 'D:/actions-runner/_work/unity-helpers/unity-helpers'
    $floorManifest = New-NullableConsumerManifest -TemplateJson $templateJson -ConfigurationJson $configurationJson -Repository $repositoryInput -UnityVersion '2021.3.45f1'
    $latestManifest = New-NullableConsumerManifest -TemplateJson $templateJson -ConfigurationJson $configurationJson -Repository $repositoryInput -UnityVersion '6000.6.0f1'
    $floorDependencies = (ConvertFrom-Json -InputObject $floorManifest -AsHashtable)['dependencies']
    $latestDependencies = (ConvertFrom-Json -InputObject $latestManifest -AsHashtable)['dependencies']
    Require ($floorDependencies['com.unity.toolchain.win-x86_64-linux-x86_64'] -ceq '2.0.11' -and $floorDependencies.Count -eq 14) 'dependency-floor-authors-exact-toolchain-before-preparation'
    Require (-not $latestDependencies.Contains('com.unity.toolchain.win-x86_64-linux-x86_64') -and $latestDependencies.Count -eq 13) 'dependency-latest-retains-no-toolchain'
    $observedFloorManifest = @'
{
  "dependencies": {
    "com.unity.toolchain.win-x86_64-linux-x86_64": "2.0.11",
    "com.unity.ugui": "1.0.0",
    "com.wallstop-studios.unity-helpers": "file:D:/actions-runner/_work/unity-helpers/unity-helpers",
    "com.unity.modules.animation": "1.0.0",
    "com.unity.modules.audio": "1.0.0",
    "com.unity.modules.imageconversion": "1.0.0",
    "com.unity.modules.imgui": "1.0.0",
    "com.unity.modules.jsonserialize": "1.0.0",
    "com.unity.modules.particlesystem": "1.0.0",
    "com.unity.modules.physics": "1.0.0",
    "com.unity.modules.physics2d": "1.0.0",
    "com.unity.modules.tilemap": "1.0.0",
    "com.unity.modules.ui": "1.0.0",
    "com.unity.modules.uielements": "1.0.0"
  }
}
'@
    Require (Test-NullableConsumerManifest -ExpectedJson $floorManifest -ActualJson $observedFloorManifest) 'dependency-observed-floor-reordered-manifest-matches-complete-recipe'
    $orderedLatest = ConvertFrom-Json -InputObject $latestManifest -AsHashtable
    $reversedLatest = [ordered]@{ dependencies = [ordered]@{} }
    $keys = @($orderedLatest['dependencies'].Keys)
    [array]::Reverse($keys)
    foreach ($key in $keys) { $reversedLatest.dependencies[$key] = $orderedLatest['dependencies'][$key] }
    Require (Test-NullableConsumerManifest -ExpectedJson $latestManifest -ActualJson (ConvertTo-Json -InputObject $reversedLatest -Depth 10)) 'dependency-latest-object-property-order-only-is-ignored'
    Require (Test-NullableConsumerManifest -ExpectedJson '{"a":{"Y":[null,true,false,1,"x"],"Z":{}},"b":[]}' -ActualJson '{"b":[],"a":{"Z":{},"Y":[null,true,false,1,"x"]}}') 'dependency-nested-object-order-preserves-json-kinds'
    Require (Test-NullableConsumerManifest -ExpectedJson '{"a":1,"A":2}' -ActualJson '{"A":2,"a":1}') 'dependency-case-distinct-object-keys-are-not-collapsed'
    $jsonNegativeControls = @(
        @('root-extra', '{"dependencies":{}}', '{"dependencies":{},"extra":null}'),
        @('root-missing', '{"dependencies":{}}', '{}'),
        @('root-key-case', '{"dependencies":{}}', '{"Dependencies":{}}'),
        @('dependency-extra', '{"dependencies":{"a":"1"}}', '{"dependencies":{"a":"1","b":"1"}}'),
        @('dependency-missing', '{"dependencies":{"a":"1"}}', '{"dependencies":{}}'),
        @('dependency-key-case', '{"dependencies":{"a":"1"}}', '{"dependencies":{"A":"1"}}'),
        @('dependency-version', '{"dependencies":{"a":"2.0.11"}}', '{"dependencies":{"a":"2.0.12"}}'),
        @('string-case', '{"a":"X"}', '{"a":"x"}'),
        @('string-number', '{"a":"1"}', '{"a":1}'),
        @('string-boolean', '{"a":"true"}', '{"a":true}'),
        @('string-null', '{"a":"null"}', '{"a":null}'),
        @('boolean-value', '{"a":true}', '{"a":false}'),
        @('object-array', '{"a":{}}', '{"a":[]}'),
        @('array-order', '{"a":[1,2]}', '{"a":[2,1]}'),
        @('array-length', '{"a":[1]}', '{"a":[1,2]}'),
        @('number-value', '{"a":1}', '{"a":2}'),
        @('duplicate-key', '{"a":1}', '{"a":1,"a":1}'),
        @('nested-extra-key', '{"a":{"b":1}}', '{"a":{"b":1,"c":1}}')
    )
    foreach ($control in $jsonNegativeControls) {
        Require (-not (Test-NullableConsumerManifest -ExpectedJson $control[1] -ActualJson $control[2])) ('dependency-rejects-' + $control[0])
    }
    Require (-not (Test-NullableConsumerManifest -ExpectedJson $floorManifest -ActualJson $observedFloorManifest.Replace($repositoryInput, 'D:/different/package')) ) 'dependency-rejects-changed-package-uri'
    $malformedRejected = $false
    try { Test-NullableConsumerManifest -ExpectedJson '{}' -ActualJson '{' | Out-Null } catch { $malformedRejected = $true }
    Require $malformedRejected 'dependency-malformed-json-fails-closed'
    $producerSource = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../unity/run-nullable-consumer.ps1'))
    $verifierSource = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../unity/verify-nullable-consumer.ps1'))
    Require ($producerSource.IndexOf('New-NullableConsumerManifest', [StringComparison]::Ordinal) -ge 0 -and $producerSource.IndexOf('New-NullableConsumerManifest', [StringComparison]::Ordinal) -lt $producerSource.IndexOf('$prepareStart =', [StringComparison]::Ordinal)) 'dependency-producer-uses-shared-recipe-before-unity-launch'
    Require ($verifierSource.Contains('New-NullableConsumerManifest', [StringComparison]::Ordinal) -and $verifierSource.Contains('Test-NullableConsumerManifest -ExpectedJson $currentDependencies -ActualJson $archivedDependencies', [StringComparison]::Ordinal)) 'dependency-verifier-uses-shared-recipe-and-typed-comparator'
    $nonStringRejected = $false
    try { New-NullableConsumerManifest -TemplateJson $templateJson -ConfigurationJson $configurationJson.Replace('"2.0.11"', '2') -Repository $repositoryInput -UnityVersion '2021.3.45f1' | Out-Null } catch { $nonStringRejected = $true }
    Require $nonStringRejected 'dependency-recipe-rejects-nonstring-version'

    $complete = Join-Path $root 'managed stripped'
    $first = Join-Path $root 'partial-a'
    $second = Join-Path $root 'partial-b'
    New-Item -ItemType Directory -Path $complete, $first, $second | Out-Null
    $required = @('NestedOnly.Consumer.dll', 'WallstopStudios.UnityHelpers.dll', 'protobuf-net.dll', 'protobuf-net.Core.dll')
    foreach ($name in $required) { [IO.File]::WriteAllText((Join-Path $complete $name), "SYNTHETIC PARSER CONTROL $name") }
    foreach ($name in $required[0..1]) { [IO.File]::WriteAllText((Join-Path $first $name), "SYNTHETIC PARSER CONTROL $name") }
    foreach ($name in $required[2..3]) { [IO.File]::WriteAllText((Join-Path $second $name), "SYNTHETIC PARSER CONTROL $name") }
    $direct = @(Get-Il2CppConversionInputs -CommandLines @("`"/Editor/il2cpp/build/deploy/il2cpp.exe`" --convert-to-cpp --directory=`"$complete`"") -Project $root)
    Require ($direct.Count -eq 1 -and @(Get-QualifiedConversionModules -Conversions $direct).Count -eq 1) 'actual-converter-direct-one-complete-input-set'
    $linker = @(Get-Il2CppConversionInputs -CommandLines @("`"/Editor/il2cpp/build/deploy/UnityLinker.exe`" --directory=`"$complete`" --include-link-xml=descriptor.xml") -Project $root)
    Require ($linker.Count -eq 0) 'UnityLinker-in-il2cpp-directory-does-not-qualify'
    $mentioned = @(Get-Il2CppConversionInputs -CommandLines @("UnityLinker.exe --note il2cpp.exe --convert-to-cpp --directory=`"$complete`"") -Project $root)
    Require ($mentioned.Count -eq 0) 'converter-mentioned-in-another-executable-invocation-rejected'
    $bare = @(Get-Il2CppConversionInputs -CommandLines @("--convert-to-cpp --directory=`"$complete`"") -Project $root)
    Require ($bare.Count -eq 0) 'bare-unassociated-response-content-does-not-qualify'
    $notConvert = @(Get-Il2CppConversionInputs -CommandLines @("il2cpp.exe --directory=`"$complete`" --print-command-line") -Project $root)
    Require ($notConvert.Count -eq 0) 'converter-name-without-conversion-does-not-qualify'
    $inner = Join-Path $root 'inner.rsp'
    $outer = Join-Path $root 'outer.rsp'
    [IO.File]::WriteAllText($inner, "--convert-to-cpp --directory=`"$complete`"")
    [IO.File]::WriteAllText($outer, '@inner.rsp')
    $chained = @(Get-Il2CppConversionInputs -CommandLines @("il2cpp.exe @`"$outer`"") -Project $root)
    Require ($chained.Count -eq 1 -and $chained[0].responseChain.Count -eq 2 -and @(Get-QualifiedConversionModules -Conversions $chained).Count -eq 1) 'actual-converter-associated-nested-response-chain'
    $partials = @(Get-Il2CppConversionInputs -CommandLines @("il2cpp.exe --convert-to-cpp --directory=`"$first`"", "il2cpp.exe --convert-to-cpp --directory=`"$second`"") -Project $root)
    Require ($partials.Count -eq 2 -and @(Get-QualifiedConversionModules -Conversions $partials).Count -eq 0) 'four-module-union-across-separate-invocations-rejected'
    $threw = $false
    try { Get-Il2CppConversionInputs -CommandLines @("il2cpp.exe --convert-to-cpp --directory=`"$first`" --directory=`"$second`"") -Project $root | Out-Null } catch { $threw = $true }
    Require $threw 'multiple-different-input-directories-fail-closed'
    $threw = $false
    try { Get-Il2CppConversionInputs -CommandLines @('il2cpp.exe @missing.rsp') -Project $root | Out-Null } catch { $threw = $true }
    Require $threw 'missing-actual-response-chain-fails-closed'
    $threw = $false
    try { Get-Il2CppConversionInputs -CommandLines @('il2cpp.exe --convert-to-cpp --directory=missing') -Project $root | Out-Null } catch { $threw = $true }
    Require $threw 'missing-actual-input-directory-fails-closed'

    function Require-PlayerRejection {
        param([scriptblock]$Action, [string]$Message, [string]$Name)
        $caught = $null
        try { & $Action | Out-Null } catch { $caught = $_.Exception.Message }
        Require ($null -ne $caught -and $caught.Contains($Message, [StringComparison]::Ordinal)) $Name
    }
    $player = Join-Path $root 'player'
    $metadata = 'IndependentConsumer_Data/il2cpp_data/Metadata/global-metadata.dat'
    New-Item -ItemType Directory -Path (Join-Path $player 'IndependentConsumer_Data/il2cpp_data/Metadata') -Force | Out-Null
    $playerRequired = @('IndependentConsumer.exe', 'GameAssembly.dll', 'UnityPlayer.dll', $metadata)
    foreach ($name in $playerRequired + @('settings.txt', 'inputs.txt', 'build-report.txt')) {
        [IO.File]::WriteAllText((Join-Path $player $name), "SYNTHETIC FILE CONTROL $name")
    }
    $manifest = Join-Path $root 'player-tree.json'
    $manifestHash = New-Il2CppPlayerManifest -PlayerRoot $player -ManifestPath $manifest
    $baselineManifest = [IO.File]::ReadAllBytes($manifest)
    Assert-Il2CppPlayerManifest -PlayerRoot $player -ManifestPath $manifest -ExpectedSHA256 $manifestHash
    $inventory = @(Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json)
    Require ($inventory.Count -eq 7 -and @($inventory | Where-Object { $_.path -eq 'settings.txt' }).Count -eq 1) 'complete-player-includes-settings-inputs-report-and-native-files'
    foreach ($name in $playerRequired) {
        $path = Join-Path $player $name
        $original = [IO.File]::ReadAllBytes($path)
        [IO.File]::WriteAllText($path, 'CHANGED NATIVE PLAYER CONTENT')
        Require-PlayerRejection { Assert-Il2CppPlayerManifest -PlayerRoot $player -ManifestPath $manifest -ExpectedSHA256 $manifestHash } 'Actual player file differs:' "player-content-mutation-rejected-$name"
        [IO.File]::WriteAllBytes($path, $original)
        Remove-Item -LiteralPath $path -Force
        Require-PlayerRejection { Assert-Il2CppPlayerManifest -PlayerRoot $player -ManifestPath $manifest -ExpectedSHA256 $manifestHash } 'Required actual player file missing:' "player-required-removal-rejected-$name"
        Require-PlayerRejection { Get-Il2CppPlayerInventory -PlayerRoot $player } 'Required actual player file missing:' "player-baseline-required-file-absent-rejected-$name"
        [IO.File]::WriteAllBytes($path, $original)
    }
    foreach ($name in @('added.bin', '.hidden-native-file')) {
        $path = Join-Path $player $name
        [IO.File]::WriteAllText($path, 'ADDED PLAYER INPUT')
        Require-PlayerRejection { Assert-Il2CppPlayerManifest -PlayerRoot $player -ManifestPath $manifest -ExpectedSHA256 $manifestHash } 'Actual player file inventory differs' "player-added-file-rejected-$name"
        Remove-Item -LiteralPath $path -Force
    }
    Move-Item -LiteralPath (Join-Path $player 'inputs.txt') -Destination (Join-Path $player 'renamed-inputs.txt')
    Require-PlayerRejection { Assert-Il2CppPlayerManifest -PlayerRoot $player -ManifestPath $manifest -ExpectedSHA256 $manifestHash } 'Actual player file differs:' 'player-same-count-rename-rejected'
    Move-Item -LiteralPath (Join-Path $player 'renamed-inputs.txt') -Destination (Join-Path $player 'inputs.txt')
    [IO.File]::WriteAllText($manifest, '[]')
    Require-PlayerRejection { Assert-Il2CppPlayerManifest -PlayerRoot $player -ManifestPath $manifest -ExpectedSHA256 $manifestHash } 'Actual player manifest hash differs' 'player-manifest-mutation-rejected'
    [IO.File]::WriteAllBytes($manifest, $baselineManifest)
    Require-PlayerRejection { New-Il2CppPlayerManifest -PlayerRoot $player -ManifestPath $manifest } 'Refusing existing actual player manifest' 'player-manifest-written-once'
    Require-PlayerRejection { New-Il2CppPlayerManifest -PlayerRoot $player -ManifestPath (Join-Path $player 'self.json') } 'Player manifest must be outside' 'player-manifest-self-reference-rejected'
    foreach ($badPath in @('../escape', '/absolute', 'data/../escape', 'data\escape', 'data//escape', "data`nescape", "data`rescape")) {
        @(@{ path = $badPath; sha256 = ('0' * 64) }) | ConvertTo-Json | Set-Content -LiteralPath $manifest
        $badHash = (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash.ToLowerInvariant()
        Require-PlayerRejection { Assert-Il2CppPlayerManifest -PlayerRoot $player -ManifestPath $manifest -ExpectedSHA256 $badHash } 'Malformed or duplicated actual player manifest entry' "player-malformed-path-rejected-$badPath"
    }
    @($inventory[0], $inventory[0]) | ConvertTo-Json | Set-Content -LiteralPath $manifest
    $badHash = (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash.ToLowerInvariant()
    Require-PlayerRejection { Assert-Il2CppPlayerManifest -PlayerRoot $player -ManifestPath $manifest -ExpectedSHA256 $badHash } 'Malformed or duplicated actual player manifest entry' 'player-duplicate-manifest-path-rejected'
    [IO.File]::WriteAllBytes($manifest, $baselineManifest)
    Assert-Il2CppPlayerManifest -PlayerRoot $player -ManifestPath $manifest -ExpectedSHA256 $manifestHash
    Require $true 'restored-player-tree-accepted'


    $assemblyArguments = @($required | ForEach-Object { '--assembly="' + (Join-Path $complete $_) + '"' })
    $assemblyCommand = 'il2cpp.exe --convert-to-cpp ' + ($assemblyArguments -join ' ')
    $assemblyInputs = @(Get-Il2CppConversionInputs -CommandLines @($assemblyCommand) -Project $root)
    Require ($assemblyInputs.Count -eq 1 -and $assemblyInputs[0].inputMode -eq 'assemblies' -and @(Get-QualifiedConversionModules -Conversions $assemblyInputs)[0].modules.Count -eq 4) 'floor-inline-explicit-assembly-set-qualified'
    $archiveInline = @(Get-Il2CppConversionInputs -CommandLines @($assemblyCommand) -Project $root -ArchivedRoot $root -ResponseRecords @())
    Require ($archiveInline.Count -eq 1 -and $archiveInline[0].explicitInputPaths.Count -eq 4) 'inline-command-archival-replay-without-invented-response'
    $incompleteCommand = 'il2cpp.exe --convert-to-cpp ' + ($assemblyArguments[0..2] -join ' ')
    $incomplete = @(Get-Il2CppConversionInputs -CommandLines @($incompleteCommand) -Project $root)
    Require (@(Get-QualifiedConversionModules -Conversions $incomplete).Count -eq 0) 'nearby-undeclared-fourth-assembly-cannot-qualify'
    Require-PlayerRejection { Get-Il2CppConversionInputs -CommandLines @($assemblyCommand + ' --assembly="' + (Join-Path $first $required[0]) + '"') -Project $root } 'one unambiguous managed input set' 'mixed-explicit-assembly-parent-directories-rejected'
    Require-PlayerRejection { Get-Il2CppConversionInputs -CommandLines @($assemblyCommand + ' ' + $assemblyArguments[0]) -Project $root } 'Duplicated converter assembly argument' 'duplicate-explicit-assembly-rejected'
    Require-PlayerRejection { Get-Il2CppConversionInputs -CommandLines @('il2cpp.exe --convert-to-cpp --assembly=missing.dll') -Project $root } 'Actual converter assembly input missing:' 'missing-explicit-assembly-rejected'
    $firstAssemblyCommand = 'il2cpp.exe --convert-to-cpp ' + ($assemblyArguments[0..1] -join ' ')
    $secondAssemblyCommand = 'il2cpp.exe --convert-to-cpp ' + ($assemblyArguments[2..3] -join ' ')
    $splitAssemblies = @(Get-Il2CppConversionInputs -CommandLines @($firstAssemblyCommand, $secondAssemblyCommand) -Project $root)
    Require ($splitAssemblies.Count -eq 2 -and @(Get-QualifiedConversionModules -Conversions $splitAssemblies).Count -eq 0) 'same-parent-assembly-union-across-invocations-rejected'
    Require-PlayerRejection { Get-Il2CppConversionInputs -CommandLines @($assemblyCommand + ' --directory="' + $first + '"') -Project $root } 'Mixed converter directory and assembly input styles' 'explicit-assemblies-conflicting-directory-rejected'
    Require-PlayerRejection { Get-Il2CppConversionInputs -CommandLines @($incompleteCommand + ' --directory="' + $complete + '"') -Project $root } 'Mixed converter directory and assembly input styles' 'same-parent-mixed-directory-and-assembly-input-styles-rejected'
    Require-PlayerRejection { Get-Il2CppConversionInputs -CommandLines @('il2cpp.exe --convert-to-cpp --assembly') -Project $root } 'argument has no value' 'missing-assembly-argument-value-rejected'
    $xmlFirst = Join-Path $root 'consumer roots.xml'
    $xmlSecond = Join-Path $root 'generated roots.xml'
    [IO.File]::WriteAllText($xmlFirst, '<linker><assembly fullname="NestedOnly.Consumer"><type fullname="NestedOnlyConsumer.PresenceDoc" preserve="all" /></assembly></linker>')
    [IO.File]::WriteAllText($xmlSecond, '<linker />')
    $xmlArguments = '--include-link-xml="' + $xmlFirst + '" --include-link-xml "' + $xmlSecond + '"'
    $linkerCommand = 'UnityLinker.exe ' + $xmlArguments
    $inlineLinker = @(Get-UnityLinkerInputs -CommandLines @($linkerCommand) -Project $root)
    Require ($inlineLinker.Count -eq 1 -and $inlineLinker[0].xmlInputPaths.Count -eq 2 -and $inlineLinker[0].responseChain.Count -eq 0) 'floor-inline-linker-xml-without-response-qualified'
    $linkerInner = Join-Path $root 'linker-inner.rsp'
    $linkerOuter = Join-Path $root 'linker-outer.rsp'
    [IO.File]::WriteAllText($linkerInner, $xmlArguments)
    [IO.File]::WriteAllText($linkerOuter, '@linker-inner.rsp')
    $rspLinkerCommand = 'UnityLinker.exe @"' + $linkerOuter + '"'
    $rspLinker = @(Get-UnityLinkerInputs -CommandLines @($rspLinkerCommand) -Project $root)
    Require ($rspLinker.Count -eq 1 -and $rspLinker[0].xmlInputPaths.Count -eq 2 -and $rspLinker[0].responseChain.Count -eq 2) 'actual-linker-associated-response-chain-qualified'
    Require (@(Get-UnityLinkerInputs -CommandLines @('Other.exe --note ' + $linkerCommand) -Project $root).Count -eq 0) 'linker-mentioned-in-other-invocation-rejected'
    Require (@(Get-UnityLinkerInputs -CommandLines @($xmlArguments) -Project $root).Count -eq 0) 'bare-linker-response-text-not-actual-invocation'
    Require-PlayerRejection { Get-UnityLinkerInputs -CommandLines @('UnityLinker.exe --include-link-xml=missing.xml') -Project $root } 'Actual linker XML input missing:' 'missing-actual-linker-xml-rejected'
    Require-PlayerRejection { Get-UnityLinkerInputs -CommandLines @('UnityLinker.exe --include-link-xml') -Project $root } 'Linker XML argument has no value' 'missing-linker-xml-argument-value-rejected'
    Require-PlayerRejection { Get-UnityLinkerInputs -CommandLines @($linkerCommand + ' --include-link-xml="' + $xmlFirst + '"') -Project $root } 'Duplicated linker XML argument' 'duplicate-linker-xml-rejected'
    $responseArchive = Join-Path $root 'response-archive'
    New-Item -ItemType Directory -Path $responseArchive | Out-Null
    foreach ($response in $rspLinker[0].responseChain) { Copy-Item -LiteralPath $response.path -Destination (Join-Path $responseArchive "$($response.sha256).linker.rsp") }
    Remove-Item -LiteralPath $linkerInner, $linkerOuter, $xmlFirst, $xmlSecond
    $replayedLinker = @(Get-UnityLinkerInputs -CommandLines @($rspLinkerCommand) -Project $root -ArchivedRoot $responseArchive -ResponseRecords $rspLinker[0].responseChain)
    Require ($replayedLinker.Count -eq 1 -and $replayedLinker[0].xmlInputPaths.Count -eq 2) 'archived-response-replay-does-not-require-original-machine-files'
    $responsePath = Join-Path $responseArchive "$($rspLinker[0].responseChain[0].sha256).linker.rsp"
    $responseBytes = [IO.File]::ReadAllBytes($responsePath)
    [IO.File]::WriteAllText($responsePath, '--include-link-xml=changed.xml')
    Require-PlayerRejection { Get-UnityLinkerInputs -CommandLines @($rspLinkerCommand) -Project $root -ArchivedRoot $responseArchive -ResponseRecords $rspLinker[0].responseChain } 'Archived response content hash differs' 'archived-response-content-mutation-rejected'
    [IO.File]::WriteAllBytes($responsePath, $responseBytes)
    $unreferenced = @($rspLinker[0].responseChain) + @(@{ path = (Join-Path $root 'unrelated.rsp'); sha256 = ('0' * 64) })
    Require-PlayerRejection { Get-UnityLinkerInputs -CommandLines @($rspLinkerCommand) -Project $root -ArchivedRoot $responseArchive -ResponseRecords $unreferenced } 'Archived response chain inventory differs' 'unreferenced-response-record-rejected'
    $wrongAssociation = @($rspLinker[0].responseChain[1], $rspLinker[0].responseChain[0])
    Require-PlayerRejection { Get-UnityLinkerInputs -CommandLines @($rspLinkerCommand) -Project $root -ArchivedRoot $responseArchive -ResponseRecords $wrongAssociation } 'Archived response chain association differs' 'reordered-response-association-rejected'
    foreach ($response in $chained[0].responseChain) { Copy-Item -LiteralPath $response.path -Destination (Join-Path $responseArchive "$($response.sha256).converter.rsp") }
    Remove-Item -LiteralPath $inner, $outer
    $archivedConverter = @(Get-Il2CppConversionInputs -CommandLines @('il2cpp.exe @"' + $outer + '"') -Project $root -ArchivedRoot $responseArchive -ResponseRecords $chained[0].responseChain)
    Require ($archivedConverter.Count -eq 1 -and $archivedConverter[0].inputMode -eq 'directory' -and $archivedConverter[0].responseChain.Count -eq 2) 'archived-converter-response-chain-retains-original-directory-path'
    foreach ($prefix in @('echo ', 'python script.py ')) {
        Require (@(Get-Il2CppConversionInputs -CommandLines @($prefix + $assemblyCommand) -Project $root).Count -eq 0) ('converter-mentioned-after-' + $prefix.Trim().Replace(' ', '-') + '-rejected')
        Require (@(Get-UnityLinkerInputs -CommandLines @($prefix + $linkerCommand) -Project $root).Count -eq 0) ('linker-mentioned-after-' + $prefix.Trim().Replace(' ', '-') + '-rejected')
    }
    $lifecycle = Join-Path $root 'linker-lifecycle'
    New-Item -ItemType Directory -Path $lifecycle | Out-Null
    $source = Join-Path $root 'TypesInScenes.xml'
    [IO.File]::WriteAllText($source, '<linker />')
    $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
    $snapshot = Join-Path $lifecycle "$sourceHash.xml"
    Copy-Item -LiteralPath $source -Destination $snapshot
    $sourceManifest = Join-Path $lifecycle 'source-paths.SHA256SUMS'
    $originalRows = "$sourceHash  $source"
    [IO.File]::WriteAllText($sourceManifest, $originalRows)
    $captured = @(Get-LinkerLifecycleInputs -CaptureRoot $lifecycle)
    Require ($captured.Count -eq 1 -and (Get-LinkerXmlContentPath -Path $source -CapturedXmlInputs $captured) -eq $snapshot) 'live-generated-linker-input-matches-lifecycle-copy'
    Remove-Item -LiteralPath $source
    $capturedLinker = @(Get-UnityLinkerInputs -CommandLines @("UnityLinker.exe --include-link-xml=`"$source`"") -Project $root -CapturedXmlInputs $captured)
    Require ($capturedLinker.Count -eq 1 -and $capturedLinker[0].xmlInputPaths[0] -eq $source) 'deleted-generated-linker-input-retains-original-command-path'
    Require-PlayerRejection { Get-UnityLinkerInputs -CommandLines @("UnityLinker.exe --include-link-xml=`"$source`"") -Project $root } 'Actual linker XML input missing:' 'deleted-generated-input-without-snapshot-rejected'
    $differentSource = Join-Path $first 'TypesInScenes.xml'
    Require-PlayerRejection { Get-LinkerXmlContentPath -Path $differentSource -CapturedXmlInputs $captured } 'Actual linker XML input missing:' 'same-basename-is-not-lifecycle-association'
    [IO.File]::WriteAllText($source, '<linker changed="true" />')
    Require-PlayerRejection { Get-LinkerXmlContentPath -Path $source -CapturedXmlInputs $captured } 'source changed after lifecycle capture' 'changed-live-source-rejected'
    Remove-Item -LiteralPath $source
    [IO.File]::WriteAllText($snapshot, 'corrupt captured content')
    Require-PlayerRejection { Get-LinkerLifecycleInputs -CaptureRoot $lifecycle } 'captured content hash differs' 'corrupt-lifecycle-content-rejected'
    [IO.File]::WriteAllText($snapshot, '<linker />')
    [IO.File]::WriteAllText($sourceManifest, $originalRows + "`n" + $originalRows)
    Require-PlayerRejection { Get-LinkerLifecycleInputs -CaptureRoot $lifecycle } 'Duplicated or nonabsolute' 'duplicate-lifecycle-source-rejected'
    [IO.File]::WriteAllText($sourceManifest, "$sourceHash  relative.xml")
    Require-PlayerRejection { Get-LinkerLifecycleInputs -CaptureRoot $lifecycle } 'Duplicated or nonabsolute' 'relative-lifecycle-source-rejected'
    [IO.File]::WriteAllText($sourceManifest, '')
    Require-PlayerRejection { Get-LinkerLifecycleInputs -CaptureRoot $lifecycle } 'Empty linker lifecycle' 'empty-lifecycle-manifest-rejected'
    [IO.File]::WriteAllText($sourceManifest, $originalRows)
    [IO.File]::WriteAllText((Join-Path $lifecycle 'unreferenced.xml'), '<linker />')
    Require-PlayerRejection { Get-LinkerLifecycleInputs -CaptureRoot $lifecycle } 'snapshot file inventory differs' 'unreferenced-lifecycle-file-rejected'
    Remove-Item -LiteralPath (Join-Path $lifecycle 'unreferenced.xml')
    Require-PlayerRejection { Get-LinkerXmlContentPath -Path $source -CapturedXmlInputs @($captured[0], $captured[0]) } 'Duplicated linker lifecycle source association' 'duplicate-resolver-association-rejected'
    Require ((Get-LinkerXmlContentPath -Path $source -CapturedXmlInputs @(Get-LinkerLifecycleInputs -CaptureRoot $lifecycle)) -eq $snapshot) 'restored-lifecycle-snapshot-accepted'

    $optionalControls = 0
    $link = Join-Path $player 'linked-native.bin'
    $linkCreated = $false
    try { New-Item -ItemType SymbolicLink -Path $link -Target (Join-Path $player 'GameAssembly.dll') -ErrorAction Stop | Out-Null; $linkCreated = $true } catch { Write-Output 'Symbolic-link creation unavailable on this platform; qualified control only.' }
    if ($linkCreated) {
        try {
            Require-PlayerRejection { Assert-Il2CppPlayerManifest -PlayerRoot $player -ManifestPath $manifest -ExpectedSHA256 $manifestHash } 'Player inventory cannot contain linked paths' 'actual-filesystem-player-symlink-rejected'
            ++$optionalControls
        } finally { Remove-Item -LiteralPath $link -Force }
    }
    $captureLink = Join-Path $root 'linked-lifecycle-root'
    $captureLinkCreated = $false
    try { New-Item -ItemType SymbolicLink -Path $captureLink -Target $lifecycle -ErrorAction Stop | Out-Null; $captureLinkCreated = $true } catch { Write-Output 'Lifecycle-root symlink creation unavailable; qualified control only.' }
    if ($captureLinkCreated) {
        try {
            Require-PlayerRejection { Get-LinkerLifecycleInputs -CaptureRoot $captureLink } 'Linked linker lifecycle snapshot root' 'actual-filesystem-lifecycle-root-symlink-rejected'
            ++$optionalControls
        } finally { Remove-Item -LiteralPath $captureLink -Force }
    }
    $linkedSnapshot = Join-Path $lifecycle "$sourceHash.xml"
    $snapshotOriginal = [IO.File]::ReadAllBytes($linkedSnapshot)
    Remove-Item -LiteralPath $linkedSnapshot
    $linkedSnapshotCreated = $false
    [IO.File]::WriteAllBytes($source, $snapshotOriginal)
    try { New-Item -ItemType SymbolicLink -Path $linkedSnapshot -Target $source -ErrorAction Stop | Out-Null; $linkedSnapshotCreated = $true } catch { Write-Output 'Lifecycle-content symlink creation unavailable; qualified control only.' }
    try {
        if ($linkedSnapshotCreated) {
            Require-PlayerRejection { Get-LinkerLifecycleInputs -CaptureRoot $lifecycle } 'Unexpected or linked linker lifecycle snapshot file' 'actual-filesystem-lifecycle-content-symlink-rejected'
            ++$optionalControls
        }
    } finally {
        if ($linkedSnapshotCreated) { Remove-Item -LiteralPath $linkedSnapshot -Force }
        [IO.File]::WriteAllBytes($linkedSnapshot, $snapshotOriginal)
    }
    if ($IsLinux) {
        $fake = Join-Path $root 'fake-unity'
        New-Item -ItemType Directory -Path $fake | Out-Null
        $UnityEditorPath = Join-Path $fake 'Unity'
        [IO.File]::WriteAllText($UnityEditorPath, "#!/bin/sh`nexec pwsh -NoProfile -File `"`$(dirname `"`$0`")/console.ps1`" `"`$@`"`n")
        & chmod +x $UnityEditorPath
        if ($LASTEXITCODE -ne 0) { throw 'Cannot make synthetic console producer executable' }
        [IO.File]::WriteAllText((Join-Path $fake 'console.ps1'), "[Console]::Out.Write('X' * 131072); [Console]::Error.Write('Y' * 131072); [IO.File]::WriteAllText(`$env:CONSUMER_OUTPUT,'SYNTHETIC PLAYER'); if (`$env:NULLABLE_CONTROL_INHERITEDPIPE -eq '1') { `$child = [Diagnostics.Process]::Start('sleep','120'); [IO.File]::WriteAllText(`$env:NULLABLE_CONTROL_CHILD_PID,`$child.Id.ToString()); `$child.Dispose() }; if (`$env:NULLABLE_CONTROL_NONZERO -eq '1') { exit 7 }; exit 0")
        $producer = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../unity/run-nullable-consumer.ps1'))
        $begin = $producer.IndexOf('$oldOutput = $env:CONSUMER_OUTPUT', [StringComparison]::Ordinal)
        $end = $producer.IndexOf('foreach ($entry in $packageHashes.GetEnumerator())', $begin, [StringComparison]::Ordinal)
        if ($begin -lt 0 -or $end -le $begin) { throw 'Actual producer build-phase source absent' }
        $buildPhase = [ScriptBlock]::Create($producer.Substring($begin, $end - $begin))
        $project = $root
        $oldNonzero = $env:NULLABLE_CONTROL_NONZERO
        try {
            foreach ($nonzero in @('0', '1')) {
                $env:NULLABLE_CONTROL_NONZERO = $nonzero
                $artifactRoot = Join-Path $fake $nonzero
                New-Item -ItemType Directory -Path $artifactRoot | Out-Null
                $executable = Join-Path $artifactRoot 'IndependentConsumer.exe'
                $editorLog = Join-Path $artifactRoot 'editor-build.log'
                $failure = $null
                try { . $buildPhase } catch { $failure = $_.Exception.Message }
                Require (([IO.File]::ReadAllText($editorLog)).Length -eq 131072 -and ([IO.File]::ReadAllText((Join-Path $artifactRoot 'editor-build.stderr.log'))).Length -eq 131072 -and (($nonzero -eq '0' -and $null -eq $failure) -or ($nonzero -eq '1' -and $failure -eq 'Independent build failed or player missing'))) "actual-producer-drains-both-large-pipes-and-retains-exit-$nonzero-logs"
                ++$optionalControls
            }
            $oldInherited = $env:NULLABLE_CONTROL_INHERITEDPIPE
            $oldChildPath = $env:NULLABLE_CONTROL_CHILD_PID
            $childPath = Join-Path $fake 'inherited-child.pid'
            try {
                $env:NULLABLE_CONTROL_NONZERO = '0'
                $env:NULLABLE_CONTROL_INHERITEDPIPE = '1'
                $env:NULLABLE_CONTROL_CHILD_PID = $childPath
                $artifactRoot = Join-Path $fake 'inherited'
                New-Item -ItemType Directory -Path $artifactRoot | Out-Null
                $executable = Join-Path $artifactRoot 'IndependentConsumer.exe'
                $editorLog = Join-Path $artifactRoot 'editor-build.log'
                $failure = $null
                try { . $buildPhase } catch { $failure = $_.Exception.Message }
                Require ($failure -eq 'Independent build stdout/stderr drain timed out; partial logs retained' -and ([IO.File]::ReadAllText($editorLog)).Length -eq 131072 -and ([IO.File]::ReadAllText((Join-Path $artifactRoot 'editor-build.stderr.log'))).Length -eq 131072) 'actual-producer-inherited-pipes-fail-with-bounded-drain-and-preserved-partial-logs'
                ++$optionalControls
            } finally {
                if (Test-Path -LiteralPath $childPath -PathType Leaf) {
                    $childProcess = [Diagnostics.Process]::GetProcessById([int]([IO.File]::ReadAllText($childPath)))
                    try { if (-not $childProcess.HasExited) { $childProcess.Kill($true); $childProcess.WaitForExit() } } finally { $childProcess.Dispose() }
                }
                $env:NULLABLE_CONTROL_INHERITEDPIPE = $oldInherited
                $env:NULLABLE_CONTROL_CHILD_PID = $oldChildPath
            }
        } finally { $env:NULLABLE_CONTROL_NONZERO = $oldNonzero }
    }

    $results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $root 'parser-results.json')
    if ($results.Count -ne (105 + $optionalControls)) { throw 'Parser controls silently omitted cases' }
    "Original parser10, player provenance28, inline/archive26, lifecycle12, dependency29, optional filesystem/process$optionalControls controls passed; synthetic files are not Unity evidence."

} finally {
    if ($usesTemporaryDirectory) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
