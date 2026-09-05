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
$selected = if ($Acceptance -eq 'all') { @('sentinel', 'intmap', 'serialization') } else { @($Acceptance) }
$names = @{
    sentinel = 'WallstopStudios.UnityHelpers.Tests.Editor.Validation.ValidationWorkspaceInteractionTests.NativePanelCallbacksRetainDraftAndPersistSettings'
    intmap = 'WallstopStudios.UnityHelpers.Tests.Runtime.Performance.IntMapPerformanceTests.IntMapLookupsComparedAgainstDictionary'
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
            $pattern = '(?m)^UH_SERIALIZATION_ACCEPTANCE commit=([0-9a-f]{40}) unity=([^\s]+) backend=IL2CPP development=False cases=4\r?$'
            $records = [regex]::Matches($player, $pattern)
            if ($records.Count -ne 1 -or $records[0].Groups[1].Value -ne $Commit -or
                $records[0].Groups[2].Value -ne $UnityVersion) {
                throw 'Serialization acceptance needs exactly one matching Release IL2CPP result from its ordinary assemblies.'
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
    foreach ($kind in @('sentinel', 'serialization')) {
        if ($kind -in $selected) {
            $status = if ($passed[$kind]) { 'passed' } else { 'failed' }
            $summary | Add-Member -NotePropertyName $kind -NotePropertyValue $status -Force
        }
    }
    $summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $summaryPath
}
if ($failures.Count -ne 0) { throw ($failures -join [Environment]::NewLine) }
