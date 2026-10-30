param([switch]$VerboseOutput)
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
& uv run --quiet --project (Join-Path $repository 'scripts/python~') --locked pytest (Join-Path $PSScriptRoot 'test_bundled_assemblies.py') -q
exit $LASTEXITCODE
