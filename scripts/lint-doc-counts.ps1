<#
.SYNOPSIS
    Validates that documentation counts match actual codebase metrics.
.DESCRIPTION
    Runs the typed Python documentation count validator through locked uv.

    Exits with code 1 if any counts are out of sync.
.PARAMETER SyncScriptPath
    Optional legacy sync script override, called with -Check in an isolated PowerShell process.
.EXAMPLE
    pwsh -NoProfile -File scripts/lint-doc-counts.ps1
#>
# lint-pwsh-invocations: allow-subprocess-pwsh Explicit legacy sync-script overrides retain isolated exit-code propagation.
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

if (-not [string]::IsNullOrWhiteSpace($SyncScriptPath)) {
    if (-not (Test-Path -LiteralPath $SyncScriptPath -PathType Leaf)) {
        Write-Error "sync-doc-counts.ps1 not found at: $SyncScriptPath"
        exit 1
    }
    & pwsh -NoProfile -File $SyncScriptPath -Check
    exit $LASTEXITCODE
}
$arguments = @(
    'run', '--quiet', '--project', (Join-Path $PSScriptRoot 'python~'), '--locked', '--no-dev',
    'python', (Join-Path $PSScriptRoot 'doc_counts.py'), 'check'
)
& uv @arguments
exit $LASTEXITCODE
