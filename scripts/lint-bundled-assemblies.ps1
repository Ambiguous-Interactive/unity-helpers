#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Asserts that no bundled BCL assembly can silently start competing with Unity's own copy.

.DESCRIPTION
    Runtime/Binaries ships assemblies from NuGet. Unity ships some of the same names from
    Editor/Data/BCLExtensions, at a different major version, and when the compiler resolves the
    family inconsistently the package fails to compile in the consuming project -- CS0012 for a
    type in an unreferenced assembly, CS0103 for a name that vanished. See
    docs/guides/bundled-assembly-conflicts.md and issue #331.

    The fix for that was a define constraint per conflicting assembly. This test exists because the
    fix is invisible: a NuGet refresh that drops a new DLL into Runtime/Binaries, or regenerates a
    .meta without the constraint, reintroduces the conflict with nothing to notice it. Every DLL
    must therefore be classified below, and its importer must match its classification.
#>
[CmdletBinding()]
param([switch]$VerboseOutput)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$binariesRoot = Join-Path $repoRoot 'Runtime/Binaries'
$guidePath = 'docs/guides/bundled-assembly-conflicts.md'

# Unity began shipping these three from Editor/Data/BCLExtensions in 6000.5, at 8.0.0.0 against our
# 9.0.0.0. Below 6000.5 -- including every 6000.0 through 6000.4 release -- Unity ships none of
# them and ours are required, so the constraint is a floor, not a removal.
$unityProvidedConstraint = '!UNITY_6000_5_OR_NEWER'
$expectedConstraints = @{
    'Microsoft.Bcl.AsyncInterfaces' = @($unityProvidedConstraint)
    'System.Text.Encodings.Web'     = @($unityProvidedConstraint)
    'System.Text.Json'              = @($unityProvidedConstraint)
    # Unity ships no copy of this one at any version, so ours must always load.
    'System.IO.Pipelines'           = @()
}

# Assemblies this package compiles against but deliberately does NOT ship, because every editor in
# the support matrix (2021.3+) provides them and a fourth copy of a contested name is what makes
# resolution unstable in the first place. Shipping one would be a decision, not a refresh artifact.
$deliberatelyNotShipped = @('System.Runtime.CompilerServices.Unsafe')

$failed = $false

function Write-Info($message) {
    if ($VerboseOutput) { Write-Host "[lint-bundled-assemblies] $message" -ForegroundColor Cyan }
}

function Get-DefineConstraints {
    param([Parameter(Mandatory = $true)][string]$MetaPath)

    [string[]]$lines = Get-Content -LiteralPath $MetaPath
    $constraints = @()
    $inBlock = $false
    foreach ($line in $lines) {
        if ($line -match '^\s*defineConstraints:\s*\[\s*\]\s*$') {
            return @()
        }
        # Flow style: defineConstraints: ['!UNITY_6000_5_OR_NEWER', 'OTHER']. Unity writes block
        # style, but reading a populated flow sequence as "unconstrained" would be a silent pass
        # in the permissive direction -- exactly the failure this linter exists to prevent.
        if ($line -match '^\s*defineConstraints:\s*\[(?<items>.+)\]\s*$') {
            foreach ($item in $Matches['items'].Split(',')) {
                $trimmed = $item.Trim().Trim("'", '"')
                if ($trimmed.Length -gt 0) {
                    $constraints += $trimmed
                }
            }
            return $constraints
        }
        if ($line -match '^\s*defineConstraints:\s*$') {
            $inBlock = $true
            continue
        }
        if ($inBlock) {
            if ($line -match "^\s*-\s*'?([^'\r\n]+?)'?\s*$") {
                $constraints += $Matches[1]
                continue
            }
            break
        }
    }

    return $constraints
}

if (-not (Test-Path -LiteralPath $binariesRoot)) {
    Write-Host "::error::Bundled assembly directory not found: $binariesRoot"
    exit 1
}

$dlls = @(Get-ChildItem -LiteralPath $binariesRoot -Filter '*.dll' -File | Sort-Object Name)
if ($dlls.Count -eq 0) {
    Write-Host "::error file=$guidePath::No DLLs found under Runtime/Binaries. If the bundled assemblies were removed deliberately, retire this linter in the same commit."
    exit 1
}

foreach ($dll in $dlls) {
    $assemblyName = [System.IO.Path]::GetFileNameWithoutExtension($dll.Name)
    $relativePath = "Runtime/Binaries/$($dll.Name)"

    if ($deliberatelyNotShipped -contains $assemblyName) {
        Write-Host "::error file=$relativePath::$assemblyName is recorded as deliberately not shipped -- every supported editor provides it, and a competing copy is what destabilizes resolution. Remove it, or move it out of the deliberately-not-shipped list with evidence in $guidePath."
        $failed = $true
        continue
    }

    if (-not $expectedConstraints.ContainsKey($assemblyName)) {
        Write-Host "::error file=$relativePath::$assemblyName is not classified by scripts/lint-bundled-assemblies.ps1. Decide whether Unity ships this name -- if it does, constrain the importer to $unityProvidedConstraint and document it in $guidePath; if it does not, add it with an empty constraint list."
        $failed = $true
        continue
    }

    $metaPath = "$($dll.FullName).meta"
    if (-not (Test-Path -LiteralPath $metaPath)) {
        Write-Host "::error file=$relativePath::Missing importer metadata: $relativePath.meta"
        $failed = $true
        continue
    }

    [string[]]$expected = @($expectedConstraints[$assemblyName])
    [string[]]$actual = @(Get-DefineConstraints -MetaPath $metaPath)

    if (($expected -join '|') -ne ($actual -join '|')) {
        $expectedText = if ($expected.Count -eq 0) { '(none)' } else { $expected -join ', ' }
        $actualText = if ($actual.Count -eq 0) { '(none)' } else { $actual -join ', ' }
        Write-Host "::error file=$relativePath.meta::$assemblyName must carry defineConstraints [$expectedText] but carries [$actualText]. A regenerated importer that drops the constraint lets this copy compete with Unity's own; see $guidePath."
        $failed = $true
        continue
    }

    Write-Info "$assemblyName importer constraints match ($(if ($actual.Count -eq 0) { 'unconstrained' } else { $actual -join ', ' }))."
}

# A refresh that DROPS an assembly is as breaking as one that adds a conflicting copy: without
# System.Text.Json below 6000.5 the package does not compile at all. Iterating the DLLs on disk
# alone cannot see that, so every classified name must still be present.
$presentAssemblies = @($dlls | ForEach-Object { [System.IO.Path]::GetFileNameWithoutExtension($_.Name) })
foreach ($assemblyName in ($expectedConstraints.Keys | Sort-Object)) {
    if ($presentAssemblies -notcontains $assemblyName) {
        Write-Host "::error file=$guidePath::$assemblyName is classified as shipped but no longer exists under Runtime/Binaries. Dropping it breaks consumers on editors that do not provide it; if the removal is deliberate, remove its classification in the same commit."
        $failed = $true
    }
}

foreach ($assemblyName in $deliberatelyNotShipped) {
    Write-Info "$assemblyName remains deliberately unshipped."
}

if ($failed) {
    exit 1
}

Write-Host "[lint-bundled-assemblies] OK: $($dlls.Count) bundled assemblies classified and constrained." -ForegroundColor Green
