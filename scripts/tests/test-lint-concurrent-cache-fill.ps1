[CmdletBinding()]
param([switch]$VerboseOutput, [string]$TestFilter)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$arguments = @(
    'run', '--quiet', '--project', (Join-Path $PSScriptRoot '../python~'), '--locked',
    'pytest', (Join-Path $PSScriptRoot 'test_concurrent_cache_fill.py')
)
if ($VerboseOutput) { $arguments += '-v' } else { $arguments += '-q' }
if (-not [string]::IsNullOrWhiteSpace($TestFilter)) { $arguments += @('-k', $TestFilter) }
& uv @arguments
exit $LASTEXITCODE
