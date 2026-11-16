param([string]$OutputPath, [switch]$Stdout, [switch]$VerboseOutput, [string]$RepoRoot)
$ErrorActionPreference = 'Stop'
$arguments = @('run', '--quiet', '--project', (Join-Path $PSScriptRoot 'python~'), '--locked', '--no-dev', 'python', (Join-Path $PSScriptRoot 'generate_skills_index.py'))
if ($OutputPath) { $arguments += @('--output-path', $OutputPath) }
if ($Stdout) { $arguments += '--stdout' }
if ($VerboseOutput) { $arguments += '--verbose' }
if ($RepoRoot) { $arguments += @('--repo-root', $RepoRoot) }
if ($Stdout) {
    & uv @arguments | ForEach-Object { [Console]::Out.Write("$_`n") }
} else {
    & uv @arguments
}
exit $LASTEXITCODE
