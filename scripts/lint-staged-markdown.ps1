#!/usr/bin/env pwsh
param(
  [Parameter(ValueFromRemainingArguments = $true)]
  [string[]]$Paths
)

$helpersPath = Join-Path -Path $PSScriptRoot -ChildPath 'git-staging-helpers.ps1'
. $helpersPath

try {
  Assert-GitAvailable | Out-Null
  $repositoryInfo = Get-GitRepositoryInfo
} catch {
  Write-Error $_.Exception.Message
  exit 1
}

$markdownGlobs = @('*.md', '*.markdown')
$Paths = Get-StagedPathsForGlobs -DefaultPaths $Paths -Globs $markdownGlobs

$existingPaths = Get-ExistingPaths -Candidates $Paths

if ($existingPaths.Count -eq 0) {
  exit 0
}

$fenceFixScript = Join-Path -Path $repositoryInfo.RepositoryRoot -ChildPath 'scripts/fix-markdown-fence-languages.ps1'
if (Test-Path -LiteralPath $fenceFixScript -PathType Leaf) {
  & $fenceFixScript -Paths $existingPaths
  if ($LASTEXITCODE -ne 0) {
    Write-Error "Markdown fence language auto-fix failed with exit code $LASTEXITCODE."
    exit $LASTEXITCODE
  }
}

$node = Get-Command node -ErrorAction SilentlyContinue
if (-not $node) {
  Write-Error "node not found; install Node.js and run npm install to run markdownlint."
  exit 1
}

$markdownlintArgs = @(
  (Join-Path -Path $repositoryInfo.RepositoryRoot -ChildPath 'scripts/run-node-bin.js'),
  'markdownlint',
  '--config',
  '.markdownlint.json',
  '--ignore-path',
  '.markdownlintignore',
  '--fix'
)
$markdownlintArgs += '--'
$markdownlintArgs += $existingPaths
& node @markdownlintArgs
if ($LASTEXITCODE -ne 0) {
  Write-Error "markdownlint failed with exit code $LASTEXITCODE."
  exit $LASTEXITCODE
}

$gitAddExitCode = Invoke-GitAddWithRetry -Items $existingPaths -IndexLockPath $repositoryInfo.IndexLockPath
if ($gitAddExitCode -ne 0) {
  Write-Error "git add failed with exit code $gitAddExitCode."
  exit $gitAddExitCode
}
