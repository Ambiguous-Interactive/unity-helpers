[CmdletBinding()]
param([switch]$VerboseOutput)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $PSScriptRoot
$arguments = @(
    'run', '--quiet', '--project', (Join-Path $scriptRoot 'python~'), '--locked',
    'pytest', (Join-Path $PSScriptRoot 'test_unity_file_naming.py')
)
if ($VerboseOutput) { $arguments += '-v' } else { $arguments += '-q' }
& uv @arguments
exit $LASTEXITCODE
