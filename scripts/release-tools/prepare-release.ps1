Param(
    [ValidateSet('major', 'minor', 'patch')]
    [string]$Bump = 'patch',
    [string]$Version = '',
    [string]$Date = (Get-Date -Format 'yyyy-MM-dd'),
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path,
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'release-helpers.ps1')

try {
    $result = Invoke-ReleasePreparation `
        -RepoRoot $RepoRoot `
        -Bump $Bump `
        -Version $Version `
        -Date $Date `
        -DryRun:$DryRun

    Write-Host "prepare-release: $($result.CurrentVersion) -> $($result.NextVersion)"
    if ($result.DryRun) {
        Write-Host 'prepare-release: dry run; no files were written.'
    } else {
        Write-Host "prepare-release: rewrote $($result.PackageJsonPath)"
        if ($result.PackageLockPath) {
            Write-Host "prepare-release: rewrote $($result.PackageLockPath)"
        }
        if ($result.ChangelogRotated) {
            Write-Host "prepare-release: rotated $($result.ChangelogPath)"
        } else {
            Write-Host "prepare-release: $($result.ChangelogPath) already had the target version heading."
        }
    }
} catch {
    Write-Error "prepare-release failed: $($_.Exception.Message)"
    exit 1
}
