[CmdletBinding()]
param(
    [switch]$VerboseOutput,
    [string]$RepoRoot = (Get-Location).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$arguments = @(
    'run', '--quiet', '--project', (Join-Path $PSScriptRoot 'python~'), '--locked', '--no-dev', '--group', 'metadata',
    'python', (Join-Path $PSScriptRoot 'lint_gitignore_docs.py'), '--repo-root', $RepoRoot
)
if ($VerboseOutput) { $arguments += '--verbose' }
& uv @arguments
exit $LASTEXITCODE
