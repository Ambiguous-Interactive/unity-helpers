param([switch]$VerboseOutput)
$ErrorActionPreference = 'Stop'
$arguments = @('run', '--quiet', '--project', (Join-Path $PSScriptRoot 'python~'), '--locked', '--no-dev', 'python', (Join-Path $PSScriptRoot 'validate_npm_package.py'))
if ($VerboseOutput) { $arguments += '--verbose' }
& uv @arguments
exit $LASTEXITCODE
