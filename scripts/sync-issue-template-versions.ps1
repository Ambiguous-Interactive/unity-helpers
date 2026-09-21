<#
.SYNOPSIS
    Syncs package versions into GitHub issue template dropdowns.
.DESCRIPTION
    Reads versions from .github/issue-template-versions.json and writes the
    sorted options between sentinel comments in both issue templates. Only
    release preparation adds a new package version to the manifest. Local
    tags and GitHub Releases cannot change the dropdown.
    Automatically stages modified files.
.PARAMETER AddPackageVersion
    Add the current package.json version to the manifest during release preparation.
#>
[CmdletBinding()]
param(
    [switch]$AddPackageVersion
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Load shared git helpers for safe index operations
$helpersPath = Join-Path -Path $PSScriptRoot -ChildPath 'git-staging-helpers.ps1'
. $helpersPath

$repoRoot = Split-Path -Parent $PSScriptRoot
$packageJsonPath = Join-Path $repoRoot 'package.json'
$manifestPath = Join-Path $repoRoot '.github/issue-template-versions.json'

$templateDir = Join-Path $repoRoot '.github' 'ISSUE_TEMPLATE'
$templateFiles = @(
    (Join-Path $templateDir 'bug_report.yml'),
    (Join-Path $templateDir 'feature_request.yml')
)

$sentinelStart = '# <!-- AUTO-UPDATED: package-versions -->'
$sentinelEnd = '# <!-- END AUTO-UPDATED: package-versions -->'

# Valid semver-like pattern: exactly digits.digits.digits (no prerelease suffix)
$semverPattern = '^\d+\.\d+\.\d+$'

# ── Git availability & lock handling ────────────────────────────────────────

$repositoryInfo = $null
$gitAvailable = $false
try {
    Assert-GitAvailable | Out-Null
    $repositoryInfo = Get-GitRepositoryInfo
    $gitAvailable = $true
} catch {
    Write-Warning "Git is not available or not in a repository: $($_.Exception.Message)"
    Write-Warning "Continuing without staging."
}

if ($gitAvailable) {
    if (-not (Invoke-EnsureNoIndexLock)) {
        Write-Warning "index.lock still held after waiting. Proceeding anyway, but operations may fail."
    }
}

# ── Read the tracked version manifest ──────────────────────────────────────

$versions = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase
)

if (-not (Test-Path $manifestPath)) {
    Write-Error "Issue template version manifest not found at: $manifestPath"
    exit 1
}

try {
    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
    foreach ($version in $manifest.versions) {
        if ($version -notmatch $semverPattern -or -not $versions.Add($version)) {
            Write-Error "Invalid or duplicate issue template version: '$version'."
            exit 1
        }
    }
} catch {
    Write-Error "Failed to parse issue template version manifest: $($_.Exception.Message)"
    exit 1
}

if ($versions.Count -eq 0) {
    Write-Error "No versions listed in $manifestPath. Cannot update templates."
    exit 1
}

if (-not (Test-Path $packageJsonPath)) {
    Write-Error "package.json not found at: $packageJsonPath"
    exit 1
}

try {
    $packageJson = Get-Content $packageJsonPath -Raw | ConvertFrom-Json
    $pkgVersion = [string]$packageJson.version
} catch {
    Write-Error "Failed to parse package.json: $($_.Exception.Message)"
    exit 1
}

if ($pkgVersion -notmatch $semverPattern) {
    Write-Error "package.json version '$pkgVersion' is not strict X.Y.Z semver."
    exit 1
}

$manifestChanged = $false
if (-not $versions.Contains($pkgVersion)) {
    if (-not $AddPackageVersion) {
        Write-Error "package.json version '$pkgVersion' is not listed in $manifestPath. Run release preparation to add it."
        exit 1
    }
    [void]$versions.Add($pkgVersion)
    $manifestChanged = $true
}

Write-Host "Total unique versions collected: $($versions.Count)"

# ── Sort descending by System.Version ───────────────────────────────────────

$parseable = [System.Collections.Generic.List[System.Version]]::new()
$unparseable = [System.Collections.Generic.List[string]]::new()

