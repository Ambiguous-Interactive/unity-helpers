param([switch]$VerboseOutput)
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$verbosity = if ($VerboseOutput) { '-v' } else { '-q' }
& uv run --quiet --project (Join-Path $repository 'scripts/python~') --locked pytest (Join-Path $PSScriptRoot 'test_preserve_attributes.py') $verbosity
exit $LASTEXITCODE
