Param(
    [switch]$VerboseOutput,
    [int]$MaxEntryLength = 300,
    [string]$ChangelogPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$arguments = @(
    'run', '--quiet', '--project', (Join-Path $PSScriptRoot 'python~'), '--locked', '--no-dev',
    'python', (Join-Path $PSScriptRoot 'lint_changelog.py'), '--max-entry-length', $MaxEntryLength
)
if ($VerboseOutput) {
    $arguments += '--verbose'
}
if (-not [string]::IsNullOrWhiteSpace($ChangelogPath)) {
    $arguments += @('--changelog-path', $ChangelogPath)
}
& uv @arguments
exit $LASTEXITCODE
