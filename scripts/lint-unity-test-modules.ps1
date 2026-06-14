#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Validate .github/unity-test-project-modules.json before any Unity job runs.

.DESCRIPTION
    The ephemeral Unity TEST PROJECT declares a fixed set of UnityEngine built-in
    modules (com.unity.modules.*) plus the editor-bundled com.unity.ugui so the
    package's Runtime/Editor code and test fixtures compile. That list is the
    SINGLE SOURCE OF TRUTH consumed by both manifest generators
    (scripts/unity/run-ci-tests.ps1 and scripts/unity/create-test-project.sh).

    If that file declares a package id that does not exist -- the classic trap is
    "com.unity.modules.grid" (there is no such package; Grid/GridLayout are
    provided by the built-in module declared as com.unity.modules.tilemap) -- the Unity
    Package Manager aborts resolution with "Package [id] cannot be found" BEFORE
    compilation. Unity then exits non-zero, writes no results.xml, and the entire
    test matrix (every editor x every mode) fails ~30 minutes later with no
    obviously-named cause.

    This lint is the FAST, pre-Unity guard for that whole class of failure: it
    runs in well under a second on a GitHub-hosted runner and turns the silent,
    matrix-wide, half-hour death into an instant, self-describing red check on the
    PR. It validates that every declared id is a REAL Unity package:
      * com.unity.modules.<name> where <name> is an actual built-in module, OR
      * com.unity.ugui (the only editor-bundled non-module package the test
        project needs).
    It also enforces that the human-readable _evidence map stays 1:1 with the
    modules list, so the "why is this here" documentation can never silently drift
    from the list it explains.

    The authoritative built-in module list lives at
    https://docs.unity3d.com/Manual/pack-build.html. If Unity ships a brand-new
    built-in module that this package legitimately needs, add its short name to
    $script:KnownBuiltinModules below; the failure message says exactly that.

.PARAMETER Path
    Path to the modules JSON. Defaults to .github/unity-test-project-modules.json
    relative to the repo root.

.PARAMETER VerboseOutput
    Show verbose progress output.

.EXAMPLE
    ./scripts/lint-unity-test-modules.ps1
    Validate the default modules manifest.

.EXAMPLE
    ./scripts/lint-unity-test-modules.ps1 -VerboseOutput
