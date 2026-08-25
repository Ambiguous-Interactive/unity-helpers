<#
.SYNOPSIS
    Validates that documentation counts match actual codebase metrics.
.DESCRIPTION
    Runs sync-doc-counts.ps1 in check mode to verify that all documentation
    files have correct counts for tests, PRNGs, editor tools, etc.

    Exits with code 1 if any counts are out of sync.
.PARAMETER SyncScriptPath
    Path to sync-doc-counts.ps1. Defaults to the copy beside this script. Exists so the self-test
    can drive both of this wrapper's rules -- the missing-script guard and the exit-code
    propagation -- without a doc tree that is valid by construction.
.EXAMPLE
    pwsh -NoProfile -File scripts/lint-doc-counts.ps1
#>
# lint-pwsh-invocations: allow-subprocess-pwsh sync-doc-counts.ps1 uses `exit` extensively for CI exit-code propagation; subprocess isolation preserves that contract without tangling parent host state.
[CmdletBinding(PositionalBinding = $false)]
param(
    [string]$SyncScriptPath,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArguments
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($RemainingArguments -and $RemainingArguments.Count -gt 0) {
    Write-Error "Unexpected arguments: $($RemainingArguments -join ', ')"
    exit 1
}

$syncScript = if ([string]::IsNullOrWhiteSpace($SyncScriptPath)) {
    Join-Path $PSScriptRoot 'sync-doc-counts.ps1'
} else {
    $SyncScriptPath
}

if (-not (Test-Path $syncScript)) {
    Write-Error "sync-doc-counts.ps1 not found at: $syncScript"
    exit 1
}

& pwsh -NoProfile -File $syncScript -Check
exit $LASTEXITCODE
