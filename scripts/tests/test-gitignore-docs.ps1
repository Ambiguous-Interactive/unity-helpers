Param(
  [switch]$VerboseOutput
)

<#
.SYNOPSIS
    Test runner for lint-gitignore-docs.ps1

.DESCRIPTION
    Tests that lint-gitignore-docs.ps1 correctly detects .gitignore safety issues
    for documentation and LLM files:

    - Gitignored docs/ and .llm/ files are detected (exit code 1)
    - Safe .gitignore patterns pass cleanly (exit code 0)
    - mkdocs.yml nav references to missing files are detected
    - mkdocs.yml nav references to gitignored files are detected
    - Wildcard patterns that accidentally match docs/ or .llm/ files are flagged

    Uses temporary git repositories with controlled .gitignore patterns and
    docs/.llm directory structures to test each scenario in isolation.

.PARAMETER VerboseOutput
    Show detailed output during test execution

.EXAMPLE
    ./scripts/tests/test-gitignore-docs.ps1
    ./scripts/tests/test-gitignore-docs.ps1 -VerboseOutput
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:TestsPassed = 0
$script:TestsFailed = 0
$script:FailedTests = @()

function Write-Info($msg) {
  if ($VerboseOutput) { Write-Host "[test-gitignore-docs] $msg" -ForegroundColor Cyan }
}

function Write-TestResult {
  param(
    [string]$TestName,
    [bool]$Passed,
    [string]$Message = ""
  )

  if ($Passed) {
    Write-Host "  [PASS] $TestName" -ForegroundColor Green
    $script:TestsPassed++
  } else {
    Write-Host "  [FAIL] $TestName" -ForegroundColor Red
    if ($Message) {
      Write-Host "         $Message" -ForegroundColor Yellow
    }
    $script:TestsFailed++
    $script:FailedTests += $TestName
  }
}

function Invoke-Scenario {
  param(
    [hashtable]$Scenario
  )

  $gitignoreContent = ''
  $docsFiles = @()
  $llmFiles = @()
  $mkdocsNavContent = ''

  if ($Scenario.ContainsKey('GitignoreContent')) { $gitignoreContent = [string]$Scenario.GitignoreContent }
  if ($Scenario.ContainsKey('DocsFiles')) { $docsFiles = [string[]]$Scenario.DocsFiles }
  if ($Scenario.ContainsKey('LlmFiles')) { $llmFiles = [string[]]$Scenario.LlmFiles }
  if ($Scenario.ContainsKey('MkdocsNavContent')) { $mkdocsNavContent = [string]$Scenario.MkdocsNavContent }

  $repo = New-TestRepo `
    -GitignoreContent $gitignoreContent `
    -DocsFiles $docsFiles `
    -LlmFiles $llmFiles `
    -MkdocsNavContent $mkdocsNavContent

  try {
    $result = Invoke-Linter -RepoDir $repo
    $expectedExit = [int]$Scenario.ExpectedExitCode
    Write-TestResult $Scenario.Name ($result.ExitCode -eq $expectedExit) "Expected exit code $expectedExit, got $($result.ExitCode)"

    if ($Scenario.ContainsKey('ExpectedOutputRegex') -and -not [string]::IsNullOrWhiteSpace([string]$Scenario.ExpectedOutputRegex)) {
      $regex = [string]$Scenario.ExpectedOutputRegex
      Write-TestResult "$($Scenario.Name)_Output" ($result.Output -match $regex) "Expected output to match regex: $regex"
    }

    if ($Scenario.ContainsKey('ForbiddenOutputRegex') -and -not [string]::IsNullOrWhiteSpace([string]$Scenario.ForbiddenOutputRegex)) {
      $forbidden = [string]$Scenario.ForbiddenOutputRegex
      Write-TestResult "$($Scenario.Name)_NoForbiddenOutput" ($result.Output -notmatch $forbidden) "Output unexpectedly matched forbidden regex: $forbidden"
    }
  } finally {
    Remove-TestRepo $repo
  }
}

