Param(
  [switch]$VerboseOutput
)

<#
.SYNOPSIS
    Tests for sync-issue-template-versions.ps1 and pre-commit CLI safety.

.DESCRIPTION
    Validates:
    1. System.Version.ToString(3) produces exactly 3-component versions (no ".0" suffix)
    2. Pre-commit hook uses end-of-options separator (--) before all file arrays

.PARAMETER VerboseOutput
    Show detailed output during test execution

.EXAMPLE
    ./scripts/tests/test-sync-issue-template-versions.ps1
    ./scripts/tests/test-sync-issue-template-versions.ps1 -VerboseOutput
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:TestsPassed = 0
$script:TestsFailed = 0
$script:FailedTests = @()

function Write-Info($msg) {
  if ($VerboseOutput) { Write-Host "[test-sync-issue-template-versions] $msg" -ForegroundColor Cyan }
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

function Get-RepoRoot {
  return (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
}

# ── Version.ToString(3) Tests ──────────────────────────────────────────────

function Run-VersionToStringTests {
  Write-Host ""
  Write-Host "Version.ToString(3) format preservation:" -ForegroundColor Magenta
  Write-Host ""

  # Test 1: 3-component version stays 3-component
  $version = [System.Version]::new("3.2.0")
  $result = $version.ToString(3)
  Write-Info "Version '3.2.0' -> ToString(3) = '$result'"
  Write-TestResult -TestName "ToString(3) of '3.2.0' returns '3.2.0'" `
    -Passed ($result -ceq "3.2.0") `
    -Message "Expected '3.2.0', got '$result'"

  # Test 2: Verify default ToString() can produce 4 components (the bug scenario)
  $version4 = [System.Version]::new(3, 2, 0, 0)
  $defaultStr = $version4.ToString()
  Write-Info "Version(3,2,0,0) -> ToString() = '$defaultStr'"
  Write-TestResult -TestName "Default ToString() of 4-component version produces 4 parts" `
    -Passed ($defaultStr -ceq "3.2.0.0") `
    -Message "Expected '3.2.0.0', got '$defaultStr'"

  # Test 3: ToString(3) always trims to 3 components
  $result3 = $version4.ToString(3)
  Write-Info "Version(3,2,0,0) -> ToString(3) = '$result3'"
  Write-TestResult -TestName "ToString(3) of 4-component version returns only 3 parts" `
    -Passed ($result3 -ceq "3.2.0") `
    -Message "Expected '3.2.0', got '$result3'"

  # Test 4: Various real-world versions
  $testVersions = @("1.0.0", "0.9.1", "10.20.30", "2.0.0")
  foreach ($v in $testVersions) {
    $parsed = [System.Version]::new($v)
    $str = $parsed.ToString(3)
    Write-Info "Version '$v' -> ToString(3) = '$str'"
    Write-TestResult -TestName "ToString(3) roundtrip for '$v'" `
      -Passed ($str -ceq $v) `
      -Message "Expected '$v', got '$str'"
  }
}

# ── Pre-commit End-of-Options Tests ────────────────────────────────────────

function Run-PreCommitEndOfOptionsTests {
  Write-Host ""
  Write-Host "Pre-commit hook end-of-options (--) safety:" -ForegroundColor Magenta
  Write-Host ""

  $repoRoot = Get-RepoRoot
  $preCommitPath = Join-Path $repoRoot '.githooks' 'pre-commit'

  if (-not (Test-Path $preCommitPath)) {
    Write-Host "  [SKIP] Pre-commit hook not found at: $preCommitPath" -ForegroundColor Yellow
    return
  }

  $content = Get-Content $preCommitPath -Raw

  # All Prettier invocations should have -- before file arrays
  $prettierLines = @(Get-Content $preCommitPath | Where-Object { $_ -match '(run_prettier|node\s+scripts/run-prettier\.js)' -and $_ -match '--(?:write|check)' })
  Write-Info "Found $($prettierLines.Count) prettier invocation(s)"

  $allPrettierSafe = $true
  $unsafePrettierLines = @()
  foreach ($line in $prettierLines) {
    if ($line -notmatch '--\s+"\$\{') {
      $allPrettierSafe = $false
      $unsafePrettierLines += $line.Trim()
    }
  }
  Write-TestResult -TestName "All prettier invocations use -- before file arrays" `
    -Passed $allPrettierSafe `
    -Message "Unsafe lines: $($unsafePrettierLines -join '; ')"

  # All markdownlint invocations should have -- before file arrays
  $markdownlintLines = @(Get-Content $preCommitPath | Where-Object { $_ -match 'run_node_tool\s+markdownlint' })
  Write-Info "Found $($markdownlintLines.Count) markdownlint invocation(s)"

  $allMarkdownlintSafe = $true
  $unsafeMarkdownlintLines = @()
  foreach ($line in $markdownlintLines) {
    if ($line -notmatch '--\s+"\$\{') {
      $allMarkdownlintSafe = $false
      $unsafeMarkdownlintLines += $line.Trim()
    }
  }
  Write-TestResult -TestName "All markdownlint invocations use -- before file arrays" `
    -Passed $allMarkdownlintSafe `
    -Message "Unsafe lines: $($unsafeMarkdownlintLines -join '; ')"

  # All yamllint invocations should have -- before file arrays
  $yamllintLines = @(Get-Content $preCommitPath | Where-Object { $_ -match 'yamllint\s+-c' })
  Write-Info "Found $($yamllintLines.Count) yamllint invocation(s)"

  $allYamllintSafe = $true
  $unsafeYamllintLines = @()
  foreach ($line in $yamllintLines) {
    if ($line -notmatch '--\s+"\$\{') {
      $allYamllintSafe = $false
      $unsafeYamllintLines += $line.Trim()
    }
  }
  Write-TestResult -TestName "All yamllint invocations use -- before file arrays" `
    -Passed $allYamllintSafe `
    -Message "Unsafe lines: $($unsafeYamllintLines -join '; ')"
}

function Run-PowerShellScriptEndOfOptionsTests {
  Write-Host ""
  Write-Host "PowerShell script end-of-options (--) safety:" -ForegroundColor Magenta
  Write-Host ""

  $repoRoot = Get-RepoRoot

  # Check format-staged-prettier.ps1 for '--' before file paths
  $prettierScript = Join-Path $repoRoot 'scripts' 'format-staged-prettier.ps1'
  if (Test-Path $prettierScript) {
    $prettierContent = Get-Content $prettierScript -Raw
    # Look for '--' in the prettier args array construction
    $prettierHasSeparator = $prettierContent -match "'--'" -and $prettierContent -match 'prettier'
    Write-TestResult -TestName "format-staged-prettier.ps1 uses '--' before file paths" `
      -Passed $prettierHasSeparator `
      -Message "Missing '--' separator in prettier argument construction"
  } else {
    Write-Host "  [SKIP] format-staged-prettier.ps1 not found at: $prettierScript" -ForegroundColor Yellow
  }

  # Check lint-yaml.ps1 for '--' before file paths
  $yamlScript = Join-Path $repoRoot 'scripts' 'lint-yaml.ps1'
  if (Test-Path $yamlScript) {
    $yamlContent = Get-Content $yamlScript -Raw
    # Look for '--' in the yamllint args array construction
    $yamlHasSeparator = $yamlContent -match "'--'" -and $yamlContent -match 'yamllint'
    Write-TestResult -TestName "lint-yaml.ps1 uses '--' before file paths" `
      -Passed $yamlHasSeparator `
      -Message "Missing '--' separator in yamllint argument construction"
  } else {
    Write-Host "  [SKIP] lint-yaml.ps1 not found at: $yamlScript" -ForegroundColor Yellow
  }

}

# ── Pre-push End-of-Options Tests ──────────────────────────────────────────

function Run-PrePushEndOfOptionsTests {
  Write-Host ""
  Write-Host "Pre-push hook end-of-options (--) safety:" -ForegroundColor Magenta
  Write-Host ""

  $repoRoot = Get-RepoRoot
  $prePushPath = Join-Path $repoRoot '.githooks' 'pre-push'

  if (-not (Test-Path $prePushPath)) {
    Write-Host "  [SKIP] Pre-push hook not found at: $prePushPath" -ForegroundColor Yellow
    return
  }

  $lines = @(Get-Content $prePushPath)

  # All Prettier invocations should have -- before file arguments
  $prettierLines = @($lines | Where-Object { $_ -match 'node\s+scripts/run-prettier\.js' -and $_ -match '--(?:write|check)' -and $_ -notmatch '^\s*echo\s+' })
  Write-Info "Found $($prettierLines.Count) prettier invocation(s) in pre-push"

  $allPrettierSafe = $true
  $unsafePrettierLines = @()
  foreach ($line in $prettierLines) {
    if ($line -notmatch '--\s+"') {
      $allPrettierSafe = $false
      $unsafePrettierLines += $line.Trim()
    }
  }
  Write-TestResult -TestName "Pre-push: all prettier invocations use -- before file args" `
    -Passed $allPrettierSafe `
    -Message "Unsafe lines: $($unsafePrettierLines -join '; ')"

  # All markdownlint invocations should have -- before file arguments
  $markdownlintLines = @($lines | Where-Object { $_ -match 'run_node_tool\s+markdownlint' -and $_ -notmatch '--version' })
  Write-Info "Found $($markdownlintLines.Count) markdownlint invocation(s) in pre-push"

  $allMarkdownlintSafe = $true
  $unsafeMarkdownlintLines = @()
  foreach ($line in $markdownlintLines) {
    if ($line -notmatch '--\s+"') {
      $allMarkdownlintSafe = $false
      $unsafeMarkdownlintLines += $line.Trim()
    }
  }
  Write-TestResult -TestName "Pre-push: all markdownlint invocations use -- before file args" `
    -Passed $allMarkdownlintSafe `
    -Message "Unsafe lines: $($unsafeMarkdownlintLines -join '; ')"

  # All lychee invocations should have -- before file arguments
  $lycheeLines = @($lines | Where-Object { $_ -match 'lychee\s+' -and $_ -notmatch '^\s*#' -and $_ -notmatch 'command -v' -and $_ -notmatch '^\s*echo\s' })
  Write-Info "Found $($lycheeLines.Count) lychee invocation(s) in pre-push"

  $allLycheeSafe = $true
  $unsafeLycheeLines = @()
  foreach ($line in $lycheeLines) {
    if ($line -notmatch '--\s+"') {
      $allLycheeSafe = $false
      $unsafeLycheeLines += $line.Trim()
    }
  }
  Write-TestResult -TestName "Pre-push: all lychee invocations use -- before file args" `
    -Passed $allLycheeSafe `
    -Message "Unsafe lines: $($unsafeLycheeLines -join '; ')"

  # All yamllint invocations should have -- before file arguments
  $yamllintLines = @($lines | Where-Object { $_ -match 'yamllint\s+' -and $_ -notmatch '^\s*#' -and $_ -notmatch 'command -v' -and $_ -notmatch '^\s*echo\s' })
  Write-Info "Found $($yamllintLines.Count) yamllint invocation(s) in pre-push"

  $allYamllintSafe = $true
  $unsafeYamllintLines = @()
  foreach ($line in $yamllintLines) {
    if ($line -notmatch '--\s+') {
      $allYamllintSafe = $false
      $unsafeYamllintLines += $line.Trim()
    }
  }
  Write-TestResult -TestName "Pre-push: all yamllint invocations use -- before file args" `
    -Passed $allYamllintSafe `
    -Message "Unsafe lines: $($unsafeYamllintLines -join '; ')"
}

# ── Workflow End-of-Options Tests ──────────────────────────────────────────

function Run-WorkflowEndOfOptionsTests {
  Write-Host ""
  Write-Host "GitHub Actions workflow end-of-options (--) safety:" -ForegroundColor Magenta
  Write-Host ""

  $repoRoot = Get-RepoRoot
  $workflowDir = Join-Path $repoRoot '.github' 'workflows'

  if (-not (Test-Path $workflowDir)) {
    Write-Host "  [SKIP] Workflows directory not found at: $workflowDir" -ForegroundColor Yellow
    return
  }

  $ymlFiles = @(Get-ChildItem -Path $workflowDir -Filter '*.yml' -File)
  Write-Info "Found $($ymlFiles.Count) workflow file(s)"

  $allSafe = $true
  $unsafeEntries = @()

  foreach ($file in $ymlFiles) {
    $lines = @(Get-Content $file.FullName)
    foreach ($line in $lines) {
      # Skip comments and lines that are just echoing or checking versions
      if ($line -match '^\s*#') { continue }
      if ($line -match '--version') { continue }

      # Check Prettier invocations (repo-local launcher or pinned workflow npx)
      if ($line -match 'prettier\S*\s+--(?:write|check)') {
        if ($line -notmatch '--\s+"' -and $line -notmatch '--\s+\.') {
          $allSafe = $false
          $unsafeEntries += "$($file.Name): $($line.Trim())"
        }
      }

      # Check markdownlint invocations
      if ($line -match 'markdownlint.*--(?:config|fix)') {
        if ($line -notmatch '--\s+"') {
          $allSafe = $false
          $unsafeEntries += "$($file.Name): $($line.Trim())"
        }
      }
    }
  }

  Write-TestResult -TestName "Workflows: all prettier/markdownlint invocations use -- before file args" `
    -Passed $allSafe `
    -Message "Unsafe entries: $($unsafeEntries -join '; ')"
}

# ── Package.json End-of-Options Tests ──────────────────────────────────────

function Run-PackageJsonEndOfOptionsTests {
  Write-Host ""
  Write-Host "package.json npm scripts end-of-options (--) safety:" -ForegroundColor Magenta
  Write-Host ""

  $repoRoot = Get-RepoRoot
  $packageJsonPath = Join-Path $repoRoot 'package.json'

  if (-not (Test-Path $packageJsonPath)) {
    Write-Host "  [SKIP] package.json not found at: $packageJsonPath" -ForegroundColor Yellow
    return
  }

  $packageJson = Get-Content $packageJsonPath -Raw | ConvertFrom-Json
  $scripts = $packageJson.scripts

  if (-not $scripts) {
    Write-Host "  [SKIP] No scripts section found in package.json" -ForegroundColor Yellow
    return
  }

  $allSafe = $true
  $unsafeEntries = @()

  # Iterate over all script entries
  $scripts.PSObject.Properties | ForEach-Object {
    $scriptName = $_.Name
    $scriptCmd = $_.Value

    # Check prettier invocations with file arguments
    if ($scriptCmd -match 'prettier\S*\s+--(?:write|check)') {
      if ($scriptCmd -notmatch '--\s+"' -and $scriptCmd -notmatch '--\s+\.') {
        $allSafe = $false
        $unsafeEntries += "$scriptName`: $scriptCmd"
      }
    }

    # Check markdownlint invocations with file arguments
    if ($scriptCmd -match 'markdownlint.*--(?:config|fix)') {
      if ($scriptCmd -notmatch '--\s+"') {
        $allSafe = $false
        $unsafeEntries += "$scriptName`: $scriptCmd"
      }
    }

    # Check standalone markdownlint with file globs (no --config but has file args)
    if ($scriptCmd -match 'markdownlint\s+' -and $scriptCmd -match '"\*\*') {
      if ($scriptCmd -notmatch '--\s+"') {
        $allSafe = $false
        $unsafeEntries += "$scriptName`: $scriptCmd"
      }
    }
  }

  Write-TestResult -TestName "package.json: all prettier/markdownlint scripts use -- before file args" `
    -Passed $allSafe `
    -Message "Unsafe entries: $($unsafeEntries -join '; ')"
}

# ── Main Execution ─────────────────────────────────────────────────────────

Write-Host ""
Write-Host "========================================" -ForegroundColor White
Write-Host "Sync Issue Template Versions Tests" -ForegroundColor White
Write-Host "========================================" -ForegroundColor White

Run-VersionToStringTests
Run-PreCommitEndOfOptionsTests
Run-PrePushEndOfOptionsTests
Run-WorkflowEndOfOptionsTests
Run-PackageJsonEndOfOptionsTests
Run-PowerShellScriptEndOfOptionsTests

# Summary
Write-Host ""
Write-Host "========================================" -ForegroundColor White
Write-Host "Test Summary" -ForegroundColor White
Write-Host "========================================" -ForegroundColor White
Write-Host ""

$totalTests = $script:TestsPassed + $script:TestsFailed

if ($script:TestsFailed -eq 0) {
  Write-Host "All $totalTests tests passed!" -ForegroundColor Green
  exit 0
} else {
  Write-Host "Passed: $($script:TestsPassed) / $totalTests" -ForegroundColor Yellow
  Write-Host "Failed: $($script:TestsFailed) / $totalTests" -ForegroundColor Red
  Write-Host ""
  Write-Host "Failed tests:" -ForegroundColor Red
  foreach ($test in $script:FailedTests) {
    Write-Host "  - $test" -ForegroundColor Red
  }
  exit 1
}
