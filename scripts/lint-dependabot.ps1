[CmdletBinding(PositionalBinding = $false)]
param(
    [string[]]$Paths,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AdditionalPaths,
    [string]$RepoRoot = '',
    [switch]$VerboseOutput
)
$ErrorActionPreference = 'Stop'
$arguments = @('run', '--quiet', '--project', (Join-Path $PSScriptRoot 'python~'), '--locked', '--no-dev', '--group', 'metadata', 'python', (Join-Path $PSScriptRoot 'lint_dependabot.py'))
if (-not [string]::IsNullOrWhiteSpace($RepoRoot)) { $arguments += @('--repo-root', $RepoRoot) }
if ($VerboseOutput) { $arguments += '--verbose' }
$selected = @($Paths) + @($AdditionalPaths) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
if ($selected) { $arguments += @('--paths') + @($selected) }
& uv @arguments
exit $LASTEXITCODE