function New-TestRepo {
  <#
  .SYNOPSIS
      Creates a temporary git repository for testing.
  .DESCRIPTION
      Sets up a minimal git repo with configurable .gitignore, docs structure,
      optional .llm structure, and optional mkdocs.yml for testing lint-gitignore-docs.ps1.
  #>
  param(
    [string]$GitignoreContent = "",
    [string[]]$DocsFiles = @(),
    [string[]]$LlmFiles = @(),
    [string]$MkdocsNavContent = ""
  )

  $tempDir = Join-Path ([System.IO.Path]::GetTempPath()) "gitignore-docs-test-$([System.Guid]::NewGuid().ToString('N').Substring(0,8))"
  New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

  # Initialize a git repo
  Push-Location $tempDir
  try {
    & git init -q 2>&1 | Out-Null
    & git config user.email "test@test.com" 2>&1 | Out-Null
    & git config user.name "Test" 2>&1 | Out-Null

    # Create .gitignore
    if ($GitignoreContent) {
      $GitignoreContent | Set-Content -Path '.gitignore' -Encoding UTF8
    }

    # Create docs files
    foreach ($docFile in $DocsFiles) {
      $docPath = Join-Path 'docs' $docFile
      $docDir = Split-Path $docPath -Parent
      if (-not (Test-Path $docDir)) {
        New-Item -ItemType Directory -Path $docDir -Force | Out-Null
      }
      "# Documentation: $docFile" | Set-Content -Path $docPath -Encoding UTF8
    }

    # Create .llm files
    foreach ($llmFile in $LlmFiles) {
      $llmPath = Join-Path '.llm' $llmFile
      $llmDir = Split-Path $llmPath -Parent
      if (-not (Test-Path $llmDir)) {
        New-Item -ItemType Directory -Path $llmDir -Force | Out-Null
      }
      "# LLM config: $llmFile" | Set-Content -Path $llmPath -Encoding UTF8
    }

    # Create mkdocs.yml if nav content provided
    if ($MkdocsNavContent) {
      $MkdocsNavContent | Set-Content -Path 'mkdocs.yml' -Encoding UTF8
    }

    # Initial commit to establish the repo
    & git add -A 2>&1 | Out-Null
    & git commit -q -m "Initial commit" --allow-empty 2>&1 | Out-Null
  } finally {
    Pop-Location
  }

  return $tempDir
}

function Invoke-Linter {
  param(
    [string]$RepoDir,
    [switch]$Verbose
  )

  $lintScript = Join-Path $PSScriptRoot '..' 'lint-gitignore-docs.ps1'

  $linterArgs = @($lintScript)
  if ($Verbose) { $linterArgs += '-VerboseOutput' }

  try {
    Push-Location $RepoDir
    $output = & pwsh -NoProfile -File @linterArgs 2>&1
    $exitCode = $LASTEXITCODE
  } finally {
    Pop-Location
  }

  return @{
    ExitCode = $exitCode
    Output = ($output -join "`n")
  }
}

function Remove-TestRepo {
  param([string]$RepoDir)
  Remove-Item -Path $RepoDir -Recurse -Force -ErrorAction SilentlyContinue
}

# ---- Test Cases ----

Write-Host "Testing lint-gitignore-docs.ps1..." -ForegroundColor White

# ==== Test Group 1: Clean scenarios (should pass) ====
Write-Host "`nTest group: Clean scenarios (should pass)" -ForegroundColor Magenta

$cleanScenarios = @(
  @{
    Name = 'NoDocs_NoGitignore_Passes'
    ExpectedExitCode = 0
  },
  @{
    Name = 'SafeGitignore_Passes'
    GitignoreContent = "node_modules/`n*.log`n.vs/"
    DocsFiles = @('index.md', 'features/inspector/overview.md')
    ExpectedExitCode = 0
  },
  @{
    Name = 'SafeWildcards_Passes'
    GitignoreContent = "*.log`n*.tmp`n*.dll`nfailed-tests-*.txt"
    DocsFiles = @('index.md', 'features/editor-tools/failed-tests-exporter.md')
    ExpectedExitCode = 0
  },
  @{
    Name = 'ValidMkdocsNav_Passes'
    GitignoreContent = "*.log"
    DocsFiles = @('index.md', 'features/guide.md')
    MkdocsNavContent = "nav:`n  - Home: index.md`n  - Guide: features/guide.md"
    ExpectedExitCode = 0
  },
  @{
    Name = 'Regression_EmptyIgnoredSet_DoesNotCrash'
    GitignoreContent = "*.log`n*.tmp"
    DocsFiles = @('index.md', 'guides/getting-started.md')
    ExpectedExitCode = 0
    ForbiddenOutputRegex = 'null-valued expression'
  }
)

foreach ($scenario in $cleanScenarios) {
  Invoke-Scenario -Scenario $scenario
}

# ==== Test Group 2: Gitignored docs (should fail) ====
Write-Host "`nTest group: Gitignored docs files (should fail)" -ForegroundColor Magenta

# Test 2a: Broad wildcard pattern that matches a docs file
$repo2a = New-TestRepo `
  -GitignoreContent "failed-tests-*" `
  -DocsFiles @('index.md', 'features/editor-tools/failed-tests-exporter.md')
$result2a = Invoke-Linter -RepoDir $repo2a
Write-TestResult "BroadWildcard_Fails" ($result2a.ExitCode -eq 1) "Expected exit code 1, got $($result2a.ExitCode)"
Write-TestResult "BroadWildcard_DetectsFile" ($result2a.Output -match 'failed-tests-exporter\.md') "Expected output to mention failed-tests-exporter.md"
Remove-TestRepo $repo2a