foreach ($v in $versions) {
    try {
        $parsed = [System.Version]::new($v)
        [void]$parseable.Add($parsed)
    } catch {
        [void]$unparseable.Add($v)
    }
}

$parseable.Sort()
$parseable.Reverse()

$unparseable.Sort()
$unparseable.Reverse()

$sortedVersions = [System.Collections.Generic.List[string]]::new()
foreach ($sv in $parseable) {
    [void]$sortedVersions.Add($sv.ToString(3))
}
foreach ($uv in $unparseable) {
    [void]$sortedVersions.Add($uv)
}

Write-Host "Versions (sorted): $($sortedVersions -join ', ')"

# ── Update template files ──────────────────────────────────────────────────

$filesToStage = [System.Collections.Generic.List[string]]::new()

if ($manifestChanged) {
    $manifestContent = @{ versions = @($sortedVersions) } | ConvertTo-Json -Depth 2
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($manifestPath, "$manifestContent`n", $utf8NoBom)
    [void]$filesToStage.Add($manifestPath)
    Write-Host "Issue template version manifest: added $pkgVersion."
}

foreach ($templatePath in $templateFiles) {
    $templateName = Split-Path -Leaf $templatePath
    if (-not (Test-Path $templatePath)) {
        Write-Warning "Template file not found, skipping: $templatePath"
        continue
    }

    # Read raw content preserving line endings
    $rawContent = [System.IO.File]::ReadAllText($templatePath)

    # Normalise to LF for consistent processing
    $content = $rawContent -replace "`r`n", "`n"

    # Locate sentinel comments
    $startIdx = $content.IndexOf($sentinelStart)
    $endIdx = $content.IndexOf($sentinelEnd)

    if ($startIdx -lt 0 -or $endIdx -lt 0) {
        Write-Warning "Sentinel comments not found in $templateName, skipping."
        continue
    }

    if ($endIdx -le $startIdx) {
        Write-Warning "Sentinel comments are misordered in $templateName, skipping."
        continue
    }

    # Find the start-of-line for the opening sentinel
    $lineStart = $content.LastIndexOf("`n", $startIdx)
    if ($lineStart -lt 0) {
        $lineStart = 0
    } else {
        $lineStart += 1  # skip the newline character itself
    }

    # Detect indentation from existing sentinel line
    $detectedIndent = $content.Substring($lineStart, $startIdx - $lineStart)

    # Build options block using detected indent
    $optionLines = [System.Collections.Generic.List[string]]::new()
    [void]$optionLines.Add("$detectedIndent$sentinelStart")
    foreach ($ver in $sortedVersions) {
        [void]$optionLines.Add("$detectedIndent- `"$ver`"")
    }
    [void]$optionLines.Add("$detectedIndent- `"Other`"")
    [void]$optionLines.Add("$detectedIndent$sentinelEnd")

    $optionsBlock = $optionLines -join "`n"

    # Find the end-of-line for the closing sentinel
    $lineEnd = $content.IndexOf("`n", $endIdx)
    if ($lineEnd -lt 0) {
        $lineEnd = $content.Length
    }

    $before = $content.Substring(0, $lineStart)
    $after = $content.Substring($lineEnd)

    $updatedContent = $before + $optionsBlock + $after

    # Check if anything actually changed (case-sensitive to catch casing fixes)
    if ($updatedContent -ceq $content) {
        Write-Host "${templateName}: already up-to-date."
        continue
    }

    # Write with LF line endings (no BOM)
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($templatePath, $updatedContent, $utf8NoBom)

    Write-Host "${templateName}: updated version dropdown."
    [void]$filesToStage.Add($templatePath)
}

# ── Stage changed files ─────────────────────────────────────────────────────

if ($filesToStage.Count -gt 0 -and $gitAvailable) {
    $gitAddExitCode = Invoke-GitAddWithRetry -Items $filesToStage.ToArray() -IndexLockPath $repositoryInfo.IndexLockPath
    if ($gitAddExitCode -ne 0) {
        Write-Error "git add failed with exit code $gitAddExitCode."
        exit $gitAddExitCode
    }
    Write-Host "Staged: $($filesToStage -join ', ')"
} elseif ($filesToStage.Count -eq 0) {
    Write-Host "No template files were modified."
}

exit 0
