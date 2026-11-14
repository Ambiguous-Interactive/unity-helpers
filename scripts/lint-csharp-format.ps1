[CmdletBinding(PositionalBinding = $false)]
param(
    [switch]$VerboseOutput,
    [switch]$Fix,
    [switch]$SkipWhenUnavailable,
    [string[]]$Paths,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AdditionalPaths
)
$ErrorActionPreference = 'Stop'
$arguments = @('run', '--quiet', '--project', (Join-Path $PSScriptRoot 'python~'), '--locked', '--no-dev', 'python', (Join-Path $PSScriptRoot 'lint_csharp_format.py'))
if ($VerboseOutput) { $arguments += '--verbose' }
if ($Fix) { $arguments += '--fix' }
if ($SkipWhenUnavailable) { $arguments += '--skip-when-unavailable' }
$selected = @($Paths) + @($AdditionalPaths) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
if ($selected) { $arguments += @('--paths') + @($selected) }
& uv @arguments
exit $LASTEXITCODE