# Test 2b: Pattern that matches docs file extension
$repo2b = New-TestRepo `
  -GitignoreContent "*.md" `
  -DocsFiles @('index.md', 'guide.md')
$result2b = Invoke-Linter -RepoDir $repo2b
Write-TestResult "IgnoreAllMd_Fails" ($result2b.ExitCode -eq 1) "Expected exit code 1, got $($result2b.ExitCode)"
Remove-TestRepo $repo2b

# ==== Test Group 3: mkdocs.yml nav issues (should fail) ====
Write-Host "`nTest group: mkdocs.yml nav issues (should fail)" -ForegroundColor Magenta

# Test 3a: mkdocs.yml references a file that does not exist
$repo3a = New-TestRepo `
  -GitignoreContent "*.log" `
  -DocsFiles @('index.md') `
  -MkdocsNavContent "nav:`n  - Home: index.md`n  - Missing: features/nonexistent.md"
$result3a = Invoke-Linter -RepoDir $repo3a
Write-TestResult "MissingNavFile_Fails" ($result3a.ExitCode -eq 1) "Expected exit code 1, got $($result3a.ExitCode)"
Write-TestResult "MissingNavFile_DetectsFailure" ($result3a.Output -match 'Nav references missing file|Gitignore docs safety check failed') "Expected output to include nav failure context"
Remove-TestRepo $repo3a

# Test 3b: mkdocs.yml references a file that is gitignored
$repo3b = New-TestRepo `
  -GitignoreContent "failed-tests-*" `
  -DocsFiles @('index.md', 'features/editor-tools/failed-tests-exporter.md') `
  -MkdocsNavContent "nav:`n  - Home: index.md`n  - Exporter: features/editor-tools/failed-tests-exporter.md"
$result3b = Invoke-Linter -RepoDir $repo3b
Write-TestResult "GitignoredNavFile_Fails" ($result3b.ExitCode -eq 1) "Expected exit code 1, got $($result3b.ExitCode)"
Write-TestResult "GitignoredNavFile_DetectsFile" ($result3b.Output -match 'failed-tests-exporter\.md') "Expected output to mention failed-tests-exporter.md"
Remove-TestRepo $repo3b

# ==== Test Group 4: Edge cases ====
Write-Host "`nTest group: Edge cases" -ForegroundColor Magenta

$edgeScenarios = @(
  @{
    Name = 'NegationPattern_Passes'
    GitignoreContent = "*.md`n!docs/**"
    DocsFiles = @('index.md', 'guide.md')
    ExpectedExitCode = 0
  },
  @{
    Name = 'AnchoredPattern_Passes'
    GitignoreContent = "/Unity/*`n/build/*"
    DocsFiles = @('index.md', 'features/guide.md')
    ExpectedExitCode = 0
  },
  @{
    Name = 'EmptyNoDocs_Passes'
    GitignoreContent = "*.md"
    ExpectedExitCode = 0
  },
  @{
    Name = 'ExtensionScopedWildcard_Passes'
    GitignoreContent = "failed-tests-*.txt`nfailed-tests-*.txt.meta"
    DocsFiles = @('index.md', 'features/editor-tools/failed-tests-exporter.md')
    ExpectedExitCode = 0
  }
)

foreach ($scenario in $edgeScenarios) {
  Invoke-Scenario -Scenario $scenario
}

# ==== Test Group 5: .llm/ directory protection ====
Write-Host "`nTest group: .llm/ directory protection" -ForegroundColor Magenta

# Test 5a: .llm/ files exist, no dangerous patterns -> should pass
$repo5a = New-TestRepo `
  -GitignoreContent "*.log`n*.tmp" `
  -LlmFiles @('context.md', 'skills/create-test.md')
$result5a = Invoke-Linter -RepoDir $repo5a
Write-TestResult "SafeLlm_Passes" ($result5a.ExitCode -eq 0) "Expected exit code 0, got $($result5a.ExitCode)"
Remove-TestRepo $repo5a

# Test 5b: Broad wildcard pattern that matches an .llm/ file -> should fail
$repo5b = New-TestRepo `
  -GitignoreContent "_llm_*" `
  -LlmFiles @('skills/avoid-reflection.md') `
  -DocsFiles @('index.md')
# _llm_* should not match .llm/ files, but let's also test a pattern that does
Remove-TestRepo $repo5b

$repo5b2 = New-TestRepo `
  -GitignoreContent "context.*" `
  -LlmFiles @('context.md', 'skills/create-test.md')
$result5b2 = Invoke-Linter -RepoDir $repo5b2
Write-TestResult "BroadWildcard_MatchesLlm_Fails" ($result5b2.ExitCode -eq 1) "Expected exit code 1, got $($result5b2.ExitCode)"
Write-TestResult "BroadWildcard_DetectsLlmFile" ($result5b2.Output -match 'context\.md') "Expected output to mention context.md"
Remove-TestRepo $repo5b2

# Test 5c: Both docs/ and .llm/ exist, safe patterns -> should pass
$repo5c = New-TestRepo `
  -GitignoreContent "*.log`nfailed-tests-*.txt" `
  -DocsFiles @('index.md', 'features/editor-tools/failed-tests-exporter.md') `
  -LlmFiles @('context.md', 'skills/create-test.md')
