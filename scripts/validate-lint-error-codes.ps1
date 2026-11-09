param([switch]$VerboseOutput, [string]$NodeCommand = 'node', [string]$RepoRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$arguments = @('run', '--quiet', '--project', (Join-Path $PSScriptRoot 'python~'), '--locked', '--no-dev', 'python', (Join-Path $PSScriptRoot 'validate_lint_error_codes.py'), '--node-command', $NodeCommand)
if ($VerboseOutput) { $arguments += '--verbose' }
if (-not [string]::IsNullOrWhiteSpace($RepoRoot)) { $arguments += '--repo-root'; $arguments += $RepoRoot }
& uv @arguments
exit $LASTEXITCODE
