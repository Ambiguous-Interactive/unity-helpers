param([switch]$VerboseOutput)
$ErrorActionPreference = 'Stop'
$arguments = @()
if ($VerboseOutput) { $arguments += '--verbose' }
& uv run --quiet --project (Join-Path $PSScriptRoot 'python~') --locked --no-dev python (Join-Path $PSScriptRoot 'lint_asmdef.py') @arguments
exit $LASTEXITCODE
