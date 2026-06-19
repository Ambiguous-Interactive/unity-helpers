#!/usr/bin/env pwsh
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (& git rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($repoRoot)) {
    exit 0
}

$repoRoot = ([string]$repoRoot).Trim()
$cachePath = (& git -C $repoRoot rev-parse --git-path license-year-cache 2>$null)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($cachePath)) {
    exit 0
}

$cachePath = ([string]$cachePath).Trim()
if (-not [System.IO.Path]::IsPathRooted($cachePath)) {
    $cachePath = Join-Path $repoRoot $cachePath
}

if (Test-Path -LiteralPath $cachePath -PathType Leaf) {
    Remove-Item -LiteralPath $cachePath -Force
    Write-Host 'License year cache invalidated (history rewritten).'
}

exit 0
