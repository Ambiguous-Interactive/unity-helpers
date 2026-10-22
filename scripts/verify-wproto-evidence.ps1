#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [ValidateSet('catalog', 'results')][string]$Mode = 'catalog',
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$ReceiptPath,
    [string]$ExpectedRunId
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'unity/lib/test-result-evidence.ps1')
. (Join-Path $PSScriptRoot 'unity/lib/catastrophic-patterns.ps1')

function Get-WProtoFileHash {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
}

function Assert-WProtoString {
    param($Value, [string]$Label)
    if ($Value -isnot [string] -or [string]::IsNullOrWhiteSpace($Value)) { throw "Expected nonempty string: $Label" }
}

function Assert-WProtoInteger {
    param($Value, [string]$Label)
    if ($Value -isnot [int] -and $Value -isnot [long]) { throw "Expected integer: $Label" }
}

function Assert-WProtoObject {
    param($Value, [string]$Label)
    if ($Value -isnot [Collections.IDictionary]) { throw "Expected object: $Label" }
}

function Assert-WProtoArray {
    param($Value, [string]$Label)
    if ($null -eq $Value -or -not $Value.GetType().IsArray) { throw "Expected array: $Label" }
}

function Get-WProtoBinaryInventory {
    param([string]$Root, [string]$BinaryRoot)
    $paths = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
    foreach ($file in Get-ChildItem -LiteralPath $BinaryRoot -File -Recurse -ErrorAction Stop) {
        if ($file.Name.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase) -or $file.Name.EndsWith('.deps.json', [StringComparison]::OrdinalIgnoreCase) -or $file.Name.EndsWith('.runtimeconfig.json', [StringComparison]::OrdinalIgnoreCase)) { [void]$paths.Add($file.FullName) }
    }
    $analyzers = @('WallstopStudios.UnityHelpers.Proto.Generator.dll', 'WallstopStudios.UnityHelpers.Analyzers.dll', 'WallstopStudios.UnityHelpers.OdinMigration.Generator.dll')
    foreach ($name in $analyzers) { [void]$paths.Add([IO.Path]::GetFullPath((Join-Path $Root "Runtime/Analyzers/$name"))) }
    foreach ($path in $paths) {
        $name = [IO.Path]::GetFileName($path)
        $role = if ($path -ceq (Join-Path $BinaryRoot 'WallstopStudios.UnityHelpers.Proto.Generator.Tests.dll')) { 'test-assembly' }
        elseif ($name -cin $analyzers -and [IO.Path]::GetDirectoryName($path) -ceq (Join-Path $Root 'Runtime/Analyzers')) { 'shipped-analyzer' }
        elseif ($name.EndsWith('.deps.json', [StringComparison]::Ordinal)) { 'output-deps' }
        elseif ($name.EndsWith('.runtimeconfig.json', [StringComparison]::Ordinal)) { 'output-runtimeconfig' }
        else { 'output-dependency' }
        [ordered]@{ path = $path; sha256 = Get-WProtoFileHash $path; role = $role }
    }
}

function Assert-WProtoProcess {
    param($Process, [string]$Label)
    Assert-WProtoObject $Process $Label
    foreach ($field in @('exitCode', 'pid')) { Assert-WProtoInteger $Process[$field] "$Label.$field" }
    foreach ($field in @('startedUtc', 'endedUtc', 'observationScope', 'timestampSource')) { Assert-WProtoString $Process[$field] "$Label.$field" }
    if ($Process.completed -isnot [bool] -or -not $Process.completed -or $Process.exitCode -ne 0 -or $Process.timedOut -isnot [bool] -or $Process.timedOut -or $null -ne $Process.terminationReason -or $null -ne $Process.exception -or $Process.pid -le 0 -or $Process.observationScope -cne 'dotnet-parent-exit-and-captured-output') { throw "$Label did not complete cleanly." }
    if ($Process.timestampSource -cne 'parent-utc-launch-and-wait-bounds') { throw "Unexpected $Label timestamp source." }
    if ([DateTimeOffset]::Parse($Process.endedUtc) -le [DateTimeOffset]::Parse($Process.startedUtc)) { throw "Invalid $Label interval." }
}

function Read-WProtoJson {
    param([string]$Path)
    $text = [IO.File]::ReadAllText($Path)
    $document = [System.Text.Json.JsonDocument]::Parse($text)
    try {
        Assert-WProtoJsonMembers $document.RootElement
        return Convert-WProtoJsonElement $document.RootElement
    } finally { $document.Dispose() }
}

