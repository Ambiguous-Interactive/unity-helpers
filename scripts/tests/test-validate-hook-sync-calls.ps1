param([switch]$VerboseOutput, [string]$TestFilter = '')
$ErrorActionPreference = 'Stop'
$arguments = @('run', '--quiet', '--project', (Join-Path $PSScriptRoot '../python~'), '--locked',
    'pytest', (Join-Path $PSScriptRoot 'test_hook_validation.py'), '-q')
if ($VerboseOutput) { $arguments += '-v' }
if ($TestFilter) { $arguments += @('-k', $TestFilter) }
& uv @arguments
exit $LASTEXITCODE
