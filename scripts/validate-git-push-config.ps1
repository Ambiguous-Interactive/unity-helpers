# Compatibility entry point; read-only Git checks run through locked Python.
[CmdletBinding(PositionalBinding = $false)]
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$RemainingArguments
)
$ErrorActionPreference = 'Stop'
if ($RemainingArguments -and $RemainingArguments.Count -gt 0) {
    [Console]::Error.WriteLine('Unexpected push validation arguments.')
    exit 1
}
$arguments = @('run', '--quiet', '--project', (Join-Path $PSScriptRoot 'python~'), '--locked', '--no-dev',
    'python', (Join-Path $PSScriptRoot 'push_validation.py'), "--repo-root=$RepoRoot")
& uv @arguments
exit $LASTEXITCODE
