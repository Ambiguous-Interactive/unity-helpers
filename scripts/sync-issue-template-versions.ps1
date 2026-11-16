# Compatibility entry point; content rules run in the locked typed implementation.
[CmdletBinding(PositionalBinding = $false)]
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [switch]$AddPackageVersion,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$RemainingArguments
)
$ErrorActionPreference = 'Stop'
if ($RemainingArguments -and $RemainingArguments.Count -gt 0) {
    [Console]::Error.WriteLine('Unexpected version synchronization arguments.')
    exit 1
}
$arguments = @('run', '--quiet', '--project', (Join-Path $PSScriptRoot 'python~'), '--locked', '--no-dev',
    'python', (Join-Path $PSScriptRoot 'version_sync.py'), 'templates', "--repo-root=$RepoRoot")
if ($AddPackageVersion) { $arguments += '--add-package-version' }
& uv @arguments
exit $LASTEXITCODE
