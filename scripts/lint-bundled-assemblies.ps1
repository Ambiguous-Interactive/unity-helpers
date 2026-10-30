#!/usr/bin/env pwsh
[CmdletBinding(PositionalBinding = $false)]
param(
    [string]$BinariesRoot,
    [switch]$VerboseOutput,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArguments
)
$ErrorActionPreference = 'Stop'
if ($RemainingArguments) { throw "Unexpected arguments: $($RemainingArguments -join ', ')" }
$arguments = @()
if (-not [string]::IsNullOrWhiteSpace($BinariesRoot)) { $arguments += @('--binaries-root', $BinariesRoot) }
if ($VerboseOutput) { $arguments += '--verbose' }
& uv run --quiet --project (Join-Path $PSScriptRoot 'python~') --locked --no-dev python (Join-Path $PSScriptRoot 'lint_bundled_assemblies.py') @arguments
exit $LASTEXITCODE
