[CmdletBinding(PositionalBinding = $false)]
param(
    [string[]]$Paths,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingPaths,
    [string]$RepoRoot = '',
    [switch]$Fix,
    [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$arguments = @(
    'run', '--quiet', '--project', (Join-Path $PSScriptRoot 'python~'), '--locked', '--no-dev',
    'python', (Join-Path $PSScriptRoot 'lint_comment_block_form.py')
)
if (-not [string]::IsNullOrWhiteSpace($RepoRoot)) { $arguments += @('--repo-root', $RepoRoot) }
if ($Fix) { $arguments += '--fix' }
if ($VerboseOutput) { $arguments += '--verbose' }
$selectedPaths = @($Paths) + @($RemainingPaths) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
if ($selectedPaths) { $arguments += @('--paths') + @($selectedPaths) }
& uv @arguments
exit $LASTEXITCODE
