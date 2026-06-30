Param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path,
    [string]$OutputPath = '',
    [switch]$Footer
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'release-helpers.ps1')

try {
    $notes = New-ReleaseNotes -RepoRoot $RepoRoot -Version $Version -Footer:$Footer
    if ([string]::IsNullOrWhiteSpace($OutputPath)) {
        Write-Output $notes
    } else {
        $parent = Split-Path -Parent $OutputPath
        if (-not [string]::IsNullOrWhiteSpace($parent)) {
            New-Item -ItemType Directory -Path $parent -Force | Out-Null
        }
        Set-ReleaseFileContent -Path $OutputPath -Content $notes
        Write-Host "write-release-notes: wrote $OutputPath"
    }
} catch {
    Write-Error "write-release-notes failed: $($_.Exception.Message)"
    exit 1
}