function Convert-WProtoJsonElement {
    param([System.Text.Json.JsonElement]$Element)
    switch ($Element.ValueKind) {
        'Object' {
            $object = [ordered]@{}
            foreach ($property in $Element.EnumerateObject()) { $object[$property.Name] = Convert-WProtoJsonElement $property.Value }
            return $object
        }
        'Array' {
            $items = [Collections.Generic.List[object]]::new()
            foreach ($item in $Element.EnumerateArray()) { $items.Add((Convert-WProtoJsonElement $item)) }
            return ,$items.ToArray()
        }
        'String' { return $Element.GetString() }
        'Number' {
            [long]$integer = 0
            if ($Element.TryGetInt64([ref]$integer)) { return $integer }
            return $Element.GetDouble()
        }
        'True' { return $true }
        'False' { return $false }
        'Null' { return $null }
        default { throw 'Unsupported JSON value.' }
    }
}

function Assert-WProtoJsonMembers {
    param([System.Text.Json.JsonElement]$Element)
    if ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($property in $Element.EnumerateObject()) {
            if (-not $names.Add($property.Name)) { throw "Duplicate JSON member: $($property.Name)" }
            Assert-WProtoJsonMembers $property.Value
        }
    } elseif ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
        foreach ($item in $Element.EnumerateArray()) { Assert-WProtoJsonMembers $item }
    }
}

