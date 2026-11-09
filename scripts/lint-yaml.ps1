[CmdletBinding(PositionalBinding = $false)]
param(
    [switch]$StagedOnly,
    [switch]$VerboseOutput,
    [string[]]$Paths,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AdditionalPaths
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$arguments = @(
    'run', '--quiet', '--project', (Join-Path $PSScriptRoot 'python~'), '--locked', '--no-dev',
    '--group', 'metadata', 'python', (Join-Path $PSScriptRoot 'lint_yaml.py')
)
if ($StagedOnly) { $arguments += '--staged-only' }
if ($VerboseOutput) { $arguments += '--verbose' }
$selectedPaths = @($Paths) + @($AdditionalPaths) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
if ($selectedPaths) { $arguments += @('--paths') + @($selectedPaths) }
& uv @arguments
exit $LASTEXITCODE
