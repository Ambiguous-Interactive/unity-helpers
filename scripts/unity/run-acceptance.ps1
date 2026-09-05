#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('sentinel', 'intmap', 'all')]
    [string]$Acceptance,
    [Parameter(Mandatory = $true)]
    [string]$UnityVersion,
    [Parameter(Mandatory = $true)]
    [string]$ArtifactsPath,
    [Parameter(Mandatory = $true)]
    [string]$TemporaryRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$failures = [System.Collections.Generic.List[string]]::new()
$selected = if ($Acceptance -eq 'all') { @('sentinel', 'intmap') } else { @($Acceptance) }
foreach ($kind in $selected) {
    $ownedProject = $null
    try {
        & (Join-Path $PSScriptRoot 'assert-no-active-unity-editor.ps1')
        $parameters = @{
            UnityVersion = $UnityVersion
            ArtifactsPath = Join-Path $ArtifactsPath $kind
            ReleaseCodeOptimization = $true
            ReleasePlayerBuild = $true
            Il2CppCompilerConfiguration = 'Release'
        }
        if ($kind -eq 'sentinel') {
            $token = [Guid]::NewGuid().ToString('N')
            $project = [IO.Path]::GetFullPath((Join-Path $TemporaryRoot "sentinel-interaction-$token"))
            New-Item -ItemType Directory -Path $project | Out-Null
            $ownedProject = $project
            [IO.File]::WriteAllText((Join-Path $project '.sentinel-interaction-disposable'), $token)
            $oldToken = $env:WALLSTOP_SENTINEL_INTERACTION_TOKEN
            $oldProject = $env:WALLSTOP_SENTINEL_INTERACTION_PROJECT
            try {
                $env:WALLSTOP_SENTINEL_INTERACTION_TOKEN = $token
                $env:WALLSTOP_SENTINEL_INTERACTION_PROJECT = $project
                $parameters.ProjectPath = $project
                $parameters.TestMode = 'editmode'
                $parameters.AssemblyNames = 'WallstopStudios.UnityHelpers.Tests.Editor.Validation'
                $parameters.TestFilter = 'WallstopStudios.UnityHelpers.Tests.Editor.Validation.ValidationWorkspaceInteractionTests.NativePanelCallbacksRetainDraftAndPersistSettings'
                & (Join-Path $PSScriptRoot 'run-ci-tests.ps1') @parameters
            } finally {
                $env:WALLSTOP_SENTINEL_INTERACTION_TOKEN = $oldToken
                $env:WALLSTOP_SENTINEL_INTERACTION_PROJECT = $oldProject
            }
        } else {
            $parameters.ProjectPath = Join-Path $TemporaryRoot "intmap-acceptance-$([Guid]::NewGuid().ToString('N'))"
            New-Item -ItemType Directory -Path $parameters.ProjectPath | Out-Null
            $ownedProject = $parameters.ProjectPath
            $parameters.TestMode = 'standalone'
            $parameters.StandaloneScriptingBackend = 'IL2CPP'
            $parameters.AssemblyNames = 'WallstopStudios.UnityHelpers.Tests.Runtime.Performance'
            $parameters.TestFilter = 'WallstopStudios.UnityHelpers.Tests.Runtime.Performance.IntMapPerformanceTests.IntMapLookupsComparedAgainstDictionary'
            & (Join-Path $PSScriptRoot 'run-ci-tests.ps1') @parameters
        }
    } catch {
        $failures.Add("${kind}: $($_.Exception.Message)")
        Write-Warning "Acceptance run failed: $kind. Preserving artifacts and continuing other selected work."
    } finally {
        if ($null -ne $ownedProject -and (Test-Path -LiteralPath $ownedProject)) {
            try {
                & (Join-Path $PSScriptRoot 'assert-no-active-unity-editor.ps1')
                Remove-Item -LiteralPath $ownedProject -Recurse -Force
            } catch {
                $failures.Add("${kind} cleanup: $($_.Exception.Message)")
                Write-Warning "Could not remove the owned acceptance project: $ownedProject"
            }
        }
    }
}
if ($failures.Count -ne 0) {
    throw ($failures -join [Environment]::NewLine)
}
