<#
.SYNOPSIS
    Validates that pre-commit hook calls all required sync scripts and
    pre-push hook reads stdin for changed-file detection.
.DESCRIPTION
    Checks that the pre-commit hook invokes all required version sync scripts:
      - sync-banner-version.ps1
      - sync-issue-template-versions.ps1

    Also validates the pre-push hook reads stdin to determine changed files
    (critical for performance — without this, pre-push scans the entire repo).

    This prevents regressions where new sync scripts are added to the
    repository but not wired into the pre-commit hook.
.EXAMPLE
    pwsh -NoProfile -File scripts/validate-hook-sync-calls.ps1
#>
[CmdletBinding()]
param(
    [switch]$VerboseOutput
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$preCommitPath = Join-Path $repoRoot '.githooks' 'pre-commit.ps1'

if (-not (Test-Path $preCommitPath)) {
    Write-Error "pre-commit PowerShell implementation not found at: $preCommitPath"
    exit 1
}

$hookContent = Get-Content $preCommitPath -Raw

# Define required sync script calls
# MAINTENANCE: When adding a new sync script to the repository, add it here
# so the validator catches missing invocations in the pre-commit hook.
$requiredSyncScripts = @(
    'scripts/sync-banner-version.ps1',
    'scripts/sync-issue-template-versions.ps1'
)

$missing = @()
foreach ($script in $requiredSyncScripts) {
    if ($hookContent -notmatch [regex]::Escape($script)) {
        $missing += $script
    }
}

if ($missing.Count -gt 0) {
    Write-Host ''
    Write-Warning "The pre-commit hook is missing calls to the following sync scripts:"
    foreach ($m in $missing) {
        Write-Warning "  - $m"
    }
    Write-Host ''
    Write-Error "Pre-commit hook is missing $($missing.Count) required sync script call(s). Add them to .githooks/pre-commit.ps1."
    exit 1
}

if ($VerboseOutput) {
    Write-Host "Pre-commit hook correctly invokes all required sync scripts." -ForegroundColor Green
}

# ---- Validate pre-push hook reads stdin for changed-file detection ----
$prePushPath = Join-Path $repoRoot '.githooks' 'pre-push.ps1'

if (-not (Test-Path $prePushPath)) {
    Write-Error "pre-push PowerShell implementation not found at: $prePushPath"
    exit 1
}

$prePushContent = Get-Content $prePushPath -Raw

# The pre-push hook MUST read stdin to determine changed files.
# Without this, last-resort checks can drift into broad repository scans.
$requiredPrePushPatterns = @(
    @{ Pattern = '[Console]::In.ReadToEnd'; Description = 'reads stdin from git pre-push' },
    @{ Pattern = 'localSha'; Description = 'parses local SHA from stdin' },
    @{ Pattern = 'remoteSha'; Description = 'parses remote SHA from stdin' },
    @{ Pattern = 'allChanged'; Description = 'stores changed files in a set' }
)

$prePushMissing = @()
foreach ($entry in $requiredPrePushPatterns) {
    if ($prePushContent -notmatch [regex]::Escape($entry.Pattern)) {
        $prePushMissing += $entry
    }
}

if ($prePushMissing.Count -gt 0) {
    Write-Host ''
    Write-Warning "The pre-push hook is missing required changed-file detection patterns:"
    foreach ($m in $prePushMissing) {
        Write-Warning "  - $($m.Description) (expected: '$($m.Pattern)')"
    }
    Write-Host ''
    Write-Error "Pre-push hook is missing $($prePushMissing.Count) required pattern(s). The hook must read stdin to detect changed files for performance."
    exit 1
}

if ($VerboseOutput) {
    Write-Host "Pre-push hook correctly reads stdin for changed-file detection." -ForegroundColor Green
}

# ---- Validate pre-merge-commit hook delegates to pre-commit ----
# Git does not run pre-commit for merge commits; the pre-merge-commit hook is
# the merge-time equivalent. Without delegation, merge commits bypass fast
# last-resort checks such as EOL, metadata, LLM instruction, and C# region
# validation.
$preMergeCommitPath = Join-Path $repoRoot '.githooks' 'pre-merge-commit.ps1'

if (-not (Test-Path $preMergeCommitPath)) {
    Write-Error "pre-merge-commit PowerShell implementation not found at: $preMergeCommitPath. Merge commits will bypass pre-commit validation."
    exit 1
}

$preMergeContent = Get-Content $preMergeCommitPath -Raw

$requiredMergePatterns = @(
    @{ Pattern = 'pre-commit.ps1'; Description = 'references pre-commit PowerShell implementation' },
    @{ Pattern = '& $preCommit @HookArgs'; Description = 'delegates in-process so merge commits run pre-commit validation' },
    @{ Pattern = 'exit $LASTEXITCODE'; Description = 'preserves pre-commit exit code' }
)

$mergeMissing = @()
foreach ($entry in $requiredMergePatterns) {
    if ($preMergeContent -notmatch [regex]::Escape($entry.Pattern)) {
        $mergeMissing += $entry
    }
}

if ($mergeMissing.Count -gt 0) {
    Write-Host ''
    Write-Warning "The pre-merge-commit hook is missing required delegation patterns:"
    foreach ($m in $mergeMissing) {
        Write-Warning "  - $($m.Description) (expected: '$($m.Pattern)')"
    }
    Write-Host ''
    Write-Error "Pre-merge-commit hook is missing $($mergeMissing.Count) required pattern(s). Without delegation, merge commits bypass pre-commit validation."
    exit 1
}

$forbiddenMergePatterns = @(
    @{ Pattern = 'Get-Process -Id $PID'; Description = 'resolves current PowerShell executable for a second startup' },
    @{ Pattern = 'Get-Command pwsh'; Description = 'searches for a second PowerShell executable' },
    @{ Pattern = '& $pwshPath'; Description = 'spawns another PowerShell process' },
    @{ Pattern = '$invokeArgs +='; Description = 'builds subprocess PowerShell arguments' }
)

$mergeForbidden = @()
foreach ($entry in $forbiddenMergePatterns) {
    if ($preMergeContent -match [regex]::Escape($entry.Pattern)) {
        $mergeForbidden += $entry
    }
}

if ($mergeForbidden.Count -gt 0) {
    Write-Host ''
    Write-Warning "The pre-merge-commit hook still contains second-startup delegation patterns:"
    foreach ($m in $mergeForbidden) {
        Write-Warning "  - $($m.Description) (forbidden: '$($m.Pattern)')"
    }
    Write-Host ''
    Write-Error "Pre-merge-commit hook must delegate to pre-commit.ps1 in-process to keep merge hooks fast."
    exit 1
}

if ($VerboseOutput) {
    Write-Host "Pre-merge-commit hook correctly delegates to pre-commit." -ForegroundColor Green
}

exit 0
