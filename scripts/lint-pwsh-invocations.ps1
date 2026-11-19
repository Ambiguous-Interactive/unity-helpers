#!/usr/bin/env pwsh
param([switch]$VerboseOutput)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$culture = [System.Globalization.CultureInfo]::CurrentCulture.Name
$arguments = @(
    'run', '--quiet', '--project', (Join-Path $PSScriptRoot 'python~'), '--locked', '--no-dev',
    'python', (Join-Path $PSScriptRoot 'lint_pwsh_invocations.py'), "--culture=$culture", "--pshome=$PSHOME"
)
if ($VerboseOutput) { $arguments += '--verbose' }
try {
    $uv = @(Get-Command uv -CommandType Application)[0].Source
    & $uv @arguments
    if ($LASTEXITCODE -in @(0, 1)) { exit $LASTEXITCODE }
    throw "PowerShell invocation policy failed (exit $LASTEXITCODE)."
}
catch {
    [Console]::Error.WriteLine("[lint-pwsh-invocations] Checks failed: " + $_.Exception.Message)
    exit 1
}
