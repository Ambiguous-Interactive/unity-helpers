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

    Pure and deterministic: no disk, no Unity, no network.

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
