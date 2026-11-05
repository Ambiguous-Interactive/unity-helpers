[CmdletBinding()]
param([switch]$Check)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$mode = if ($Check) { 'check' } else { 'sync' }
$arguments = @(
    'run', '--quiet', '--project', (Join-Path $PSScriptRoot 'python~'), '--locked', '--no-dev',
    'python', (Join-Path $PSScriptRoot 'doc_counts.py'), $mode
)
& uv @arguments
exit $LASTEXITCODE
