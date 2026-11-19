#!/usr/bin/env pwsh
param([switch]$VerboseOutput)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$culture = [System.Globalization.CultureInfo]::CurrentCulture.Name
$arguments = @(
    'run', '--quiet', '--project', (Join-Path $PSScriptRoot 'python~'), '--locked', '--no-dev',
    'python', (Join-Path $PSScriptRoot 'lint_pwsh_invocations.py'), "--culture=$culture"
)
if ($PSVersionTable.PSVersion.Major -ge 7) { $arguments += "--pshome=$PSHOME" }
if ($VerboseOutput) { $arguments += '--verbose' }
$previousEncoding = [Console]::OutputEncoding
try {
    [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
    $uv = @(Get-Command uv -CommandType Application)[0].Source
    & $uv @arguments
    $status = $LASTEXITCODE
    if ($status -notin @(0, 1)) { throw "PowerShell invocation policy failed (exit $status)." }
}
catch {
    [Console]::Error.WriteLine("[lint-pwsh-invocations] Checks failed: " + $_.Exception.Message)
    $status = 1
}
finally {
    [Console]::OutputEncoding = $previousEncoding
}
exit $status
