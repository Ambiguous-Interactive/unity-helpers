# Compatibility entry point; hook text rules run through locked Python.
[CmdletBinding(PositionalBinding = $false)]
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [switch]$VerboseOutput,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$RemainingArguments
)
$ErrorActionPreference = 'Stop'
if ($RemainingArguments -and $RemainingArguments.Count -gt 0) {
    [Console]::Error.WriteLine('Unexpected hook validation arguments.')
    exit 1
}
$resolvedRepoRoot = if ([string]::IsNullOrWhiteSpace($RepoRoot)) { Split-Path -Parent $PSScriptRoot } else { $RepoRoot }
$arguments = @('run', '--quiet', '--project', (Join-Path $PSScriptRoot 'python~'), '--locked', '--no-dev',
    'python', (Join-Path $PSScriptRoot 'hook_validation.py'), "--repo-root=$resolvedRepoRoot")
if ($VerboseOutput) { $arguments += '--verbose' }
& uv @arguments
exit $LASTEXITCODE
