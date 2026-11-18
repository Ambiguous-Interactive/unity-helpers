param(
  [string]$RepoRoot,
  [switch]$VerboseOutput
)

$ErrorActionPreference = 'Stop'
$arguments = @('run', '--quiet', '--project', (Join-Path $PSScriptRoot 'python~'), '--locked', '--no-dev', '--group', 'documentation', 'python', (Join-Path $PSScriptRoot 'validate_mcp_config.py'))
if (-not [string]::IsNullOrWhiteSpace($RepoRoot)) { $arguments += @('--repo-root', $RepoRoot) }
if ($VerboseOutput) { $arguments += '--verbose' }
& uv @arguments
exit $LASTEXITCODE
