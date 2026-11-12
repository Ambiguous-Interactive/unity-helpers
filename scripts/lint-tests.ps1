[CmdletBinding(PositionalBinding = $false)]
param(
    [switch]$VerboseOutput,
    [string[]]$Paths,
    [switch]$FixNullChecks,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AdditionalPaths
)
$ErrorActionPreference = 'Stop'
$arguments = @('run', '--quiet', '--project', (Join-Path $PSScriptRoot 'python~'), '--locked', '--no-dev', 'python', (Join-Path $PSScriptRoot 'lint_tests.py'))
if ($VerboseOutput) { $arguments += '--verbose' }
if ($FixNullChecks) { $arguments += '--fix-null-checks' }
$selected = @($Paths) + @($AdditionalPaths) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
if ($selected) { $arguments += @('--paths') + @($selected) }
& uv @arguments
exit $LASTEXITCODE
