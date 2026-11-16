param([Parameter(Mandatory = $true)][string]$Version, [string]$RepoRoot = (Join-Path $PSScriptRoot '../..'), [string]$OutputPath = '', [switch]$Footer)
$ErrorActionPreference = 'Stop'
$arguments = @('run', '--quiet', '--project', (Join-Path $PSScriptRoot '../python~'), '--locked', '--no-dev', 'python', (Join-Path $PSScriptRoot '../release_tools.py'), 'notes', "--version=$Version", '--repo-root', $RepoRoot)
if ($OutputPath) { $arguments += @('--output-path', $OutputPath) }
if ($Footer) { $arguments += '--footer' }
& uv @arguments
exit $LASTEXITCODE