#>
param(
    [string]$Path,
    [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Info($msg) {
    if ($VerboseOutput) { Write-Host "[lint-unity-test-modules] $msg" -ForegroundColor Cyan }
}

function Write-Fail($msg) {
    # ::error:: so the cause is a one-line GitHub annotation, not buried in logs.
    Write-Host "::error::$msg"
}

# The set of real UnityEngine built-in module short names (the part after
# "com.unity.modules.") that are stable across EVERY Unity version this repo
# targets (2021.3, 2022.3, 6000.x). Source: a default project's Packages/
# manifest.json on those editors, cross-checked with
# https://docs.unity3d.com/Manual/pack-build.html.
#
# This list is deliberately CONSERVATIVE: it omits ids that exist only on newer
# editors (e.g. accessibility/amd/nvidia, Unity-6-only) and ids whose package
# form is disputed or non-existent (e.g. there is NO com.unity.modules.grid --
# Grid is covered by com.unity.modules.tilemap; "adaptiveperformance" is the full
# package com.unity.adaptiveperformance, not a built-in module). Listing such ids
# would WEAKEN the guard: the lint would silently pass a manifest declaring one,
# then Unity would fail with the exact "Package [id] cannot be found" this lint
# exists to prevent. A strict, definitely-real allowlist -- not a permissive
# "any com.unity.modules.* id" regex -- is what catches typos (physisc2d) and
# non-existent ids (grid) alike. If the package legitimately needs a newer/exotic
# built-in module, the failure message says exactly what to do: add its short
# name here.
$script:KnownBuiltinModules = @(
    'ai'
    'androidjni'
    'animation'
    'assetbundle'
    'audio'
    'cloth'
    'director'
    'imageconversion'
    'imgui'
    'jsonserialize'
    'particlesystem'
    'physics'
    'physics2d'
    'screencapture'
    'terrain'
    'terrainphysics'
    'tilemap'
    'ui'
    'uielements'
    'umbra'
    'unitywebrequest'
    'unitywebrequestassetbundle'
    'unitywebrequestaudio'
    'unitywebrequesttexture'
    'unitywebrequestwww'
    'vehicles'
    'video'
    'vr'
    'wind'
    'xr'
)

# Non-module packages that legitimately belong in the test-project module list.
# com.unity.ugui (UnityEngine.UI: Image/ColorBlock/Canvas) is editor-bundled but
# is NOT a com.unity.modules.* id, so it gets an explicit pass here.
$script:KnownBundledPackages = @(
    'com.unity.ugui'
)

# Known non-existent ids that are tempting to write because a type's assembly is
# UnityEngine.<X>Module, but whose real package is a DIFFERENT id. Mapped to the
# real id so the error message is directly actionable instead of just "not a real
# module".
$script:NonExistentModuleAliases = @{
    'com.unity.modules.grid' = 'com.unity.modules.tilemap'
}

$modulePrefix = 'com.unity.modules.'

# ── Resolve the file to check ────────────────────────────────────────────────
if ([string]::IsNullOrWhiteSpace($Path)) {
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    $Path = Join-Path $repoRoot '.github' 'unity-test-project-modules.json'
}

if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    Write-Fail "Unity test-project modules manifest not found: $Path"
    exit 1
}

Write-Info "Checking: $Path"

# ── Parse ────────────────────────────────────────────────────────────────────
try {
    $json = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
} catch {
    Write-Fail "$Path is not valid JSON: $($_.Exception.Message)"
    exit 1
}

if (-not $json.PSObject.Properties['modules'] -or $null -eq $json.modules) {
    Write-Fail "$Path is missing a 'modules' object."
    exit 1
}

$moduleProps = @($json.modules.PSObject.Properties)
if ($moduleProps.Count -lt 1) {
    Write-Fail "$Path declares no modules; the test project would fail to compile."
    exit 1
}

$errorCount = 0

# ── Validate every declared id ───────────────────────────────────────────────
foreach ($prop in $moduleProps) {
    $id = $prop.Name
    $version = $prop.Value

    if ([string]::IsNullOrWhiteSpace([string]$version)) {
        Write-Fail "Module '$id' has an empty version; built-in modules use '1.0.0'."
        $errorCount++
    }

    if ($script:NonExistentModuleAliases.ContainsKey($id)) {
        $real = $script:NonExistentModuleAliases[$id]
        Write-Fail "'$id' is NOT a real package id. Use '$real' instead (it covers the same types). Replace '$id' with '$real' in $Path (and its _evidence entry)."
        $errorCount++
        continue
    }

    if ($id.StartsWith($modulePrefix)) {
        $shortName = $id.Substring($modulePrefix.Length)
        if ($script:KnownBuiltinModules -notcontains $shortName) {
            Write-Fail "'$id' is not a known Unity built-in module. If this is a typo, fix it; if Unity genuinely added this module, add '$shortName' to `$script:KnownBuiltinModules in scripts/lint-unity-test-modules.ps1. Authoritative list: https://docs.unity3d.com/Manual/pack-build.html"
            $errorCount++
        }
        continue
    }

    if ($script:KnownBundledPackages -contains $id) {
        continue
    }

    Write-Fail "'$id' is not allowed here. Only com.unity.modules.* built-ins and these bundled packages belong in the test-project module list: $($script:KnownBundledPackages -join ', ')."
    $errorCount++
}

# ── Enforce the _evidence map stays 1:1 with modules ─────────────────────────
# _evidence documents WHY each module is required. Keeping it exactly in step
# with the modules list means the rationale can never silently rot (the grid
# entry, for instance, was wrong in both places at once -- this guard makes that
# impossible to merge).
if ($json.PSObject.Properties['_evidence'] -and $null -ne $json._evidence) {
    $moduleNames = [System.Collections.Generic.HashSet[string]]::new([string[]]@($moduleProps | ForEach-Object { $_.Name }))
    $evidenceNames = [System.Collections.Generic.HashSet[string]]::new([string[]]@($json._evidence.PSObject.Properties | ForEach-Object { $_.Name }))

    foreach ($name in $moduleNames) {
        if (-not $evidenceNames.Contains($name)) {
            Write-Fail "Module '$name' has no matching _evidence entry in $Path. Add one line describing where the module is used."
            $errorCount++
        }
    }
    foreach ($name in $evidenceNames) {
        if (-not $moduleNames.Contains($name)) {
            Write-Fail "_evidence documents '$name' but it is not in the modules list of $Path. Remove the stale entry or add the module."
            $errorCount++
        }
    }
}

if ($errorCount -gt 0) {
    Write-Host "[lint-unity-test-modules] FAILED with $errorCount error(s)." -ForegroundColor Red
    exit 1
}

Write-Info "All $($moduleProps.Count) declared package id(s) are valid and _evidence is in sync."
if ($VerboseOutput) {
    Write-Host "[lint-unity-test-modules] PASS" -ForegroundColor Green
}
exit 0
