<#
.SYNOPSIS
    Validates devcontainer.json formatter assignments are consistent with
    pre-commit hook file type handling.
.DESCRIPTION
    Checks that every language/file type formatted or linted in the pre-commit
    hook has a corresponding explicit "[language]" formatter entry in
    devcontainer.json. Reports missing entries as warnings, then exits with error code 1 if any are found.

    This prevents drift between the hook's formatting pipeline and the
    editor's formatter assignments.
.EXAMPLE
    pwsh -NoProfile -File scripts/validate-devcontainer-config.ps1
#>
[CmdletBinding()]
param(
    [switch]$VerboseOutput
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$devcontainerPath = Join-Path $repoRoot '.devcontainer' 'devcontainer.json'
$preCommitPath = Join-Path $repoRoot '.githooks' 'pre-commit'
$publishWorkflowPath = Join-Path $repoRoot '.github' 'workflows' 'build-publish-devcontainer.yml'
$validateWorkflowPath = Join-Path $repoRoot '.github' 'workflows' 'validate-devcontainer.yml'

# ── Validate files exist ────────────────────────────────────────────────────

if (-not (Test-Path $devcontainerPath)) {
    Write-Error "devcontainer.json not found at: $devcontainerPath"
    exit 1
}

if (-not (Test-Path $preCommitPath)) {
    Write-Error "pre-commit hook not found at: $preCommitPath"
    exit 1
}

if (-not (Test-Path $publishWorkflowPath)) {
    Write-Error "Devcontainer publish workflow not found at: $publishWorkflowPath"
    exit 1
}

if (-not (Test-Path $validateWorkflowPath)) {
    Write-Error "Devcontainer validation workflow not found at: $validateWorkflowPath"
    exit 1
}

# ── Read devcontainer.json ──────────────────────────────────────────────────

$devcontainerContent = Get-Content $devcontainerPath -Raw
$publishWorkflowContent = Get-Content $publishWorkflowPath -Raw
$validateWorkflowContent = Get-Content $validateWorkflowPath -Raw

$expectedImageName = 'ambiguous-interactive/unity-helpers/devcontainer'
$expectedImageReference = "ghcr.io/$expectedImageName"
$legacyImageReference = 'ghcr.io/wallstop/unity-helpers/devcontainer'

if ($devcontainerContent.Contains($legacyImageReference) -or $publishWorkflowContent.Contains($legacyImageReference)) {
    Write-Error "Devcontainer image references must use $expectedImageReference, not $legacyImageReference."
    exit 1
}

$devcontainerUsesCurrentCache = (
    $devcontainerContent.Contains("${expectedImageReference}:buildcache") -and
    $devcontainerContent.Contains("${expectedImageReference}:latest")
)
if (-not $devcontainerUsesCurrentCache) {
    Write-Error "devcontainer.json must cache from $expectedImageReference buildcache and latest tags."
    exit 1
}

$publishWorkflowUsesCurrentImage = $publishWorkflowContent.Contains("IMAGE_NAME: $expectedImageName")
if (-not $publishWorkflowUsesCurrentImage) {
    Write-Error "build-publish-devcontainer.yml must publish IMAGE_NAME: $expectedImageName."
    exit 1
}

$publishWorkflowHasPackagePermission = $publishWorkflowContent.Contains('packages: write')
if (-not $publishWorkflowHasPackagePermission) {
    Write-Error "build-publish-devcontainer.yml must grant packages: write for GHCR publishing."
    exit 1
}

$publishWorkflowHasSourceLabel = $publishWorkflowContent.Contains('org.opencontainers.image.source=https://github.com/${{ github.repository }}')
if (-not $publishWorkflowHasSourceLabel) {
    Write-Error "build-publish-devcontainer.yml must publish an org.opencontainers.image.source label so GHCR links the image to this repository."
    exit 1
}

$publishWorkflowRunsConfigValidation = $publishWorkflowContent.Contains('./scripts/validate-devcontainer-config.ps1 -VerboseOutput')
if (-not $publishWorkflowRunsConfigValidation) {
    Write-Error "build-publish-devcontainer.yml must run validate-devcontainer-config.ps1 before publishing to GHCR."
    exit 1
}

$validateWorkflowWatchesPublishWorkflow = $validateWorkflowContent.Contains('.github/workflows/build-publish-devcontainer.yml')
if (-not $validateWorkflowWatchesPublishWorkflow) {
    Write-Error "validate-devcontainer.yml must run when build-publish-devcontainer.yml changes."
    exit 1
}

if ($VerboseOutput) {
    Write-Host "Devcontainer image namespace and GHCR publish metadata are valid." -ForegroundColor Green
}

# Extract all "[language]" entries from devcontainer.json
# Matches patterns like: "[javascript]", "[csharp]", etc.
$languageEntries = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase
)
$languagePattern = '"\[(\w+)\]"'
$matches = [regex]::Matches($devcontainerContent, $languagePattern)
foreach ($match in $matches) {
    [void]$languageEntries.Add($match.Groups[1].Value)
}

if ($VerboseOutput) {
    Write-Host "Found devcontainer formatter entries for: $($languageEntries -join ', ')"
}

# ── Define expected language/formatter mappings ─────────────────────────────
# MAINTENANCE: When adding a new language/file type to the pre-commit hook,
# add a corresponding entry here so the validator catches missing devcontainer
# formatter assignments. Keep this list in sync with the hook's file type arrays.

$expectedLanguages = @(
    @{ Language = 'csharp'; FilePattern = '*.cs'; Formatter = 'csharpier.csharpier-vscode' },
    @{ Language = 'json'; FilePattern = '*.json'; Formatter = 'esbenp.prettier-vscode' },
    @{ Language = 'jsonc'; FilePattern = '*.jsonc'; Formatter = 'esbenp.prettier-vscode' },
    @{ Language = 'yaml'; FilePattern = '*.yml/*.yaml'; Formatter = 'esbenp.prettier-vscode' },
    @{ Language = 'markdown'; FilePattern = '*.md'; Formatter = 'esbenp.prettier-vscode' },
    @{ Language = 'javascript'; FilePattern = '*.js'; Formatter = 'esbenp.prettier-vscode' },
    @{ Language = 'xml'; FilePattern = '*.xml'; Formatter = '(formatOnSave: false)' },
    @{ Language = 'shellscript'; FilePattern = '*.sh'; Formatter = '(formatOnSave: false)' },
    @{ Language = 'powershell'; FilePattern = '*.ps1'; Formatter = '(formatOnSave: false)' },
    @{ Language = 'hlsl'; FilePattern = '*.hlsl'; Formatter = '(formatOnSave: false)' },
    @{ Language = 'shaderlab'; FilePattern = '*.shader'; Formatter = '(formatOnSave: false)' }
)

# ── Check for missing entries ───────────────────────────────────────────────

$missing = @()
foreach ($entry in $expectedLanguages) {
    if (-not $languageEntries.Contains($entry.Language)) {
        $missing += $entry
    }
}

if ($missing.Count -gt 0) {
    Write-Host ''
    Write-Warning "The following languages are handled by pre-commit but have no explicit devcontainer.json formatter entry:"
    foreach ($m in $missing) {
        Write-Warning "  [$($m.Language)] ($($m.FilePattern)) - expected formatter: $($m.Formatter)"
    }
    Write-Host ''
    Write-Error "devcontainer.json is missing $($missing.Count) explicit formatter assignment(s). See warnings above."
    exit 1
}

if ($VerboseOutput) {
    Write-Host "All expected formatter assignments are present in devcontainer.json." -ForegroundColor Green
}
exit 0
