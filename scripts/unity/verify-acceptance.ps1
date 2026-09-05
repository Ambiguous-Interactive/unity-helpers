#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('sentinel', 'intmap', 'all')]
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
$selected = if ($Acceptance -eq 'all') { @('sentinel', 'intmap') } else { @($Acceptance) }
$names = @{
    sentinel = 'WallstopStudios.UnityHelpers.Tests.Editor.Validation.ValidationWorkspaceInteractionTests.NativePanelCallbacksRetainDraftAndPersistSettings'
    intmap = 'WallstopStudios.UnityHelpers.Tests.Runtime.Performance.IntMapPerformanceTests.IntMapLookupsComparedAgainstDictionary'
}
$failures = [System.Collections.Generic.List[string]]::new()
$passed = @{}
foreach ($kind in $selected) {
    $passed[$kind] = $false
    try {
        [xml]$document = Get-Content -LiteralPath (Join-Path $ArtifactsPath "$kind/results.xml") -Raw
        $cases = @($document.SelectNodes('//test-case'))
        if ($cases.Count -ne 1 -or $cases[0].fullname -ne $names[$kind] -or $cases[0].result -ne 'Passed' -or
            $document.SelectNodes("//*[@result='Failed']").Count -ne 0) {
            throw "${kind}: the exact selected acceptance test must pass once without fixture failures."
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
    } elseif ('sentinel' -in $selected) {
        $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
        $status = if ($passed.sentinel) { 'passed' } else { 'failed' }
        $summary | Add-Member -NotePropertyName sentinel -NotePropertyValue $status
        $summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $summaryPath
    }
} else {
    $status = if ($passed.sentinel) { 'passed' } else { 'failed' }
    @{ sentinel = $status } | ConvertTo-Json | Set-Content -LiteralPath $summaryPath
}
if ($failures.Count -ne 0) { throw ($failures -join [Environment]::NewLine) }
