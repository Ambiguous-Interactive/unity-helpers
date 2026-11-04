[CmdletBinding()]
param(
    [switch]$VerboseOutput,
    [string]$RepoRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$arguments = @(
    'run', '--quiet', '--project', (Join-Path $PSScriptRoot 'python~'), '--locked', '--no-dev',
    'python', (Join-Path $PSScriptRoot 'lint_license_headers.py')
)
if ($VerboseOutput) {
    $arguments += '--verbose'
}
if (-not [string]::IsNullOrWhiteSpace($RepoRoot)) {
    $arguments += @('--repo-root', $RepoRoot)
}
& uv @arguments
exit $LASTEXITCODE
