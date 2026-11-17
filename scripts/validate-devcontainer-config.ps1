[CmdletBinding(PositionalBinding = $false)]
param(
    [string]$RepoRoot,
    [switch]$VerboseOutput,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArguments
)
$ErrorActionPreference = 'Stop'
if ($RemainingArguments -and $RemainingArguments.Count -gt 0) {
    Write-Error "Unexpected arguments: $($RemainingArguments -join ', ')"
    exit 1
}
$arguments = @('run', '--quiet', '--project', (Join-Path $PSScriptRoot 'python~'), '--locked', '--no-dev', 'python', (Join-Path $PSScriptRoot 'validate_devcontainer_config.py'))
if (-not [string]::IsNullOrWhiteSpace($RepoRoot)) { $arguments += @('--repo-root', $RepoRoot) }
if ($VerboseOutput) { $arguments += '--verbose' }
& uv @arguments
exit $LASTEXITCODE