function Resolve-WProtoSourcePath {
    param([string]$Root, [string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or [IO.Path]::IsPathRooted($Path)) { throw 'Expected a repository-relative source path.' }
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $full = [IO.Path]::GetFullPath((Join-Path $rootPath $Path))
    if (-not $full.StartsWith($rootPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) { throw 'Source path leaves the repository.' }
    return $full
}

function Get-WProtoSourceInventory {
    param([string]$Root)
    $paths = (& git -C $Root ls-files --cached --others --exclude-standard -z -- .editorconfig .gitignore .gitattributes global.json NuGet.Config nuget.config Directory.Build.props Directory.Build.targets .github/workflows/wproto-generator.yml scripts/verify-wproto-evidence.ps1 scripts/run-wproto-evidence.ps1 scripts/unity/lib/test-result-evidence.ps1 scripts/unity/lib/catastrophic-patterns.ps1) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate WProto build inputs.' }
    $unique = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
    foreach ($path in $paths.Split([char]0)) {
        if (-not [string]::IsNullOrEmpty($path)) { [void]$unique.Add($path) }
    }
    $pending = [Collections.Generic.Stack[string]]::new()
    foreach ($tree in @('Runtime', 'Editor', 'Tests', 'Generator~')) {
        $directory = Join-Path $Root $tree
        if (Test-Path -LiteralPath $directory -PathType Container) { $pending.Push($directory) }
    }
    while ($pending.Count -gt 0) {
        foreach ($entry in Get-ChildItem -LiteralPath $pending.Pop() -Force -ErrorAction Stop) {
            if ($entry.PSIsContainer -and $entry.Name -cin @('bin', 'obj', '.git')) { continue }
            if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Source census cannot follow an unrecorded link.' }
            if ($entry.PSIsContainer) { $pending.Push($entry.FullName) }
            else { [void]$unique.Add([IO.Path]::GetRelativePath($Root, $entry.FullName).Replace('\', '/')) }
        }
    }
    if ($unique.Count -eq 0 -or -not $unique.Contains('Runtime/Core/Serialization/WallstopProto/WProtoReader.cs')) { throw 'Missing WProto source inputs.' }
    foreach ($path in $unique) {
        [ordered]@{ path = $path; sha256 = Get-WProtoFileHash (Resolve-WProtoSourcePath $Root $path) }
    }
}

function Assert-WProtoUniqueIds {
    param($Items, [string]$Property, [string]$Label)
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    if (@($Items).Count -eq 0) { throw "Empty $Label." }
    foreach ($item in $Items) {
        Assert-WProtoObject $item $Label
        Assert-WProtoString $item[$Property] "$Label.$Property"
        if (-not $seen.Add($item[$Property])) { throw "Duplicate $Label identity." }
    }
}

function Assert-WProtoDiagnosticCatalog {
    param([string]$Root, $Catalog, $Bindings)
    $advertised = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($match in [regex]::Matches([IO.File]::ReadAllText((Resolve-WProtoSourcePath $Root $Catalog.guidePath)), '\bWPROTO[0-9]{3}\b')) { [void]$advertised.Add($match.Value) }
    if ($advertised.Count -eq 0) { throw 'The guide has no advertised diagnostic subjects.' }
    Assert-WProtoUniqueIds $Catalog.diagnostics 'code' 'diagnostics'
    Assert-WProtoArray $Catalog.diagnosticAssertionSources 'diagnosticAssertionSources'
    Assert-WProtoUniqueIds $Catalog.diagnosticAssertionSources 'path' 'diagnostic assertion sources'
    $reviewedSources = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($source in $Catalog.diagnosticAssertionSources) {
        Assert-WProtoString $source.sha256 'diagnostic source sha256'
        if ((Get-WProtoFileHash (Resolve-WProtoSourcePath $Root $source.path)) -cne $source.sha256) { throw 'Diagnostic assertion source changed without semantic review.' }
        [void]$reviewedSources.Add($source.path)
    }
    $usedSources = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $active = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $hostLanes = @($Catalog.lanes | Where-Object { $_.backend -ceq 'CoreCLR' } | ForEach-Object { $_.id })
    if ($hostLanes.Count -eq 0) { throw 'Diagnostic assertions have no host execution lane.' }
    $editorPolicy = @{
        'SubtypeTagManifestTests.ATagLessDeclarationWithNoManifestEntryCannotReachAPlayer' = @('Error')
        'SubtypeTagManifestTests.ATagLessDeclarationWithNoManifestEntryIsOnlyAWarningInTheEditor' = @('Warning')
        'SubtypeTagManifestTests.TheUnassignedSeverityFollowsTheUnityEditorSymbolAndNothingElse' = @('Error', 'Warning')
    }
    $retired = 0
    foreach ($diagnostic in $Catalog.diagnostics) {
        Assert-WProtoString $diagnostic.status 'diagnostic status'
        Assert-WProtoArray $diagnostic.assertions 'diagnostic assertions'
        if ($diagnostic.code -ceq 'WPROTO007') {
            if ($advertised.Contains($diagnostic.code) -or $diagnostic.status -cne 'resolved-stale-advertisement' -or $null -ne $diagnostic.claimId -or $diagnostic.assertions.Count -ne 0) { throw 'Retired WPROTO007 cannot assert active behavior.' }
            ++$retired
            continue
        }
        if (-not $advertised.Contains($diagnostic.code) -or $diagnostic.status -cne 'reviewed-assertions') { throw 'Diagnostic has no reviewed advertised assertion mapping.' }
        [void]$active.Add($diagnostic.code)
        Assert-WProtoString $diagnostic.claimId 'diagnostic claimId'
        $claim = @($Catalog.claims | Where-Object { $_.id -ceq $diagnostic.claimId })
        if ($claim.Count -ne 1) { throw 'Diagnostic has no catalog claim.' }
        Assert-WProtoUniqueIds $diagnostic.assertions 'selector' 'diagnostic assertions'
        $assertionIds = [Collections.Generic.HashSet[string]]::new([string[]]@($diagnostic.assertions.selector), [StringComparer]::Ordinal)
        if ($diagnostic.code -ceq 'WPROTO041' -and -not $assertionIds.SetEquals([string[]]@($editorPolicy.Keys))) { throw 'WPROTO041 requires the reviewed player, editor and symbol-control assertions.' }
        foreach ($assertion in $diagnostic.assertions) {
            $selector = @($Bindings.selectors | Where-Object { $_.id -ceq $assertion.selector })
            if ($selector.Count -ne 1 -or $assertion.selector -cnotin @($claim[0].selectors) -or -not $reviewedSources.Contains($selector[0].sourcePath)) { throw 'Diagnostic assertion has no reviewed claim/source selector.' }
            [void]$usedSources.Add($selector[0].sourcePath)
            Assert-WProtoArray $assertion.expectedSeverities 'diagnostic expectedSeverities'
            $severities = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($severity in $assertion.expectedSeverities) {
                Assert-WProtoString $severity 'diagnostic severity'
                if (-not $severities.Add($severity)) { throw 'Duplicate diagnostic severity.' }
            }
            $expected = if ($diagnostic.code -ceq 'WPROTO041') { $editorPolicy[$assertion.selector] }
            elseif ($diagnostic.code -cin @('WPROTO028', 'WPROTO031', 'WPROTO033', 'WPROTO034', 'WPROTO047')) { @('Warning') }
            elseif ($diagnostic.code -cin @('WPROTO030', 'WPROTO048')) { @('Info') }
            else { @('Error') }
            if (-not $severities.SetEquals([string[]]@($expected))) { throw 'Diagnostic severity differs from the reviewed policy.' }
            Assert-WProtoArray $assertion.lanes 'diagnostic assertion lanes'
            Assert-WProtoUniqueIds $assertion.lanes 'lane' 'diagnostic assertion lanes'
            $lanes = [Collections.Generic.HashSet[string]]::new([string[]]@($assertion.lanes.lane), [StringComparer]::Ordinal)
            if (-not $lanes.SetEquals([string[]]$hostLanes)) { throw 'Diagnostic assertions must bind every host lane.' }
            $fixture = @($Bindings.fixtures | Where-Object { $_.fixture -ceq $selector[0].fixture -and $_.sourcePath -ceq $selector[0].sourcePath })
            if ($fixture.Count -ne 1) { throw 'Diagnostic assertion fixture is missing.' }
            foreach ($lane in $assertion.lanes) {
                Assert-WProtoArray $lane.caseNames 'diagnostic assertion cases'
                if ($lane.caseNames.Count -eq 0) { throw 'Diagnostic assertion has no exact cases.' }
                $binding = @($fixture[0].lanes | Where-Object { $_.lane -ceq $lane.lane -and $_.status -ceq 'discovered' })
                $requirement = @($claim[0].laneRequirements | Where-Object { $_.lane -ceq $lane.lane -and $_.status -ceq 'witnesses' })
                if ($binding.Count -ne 1 -or $requirement.Count -ne 1 -or $assertion.selector -cnotin @($requirement[0].selectors)) { throw 'Diagnostic assertion has no discovered claim/fixture lane.' }
                $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                foreach ($name in $lane.caseNames) {
                    Assert-WProtoString $name 'diagnostic case name'
                    if (-not $names.Add($name) -or $name -cnotin @($binding[0].caseNames)) { throw 'Diagnostic case is duplicated or outside its discovered fixture.' }
                }
            }
        }
    }
    if ($retired -ne 1 -or -not $active.SetEquals($advertised) -or -not $usedSources.SetEquals($reviewedSources)) { throw 'Advertised diagnostics or reviewed assertion sources are incomplete.' }
}

function Read-WProtoCatalog {
    param([string]$Root)
    $directory = Join-Path $Root 'Generator~/WallstopStudios.UnityHelpers.Proto.Generator.Tests'
    $catalogPath = Join-Path $directory 'CompatibilityClaimCatalog.v1.json'
    $bindingsPath = Join-Path $directory 'CompatibilityCaseBindings.v1.json'
    $catalog = Read-WProtoJson $catalogPath
    $bindings = Read-WProtoJson $bindingsPath
    Assert-WProtoObject $catalog 'catalog'
    Assert-WProtoObject $bindings 'bindings'
    Assert-WProtoInteger $catalog.schemaVersion 'catalog.schemaVersion'
    Assert-WProtoInteger $bindings.schemaVersion 'bindings.schemaVersion'
    if ($catalog.schemaVersion -ne 1 -or $bindings.schemaVersion -ne 1) { throw 'Unsupported WProto evidence schema.' }
    foreach ($field in @('claims', 'lanes', 'gaps', 'diagnostics')) { Assert-WProtoArray $catalog[$field] "catalog.$field" }
    foreach ($field in @('selectors', 'fixtures')) { Assert-WProtoArray $bindings[$field] "bindings.$field" }
    Assert-WProtoString $catalog.guidePath 'guidePath'
    Assert-WProtoString $catalog.guideSha256 'guideSha256'
    if ((Get-WProtoFileHash (Resolve-WProtoSourcePath $Root $catalog.guidePath)) -cne $catalog.guideSha256) { throw 'Guide changed without catalog review.' }
    Assert-WProtoUniqueIds $catalog.claims 'id' 'claims'
    Assert-WProtoUniqueIds $catalog.lanes 'id' 'lanes'
    Assert-WProtoUniqueIds $catalog.gaps 'id' 'gaps'
    Assert-WProtoUniqueIds $bindings.selectors 'id' 'selectors'
    Assert-WProtoUniqueIds $bindings.fixtures 'fixture' 'fixtures'
    $selectorIds = @($bindings.selectors.id)
    $laneIds = @($catalog.lanes.id)
    $gapIds = @($catalog.gaps.id)
    foreach ($lane in $catalog.lanes) {
        foreach ($field in @('backend', 'profile', 'oracle')) { Assert-WProtoString $lane[$field] "lane.$field" }
    }
    foreach ($gap in $catalog.gaps) {
        Assert-WProtoString $gap.status 'gap.status'
        Assert-WProtoArray $gap.claimIds 'gap.claimIds'
        if ($gap.status -cnotin @('open', 'resolved-by-witness', 'resolved-by-documentation') -or $gap.claimIds.Count -eq 0) { throw 'Gap has no declared state or claims.' }
        foreach ($id in $gap.claimIds) {
            $claim = @($catalog.claims | Where-Object { $_.id -ceq $id })
            if ($claim.Count -ne 1 -or $gap.id -cnotin @($claim[0].gapIds)) { throw 'Gap is not reciprocally bound to a claim.' }
        }
    }
    foreach ($selector in $bindings.selectors) {
        Assert-WProtoString $selector.sourcePath 'selector.sourcePath'
        Assert-WProtoString $selector.fixture 'selector.fixture'
        if (-not (Test-Path -LiteralPath (Resolve-WProtoSourcePath $Root $selector.sourcePath) -PathType Leaf)) { throw "Missing witness source: $($selector.id)" }
        $fixture = @($bindings.fixtures | Where-Object { $_.fixture -ceq $selector.fixture -and $_.sourcePath -ceq $selector.sourcePath })
        if ($fixture.Count -ne 1) { throw "Missing witness fixture: $($selector.id)" }
    }
    foreach ($fixture in $bindings.fixtures) {
        Assert-WProtoArray $fixture.lanes 'fixture.lanes'
        Assert-WProtoUniqueIds $fixture.lanes 'lane' 'fixture lanes'
        foreach ($lane in $fixture.lanes) {
            Assert-WProtoString $lane.status 'fixture lane status'
            Assert-WProtoArray $lane.caseNames 'fixture caseNames'
            if ($lane.lane -cnotin $laneIds -or $lane.status -cnotin @('discovered', 'missing-discovery', 'not-applicable')) { throw 'Invalid fixture lane.' }
            $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($name in $lane.caseNames) {
                Assert-WProtoString $name 'case name'
                if (-not $name.StartsWith($fixture.fixture + '.', [StringComparison]::Ordinal) -or -not $names.Add($name)) { throw 'Missing or duplicate case binding.' }
            }
            if (($lane.status -ceq 'discovered') -ne ($names.Count -gt 0)) { throw 'Discovered fixture must have an exact nonempty case set; other states must not.' }
        }
    }
    foreach ($claim in $catalog.claims) {
        foreach ($field in @('claim', 'guideAnchor', 'classification', 'status')) { Assert-WProtoString $claim[$field] "claim.$field" }
        foreach ($field in @('readerDirections', 'selectors', 'gapIds', 'laneRequirements')) { Assert-WProtoArray $claim[$field] "claim.$field" }
        if ($claim.classification -cnotin @('identical-bytes', 'readable-alternate', 'known-oracle-defect', 'behavior', 'unsupported-declaration')) { throw 'Unknown compatibility classification.' }
        if ([string]::IsNullOrWhiteSpace($claim.claim) -or [string]::IsNullOrWhiteSpace($claim.guideAnchor) -or @($claim.readerDirections).Count -eq 0 -or $claim.status -cnotin @('supported-with-witness', 'unsupported-with-witness', 'evidence-gap', 'limitation')) { throw 'Incomplete claim.' }
        if (@($claim.selectors).Count -eq 0 -and @($claim.gapIds).Count -eq 0) { throw "Claim has neither witness nor gap: $($claim.id)" }
        foreach ($id in $claim.selectors) { if ($id -cnotin $selectorIds) { throw "Unknown selector: $id" } }
        foreach ($id in $claim.gapIds) {
            $gap = @($catalog.gaps | Where-Object { $_.id -ceq $id })
            if ($id -cnotin $gapIds -or $claim.id -cnotin @($gap[0].claimIds)) { throw "Unknown or nonreciprocal gap: $id" }
        }
        Assert-WProtoUniqueIds $claim.laneRequirements 'lane' 'claim lane requirements'
        foreach ($requirement in $claim.laneRequirements) {
            Assert-WProtoString $requirement.status 'requirement.status'
            Assert-WProtoString $requirement.scope 'requirement.scope'
            Assert-WProtoArray $requirement.selectors 'requirement.selectors'
            if ($requirement.lane -cnotin $laneIds -or $requirement.status -cnotin @('witnesses', 'missing-discovery', 'not-applicable') -or $requirement.scope -cnotin @('complete', 'partial', 'none')) { throw 'Invalid claim lane requirement.' }
            foreach ($id in $requirement.selectors) { if ($id -cnotin @($claim.selectors)) { throw 'Lane requirement selects a different claim witness.' } }
            if ($requirement.status -ceq 'witnesses' -and @($requirement.selectors).Count -eq 0) { throw 'Witness lane cannot be empty.' }
        }
    }
    Assert-WProtoDiagnosticCatalog $Root $catalog $bindings
    return @{ catalog = $catalog; bindings = $bindings; catalogPath = $catalogPath; bindingsPath = $bindingsPath }
}

function Assert-WProtoEvidence {
    param([string]$Root, [string]$Receipt, [string]$RunId)
    $Root = [IO.Path]::GetFullPath($Root)
    $model = Read-WProtoCatalog $Root
    $record = Read-WProtoJson $Receipt
    Assert-WProtoObject $record 'receipt'
    Assert-WProtoInteger $record.schemaVersion 'receipt.schemaVersion'
    foreach ($field in @('runId', 'lane', 'catalogSha256', 'bindingsSha256', 'sourceRevision', 'binaryRoot', 'resultRunId')) { Assert-WProtoString $record[$field] "receipt.$field" }
    foreach ($field in @('sourceFiles', 'sourceFilesAfterRun', 'binaries', 'binariesAfterRun')) { Assert-WProtoArray $record[$field] "receipt.$field" }
    foreach ($field in @('environment', 'process', 'build', 'sdkProcess', 'results', 'log', 'buildLog', 'sdkLog')) { Assert-WProtoObject $record[$field] "receipt.$field" }
    if ([string]::IsNullOrWhiteSpace($RunId) -or $record.schemaVersion -ne 1 -or $record.runId -cne $RunId) { throw 'Wrong invocation identity.' }
    if ($null -ne $record.exception) { throw 'Invocation collector reported an exception.' }
    if ($record.catalogSha256 -cne (Get-WProtoFileHash $model.catalogPath) -or $record.bindingsSha256 -cne (Get-WProtoFileHash $model.bindingsPath)) { throw 'Catalog or case bindings changed.' }
    $revision = (& git -C $Root rev-parse HEAD) -join ''
    if ($LASTEXITCODE -ne 0 -or $revision -cne $record.sourceRevision) { throw 'Source revision differs.' }
    $currentSources = @(Get-WProtoSourceInventory $Root)
    foreach ($snapshot in @('sourceFiles', 'sourceFilesAfterRun')) {
        if ((ConvertTo-Json -InputObject $currentSources -Depth 5 -Compress) -cne (ConvertTo-Json -InputObject $record[$snapshot] -Depth 5 -Compress)) { throw 'Source inventory differs.' }
    }
    foreach ($field in @('backend', 'profile', 'oracle', 'configuration', 'sdkVersion')) { Assert-WProtoString $record.environment[$field] "environment.$field" }
    $lane = @($model.catalog.lanes | Where-Object { $_.id -ceq $record.lane })
    if ($lane.Count -ne 1 -or $record.environment.backend -cne 'CoreCLR' -or $record.environment.backend -cne $lane[0].backend -or $record.environment.profile -cne $lane[0].profile -or $record.environment.oracle -cne $lane[0].oracle -or $record.environment.configuration -cne 'Release') { throw 'Wrong oracle, backend or profile. Native lanes require a separate observed process collector.' }
    $process = $record.process
    foreach ($field in @('sdkProcess', 'build', 'process')) { Assert-WProtoProcess $record[$field] $field }
    $started = [DateTimeOffset]::Parse($process.startedUtc)
    $ended = [DateTimeOffset]::Parse($process.endedUtc)
    if ([DateTimeOffset]::Parse($record.sdkProcess.endedUtc) -gt [DateTimeOffset]::Parse($record.build.startedUtc) -or [DateTimeOffset]::Parse($record.build.endedUtc) -gt $started) { throw 'SDK/build/test processes are out of order.' }
    $oracleKey = if ($record.lane -ceq 'host-v2') { 'v2' } elseif ($record.lane -ceq 'host-v3') { 'v3' } else { throw 'Unknown host lane.' }
    $expectedBinaryRoot = [IO.Path]::GetFullPath((Join-Path $Root "Generator~/WallstopStudios.UnityHelpers.Proto.Generator.Tests/bin/$oracleKey/Release/net9.0"))
    $project = Join-Path $Root 'Generator~/WallstopStudios.UnityHelpers.Proto.Generator.Tests/WallstopStudios.UnityHelpers.Proto.Generator.Tests.csproj'
    Assert-WProtoString $record.results.path 'results.path'
    Assert-WProtoString $record.results.format 'results.format'
    $expectedArguments = @{
        sdkProcess = @('--version')
        build = @('build', $project, '-c', 'Release', "-p:ProtobufNetOracle=$oracleKey", '--no-incremental', '-p:UseSharedCompilation=false')
        process = @('test', $project, '-c', 'Release', "-p:ProtobufNetOracle=$oracleKey", '--no-build', '--no-restore', '--logger', 'trx;LogFileName=results.trx', '--results-directory', [IO.Path]::GetDirectoryName($record.results.path))
    }
    foreach ($field in @('sdkProcess', 'build', 'process')) {
        $observation = $record[$field]
        foreach ($name in @('executable', 'executableSha256', 'workingDirectory')) { Assert-WProtoString $observation[$name] "process.$name" }
        Assert-WProtoArray $observation.arguments 'process.arguments'
        foreach ($argument in $observation.arguments) { Assert-WProtoString $argument 'process argument' }
        if ($observation.executable -cne $record.sdkProcess.executable -or $observation.executableSha256 -cne (Get-WProtoFileHash $observation.executable) -or $observation.workingDirectory -cne $Root -or
            (ConvertTo-Json -InputObject $observation.arguments -Compress) -cne (ConvertTo-Json -InputObject $expectedArguments[$field] -Compress)) { throw 'Observed executable or SDK/build/test command differs.' }
        foreach ($channel in @('stdout', 'stderr', 'log')) {
            Assert-WProtoObject $observation[$channel] "process.$channel"
            $artifact = $observation[$channel]
            Assert-WProtoString $artifact.path 'process artifact path'
            Assert-WProtoString $artifact.sha256 'process artifact hash'
            if ((Get-WProtoFileHash $artifact.path) -cne $artifact.sha256) { throw 'Captured process stream changed.' }
            $native = Get-NativeReportSummary -LogPath $artifact.path
            if ($native.State -cne 'observed' -or $native.NativeReportHeaderCount -ne 0 -or $native.UnknownNativeConditionCount -ne 0) { throw 'Captured process stream contains a native report or could not be read.' }
        }
    }
    foreach ($pair in @(@($record.sdkLog, $record.sdkProcess.stdout), @($record.buildLog, $record.build.log), @($record.log, $record.process.log))) {
        if ($pair[0].path -cne $pair[1].path -or $pair[0].sha256 -cne $pair[1].sha256) { throw 'Receipt log does not belong to its observed process.' }
    }
    if ($record.binaryRoot -cne $expectedBinaryRoot) { throw 'Unexpected compiled output path.' }
    $currentBinaries = @(Get-WProtoBinaryInventory $Root $record.binaryRoot)
    foreach ($snapshot in @('binaries', 'binariesAfterRun')) {
        if ((ConvertTo-Json -InputObject $currentBinaries -Depth 5 -Compress) -cne (ConvertTo-Json -InputObject $record[$snapshot] -Depth 5 -Compress)) { throw 'Binary inventory differs.' }
    }
    $oracleSources = if ($oracleKey -ceq 'v2') { @(@{ output = 'protobuf-net.dll'; source = 'Generator~/ProtobufNetV2Oracle/protobuf-net.dll' }) }
    else { @(@{ output = 'protobuf-net.dll'; source = 'Generator~/ProtobufNetV3Oracle/protobuf-net.dll' }, @{ output = 'protobuf-net.Core.dll'; source = 'Generator~/ProtobufNetV3Oracle/protobuf-net.Core.dll' }) }
    foreach ($oracleFile in $oracleSources) {
        if ((Get-WProtoFileHash (Join-Path $record.binaryRoot $oracleFile.output)) -cne (Get-WProtoFileHash (Join-Path $Root $oracleFile.source))) { throw 'Compiled oracle differs from the pinned input.' }
    }
    Assert-WProtoUniqueIds $record.binaries 'path' 'binaries'
    foreach ($file in @($record.binaries) + @($record.results, $record.log, $record.buildLog, $record.sdkLog)) {
        Assert-WProtoString $file.path 'artifact path'
        Assert-WProtoString $file.sha256 'artifact hash'
        if ((Get-WProtoFileHash $file.path) -cne $file.sha256) { throw "Artifact changed: $($file.path)" }
    }
    if ([IO.File]::ReadAllText($record.sdkLog.path).Trim() -cne $record.environment.sdkVersion) { throw 'SDK identity differs from its observation.' }
    $testBinaries = @($record.binaries | Where-Object { $_.role -ceq 'test-assembly' })
    if ($testBinaries.Count -ne 1) { throw 'Exactly one test assembly is required.' }
    $result = Read-TestResultEvidence -Path $record.results.path -Format $record.results.format
    $resultStarted = [DateTimeOffset]::Parse($result.StartedUtc)
    $resultEnded = [DateTimeOffset]::Parse($result.CompletedUtc)
    if ([string]::IsNullOrWhiteSpace($result.RunId) -or $result.RunId -cne $record.resultRunId -or $resultStarted -lt $started -or $resultStarted -gt $resultEnded -or $resultEnded -gt $ended) { throw 'Results do not belong to the observed process.' }
    foreach ($case in $result.Cases) {
        if ([string]::IsNullOrWhiteSpace($case.Fixture) -or $case.AssemblyPath -cne $testBinaries[0].path -or $case.AdapterTypeName -cne 'executor://nunit3testexecutor/') { throw 'Result case has missing or unexpected assembly/fixture/adapter identity.' }
    }
    $identity = @($result.Cases | Where-Object { $_.Fixture -ceq 'WallstopStudios.UnityHelpers.Proto.Generator.Tests.OracleIdentityTests' -and $_.FullName -ceq 'LoadedOracleHasTheExpectedPhysicalIdentity' })
    if ($record.results.format -cne 'Trx' -or $identity.Count -ne 1) { throw 'The pinned physical oracle identity witness must run.' }
    $fixtures = @($model.bindings.fixtures | Where-Object { @($_.lanes | Where-Object { $_.lane -ceq $record.lane -and $_.status -ceq 'discovered' }).Count -eq 1 })
    if ($fixtures.Count -eq 0) { throw 'No fixtures are bound for this lane.' }
    foreach ($fixture in $fixtures) {
        $binding = @($fixture.lanes | Where-Object { $_.lane -ceq $record.lane })[0]
        $cases = @($result.Cases | Where-Object { $_.Fixture -ceq $fixture.fixture })
        $actual = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($case in $cases) {
            if ($case.Result -cne 'Passed' -or $case.AssemblyPath -cne $testBinaries[0].path) { throw 'Case did not pass in the observed assembly.' }
            $name = if ($record.results.format -ceq 'Trx') { $case.Fixture + '.' + $case.FullName } else { $case.FullName }
            if (-not $actual.Add($name)) { throw 'Duplicate case execution.' }
        }
        if (-not $actual.SetEquals([string[]]$binding.caseNames)) { throw "Fixture cases changed: $($fixture.fixture)" }
    }
    $missing = @($model.bindings.fixtures | Where-Object { @($_.lanes | Where-Object { $_.lane -ceq $record.lane -and $_.status -ceq 'missing-discovery' }).Count -gt 0 })
    $unbound = @($result.Cases | Where-Object { $_.Fixture -cnotin @($fixtures.fixture) } | Select-Object -ExpandProperty Fixture -Unique)
    return [ordered]@{ runId = $RunId; lane = $record.lane; fixtureEvidenceAccepted = $fixtures.Count; missingFixtureDiscovery = $missing.Count; unboundFixtures = $unbound; processObservation = $process.observationScope; completeCatalogQualified = $false }
}

if ($MyInvocation.InvocationName -ne '.') {
    try {
        if ($Mode -ceq 'catalog') {
            $model = Read-WProtoCatalog $RepositoryRoot
            [ordered]@{ claims = @($model.catalog.claims).Count; selectors = @($model.bindings.selectors).Count; fixtures = @($model.bindings.fixtures).Count; completeCatalogQualified = $false } | ConvertTo-Json
        } else { Assert-WProtoEvidence $RepositoryRoot $ReceiptPath $ExpectedRunId | ConvertTo-Json }
    } catch { [Console]::Error.WriteLine($_.ToString()); exit 1 }
}
