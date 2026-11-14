[CmdletBinding(PositionalBinding = $false)]
param(
    [string[]]$Paths,
    [switch]$VerboseOutput,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AdditionalPaths
)
$ErrorActionPreference = 'Stop'
$arguments = @('run', '--quiet', '--project', (Join-Path $PSScriptRoot 'python~'), '--locked', '--no-dev', '--group', 'documentation', 'python', (Join-Path $PSScriptRoot 'fix_markdown_fence_languages.py'))
if ($VerboseOutput) { $arguments += '--verbose' }
$selected = @($Paths) + @($AdditionalPaths) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
if ($selected) { $arguments += @('--paths') + @($selected) }
& uv @arguments
exit $LASTEXITCODE
