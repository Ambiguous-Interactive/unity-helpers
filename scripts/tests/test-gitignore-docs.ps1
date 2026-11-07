[CmdletBinding()]
param([switch]$VerboseOutput, [string]$TestFilter)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$arguments = @(
    'run', '--quiet', '--project', (Join-Path $PSScriptRoot '../python~'), '--locked',
    'pytest', (Join-Path $PSScriptRoot 'test_gitignore_docs.py')
)
if ($VerboseOutput) { $arguments += '-v' } else { $arguments += '-q' }
if (-not [string]::IsNullOrWhiteSpace($TestFilter)) { $arguments += @('-k', $TestFilter) }
& uv @arguments
exit $LASTEXITCODE
