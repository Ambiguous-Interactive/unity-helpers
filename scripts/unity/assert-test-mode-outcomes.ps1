#!/usr/bin/env pwsh
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

& uv run --quiet --project (Join-Path $PSScriptRoot '../python~') --locked --no-dev python (Join-Path $PSScriptRoot 'assert_test_mode_outcomes.py')
exit $LASTEXITCODE
