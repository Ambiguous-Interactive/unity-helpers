param([switch]$VerboseOutput, [string]$TestFilter)
$ErrorActionPreference = 'Stop'
$arguments = @('run', '--quiet', '--project', (Join-Path $PSScriptRoot '../python~'), '--locked', 'pytest', (Join-Path $PSScriptRoot 'test_code_samples.py'), '-q')
if (-not [string]::IsNullOrWhiteSpace($TestFilter)) { $arguments += '-k'; $arguments += $TestFilter }
& uv @arguments
exit $LASTEXITCODE
