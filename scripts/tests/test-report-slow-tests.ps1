Param([switch]$VerboseOutput)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
& uv run --quiet --project (Join-Path $PSScriptRoot '../python~') --locked pytest (Join-Path $PSScriptRoot 'test_slow_tests.py') -q
exit $LASTEXITCODE
