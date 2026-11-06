[CmdletBinding()]
param([string]$Path, [switch]$VerboseOutput)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$arguments = @(
    'run', '--quiet', '--project', (Join-Path $PSScriptRoot 'python~'), '--locked', '--no-dev',
    'python', (Join-Path $PSScriptRoot 'lint_unity_test_modules.py')
)
if (-not [string]::IsNullOrWhiteSpace($Path)) { $arguments += @('--path', $Path) }
if ($VerboseOutput) { $arguments += '--verbose' }
& uv @arguments
exit $LASTEXITCODE
