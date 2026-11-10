[CmdletBinding(PositionalBinding = $false)]
param(
    [string[]]$Paths,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingPaths,
    [string]$RepoRoot = '',
    [switch]$StagedOnly,
    [switch]$Fix,
    [switch]$VerboseOutput
)
$ErrorActionPreference = 'Stop'
$arguments = @('run', '--quiet', '--project', (Join-Path $PSScriptRoot 'python~'), '--locked', '--no-dev', 'python', (Join-Path $PSScriptRoot 'lint_no_regions.py'))
if (-not [string]::IsNullOrWhiteSpace($RepoRoot)) { $arguments += @('--repo-root', $RepoRoot) }
if ($StagedOnly) { $arguments += '--staged-only' }
if ($Fix) { $arguments += '--fix' }
if ($VerboseOutput) { $arguments += '--verbose' }
$selected = @($Paths) + @($RemainingPaths) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
if ($selected) { $arguments += @('--paths') + @($selected) }
& uv @arguments
exit $LASTEXITCODE
