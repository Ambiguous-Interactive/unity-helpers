param([switch]$VerboseOutput, [string]$TestFilter = 'signature')
$ErrorActionPreference = 'Stop'
$arguments = @('run', '--quiet', '--project', (Join-Path $PSScriptRoot '../python~'), '--locked', 'pytest', (Join-Path $PSScriptRoot 'test_npm_package.py'), '-q', '-k', $TestFilter)
& uv @arguments
exit $LASTEXITCODE
