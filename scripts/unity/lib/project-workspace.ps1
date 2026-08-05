#!/usr/bin/env pwsh
# project-workspace.ps1 - where the generated Unity test project and its Library live.
#
# Dot-sourceable library (NO param() block, just function definitions) so the
# path/prune logic can be unit-tested with plain pwsh without triggering
# run-ci-tests.ps1's main / mandatory param() prompts.
#
# WHY THIS EXISTS
#   The generated project's `Library` is Unity's import database plus its
#   compiled assemblies -- pure derived state, and the single biggest lever on
#   how long a Unity leg takes. It used to live under the repo's gitignored
#   `.artifacts/` tree, INSIDE $GITHUB_WORKSPACE. `actions/checkout` runs
#   `git clean -ffdx` on every job, and `-x` means gitignored: every CI job
#   deleted the Library the previous job had just built, on the same disk,
#   minutes earlier. `actions/cache` then paid to download it back.
#
#   Pointing the project at a root OUTSIDE the workspace makes `git clean`
#   structurally unable to reach it, so a self-hosted runner reuses its own
#   local copy. Run artifacts (logs, results.xml) deliberately STAY inside the
#   workspace -- those are per-run outputs the workflow uploads, not reusable
#   state.
#
# Functions:
#   Get-UnityProjectLeafName        - pure <version>-<mode>[-<scope>] leaf name.
#   Resolve-UnityProjectWorkspace   - resolves project + cache roots from the
#                                     explicit path / persistent root / repo default.
#   Get-PersistentProjectPruneOrder - pure LRU prune plan for a free-space floor.
#   Invoke-PersistentProjectPrune   - applies that plan on disk.

function Get-UnityProjectLeafName {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Mode,
        [string]$Scope
    )

    if ([string]::IsNullOrWhiteSpace($Scope)) {
        return "$Version-$Mode"
    }

    return "$Version-$Mode-$($Scope.Trim())"
}

function Resolve-UnityProjectWorkspace {
    <#
    .SYNOPSIS
        Resolve where the generated Unity project and the UPM caches live.

    .DESCRIPTION
        Precedence, highest first:
          1. -ExplicitProjectPath  (an operator/caller override; used verbatim)
          2. -PersistentRoot       (CI: a directory OUTSIDE $GITHUB_WORKSPACE)
          3. $RepoRoot/.artifacts/unity/projects (the local-developer default)

        Returns a hashtable with:
          ProjectPath  - the generated project directory for this leg
          CacheRoot    - the UPM/npm/git-lfs cache root for this Unity version
          Persistent   - $true when the location survives `git clean -ffdx`
          PruneRoot    - the parent holding sibling legs' projects, or '' when
                         not persistent (never prune a developer's .artifacts)
          LeafName     - the per-leg directory name

        Pure apart from path normalization: creates nothing, reads nothing.
    #>
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Mode,
        [string]$Scope,
        [string]$ExplicitProjectPath,
        [string]$PersistentRoot
    )

    $leaf = Get-UnityProjectLeafName -Version $Version -Mode $Mode -Scope $Scope
    $repoProjects = Join-Path (Join-Path (Join-Path $RepoRoot '.artifacts') 'unity') 'projects'
    $repoCaches = Join-Path (Join-Path (Join-Path $RepoRoot '.artifacts') 'unity') 'cache'

    if (-not [string]::IsNullOrWhiteSpace($ExplicitProjectPath)) {
        return @{
            ProjectPath = $ExplicitProjectPath
            CacheRoot = Join-Path $repoCaches $Version
            Persistent = $false
            PruneRoot = ''
            LeafName = $leaf
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($PersistentRoot)) {
        $root = $PersistentRoot.Trim()
        $projectsRoot = Join-Path $root 'projects'
        return @{
            ProjectPath = Join-Path $projectsRoot $leaf
            CacheRoot = Join-Path (Join-Path $root 'cache') $Version
            Persistent = $true
            PruneRoot = $projectsRoot
            LeafName = $leaf
        }
    }

    return @{
        ProjectPath = Join-Path $repoProjects $leaf
        CacheRoot = Join-Path $repoCaches $Version
        Persistent = $false
        PruneRoot = ''
        LeafName = $leaf
    }
}

function Get-PersistentProjectPruneOrder {
    <#
    .SYNOPSIS
        Decide which sibling projects to delete to get back above a free-space floor.

    .DESCRIPTION
        Pure planner so the policy is testable without a real disk. Given the
        candidate directories (name, last-use timestamp, size in bytes), the
        current free bytes, and the floor, returns the names to delete in
        least-recently-used-first order, stopping as soon as the projected free
        space clears the floor.

        -KeepName is never returned: the leg about to run must keep its own
        project, because deleting it is exactly the cold import this whole
        mechanism exists to avoid.

        Returns @() when already above the floor or when nothing is prunable.
    #>
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Candidates,
        [Parameter(Mandatory = $true)][double]$FreeBytes,
        [Parameter(Mandatory = $true)][double]$MinimumFreeBytes,
        [string]$KeepName
    )

    if ($FreeBytes -ge $MinimumFreeBytes) {
        return @()
    }

    $prunable = @(
        $Candidates |
            Where-Object { $null -ne $_ } |
            Where-Object { $_.Name -ne $KeepName } |
            Sort-Object -Property @{ Expression = { [datetime]$_.LastUsed } }
    )

    $planned = New-Object System.Collections.Generic.List[string]
    $projectedFree = $FreeBytes
    foreach ($candidate in $prunable) {
        if ($projectedFree -ge $MinimumFreeBytes) {
            break
        }
        $planned.Add([string]$candidate.Name)
        $projectedFree += [double]$candidate.SizeBytes
    }

    return $planned.ToArray()
}

