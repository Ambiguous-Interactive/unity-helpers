[CmdletBinding(PositionalBinding = $false)]
param(
    [string[]]$Paths,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AdditionalPaths,
    [switch]$VerboseOutput,
    [ValidateSet('All', 'Targets', 'Format')]
    [string]$Mode = 'All'
)
$ErrorActionPreference = 'Stop'
$arguments = @('run', '--quiet', '--project', (Join-Path $PSScriptRoot 'python~'), '--locked', '--no-dev', 'python', (Join-Path $PSScriptRoot 'lint_doc_links.py'), '--mode', $Mode)
if ($VerboseOutput) { $arguments += '--verbose' }
$selected = @($Paths) + @($AdditionalPaths) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
if ($selected) { $arguments += @('--paths') + @($selected) }
& uv @arguments
exit $LASTEXITCODE
