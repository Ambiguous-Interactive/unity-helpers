#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Test runner for scripts/unity/lib/project-workspace.ps1.

.DESCRIPTION
    Pins the two behaviours that decide whether a Unity leg reuses its Library or
    imports cold:

      1. Resolve-UnityProjectWorkspace precedence. An explicit path wins; a
         persistent root puts BOTH the project and the UPM caches outside the
         repository; neither set falls back to the repo-local .artifacts tree.
         The persistent flag must be $false for the two non-persistent forms --
         it gates disk pruning, and pruning a developer's .artifacts tree would
         delete work they can see.

      2. Get-PersistentProjectPruneOrder policy. Least-recently-used first, never
         the leg that is about to run, and it stops as soon as the projected free
         space clears the floor.

    Uses temporary filesystem controls; no Unity or network access.

.PARAMETER VerboseOutput
    Show detailed output during test execution.

.EXAMPLE
    pwsh -NoProfile -File scripts/tests/test-project-workspace.ps1
#>
param(
    [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:TestsPassed = 0
$script:TestsFailed = 0
$script:FailedTests = @()

function Write-Info($msg) {
    if ($VerboseOutput) { Write-Host "[test-project-workspace] $msg" -ForegroundColor Cyan }
}

function Write-TestResult {
    param(
        [string]$TestName,
        [bool]$Passed,
        [string]$Message = ''
    )
    if ($Passed) {
        Write-Host "  [PASS] $TestName" -ForegroundColor Green
        $script:TestsPassed++
    } else {
        Write-Host "  [FAIL] $TestName" -ForegroundColor Red
        if ($Message) {
            Write-Host "         $Message" -ForegroundColor Yellow
        }
        $script:TestsFailed++
        $script:FailedTests += $TestName
    }
}

function Assert-Equal {
    param(
        [string]$TestName,
        $Expected,
        $Actual
    )
    $expectedText = if ($null -eq $Expected) { '<null>' } else { [string]$Expected }
    $actualText = if ($null -eq $Actual) { '<null>' } else { [string]$Actual }
    Write-TestResult -TestName $TestName -Passed ($expectedText -eq $actualText) -Message "expected '$expectedText', got '$actualText'"
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
. (Join-Path $repoRoot 'scripts/unity/lib/project-workspace.ps1')

# Compare paths through the platform separator so the same expectations hold on
# the Linux devcontainer and the Windows self-hosted runners.
function Join-Segments {
    param([Parameter(Mandatory = $true)][string[]]$Segments)
    $path = $Segments[0]
    foreach ($segment in $Segments[1..($Segments.Length - 1)]) {
        $path = Join-Path $path $segment
    }
    return $path
}

Write-Host ''
Write-Host 'Get-UnityProjectLeafName' -ForegroundColor White
Assert-Equal -TestName 'leaf without scope' -Expected '6000.3.16f1-editmode' -Actual (Get-UnityProjectLeafName -Version '6000.3.16f1' -Mode 'editmode')
Assert-Equal -TestName 'leaf with scope' -Expected '6000.3.16f1-editmode-single-threaded' -Actual (Get-UnityProjectLeafName -Version '6000.3.16f1' -Mode 'editmode' -Scope 'single-threaded')
Assert-Equal -TestName 'whitespace scope is no scope' -Expected '6000.3.16f1-playmode' -Actual (Get-UnityProjectLeafName -Version '6000.3.16f1' -Mode 'playmode' -Scope '   ')
Assert-Equal -TestName 'shared EditMode leaf' -Expected '6000.3.16f1-editmode' -Actual (Get-UnityProjectLeafName -Version '6000.3.16f1' -Mode 'editmode' -ShareEditorProject)
Assert-Equal -TestName 'shared PlayMode leaf' -Expected '6000.3.16f1-editmode' -Actual (Get-UnityProjectLeafName -Version '6000.3.16f1' -Mode 'playmode' -ShareEditorProject)
Assert-Equal -TestName 'shared scoped editor leaf' -Expected '6000.3.16f1-editmode-single-threaded' -Actual (Get-UnityProjectLeafName -Version '6000.3.16f1' -Mode 'playmode' -Scope 'single-threaded' -ShareEditorProject)
Assert-Equal -TestName 'Standalone remains separate' -Expected '6000.3.16f1-standalone' -Actual (Get-UnityProjectLeafName -Version '6000.3.16f1' -Mode 'standalone' -ShareEditorProject)

Write-Host ''
Write-Host 'Resolve-UnityProjectWorkspace' -ForegroundColor White

$fakeRepo = Join-Segments @('/', 'repo')
$fakePersistent = Join-Segments @('/', 'work', 'unity-workspace')

$default = Resolve-UnityProjectWorkspace -RepoRoot $fakeRepo -Version '6000.3.16f1' -Mode 'editmode'
Assert-Equal -TestName 'repo-local project path' `
    -Expected (Join-Segments @($fakeRepo, '.artifacts', 'unity', 'projects', '6000.3.16f1-editmode')) `
    -Actual $default.ProjectPath
Assert-Equal -TestName 'repo-local cache root' `
    -Expected (Join-Segments @($fakeRepo, '.artifacts', 'unity', 'cache', '6000.3.16f1')) `
    -Actual $default.CacheRoot
Assert-Equal -TestName 'repo-local is not persistent' -Expected $false -Actual $default.Persistent
Assert-Equal -TestName 'repo-local has no prune root' -Expected '' -Actual $default.PruneRoot

$persistent = Resolve-UnityProjectWorkspace -RepoRoot $fakeRepo -Version '6000.3.16f1' -Mode 'editmode' -Scope 'single-threaded' -PersistentRoot $fakePersistent
Assert-Equal -TestName 'persistent project path leaves the repo' `
    -Expected (Join-Segments @($fakePersistent, 'projects', '6000.3.16f1-editmode-single-threaded')) `
    -Actual $persistent.ProjectPath
Assert-Equal -TestName 'persistent cache root leaves the repo' `
    -Expected (Join-Segments @($fakePersistent, 'cache', '6000.3.16f1')) `
    -Actual $persistent.CacheRoot
Assert-Equal -TestName 'persistent is flagged persistent' -Expected $true -Actual $persistent.Persistent
Assert-Equal -TestName 'persistent prune root is the projects parent' `
    -Expected (Join-Segments @($fakePersistent, 'projects')) `
    -Actual $persistent.PruneRoot

$sharedEditMode = Resolve-UnityProjectWorkspace -RepoRoot $fakeRepo -Version '6000.3.16f1' -Mode 'editmode' -Scope 'single-threaded' -PersistentRoot $fakePersistent -ShareEditorProject
$sharedPlayMode = Resolve-UnityProjectWorkspace -RepoRoot $fakeRepo -Version '6000.3.16f1' -Mode 'playmode' -Scope 'single-threaded' -PersistentRoot $fakePersistent -ShareEditorProject
Assert-Equal -TestName 'serial editor modes reuse the same persistent project' -Expected $sharedEditMode.ProjectPath -Actual $sharedPlayMode.ProjectPath
Assert-Equal -TestName 'shared editor scope remains separate from default' `
    -Expected (Join-Segments @($fakePersistent, 'projects', '6000.3.16f1-editmode-single-threaded')) `
    -Actual $sharedPlayMode.ProjectPath

# The UPM caches must move with the project. Leaving them under .artifacts/ would
# put them back in `git clean -ffdx`'s path, re-downloading every UPM dependency
# on every job -- the exact cost this whole mechanism exists to remove.
Write-TestResult -TestName 'persistent cache root is not under the repo' `
    -Passed (-not $persistent.CacheRoot.StartsWith($fakeRepo)) `
    -Message "cache root '$($persistent.CacheRoot)' is still inside '$fakeRepo'"

$explicitPath = Join-Segments @('/', 'tmp', 'explicit-project')
$explicit = Resolve-UnityProjectWorkspace -RepoRoot $fakeRepo -Version '6000.3.16f1' -Mode 'editmode' -ExplicitProjectPath $explicitPath -PersistentRoot $fakePersistent
Assert-Equal -TestName 'explicit path outranks the persistent root' -Expected $explicitPath -Actual $explicit.ProjectPath
Assert-Equal -TestName 'explicit path is not treated as persistent' -Expected $false -Actual $explicit.Persistent

$blankRoot = Resolve-UnityProjectWorkspace -RepoRoot $fakeRepo -Version '6000.3.16f1' -Mode 'editmode' -PersistentRoot '   '
Assert-Equal -TestName 'whitespace persistent root falls back to the repo' `
    -Expected (Join-Segments @($fakeRepo, '.artifacts', 'unity', 'projects', '6000.3.16f1-editmode')) `
    -Actual $blankRoot.ProjectPath

Write-Host ''
Write-Host 'Get-PersistentProjectPruneOrder' -ForegroundColor White

$candidates = @(
    [pscustomobject]@{ Name = 'oldest'; LastUsed = [datetime]'2020-01-01'; SizeBytes = 30 },
    [pscustomobject]@{ Name = 'middle'; LastUsed = [datetime]'2021-01-01'; SizeBytes = 30 },
    [pscustomobject]@{ Name = 'current'; LastUsed = [datetime]'2019-01-01'; SizeBytes = 5000 },
    [pscustomobject]@{ Name = 'newest'; LastUsed = [datetime]'2022-01-01'; SizeBytes = 30 }
)

Assert-Equal -TestName 'above the floor prunes nothing' -Expected '' `
    -Actual ((Get-PersistentProjectPruneOrder -Candidates $candidates -FreeBytes 100 -MinimumFreeBytes 100 -KeepName 'current') -join ',')
Assert-Equal -TestName 'prunes least-recently-used first and stops at the floor' -Expected 'oldest' `
    -Actual ((Get-PersistentProjectPruneOrder -Candidates $candidates -FreeBytes 10 -MinimumFreeBytes 40 -KeepName 'current') -join ',')
Assert-Equal -TestName 'keeps pruning while still under the floor' -Expected 'oldest,middle,newest' `
    -Actual ((Get-PersistentProjectPruneOrder -Candidates $candidates -FreeBytes 10 -MinimumFreeBytes 200 -KeepName 'current') -join ',')

# The current leg's own project is the one thing pruning must never take: deleting
# it is precisely the cold import the persistent root exists to avoid, and it is
# also the largest entry here, so a size-greedy planner would grab it first.
$unreachable = Get-PersistentProjectPruneOrder -Candidates $candidates -FreeBytes 0 -MinimumFreeBytes 999999 -KeepName 'current'
Write-TestResult -TestName 'never prunes the current leg' `
    -Passed (@($unreachable) -notcontains 'current') `
    -Message "plan was [$(@($unreachable) -join ',')]"
Assert-Equal -TestName 'no candidates prunes nothing' -Expected '' `
    -Actual ((Get-PersistentProjectPruneOrder -Candidates @() -FreeBytes 1 -MinimumFreeBytes 500 -KeepName 'current') -join ',')

Write-Host ''
Write-Host 'Invoke-PersistentProjectPrune' -ForegroundColor White

$missingRoot = Join-Path ([System.IO.Path]::GetTempPath()) "project-workspace-missing-$PID-$(Get-Random)"
Assert-Equal -TestName 'missing project root is a no-op' -Expected '' `
    -Actual ((Invoke-PersistentProjectPrune -ProjectsRoot $missingRoot -KeepName 'current' -MinimumFreeGb 0) -join ',')

$realRoot = Join-Path ([System.IO.Path]::GetTempPath()) "project-workspace-$PID-$(Get-Random)"
try {
    New-Item -ItemType Directory -Force -Path (Join-Path $realRoot 'stale') | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $realRoot 'current') | Out-Null
    # A zero floor can never be under-water, so a healthy disk prunes nothing --
    # the guard must not delete a warm Library just because it ran.
    $pruned = Invoke-PersistentProjectPrune -ProjectsRoot $realRoot -KeepName 'current' -MinimumFreeGb 0
    Assert-Equal -TestName 'healthy free space prunes nothing' -Expected '' -Actual (($pruned) -join ',')
    Write-TestResult -TestName 'existing projects survive a no-op prune' `
        -Passed ((Test-Path -LiteralPath (Join-Path $realRoot 'stale')) -and (Test-Path -LiteralPath (Join-Path $realRoot 'current'))) `
        -Message 'a no-op prune deleted a project directory'
} finally {
    Remove-Item -LiteralPath $realRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host 'Interrupted migration project isolation' -ForegroundColor White
$residueRoot = Join-Path ([IO.Path]::GetTempPath()) ('unity-migration-residue-' + [guid]::NewGuid().ToString('N'))
try {
    $project = Join-Path $residueRoot 'projects/editor'
    $sourceRelative = 'Assets/OdinApplyIntegration-0123456789abcdef0123456789abcdef/Consumer.cs'
    $contents = @{
        'Assets/Editor/UhCiTestConfigurator.cs' = 'public static class UhCiTestConfigurator {}'
        'Packages/manifest.json' = '{"dependencies":{}}'
        'Library/ScriptAssemblies/Consumer.dll' = 'derived assembly control'
        'Library/WallstopOdinMigrationApply/active.txt' = 'retained-migration'
        'Library/WallstopOdinMigrationBackups/retained-migration/state.json' = 'retained backup control'
        $sourceRelative = 'interrupted consumer control'
    }
    foreach ($relative in $contents.Keys) {
        $target = Join-Path $project $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        [IO.File]::WriteAllText($target, $contents[$relative])
    }
    $retained = Move-UnityProjectWithMigrationResidue -Project $project
    Write-TestResult -TestName 'interrupted migration cannot contaminate the next generated project' `
        -Passed (-not (Test-Path -LiteralPath $project) -and $null -ne $retained)
    foreach ($relative in $contents.Keys) {
        Assert-Equal -TestName "retained project preserves $relative" -Expected $contents[$relative] `
            -Actual ([IO.File]::ReadAllText((Join-Path $retained.RetainedPath $relative)))
    }
    Write-TestResult -TestName 'retained project is outside ordinary sibling pruning' `
        -Passed ((Split-Path -Parent $retained.RetainedPath) -eq (Join-Path $residueRoot 'retained-projects'))
    Assert-Equal -TestName 'missing replacement is an idempotent no-op' -Expected $null `
        -Actual (Move-UnityProjectWithMigrationResidue -Project $project)

    foreach ($relative in @('Assets/Editor/UhCiTestConfigurator.cs', 'Library/cache.bin', 'Assets/OdinApplyIntegration-not-a-guid/Consumer.cs')) {
        $target = Join-Path $project $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        [IO.File]::WriteAllText($target, 'healthy project control')
    }
    Assert-Equal -TestName 'healthy project keeps its warm Library' -Expected $null `
        -Actual (Move-UnityProjectWithMigrationResidue -Project $project)
    Assert-Equal -TestName 'healthy Library bytes remain unchanged' -Expected 'healthy project control' `
        -Actual ([IO.File]::ReadAllText((Join-Path $project 'Library/cache.bin')))

    foreach ($prefix in @('OdinApplyIntegration', 'OdinImportVerification', 'OdinBoundConversion', 'OdinSceneScope',
            'OdinMigrationImport', 'OdinMigrationReferences', 'OdinPairedPreview', 'OdinLiveAddress')) {
        $fixture = Join-Path $residueRoot ('projects/' + $prefix)
        $asset = Join-Path $fixture ('Assets/' + $prefix + '-0123456789abcdef0123456789abcdef')
        New-Item -ItemType Directory -Force -Path $asset | Out-Null
        New-Item -ItemType Directory -Force -Path (Join-Path $fixture 'Assets/Editor') | Out-Null
        [IO.File]::WriteAllText((Join-Path $fixture 'Assets/Editor/UhCiTestConfigurator.cs'), 'public static class UhCiTestConfigurator {}')
        $moved = Move-UnityProjectWithMigrationResidue -Project $fixture
        Write-TestResult -TestName "$prefix residue is isolated without requiring a pointer" `
            -Passed ($null -ne $moved -and (Test-Path -LiteralPath (Join-Path $moved.RetainedPath ('Assets/' + (Split-Path -Leaf $asset)))))
    }

    $pointer = Join-Path $project 'Library/WallstopOdinMigrationApply/active.txt'
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $pointer) | Out-Null
    [IO.File]::WriteAllText($pointer, 'active-only control')
    $refused = $false
    try { Move-UnityProjectWithMigrationResidue -Project $project -ExplicitProject | Out-Null }
    catch { $refused = $_.Exception.Message -match 'explicit' }
    Write-TestResult -TestName 'explicit caller project is refused without moving its migration' `
        -Passed ($refused -and [IO.File]::ReadAllText($pointer) -eq 'active-only control')
    [IO.File]::WriteAllText((Join-Path $project 'Assets/Editor/UhCiTestConfigurator.cs'), 'unrelated authoring source')
    $refused = $false
    try { Move-UnityProjectWithMigrationResidue -Project $project | Out-Null }
    catch { $refused = $_.Exception.Message -match 'generated' }
    Write-TestResult -TestName 'unproven project provenance is refused without moving its migration' `
        -Passed ($refused -and [IO.File]::ReadAllText($pointer) -eq 'active-only control')
    [IO.File]::WriteAllText((Join-Path $project 'Assets/Editor/UhCiTestConfigurator.cs'), 'public static class UhCiTestConfigurator {}')
    $moved = Move-UnityProjectWithMigrationResidue -Project $project
    Assert-Equal -TestName 'pointer alone preserves the interrupted project' -Expected 'active-only control' `
        -Actual ([IO.File]::ReadAllText((Join-Path $moved.RetainedPath 'Library/WallstopOdinMigrationApply/active.txt')))

    $failureProject = Join-Path $residueRoot 'failed-preservation/projects/editor'
    $failurePointer = Join-Path $failureProject 'Library/WallstopOdinMigrationApply/active.txt'
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $failurePointer) | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $failureProject 'Assets/Editor') | Out-Null
    [IO.File]::WriteAllText((Join-Path $failureProject 'Assets/Editor/UhCiTestConfigurator.cs'), 'public static class UhCiTestConfigurator {}')
    [IO.File]::WriteAllText($failurePointer, 'failed preservation control')
    $blockedDestination = Join-Path $residueRoot 'failed-preservation/retained-projects'
    [IO.File]::WriteAllText($blockedDestination, 'destination conflict control')
    $refused = $false
    try { Move-UnityProjectWithMigrationResidue -Project $failureProject | Out-Null }
    catch { $refused = $true }
    Write-TestResult -TestName 'failed preservation leaves the original pointer and conflicting destination unchanged' `
        -Passed ($refused -and [IO.File]::ReadAllText($failurePointer) -ceq 'failed preservation control' -and
            [IO.File]::ReadAllText($blockedDestination) -ceq 'destination conflict control')

    $linkType = if ($env:OS -eq 'Windows_NT') { 'Junction' } else { 'SymbolicLink' }
    foreach ($relative in @('', 'Assets', 'Library', 'Assets/Editor', 'Library/WallstopOdinMigrationApply')) {
        $linkRoot = Join-Path $residueRoot ('linked-' + [guid]::NewGuid().ToString('N'))
        $linkedProject = Join-Path $linkRoot 'projects/editor'
        $linkTarget = Join-Path $linkRoot 'outside'
        New-Item -ItemType Directory -Force -Path $linkTarget | Out-Null
        [IO.File]::WriteAllText((Join-Path $linkTarget 'canary'), 'outside control')
        $linkedPath = if ($relative) { Join-Path $linkedProject $relative } else { $linkedProject }
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $linkedPath) | Out-Null
        New-Item -ItemType $linkType -Path $linkedPath -Target $linkTarget | Out-Null
        $refused = $false
        try { Move-UnityProjectWithMigrationResidue -Project $linkedProject | Out-Null }
        catch { $refused = $_.Exception.Message -match 'linked' }
        Write-TestResult -TestName "linked project path '$relative' is refused without changing its target" `
            -Passed ($refused -and [IO.File]::ReadAllText((Join-Path $linkTarget 'canary')) -ceq 'outside control')
        Remove-Item -LiteralPath $linkedPath -Force
    }
    Remove-Item -LiteralPath $blockedDestination
    $outside = Join-Path $residueRoot 'retention-link-target'
    New-Item -ItemType Directory -Path $outside | Out-Null
    New-Item -ItemType $linkType -Path $blockedDestination -Target $outside | Out-Null
    $refused = $false
    try { Move-UnityProjectWithMigrationResidue -Project $failureProject | Out-Null }
    catch { $refused = $_.Exception.Message -match 'linked' }
    Write-TestResult -TestName 'linked retention root is refused without moving the source' `
        -Passed ($refused -and [IO.File]::ReadAllText($failurePointer) -ceq 'failed preservation control' -and
            @(Get-ChildItem -LiteralPath $outside -Force).Count -eq 0)
    Remove-Item -LiteralPath $blockedDestination -Force

    $runnerSource = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts/unity/run-ci-tests.ps1') -Raw
    $guardOffset = $runnerSource.IndexOf('$retainedProject = Move-UnityProjectWithMigrationResidue')
    $initializeOffset = $runnerSource.IndexOf('$ProjectPath = Initialize-EphemeralProject')
    $warmthOffset = $runnerSource.IndexOf('$LibraryWarmth =')
    Write-TestResult -TestName 'runner isolates residue before measuring cache warmth or generating compiler inputs' `
        -Passed ($guardOffset -ge 0 -and $guardOffset -lt $warmthOffset -and $guardOffset -lt $initializeOffset)
    Write-TestResult -TestName 'runner retains explicit-path provenance before resolving workspace' `
        -Passed ($runnerSource.IndexOf('$ExplicitProjectRequested =') -ge 0 -and
            $runnerSource.IndexOf('$ExplicitProjectRequested =') -lt $runnerSource.IndexOf('$ProjectWorkspace = Resolve-UnityProjectWorkspace'))
    $tokens = $null
    $parseErrors = $null
    $runnerAst = [Management.Automation.Language.Parser]::ParseInput($runnerSource, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count -ne 0) { throw 'Unity runner must parse before exercising its retention guard.' }
    $guard = @($runnerAst.EndBlock.Statements | Where-Object {
        $_ -is [Management.Automation.Language.IfStatementAst] -and
        $_.Extent.Text.Contains('$retainedProject = Move-UnityProjectWithMigrationResidue')
    })
    if ($guard.Count -ne 1) { throw 'Expected exactly one top-level project retention guard.' }
    $ProjectPath = $failureProject
    $ArtifactsPath = Join-Path $residueRoot 'public-artifacts'
    $ExplicitProjectRequested = $false
    function Write-CiWarning { param([string]$Message) }
    $TestMode = 'export'
    Invoke-Expression $guard[0].Extent.Text
    Write-TestResult -TestName 'export never moves its staged project or writes a retention receipt' `
        -Passed ((Test-Path -LiteralPath $failurePointer) -and -not (Test-Path -LiteralPath $ArtifactsPath))
    $TestMode = 'editmode'
    Invoke-Expression $guard[0].Extent.Text
    $receipt = Get-Content -LiteralPath (Join-Path $ArtifactsPath 'retained-project.json') -Raw | ConvertFrom-Json
    Write-TestResult -TestName 'shipping guard preserves residue and writes the actual destination receipt' `
        -Passed ($receipt.OriginalPath -ceq $failureProject -and -not (Test-Path -LiteralPath $failureProject) -and
            [IO.File]::ReadAllText((Join-Path $receipt.RetainedPath 'Library/WallstopOdinMigrationApply/active.txt')) -ceq 'failed preservation control')
    $lifecycle = @($runnerAst.EndBlock.Statements | Where-Object {
        $_ -is [Management.Automation.Language.TryStatementAst] -and
        $_.Body.Extent.Text.Contains('Invoke-UnityNativeStartupProbe')
    })
    Write-TestResult -TestName 'shipping guard runs before the native editor and license lifecycle' `
        -Passed ($lifecycle.Count -eq 1 -and $guard[0].Extent.EndOffset -lt $lifecycle[0].Extent.StartOffset)
} finally {
    if (Test-Path -LiteralPath $residueRoot) { Remove-Item -LiteralPath $residueRoot -Recurse -Force }
}

Write-Host "Passed: $script:TestsPassed" -ForegroundColor Green
if ($script:TestsFailed -gt 0) {
    Write-Host "Failed: $script:TestsFailed" -ForegroundColor Red
    foreach ($failed in $script:FailedTests) {
        Write-Host "  - $failed" -ForegroundColor Red
    }
    exit 1
}
Write-Host 'All project-workspace tests passed.' -ForegroundColor Green
exit 0
