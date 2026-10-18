#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('sentinel', 'intmap', 'serialization', 'nullableconsumer', 'borrowed', 'wproto', 'all')]
    [string]$Acceptance,
    [Parameter(Mandatory = $true)]
    [string]$ArtifactsPath,
    [Parameter(Mandatory = $true)]
    [string]$Commit,
    [Parameter(Mandatory = $true)]
    [string]$UnityVersion,
    [string]$Repository = (Join-Path $PSScriptRoot '../..')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
function Assert-NativeAllocationQualification {
    param($Record, $Declaration, $Report, [string]$Directory, [string]$UnityVersion)

    $buildPath = Join-Path $Directory 'native-allocation-probe/build.json'
    $build = Get-Content -LiteralPath $buildPath -Raw | ConvertFrom-Json
    $source = @($Declaration.SourceFiles | Where-Object { $_.Path -ceq 'Tests/Acceptance~/NativeAllocation/native-allocation-probe.cpp' })
    $expectedBackends = if ($UnityVersion -like '2021.3.*') { @(1, 2) } else { @(3) }
    if ($Record.NativeAllocationSourceSha256 -isnot [string] -or
        ($Record.NativeAllocationBackend -isnot [int] -and $Record.NativeAllocationBackend -isnot [long]) -or
        $source.Count -ne 1 -or $Record.NativeAllocationSourceSha256 -cne $source[0].Sha256 -or
        $build.SourceSha256 -cne $source[0].Sha256 -or $build.SourcePath -cne $source[0].Path -or
        $build.BinarySha256 -cnotmatch '^[0-9a-f]{64}$' -or
        $Record.NativeAllocationCountState -cne 'passed' -or $null -ne $Record.NativeAllocationError -or
        $Record.NativeAllocationBackend -notin $expectedBackends) {
        throw 'Native allocation probe identity or backend does not match the frozen source.'
    }
    $payloads = @($Report.Snapshots | Where-Object { $_.PayloadFiles.Count -ne 0 })
    if ($payloads.Count -lt 3) { throw 'Native allocation probe needs stable build, launch and final payload snapshots.' }
    foreach ($snapshot in $payloads) {
        $binary = @($snapshot.PayloadFiles | Where-Object { $_.Path -cmatch '(^|/)unity-helpers-allocation-probe\.dll$' })
        if ($snapshot.State -cne 'passed' -or $binary.Count -ne 1 -or $binary[0].Sha256 -cne $build.BinarySha256) {
            throw 'Native allocation probe binary differs from the compiled and sealed player payload.'
        }
    }
    $windows = @($Record.NativeAllocationObservations)
    if ($windows.Count -ne 15) { throw 'Native allocation controls need three warmups and twelve measured windows.' }
    for ($index = 0; $index -lt $windows.Count; $index++) {
        $window = $windows[$index]
        $kind = if ($index -lt 3) { $index } else { ($index - 3) % 4 }
        $requested = if ($index -lt 3) { if ($kind -eq 2) { 0 } else { 8192 } } elseif ($kind -eq 0) { 0 } elseif ($kind -eq 2) { 4096 } else { 8192 }
        $preboxed = $kind -eq 1
        $count = if ($preboxed) { 0 } else { $requested }
        foreach ($name in @('ChronologicalIndex', 'Requested', 'Count', 'LogicalObjectBytes')) {
            if ($window.$name -isnot [int] -and $window.$name -isnot [long]) { throw "Native $name must be a JSON integer." }
        }
        foreach ($name in @('IsWarmup', 'Preboxed', 'Completed', 'ValuesVerified')) {
            if ($window.$name -isnot [bool]) { throw "Native $name must be a JSON boolean." }
        }
        if ($window.ChronologicalIndex -ne $index -or $window.IsWarmup -ne ($index -lt 3) -or
            $window.Requested -ne $requested -or $window.Preboxed -ne $preboxed -or $window.Count -ne $count -or
            -not $window.Completed -or -not $window.ValuesVerified -or
            ($count -eq 0 -and $window.LogicalObjectBytes -ne 0) -or
            ($count -ne 0 -and $window.LogicalObjectBytes -lt $count * 4) -or
            $window.LogicalObjectBytes -gt 1048576) {
            throw 'Native allocation control count, shape or logical size is invalid.'
        }
    }
    for ($repetition = 0; $repetition -lt 3; $repetition++) {
        $offset = 3 + $repetition * 4
        if ($windows[$offset + 3].LogicalObjectBytes -ne $windows[$offset + 2].LogicalObjectBytes * 2) {
            throw 'Native logical object sizes do not scale with the retained allocating control.'
        }
    }
    foreach ($channel in @('TraversalTimingObservations', 'TraversalAllocationObservations')) {
        $subjects = @($Record.$channel)
        if ($subjects.Count -ne 36) { throw 'Traversal qualification requires all four shapes, three sizes and three repetitions.' }
        $keys = New-Object 'System.Collections.Generic.HashSet[string]'
        foreach ($subject in $subjects) {
            if ($subject.Shape -notin @('array', 'list', 'hash-set', 'dictionary') -or
                $subject.Size -notin @(1, 16, 4096) -or $subject.Repetition -notin @(0, 1, 2) -or
                -not $keys.Add("$($subject.Shape)|$($subject.Size)|$($subject.Repetition)")) {
                throw 'Traversal qualification has a duplicate or unsupported subject.'
            }
            $names = if ($channel -ceq 'TraversalTimingObservations') { @('Size', 'Repetition', 'Calls', 'ReferenceTicks', 'ValueTicks') } else { @('Size', 'Repetition', 'Calls', 'ReferenceCount', 'ValueCount') }
            foreach ($name in $names) {
                if ($subject.$name -isnot [int] -and $subject.$name -isnot [long]) { throw "Traversal $name must be a JSON integer." }
            }
            if ($channel -ceq 'TraversalTimingObservations') {
                if ($subject.Calls -ne [Math]::Max(128, [Math]::Floor(131072 / $subject.Size)) -or
                    $subject.ReferenceTicks -le 0 -or $subject.ValueTicks -le 0) {
                    throw 'Traversal timing contains an empty or unexpected batch.'
                }
            } elseif ($subject.Calls -ne 128 -or $subject.ReferenceCount -ne 128 -or $subject.ValueCount -ne 0) {
                throw 'Traversal allocation control or value view did not meet its count contract.'
            }
        }
    }
}
if ($Acceptance -eq 'wproto') {
    if ($UnityVersion -cnotin @('2021.3.45f1', '6000.6.0f1')) {
        throw 'WProto corpus acceptance requires the pinned floor or latest Unity version.'
    }
    . (Join-Path $PSScriptRoot 'lib/test-result-evidence.ps1')
    . (Join-Path $Repository 'scripts/unity/lib/wproto-player-provenance.ps1')
    $directory = Join-Path $ArtifactsPath 'wproto'
    $summaryPath = Join-Path $directory 'corpus-summary.json'
    if (Test-Path -LiteralPath $summaryPath) { Remove-Item -LiteralPath $summaryPath -Force }
    $summary = Assert-WProtoPlayerProvenance -Repository $Repository -Directory $directory -Commit $Commit -UnityVersion $UnityVersion
    $stream = [IO.File]::Open($summaryPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject $summary -Depth 100))
        $stream.Write($bytes, 0, $bytes.Length)
    } finally { $stream.Dispose() }
    return
}
if ($Acceptance -eq 'nullableconsumer') {
    & (Join-Path $PSScriptRoot 'verify-nullable-consumer.ps1') -ArtifactsPath $ArtifactsPath -Commit $Commit -UnityVersion $UnityVersion -Repository $Repository
    return
}
if ($Acceptance -eq 'borrowed') {
    $directory = Join-Path $ArtifactsPath 'borrowed'
    $declarationPath = Join-Path $directory 'declaration.json'
    $declaration = Get-Content -LiteralPath $declarationPath -Raw | ConvertFrom-Json
    $reports = @(Get-ChildItem -LiteralPath $directory -Filter 'provenance.json' -File -Recurse)
    if ($reports.Count -ne 1) { throw 'Borrowed acceptance requires exactly one frozen-player provenance report.' }
    $report = Get-Content -LiteralPath $reports[0].FullName -Raw | ConvertFrom-Json
    $declarationHash = (Get-FileHash -LiteralPath $declarationPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $expectedBackend = if ($UnityVersion -like '2021.3.*') { 'Mono2x' } else { 'IL2CPP' }
    if ($declaration.SourceCommit -cne $Commit -or $declaration.UnityVersion -cne $UnityVersion -or
        $declaration.Backend -cne $expectedBackend -or $report.DeclarationSha256 -cne $declarationHash -or
        $report.RunId -cne $declaration.RunId -or $report.RequestedUnityVersion -cne $UnityVersion -or
        $report.RequestedBackend -cne $expectedBackend -or $report.ProvenanceStableState -cne 'passed' -or
        $report.JointCorrectness.State -cne 'passed' -or $report.RuntimeRecordJoinState -cne 'passed' -or
        $report.CorrectnessCorpusConsumptionState -cne 'passed' -or
        -not $report.LaunchAttempted -or -not $report.InvocationObserved -or -not $report.ResultsValidated -or
        $null -ne $report.ExecutionError -or $report.CampaignEligible -or $report.AdoptionEligible) {
        throw 'Borrowed channel qualification lacks matching source, current invocation, correctness or stable payload evidence.'
    }
    $preflight = @($report.RuntimeJoin.RawRecords | Where-Object { $_.Marker -ceq 'BORROWED_PREFLIGHT' })
    if ($preflight.Count -ne 1 -or $preflight[0].Record.RuntimeIdentity.Commit -cne $Commit -or
        $preflight[0].Record.RuntimeReleasePlayerState -cne 'passed' -or
        $preflight[0].Record.ClockState -cne 'passed') {
        throw 'Borrowed acceptance requires qualified Release-player clock and allocating positive controls.'
    }
    $allocationChannel = 'managed-allocated-bytes'
    if ($preflight[0].Record.AllocatedBytesState -cne 'passed') {
        Assert-NativeAllocationQualification -Record $preflight[0].Record -Declaration $declaration -Report $report -Directory $directory -UnityVersion $UnityVersion
        $allocationChannel = 'native-managed-object-counts'
    }
    [ordered]@{ borrowed = 'passed'; SourceCommit = $Commit; UnityVersion = $UnityVersion
        Backend = $expectedBackend; ChannelQualification = 'passed'; AllocationChannel = $allocationChannel; CampaignEligible = $false; AdoptionEligible = $false } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ArtifactsPath 'acceptance-summary.json')
    return
}
$selected = if ($Acceptance -eq 'all') { @('sentinel', 'capture', 'intmap', 'serialization', 'owners') } elseif ($Acceptance -eq 'serialization') { @('serialization', 'owners') } elseif ($Acceptance -eq 'sentinel') { @('sentinel', 'capture') } else { @($Acceptance) }
$names = @{
    capture = 'WallstopStudios.UnityHelpers.Tests.Editor.Validation.SentinelSurfaceCaptureTests.CaptureBothActualEditorSkins'
    sentinel = 'WallstopStudios.UnityHelpers.Tests.Editor.Validation.ValidationWorkspaceInteractionTests.NativePanelCallbacksRetainDraftAndPersistSettings'
    intmap = 'WallstopStudios.UnityHelpers.Tests.Runtime.Performance.IntMapPerformanceTests.IntMapLookupsComparedAgainstDictionary'
    owners = 'WallstopStudios.UnityHelpers.Tests.Editor.Tools.WProtoSubtypeTagAssignerClassificationTests.NativeSiblingOwnersRefuseAssignmentAndThePlayerBuildGate'
    serialization = @(
        'AnExtendingAssemblyRoundTripsThroughPrecompiledMembersAndCollections',
        'ExtendingAPreviouslyMergeableBasePreservesItsConstructorSeed',
        'SeededBasePromotionKeepsInheritedMembersAndConsumerFields',
        'AConcreteSubtypeEntryPointUsesTheReplacementRootChain'
    ) | ForEach-Object { "WallstopStudios.UnityHelpers.Tests.Serialization.WProtoCrossAssemblyTests.$_" }
}
$names.serialization += @(
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
$failures = [System.Collections.Generic.List[string]]::new()
$passed = @{}
foreach ($kind in $selected) {
    $passed[$kind] = $false
    try {
        [xml]$document = Get-Content -LiteralPath (Join-Path $ArtifactsPath "$kind/results.xml") -Raw
        $cases = @($document.SelectNodes('//test-case'))
        $expected = @($names[$kind])
        if ($cases.Count -ne $expected.Count -or
            @($cases | Where-Object { $_.result -ne 'Passed' }).Count -ne 0 -or
            @(Compare-Object ($cases | ForEach-Object { $_.fullname } | Sort-Object) ($expected | Sort-Object)).Count -ne 0 -or
            $document.SelectNodes("//*[@result='Failed']").Count -ne 0) {
            throw "${kind}: every exact selected acceptance test must pass once without fixture failures."
        }
        if ($kind -eq 'serialization') {
            $configure = Get-Content -LiteralPath (Join-Path $ArtifactsPath 'serialization/configure.log') -Raw
            $configurations = [regex]::Matches($configure, '(?m)^UH perf config: [^\r\n]+')
            if ($configurations.Count -ne 1 -or $configurations[0].Value -notmatch '^UH perf config: backend=IL2CPP, api=NET_Standard(?:_2_0)?, codeOpt=Release, il2cppConfig=Release, stripping=High, defines=') {
                throw 'Serialization acceptance did not prove the effective High stripping configuration.'
            }
            $build = Get-Content -LiteralPath (Join-Path $ArtifactsPath 'serialization/unity.log') -Raw
            $builds = [regex]::Matches($build, '(?m)^UH player build config: [^\r\n]+')
            if ($builds.Count -ne 1 -or $builds[0].Value -ne 'UH player build config: backend=IL2CPP, stripping=High, development=False') {
                throw 'Serialization acceptance did not prove High stripping at the player build.'
            }
            $player = Get-Content -LiteralPath (Join-Path $ArtifactsPath 'serialization/player.log') -Raw
            $pattern = '(?m)^UH_SERIALIZATION_ACCEPTANCE commit=([0-9a-f]{40}) unity=([^\s]+) backend=IL2CPP development=False cases=4 conflicts=2 migrations=12\r?$'
            $records = [regex]::Matches($player, $pattern)
            if ($records.Count -ne 1 -or $records[0].Groups[1].Value -ne $Commit -or
                $records[0].Groups[2].Value -ne $UnityVersion) {
                throw 'Serialization acceptance needs exactly one matching Release IL2CPP result from its ordinary assemblies.'
            }
        }
        if ($kind -eq 'capture') {
            $log = Get-Content -LiteralPath (Join-Path $ArtifactsPath 'capture/unity.log') -Raw
            $records = [regex]::Matches($log, '(?m)^UH_SENTINEL_CAPTURE unity=([^\s]+) graphics=([^\s]+) skin=(dark|light) images=6\r?$')
            if ($records.Count -ne 2 -or
                @($records | Where-Object { $_.Groups[1].Value -ne $UnityVersion -or $_.Groups[2].Value -ne 'Direct3D11' }).Count -ne 0 -or
                (($records | ForEach-Object { $_.Groups[3].Value } | Sort-Object) -join ',') -ne 'dark,light') {
                throw 'Sentinel capture requires both actual skins rendered with the selected native graphics device.'
            }
            foreach ($skin in @('dark', 'light')) {
                foreach ($surface in @('after-issues', 'after-rules', 'after-builder', 'after-settings', 'after-builder-fix', 'after-graph', 'control-red', 'control-green')) {
                    $path = Join-Path $ArtifactsPath "capture/images/$surface-$skin.png"
                    $bytes = [IO.File]::ReadAllBytes($path)
                    $width = if ($surface.StartsWith('control-')) { 64 } else { 1280 }
                    $height = if ($surface.StartsWith('control-')) { 64 } else { 720 }
                    if ($bytes.Length -le 24 -or [Convert]::ToHexString($bytes[0..7]) -ne '89504E470D0A1A0A' -or
                        [Text.Encoding]::ASCII.GetString($bytes[12..15]) -ne 'IHDR' -or
                        [Convert]::ToUInt32([Convert]::ToHexString($bytes[16..19]), 16) -ne $width -or
                        [Convert]::ToUInt32([Convert]::ToHexString($bytes[20..23]), 16) -ne $height) {
                        throw "Sentinel capture has a missing or malformed PNG header: $path"
                    }
                }
            }
        }
        $passed[$kind] = $true
    } catch {
        $failures.Add($_.Exception.Message)
    }
}
$summaryPath = Join-Path $ArtifactsPath 'acceptance-summary.json'
if ('intmap' -in $selected) {
    node (Join-Path $PSScriptRoot 'verify-acceptance.js') $ArtifactsPath $Commit $UnityVersion
    if ($LASTEXITCODE -ne 0) {
        $failures.Add('IntMap player evidence verification failed.')
    }
} else {
    @{} | ConvertTo-Json | Set-Content -LiteralPath $summaryPath
}
if (Test-Path -LiteralPath $summaryPath) {
    $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
    foreach ($kind in @('sentinel', 'capture', 'serialization', 'owners')) {
        if ($kind -in $selected) {
            $status = if ($passed[$kind]) { 'passed' } else { 'failed' }
            $summary | Add-Member -NotePropertyName $kind -NotePropertyValue $status -Force
        }
    }
    $summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $summaryPath
}
if ($failures.Count -ne 0) { throw ($failures -join [Environment]::NewLine) }
