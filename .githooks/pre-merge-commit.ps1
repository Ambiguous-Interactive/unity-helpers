#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$HookArgs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$hookDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$preCommit = Join-Path $hookDir 'pre-commit.ps1'

if (-not (Test-Path -LiteralPath $preCommit -PathType Leaf)) {
    Write-Host "Error: pre-commit PowerShell implementation not found at $preCommit" -ForegroundColor Red
    Write-Host 'Run npm run hooks:install to restore tracked hooks.' -ForegroundColor Cyan
    exit 1
}

& $preCommit @HookArgs
exit $LASTEXITCODE
