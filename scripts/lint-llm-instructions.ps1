param([switch]$Fix, [switch]$VerboseOutput, [switch]$AuthorshipPolicyOnly, [string]$RepoRoot)
$ErrorActionPreference = 'Stop'
$arguments = @('run', '--quiet', '--project', (Join-Path $PSScriptRoot 'python~'), '--locked', '--no-dev', 'python', (Join-Path $PSScriptRoot 'lint_llm_instructions.py'))
if ($Fix) { $arguments += '--fix' }
if ($VerboseOutput) { $arguments += '--verbose' }
if ($AuthorshipPolicyOnly) { $arguments += '--authorship-policy-only' }
if ($RepoRoot) { $arguments += @('--repo-root', $RepoRoot) }
& uv @arguments
exit $LASTEXITCODE
