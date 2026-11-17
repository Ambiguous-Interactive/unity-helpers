param([switch]$VerboseOutput, [string]$TestFilter)
$ErrorActionPreference = 'Stop'
$arguments = @('run', '--quiet', '--project', (Join-Path $PSScriptRoot '../python~'), '--locked', 'pytest', (Join-Path $PSScriptRoot 'test_devcontainer_config.py'), '-q')
if (-not [string]::IsNullOrWhiteSpace($TestFilter)) { $arguments += @('-k', $TestFilter) }
if ($VerboseOutput) { $arguments += '-v' }
& uv @arguments
exit $LASTEXITCODE
