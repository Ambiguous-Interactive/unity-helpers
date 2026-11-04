[CmdletBinding()]
param([switch]$VerboseOutput)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$arguments = @(
    'run', '--quiet', '--project', (Join-Path $PSScriptRoot '..' 'python~'), '--locked',
    'pytest', (Join-Path $PSScriptRoot 'test_csharp_lints.py'), '-k', 'odin'
)
if ($VerboseOutput) { $arguments += '-v' } else { $arguments += '-q' }
& uv @arguments
exit $LASTEXITCODE