$result5c = Invoke-Linter -RepoDir $repo5c
Write-TestResult "SafeDocsAndLlm_Passes" ($result5c.ExitCode -eq 0) "Expected exit code 0, got $($result5c.ExitCode)"
Remove-TestRepo $repo5c

# Test 5d: .llm/ without docs/ directory -> should still check .llm/
$repo5d = New-TestRepo `
  -GitignoreContent "context.*" `
  -LlmFiles @('context.md')
$result5d = Invoke-Linter -RepoDir $repo5d
Write-TestResult "LlmOnly_NoDocs_Fails" ($result5d.ExitCode -eq 1) "Expected exit code 1, got $($result5d.ExitCode)"
Remove-TestRepo $repo5d

# ==== Test Group 6: Check 4 -- a tracked file that is also gitignored ====
# The state that produced this check: a file force-added past its own ignore rule stays in the
# repository forever, `git status` never mentions it, and re-adding it after a delete silently does
# nothing. Which side is wrong differs per file, so the linter reports rather than guesses.

# Test 6a: a file committed BEFORE its ignore rule existed is still tracked, and is reported.
$repo6a = New-TestRepo -DocsFiles @('index.md')
Push-Location $repo6a
try {
  'note' | Set-Content -Path 'progress.md' -Encoding UTF8
  & git add -f 'progress.md' 2>&1 | Out-Null
  'progress.md' | Set-Content -Path '.gitignore' -Encoding UTF8
  & git add -A 2>&1 | Out-Null
  & git commit -q -m 'Track then ignore' 2>&1 | Out-Null
} finally {
  Pop-Location
}
$result6a = Invoke-Linter -RepoDir $repo6a
Write-TestResult "TrackedAndIgnoredFile_Fails" ($result6a.ExitCode -eq 1) "Expected exit code 1, got $($result6a.ExitCode)"
Write-TestResult "TrackedAndIgnoredFile_NamesThePath" ($result6a.Output -match 'progress\.md') "Expected the message to name progress.md"
Remove-TestRepo $repo6a

# Test 6b: the same file untracked -- on disk, ignored, not in the index -- passes.
$repo6b = New-TestRepo -GitignoreContent 'progress.md' -DocsFiles @('index.md')
Push-Location $repo6b
try {
  'note' | Set-Content -Path 'progress.md' -Encoding UTF8
} finally {
  Pop-Location
}
$result6b = Invoke-Linter -RepoDir $repo6b
Write-TestResult "IgnoredButUntrackedFile_Passes" ($result6b.ExitCode -eq 0) "Expected exit code 0, got $($result6b.ExitCode)"
Remove-TestRepo $repo6b

# Test 6c: a negation resolves it the other way -- the file is meant to be tracked, so the pattern
# is what gets narrowed. This is what PLAN.md needed.
$repo6c = New-TestRepo -GitignoreContent "PLAN.md*`n!PLAN.md" -DocsFiles @('index.md')
Push-Location $repo6c
try {
  '# Plan' | Set-Content -Path 'PLAN.md' -Encoding UTF8
  & git add -A 2>&1 | Out-Null
  & git commit -q -m 'Track the plan' 2>&1 | Out-Null
} finally {
  Pop-Location
}
$result6c = Invoke-Linter -RepoDir $repo6c
Write-TestResult "NegatedPatternKeepsTrackedFile_Passes" ($result6c.ExitCode -eq 0) "Expected exit code 0, got $($result6c.ExitCode)"
Remove-TestRepo $repo6c

# ---- Summary ----

Write-Host ""
Write-Host ("=" * 60)
Write-Host ("Tests passed: {0}" -f $script:TestsPassed) -ForegroundColor Green
Write-Host ("Tests failed: {0}" -f $script:TestsFailed) -ForegroundColor $(if ($script:TestsFailed -gt 0) { "Red" } else { "Green" })

if ($script:FailedTests.Count -gt 0) {
  Write-Host "Failed tests:" -ForegroundColor Red
  foreach ($t in $script:FailedTests) {
    Write-Host "  - $t" -ForegroundColor Red
  }
}

exit $script:TestsFailed
