# Native filesystem and PowerShell comparison boundary for typed workspace policy.
function Invoke-UnityWorkspacePolicy {
    param([Parameter(Mandatory = $true)][hashtable]$Request)
    $priorExitVariable = Get-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue
    $hadExitVariable = $null -ne $priorExitVariable
    $priorExitCode = if ($hadExitVariable) { $priorExitVariable.Value } else { $null }
    try {
        $scriptsRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $project = Join-Path $scriptsRoot 'python~'
        $module = Join-Path $scriptsRoot 'unity/project_workspace.py'
        $payload = ConvertTo-Json -InputObject $Request -Depth 10 -Compress -EscapeHandling EscapeNonAscii
        $output = @($payload | & uv run --project $project --locked python $module)
        $status = $global:LASTEXITCODE
        $document = [System.Text.Json.JsonDocument]::Parse(($output -join "`n"))
        try {
            $root = $document.RootElement
            $errorText = $root.GetProperty('error').GetString()
            if ($status -ne 0 -or -not [string]::IsNullOrEmpty($errorText)) {
                throw "Workspace policy failed: $errorText"
            }
            return $root.GetProperty('value').Clone()
        }
        finally { $document.Dispose() }
    }
    finally {
        if ($hadExitVariable) { $global:LASTEXITCODE = $priorExitCode }
        else { Remove-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue }
    }
}

function Get-UnityProjectLeafName {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Mode,
        [string]$Scope,
        [switch]$ShareEditorProject
    )
    if ($ShareEditorProject -and $Mode -eq 'playmode') { $Mode = 'editmode' }
    $value = Invoke-UnityWorkspacePolicy @{
        action = 'leaf'; version = $Version; mode = $Mode; scope = [string]$Scope
        share_editor = [bool]$ShareEditorProject
    }
    return $value.GetString()
}

function Resolve-UnityProjectWorkspace {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Mode,
        [string]$Scope,
        [switch]$ShareEditorProject,
        [string]$ExplicitProjectPath,
        [string]$PersistentRoot,
        [string]$GenerationFingerprint
    )
    if ($ShareEditorProject -and $Mode -eq 'playmode') { $Mode = 'editmode' }
    $value = Invoke-UnityWorkspacePolicy @{
        action = 'workspace'; repo = $RepoRoot; version = $Version; mode = $Mode
        scope = [string]$Scope; share_editor = [bool]$ShareEditorProject
        explicit = [string]$ExplicitProjectPath; persistent = [string]$PersistentRoot
        fingerprint = [string]$GenerationFingerprint
    }
    $result = @{}
    foreach ($key in @('LegacyProjectPath', 'ProjectPath', 'CacheRoot', 'PruneRoot', 'LeafName')) {
        $result[$key] = $value.GetProperty($key).GetString()
    }
    $result.Persistent = $value.GetProperty('Persistent').GetBoolean()
    return $result
}

function Move-UnityProjectWithMigrationResidue {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Project,
        [switch]$ExplicitProject
    )
    $value = Invoke-UnityWorkspacePolicy @{
        action = 'retention-plan'; project = $Project; explicit = [bool]$ExplicitProject
    }
    if ($value.ValueKind -eq [System.Text.Json.JsonValueKind]::Null) { return }
    $projectPath = $value.GetProperty('OriginalPath').GetString()
    $retainedRoot = $value.GetProperty('RetainedRoot').GetString()
    $retainedPath = $value.GetProperty('RetainedPath').GetString()
    [void][IO.Directory]::CreateDirectory($retainedRoot)
    try { [IO.Directory]::Move($projectPath, $retainedPath) }
    catch { throw "Generated project retention move failed from '$projectPath' to '$retainedPath': $($_.Exception.Message)" }
    $reasons = @($value.GetProperty('Reasons').EnumerateArray() | ForEach-Object { $_.GetString() })
    [pscustomobject]@{
        OriginalPath = $projectPath
        RetainedPath = $retainedPath
        Reasons = $reasons
        CapturedUtc = [datetime]::UtcNow.ToString('o')
    }
}

function Get-PersistentProjectPruneOrder {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Candidates,
        [Parameter(Mandatory = $true)][double]$FreeBytes,
        [Parameter(Mandatory = $true)][double]$MinimumFreeBytes,
        [string]$KeepName
    )
    if (-not [double]::IsFinite($FreeBytes) -or -not [double]::IsFinite($MinimumFreeBytes)) {
        throw 'Prune free-space values must be finite.'
    }
    if ($FreeBytes -ge $MinimumFreeBytes) { return @() }
    $ordered = @($Candidates | Where-Object { $null -ne $_ -and $_.Name -ne $KeepName } |
        Sort-Object -Property @{ Expression = { [datetime]$_.LastUsed } })
    $rows = New-Object System.Collections.Generic.List[object]
    foreach ($candidate in $ordered) {
        $rows.Add(@{ name = [string]$candidate.Name; size = $candidate.SizeBytes })
    }
    $value = Invoke-UnityWorkspacePolicy @{
        action = 'prune-plan'; candidates = $rows.ToArray()
        free = $FreeBytes.ToString('R', [Globalization.CultureInfo]::InvariantCulture)
        minimum = $MinimumFreeBytes.ToString('R', [Globalization.CultureInfo]::InvariantCulture)
    }
    return @($value.EnumerateArray() | ForEach-Object { $_.GetString() })
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

    try {
        $plan = Get-PersistentProjectPruneOrder `
            -Candidates $candidates `
            -FreeBytes $freeBytes `
            -MinimumFreeBytes $minimumFreeBytes `
            -KeepName $KeepName
    } catch {
        Write-Host "::notice::Persistent Unity project prune skipped; planning failed: $($_.Exception.Message)"
        return @()
    }

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
