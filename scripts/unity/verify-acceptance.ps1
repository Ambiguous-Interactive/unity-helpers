#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('sentinel', 'intmap', 'serialization', 'all')]
    [string]$Acceptance,
    [Parameter(Mandatory = $true)]
    [string]$ArtifactsPath,
    [Parameter(Mandatory = $true)]
    [string]$Commit,
    [Parameter(Mandatory = $true)]
    [string]$UnityVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
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
            $pattern = '(?m)^UH_SERIALIZATION_ACCEPTANCE commit=([0-9a-f]{40}) unity=([^\s]+) backend=IL2CPP development=False cases=4 conflicts=2\r?$'
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
