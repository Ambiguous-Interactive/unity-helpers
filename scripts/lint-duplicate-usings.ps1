[CmdletBinding(PositionalBinding = $false)]
param(
    [switch]$VerboseOutput,
    [switch]$StagedOnly,
    [string[]]$Paths,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AdditionalPaths
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$arguments = @(
    'run', '--quiet', '--project', (Join-Path $PSScriptRoot 'python~'), '--locked', '--no-dev',
    'python', (Join-Path $PSScriptRoot 'lint_duplicate_usings.py')
)
if ($VerboseOutput) { $arguments += '--verbose' }
if ($StagedOnly) { $arguments += '--staged-only' }
$selectedPaths = @($Paths) + @($AdditionalPaths) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
if ($selectedPaths) { $arguments += @('--paths') + @($selectedPaths) }
& uv @arguments
exit $LASTEXITCODE
