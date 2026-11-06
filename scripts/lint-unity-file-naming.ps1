[CmdletBinding()]
param(
    [switch]$VerboseOutput,
    [switch]$StagedOnly,
    [string]$RepoRoot = (Get-Location).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$arguments = @(
    'run', '--quiet', '--project', (Join-Path $PSScriptRoot 'python~'), '--locked', '--no-dev',
    'python', (Join-Path $PSScriptRoot 'lint_unity_file_naming.py'), '--repo-root', $RepoRoot
)
if ($VerboseOutput) { $arguments += '--verbose' }
if ($StagedOnly) { $arguments += '--staged-only' }
& uv @arguments
exit $LASTEXITCODE
