[CmdletBinding(PositionalBinding = $false)]
param(
    [string[]]$Paths,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AdditionalPaths,
    [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$arguments = @(
    'run', '--quiet', '--project', (Join-Path $PSScriptRoot 'python~'), '--locked', '--no-dev',
    'python', (Join-Path $PSScriptRoot 'check_eol.py')
)
if ($VerboseOutput) { $arguments += '--verbose' }
$selectedPaths = @($Paths) + @($AdditionalPaths)
$selectedPaths = @($selectedPaths | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($selectedPaths.Count -gt 0) { $arguments += '--paths'; $arguments += $selectedPaths }
& uv @arguments
exit $LASTEXITCODE
