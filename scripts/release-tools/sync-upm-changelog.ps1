param([string]$RepoRoot = (Join-Path $PSScriptRoot '../..'), [switch]$Check)
$ErrorActionPreference = 'Stop'
$arguments = @('run', '--quiet', '--project', (Join-Path $PSScriptRoot '../python~'), '--locked', '--no-dev', 'python', (Join-Path $PSScriptRoot '../release_tools.py'), 'sync-upm', '--repo-root', $RepoRoot)
if ($Check) { $arguments += '--check' }
& uv @arguments
exit $LASTEXITCODE
