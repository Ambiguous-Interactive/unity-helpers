param([ValidateSet('major', 'minor', 'patch')][string]$Bump = 'patch', [string]$Version = '', [string]$Date = (Get-Date -Format 'yyyy-MM-dd'), [string]$RepoRoot = (Join-Path $PSScriptRoot '../..'), [switch]$DryRun)
$ErrorActionPreference = 'Stop'
$arguments = @('run', '--quiet', '--project', (Join-Path $PSScriptRoot '../python~'), '--locked', '--no-dev', 'python', (Join-Path $PSScriptRoot '../release_tools.py'), 'prepare', '--bump', $Bump, "--version=$Version", '--date', $Date, '--repo-root', $RepoRoot)
if ($DryRun) { $arguments += '--dry-run' }
& uv @arguments
exit $LASTEXITCODE