function Invoke-PersistentProjectPrune {
    <#
    .SYNOPSIS
        Keep the persistent project root from filling a self-hosted runner's disk.

    .DESCRIPTION
        Reads the sibling project directories under -ProjectsRoot, measures free
        space on that volume, and deletes least-recently-used siblings until free
        space clears -MinimumFreeGb. Best-effort by design: a measurement failure
        or an undeletable directory logs and returns rather than failing the leg,
        because a full disk is a slow run and a failed prune must not become a red
        run. Returns the directory names it deleted.
    #>
    param(
        [Parameter(Mandatory = $true)][string]$ProjectsRoot,
        [Parameter(Mandatory = $true)][string]$KeepName,
        [double]$MinimumFreeGb = 60
    )

    if (-not (Test-Path -LiteralPath $ProjectsRoot -PathType Container)) {
        return @()
    }

    $freeBytes = $null
    try {
        $driveInfo = New-Object System.IO.DriveInfo(
            [System.IO.Path]::GetPathRoot([System.IO.Path]::GetFullPath($ProjectsRoot))
        )
        $freeBytes = [double]$driveInfo.AvailableFreeSpace
    } catch {
        Write-Host "::notice::Persistent Unity project prune skipped; free space unreadable for '$ProjectsRoot': $($_.Exception.Message)"
        return @()
    }

    $minimumFreeBytes = $MinimumFreeGb * 1GB
    Write-Host ("Persistent project root: {0} ({1:N1} GB free, floor {2:N0} GB)" -f $ProjectsRoot, ($freeBytes / 1GB), $MinimumFreeGb)
    if ($freeBytes -ge $minimumFreeBytes) {
        return @()
    }

    $candidates = @(
        Get-ChildItem -LiteralPath $ProjectsRoot -Directory -ErrorAction SilentlyContinue |
            ForEach-Object {
                $sizeBytes = 0
                try {
                    $sizeBytes = (
                        Get-ChildItem -LiteralPath $_.FullName -Recurse -File -ErrorAction SilentlyContinue |
                            Measure-Object -Property Length -Sum
                    ).Sum
                } catch {
                    $sizeBytes = 0
                }
                if ($null -eq $sizeBytes) { $sizeBytes = 0 }
                [pscustomobject]@{
                    Name = $_.Name
                    LastUsed = $_.LastWriteTimeUtc
                    SizeBytes = [double]$sizeBytes
                }
            }
    )

    $plan = Get-PersistentProjectPruneOrder `
        -Candidates $candidates `
        -FreeBytes $freeBytes `
        -MinimumFreeBytes $minimumFreeBytes `
        -KeepName $KeepName

    $deleted = New-Object System.Collections.Generic.List[string]
    foreach ($name in $plan) {
        $path = Join-Path $ProjectsRoot $name
        try {
            Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop
            $deleted.Add($name)
            Write-Host "::notice::Pruned least-recently-used persistent Unity project '$name' to reclaim disk space."
        } catch {
            Write-Host "::warning::Could not prune persistent Unity project '$name': $($_.Exception.Message)"
        }
    }

    return $deleted.ToArray()
}
