Param(
  [switch]$VerboseOutput
)

<#
.SYNOPSIS
    Regression tests for sync script newline safety and cspell linter contract drift.

.DESCRIPTION
    Validates:
    1. Production scripts that write with Set-Content -NoNewline normalize content to a final LF.
    2. lint-cspell-config.js header-declared checks match implemented "Check N" sections.

.PARAMETER VerboseOutput
    Show detailed output during test execution.

.EXAMPLE
    pwsh -NoProfile -File scripts/tests/test-sync-script-contracts.ps1
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# cspell:ignore Eqi

$script:TestsPassed = 0
$script:TestsFailed = 0
$script:FailedTests = @()

function Write-Info {
  param([string]$Message)
  if ($VerboseOutput) {
    Write-Host "[test-sync-script-contracts] $Message" -ForegroundColor Cyan
  }
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
  }
  else {
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

function Get-PowerShellSingleQuotedArrayEntries {
  param(
    [string]$Content,
    [string]$VariableName
  )

  $escapedName = [regex]::Escape($VariableName)
  $arrayPattern = '(?ms)\${0}\s*=\s*@\((?<body>.*?)\)' -f $escapedName
  $match = [regex]::Match($Content, $arrayPattern)
  if (-not $match.Success) {
    return @()
  }

  return @(
    [regex]::Matches($match.Groups['body'].Value, "'(?<entry>[^']+)'") |
      ForEach-Object { $_.Groups['entry'].Value }
  )
}

function Assert-NoNewlineWriteHasFinalLfNormalization {
  param(
    [string]$ScriptPath,
    [string]$ValueVariable,
    [string]$TestName
  )

  if (-not (Test-Path $ScriptPath)) {
    Write-TestResult -TestName $TestName -Passed $false -Message "Missing file: $ScriptPath"
    return
  }

  $lines = @(Get-Content -Path $ScriptPath)
  $setContentNeedle = '-Value $' + $ValueVariable + ' -NoNewline'
  $normalizationNeedle = '$' + $ValueVariable + ' = $' + $ValueVariable + '.TrimEnd() + "`n"'

  $matchingIndices = @()
  for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i].Contains('Set-Content') -and $lines[$i].Contains($setContentNeedle)) {
      $matchingIndices += $i
    }
  }

  if ($matchingIndices.Count -eq 0) {
    Write-TestResult -TestName $TestName -Passed $false -Message "No Set-Content -NoNewline write found for variable '$ValueVariable'."
    return
  }

  $violations = @()
  foreach ($index in $matchingIndices) {
    # Keep this window broad enough to tolerate nearby comment churn while still
    # requiring normalization to occur before the write call.
    $windowStart = [Math]::Max(0, $index - 10)
    $windowEnd = $index - 1
    $window = @()
    if ($windowStart -le $windowEnd) {
      $window = $lines[$windowStart..$windowEnd]
    }
    $hasNormalization = $false
    foreach ($windowLine in $window) {
      if ($windowLine.Contains($normalizationNeedle)) {
        $hasNormalization = $true
        break
      }
    }

    if (-not $hasNormalization) {
      $lineNumber = $index + 1
      $violations += "line $lineNumber"
    }
  }

  if ($violations.Count -gt 0) {
    Write-TestResult -TestName $TestName -Passed $false -Message "Missing final-LF normalization near $($violations -join ', ')."
  }
  else {
    Write-TestResult -TestName $TestName -Passed $true
  }
}

function Run-SyncScriptContractTests {
  Write-Host ""
  Write-Host "Sync script newline-safety contracts:" -ForegroundColor Magenta
  Write-Host ""

  $repoRoot = Get-RepoRoot

  Assert-NoNewlineWriteHasFinalLfNormalization `
    -ScriptPath (Join-Path $repoRoot 'scripts/sync-doc-counts.ps1') `
    -ValueVariable 'content' `
    -TestName 'sync-doc-counts.ps1 normalizes content before -NoNewline write'

  Assert-NoNewlineWriteHasFinalLfNormalization `
    -ScriptPath (Join-Path $repoRoot 'scripts/sync-banner-version.ps1') `
    -ValueVariable 'updatedContent' `
    -TestName 'sync-banner-version.ps1 normalizes SVG content before -NoNewline write'

  Assert-NoNewlineWriteHasFinalLfNormalization `
    -ScriptPath (Join-Path $repoRoot 'scripts/sync-banner-version.ps1') `
    -ValueVariable 'updatedContextContent' `
    -TestName 'sync-banner-version.ps1 normalizes context content before -NoNewline write'
}

function Run-CspellContractTests {
  Write-Host ""
  Write-Host "cspell config linter header/implementation contracts:" -ForegroundColor Magenta
  Write-Host ""

  $repoRoot = Get-RepoRoot
  $linterPath = Join-Path $repoRoot 'scripts/lint-cspell-config.js'

  if (-not (Test-Path $linterPath)) {
    Write-TestResult -TestName 'lint-cspell-config.js exists' -Passed $false -Message "Missing file: $linterPath"
    return
  }

  $lines = @(Get-Content -Path $linterPath)

  $headerCheckNumbers = @()
  $checkSectionNumbers = @()
  $mentionsRemovedCheck = $false

  $inHeader = $false
  foreach ($line in $lines) {
    $trimmed = $line.Trim()
    if ($trimmed -match '^//\s+Lints\s+cspell\.json\s+for\s+common\s+configuration\s+issues:') {
      $inHeader = $true
      continue
    }

    if ($inHeader -and $trimmed -match '^//\s+([0-9]+)\.\s+') {
      $headerCheckNumbers += [int]$Matches[1]
    }

    if ($inHeader -and -not $trimmed.StartsWith('//')) {
      $inHeader = $false
    }

    if ($trimmed -match '^//\s+[^\n]*?Check\s+([0-9]+):') {
      $checkSectionNumbers += [int]$Matches[1]
    }

    if ($trimmed -match 'Root words that belong in a categorized dictionary') {
      $mentionsRemovedCheck = $true
    }
  }

  $headerUnique = @($headerCheckNumbers | Sort-Object -Unique)
  $sectionsUnique = @($checkSectionNumbers | Sort-Object -Unique)

  Write-Info "Header checks: $($headerUnique -join ', ')"
  Write-Info "Section checks: $($sectionsUnique -join ', ')"

  $headerMatchesSections = ($headerUnique.Count -eq $sectionsUnique.Count) -and
    (@($headerUnique) -join ',') -ceq (@($sectionsUnique) -join ',')

  Write-TestResult `
    -TestName 'lint-cspell-config.js header check list matches implemented Check sections' `
    -Passed $headerMatchesSections `
    -Message "Header: [$($headerUnique -join ', ')], Sections: [$($sectionsUnique -join ', ')]"

  Write-TestResult `
    -TestName 'lint-cspell-config.js does not claim removed check 3 in header' `
    -Passed (-not $mentionsRemovedCheck) `
    -Message 'Found stale header text: "Root words that belong in a categorized dictionary"'
}

function Run-AgentValidationContractTests {
  Write-Host ""
  Write-Host "Agent spelling contract checks:" -ForegroundColor Magenta
  Write-Host ""

  $repoRoot = Get-RepoRoot
  $packageJsonPath = Join-Path $repoRoot 'package.json'
  $agentPreflightPath = Join-Path $repoRoot 'scripts/agent-preflight.ps1'

  if (-not (Test-Path $packageJsonPath)) {
    Write-TestResult -TestName 'package.json exists for validate:prepush contract' -Passed $false -Message "Missing file: $packageJsonPath"
    return
  }

  if (-not (Test-Path $agentPreflightPath)) {
    Write-TestResult -TestName 'agent-preflight.ps1 exists for spelling contract' -Passed $false -Message "Missing file: $agentPreflightPath"
    return
  }

  $packageJson = Get-Content -Path $packageJsonPath -Raw | ConvertFrom-Json
  $validatePrepushScript = [string]$packageJson.scripts.'validate:prepush'

  $includesLintSpelling = $validatePrepushScript -match 'npm run lint:spelling(?!:config)'
  Write-TestResult `
    -TestName 'validate:prepush includes npm run lint:spelling' `
    -Passed $includesLintSpelling `
    -Message "Current validate:prepush script: $validatePrepushScript"

  $includesLintSpellingConfig = $validatePrepushScript -match 'npm run lint:spelling:config'
  Write-TestResult `
    -TestName 'validate:prepush includes npm run lint:spelling:config' `
    -Passed $includesLintSpellingConfig `
    -Message "Current validate:prepush script: $validatePrepushScript"

  $agentPreflightContent = Get-Content -Path $agentPreflightPath -Raw

  Write-TestResult `
    -TestName 'agent-preflight reports changed spell-checkable file spelling checks' `
    -Passed ($agentPreflightContent -match 'Checking spelling on changed spell-checkable files') `
    -Message 'Expected status message for changed spell-checkable file spelling checks was not found.'

  Write-TestResult `
    -TestName 'agent-preflight runs cspell lint command' `
    -Passed ($agentPreflightContent -match 'cspell\s+lint') `
    -Message 'Expected cspell lint invocation was not found.'

  Write-TestResult `
    -TestName 'agent-preflight runs cspell through repo-local Node launcher' `
    -Passed ($agentPreflightContent -match 'run-node-bin\.js''\)\s+cspell|run-node-bin\.js"\)\s+cspell') `
    -Message 'Expected cspell invocation through scripts/run-node-bin.js was not found.'
}

function Run-PowerShellPathBindingContractTests {
  Write-Host ""
  Write-Host "PowerShell CLI path binding contracts:" -ForegroundColor Magenta
  Write-Host ""

  $repoRoot = Get-RepoRoot
  $pathScripts = @(
    @{ Name = 'check-eol.ps1'; Path = Join-Path $repoRoot 'scripts/check-eol.ps1' },
    @{ Name = 'normalize-eol.ps1'; Path = Join-Path $repoRoot 'scripts/normalize-eol.ps1' }
  )

  foreach ($scriptInfo in $pathScripts) {
    if (-not (Test-Path $scriptInfo.Path)) {
      Write-TestResult `
        -TestName "$($scriptInfo.Name) exists for path binding contract" `
        -Passed $false `
        -Message "Missing file: $($scriptInfo.Path)"
      continue
    }

    $content = Get-Content -Path $scriptInfo.Path -Raw
    $hasRemainingArgs = $content -match 'ValueFromRemainingArguments\s*=\s*\$true' -and
      $content -match '\$AdditionalPaths'
    $usesEffectivePaths = $content -match '\$effectivePaths'
    $coversGitignore = $content -match '\^\\\.gitignore\$'

    Write-TestResult `
      -TestName "$($scriptInfo.Name) captures trailing -Paths arguments under pwsh -File" `
      -Passed ($hasRemainingArgs -and $usesEffectivePaths) `
      -Message 'Expected ValueFromRemainingArguments AdditionalPaths and effective path merging.'

    Write-TestResult `
      -TestName "$($scriptInfo.Name) covers extensionless .gitignore EOL policy" `
      -Passed $coversGitignore `
      -Message 'Expected .gitignore in trackedTextPathPatterns so extensionless git config files are checked.'
  }

  $normalizePath = Join-Path $repoRoot 'scripts/normalize-eol.ps1'
  if (Test-Path $normalizePath) {
    $normalizeContent = Get-Content -Path $normalizePath -Raw
    Write-TestResult `
      -TestName 'normalize-eol.ps1 can emit modified paths for exact preflight restaging' `
      -Passed ($normalizeContent -match '\$ModifiedPathList' -and $normalizeContent -match '\[char\]0') `
      -Message 'Expected a NUL-delimited ModifiedPathList output contract for agent-preflight restaging.'
  }

  $preCommitPath = Join-Path $repoRoot '.githooks/pre-commit.ps1'
  if (Test-Path $preCommitPath) {
    $preCommitContent = Get-Content -Path $preCommitPath -Raw
    Write-TestResult `
      -TestName 'pre-commit delegates EOL normalization to agent-preflight' `
      -Passed ($preCommitContent -notmatch 'normalize-eol\.ps1' -and $preCommitContent -notmatch 'Invoke-EolNormalization') `
      -Message 'Expected pre-commit to avoid spawning EOL normalization; agent-preflight owns EOL repair.'

    Write-TestResult `
      -TestName 'pre-commit restages generated LLM instruction index after auto-fix' `
      -Passed ($preCommitContent -match 'LLM instruction auto-fix' -and $preCommitContent -match '\.llm/skills/index\.md') `
      -Message 'Expected pre-commit to stage both .llm/context.md and .llm/skills/index.md after lint-llm-instructions.ps1 -Fix.'
  }

  $agentPreflightPath = Join-Path $repoRoot 'scripts/agent-preflight.ps1'
  if (Test-Path $agentPreflightPath) {
    $agentPreflightContent = Get-Content -Path $agentPreflightPath -Raw
    Write-TestResult `
      -TestName 'agent-preflight reads git path lists as NUL-delimited process output' `
      -Passed ($agentPreflightContent -match 'Invoke-GitPathList' -and $agentPreflightContent -match "'-z'") `
      -Message 'Expected agent-preflight Git path discovery to avoid line-delimited path parsing.'

    Write-TestResult `
      -TestName 'agent-preflight staged path detection includes renames' `
      -Passed ($agentPreflightContent -match "--diff-filter=ACMR") `
      -Message 'Expected Get-GitStagedPaths to include renamed paths so auto-fixes can restage them.'

    Write-TestResult `
      -TestName 'agent-preflight owns EOL normalization and exact restaging' `
      -Passed ($agentPreflightContent -match 'normalize-eol\.ps1' -and $agentPreflightContent -match 'ModifiedPathList') `
      -Message 'Expected agent-preflight to run normalize-eol.ps1 and restage the NUL-delimited modified paths.'

    Write-TestResult `
      -TestName 'agent-preflight restages generated LLM instruction index after auto-fix' `
      -Passed ($agentPreflightContent -match 'LLM instruction auto-fix' -and $agentPreflightContent -match '\.llm/skills/index\.md') `
      -Message 'Expected agent-preflight -Fix to stage both .llm/context.md and .llm/skills/index.md after lint-llm-instructions.ps1 -Fix.'
  }
}

function Run-HookInstallContractTests {
  Write-Host ""
  Write-Host "Hook install contracts:" -ForegroundColor Magenta
  Write-Host ""

  $repoRoot = Get-RepoRoot
  $packageJsonPath = Join-Path $repoRoot 'package.json'
  $installHooksPath = Join-Path $repoRoot 'scripts/install-hooks.ps1'

  if (-not (Test-Path $packageJsonPath)) {
    Write-TestResult `
      -TestName 'package.json exists for hooks:install contract' `
      -Passed $false `
      -Message "Missing file: $packageJsonPath"
  }
  else {
    $packageJson = Get-Content -Path $packageJsonPath -Raw | ConvertFrom-Json
    $hooksInstallScript = [string]$packageJson.scripts.'hooks:install'
    Write-TestResult `
      -TestName 'hooks:install uses PowerShell installer instead of Unix-only chmod' `
      -Passed ($hooksInstallScript -eq 'pwsh -NoProfile -File scripts/install-hooks.ps1 -HooksOnly') `
      -Message "hooks:install: $hooksInstallScript"
  }

  if (-not (Test-Path $installHooksPath)) {
    Write-TestResult `
      -TestName 'install-hooks.ps1 exists for HooksOnly contract' `
      -Passed $false `
      -Message "Missing file: $installHooksPath"
  }
  else {
    $installHooksContent = Get-Content -Path $installHooksPath -Raw
    $hasHooksOnlyParam = $installHooksContent -match '\[switch\]\$HooksOnly'
    $hasHooksOnlyBranch = $installHooksContent -match 'if \(\$HooksOnly\)' -and
      $installHooksContent -match 'Install-GitHooks' -and
      $installHooksContent -match 'Set-GitPushDefaults'

    Write-TestResult `
      -TestName 'install-hooks.ps1 exposes HooksOnly hook setup path' `
      -Passed ($hasHooksOnlyParam -and $hasHooksOnlyBranch) `
      -Message 'Expected -HooksOnly param and branch configuring hooks plus push defaults.'
  }

  $hookEntrypoints = @('pre-commit', 'pre-push', 'pre-merge-commit', 'post-rewrite')
  $nonShellHooks = @()
  foreach ($hook in $hookEntrypoints) {
    $hookPath = Join-Path $repoRoot ".githooks/$hook"
    if (-not (Test-Path $hookPath)) {
      $nonShellHooks += "${hook}: missing"
      continue
    }

    $firstLine = Get-Content -Path $hookPath -TotalCount 1
    if ($firstLine -ne '#!/usr/bin/env sh') {
      $nonShellHooks += "${hook}: $firstLine"
    }
  }

  Write-TestResult `
    -TestName 'extensionless git hook entrypoints are POSIX launchers' `
    -Passed ($nonShellHooks.Count -eq 0) `
    -Message "Non-POSIX hook entrypoints: $($nonShellHooks -join '; ')"

  $preMergeImplementationPath = Join-Path $repoRoot '.githooks/pre-merge-commit.ps1'
  $preMergeImplementationContent = if (Test-Path $preMergeImplementationPath) {
    Get-Content -Path $preMergeImplementationPath -Raw
  }
  else {
    ''
  }
  $preMergeSpawnsPowerShell = $preMergeImplementationContent -match 'Get-Process -Id \$PID' -or
    $preMergeImplementationContent -match 'Get-Command pwsh' -or
    $preMergeImplementationContent -match '&\s*\$pwshPath\b' -or
    $preMergeImplementationContent -match '\$invokeArgs\s*\+='

  Write-TestResult `
    -TestName 'pre-merge-commit delegates to pre-commit without a second PowerShell startup' `
    -Passed (
      $preMergeImplementationContent -match '&\s*\$preCommit\s+@HookArgs' -and
      -not $preMergeSpawnsPowerShell
    ) `
    -Message 'Expected pre-merge-commit.ps1 to invoke pre-commit.ps1 in-process instead of spawning pwsh/powershell again.'

  $installHooksShPath = Join-Path $repoRoot 'scripts/install-hooks.sh'
  $installHooksShContent = if (Test-Path $installHooksShPath) {
    Get-Content -Path $installHooksShPath -Raw
  }
  else {
    ''
  }

  Write-TestResult `
    -TestName 'install-hooks.sh requires pwsh for tracked hook runtime' `
    -Passed (
      $installHooksShContent -match 'pwsh is required because tracked git hook entrypoints delegate to \.ps1 implementations' -and
      $installHooksShContent -notmatch 'elif check_command powershell'
    ) `
    -Message 'Expected Bash installer to require pwsh instead of accepting Windows PowerShell as a hook runtime.'

  Write-TestResult `
    -TestName 'install-hooks.sh filters hook entrypoints by basename before chmod' `
    -Passed (
      $installHooksShContent -match '\bhook_name="\$\{hook_file##\*/\}"' -and
      $installHooksShContent -match 'case "\$hook_name" in'
    ) `
    -Message 'Expected Bash installer to skip .ps1/artifact companions by basename, not by .githooks/<file> path.'

  Write-TestResult `
    -TestName 'install-hooks.ps1 requires pwsh for tracked hook runtime' `
    -Passed (
      $installHooksContent -match 'pwsh: NOT FOUND \(required git hook runtime\)' -and
      $installHooksContent -match 'Get-Command \$Command -ErrorAction SilentlyContinue'
    ) `
    -Message 'Expected PowerShell installer to detect missing pwsh accurately and fail hook installation.'
}

function Run-RepoLocalPrettierContractTests {
  Write-Host ""
  Write-Host "Repo-local Node tool invocation contracts:" -ForegroundColor Magenta
  Write-Host ""

  $repoRoot = Get-RepoRoot
  $launcherPath = Join-Path $repoRoot 'scripts/run-prettier.js'
  $packageJsonPath = Join-Path $repoRoot 'package.json'
  $prettierConfigPath = Join-Path $repoRoot '.prettierrc.json'
  $formatStagedPath = Join-Path $repoRoot 'scripts/format-staged-prettier.ps1'
  $lintStagedMarkdownPath = Join-Path $repoRoot 'scripts/lint-staged-markdown.ps1'
  $agentPreflightPath = Join-Path $repoRoot 'scripts/agent-preflight.ps1'
  $validateLintErrorCodesPath = Join-Path $repoRoot 'scripts/validate-lint-error-codes.ps1'
  $preCommitPath = Join-Path $repoRoot '.githooks/pre-commit'
  $preCommitImplPath = Join-Path $repoRoot '.githooks/pre-commit.ps1'
  $prePushPath = Join-Path $repoRoot '.githooks/pre-push'

  Write-TestResult `
    -TestName 'repo-local Prettier launcher exists' `
    -Passed (Test-Path $launcherPath) `
    -Message "Missing file: $launcherPath"

  $packageJson = Get-Content -Path $packageJsonPath -Raw | ConvertFrom-Json
  $formatScripts = @(
    'format:md',
    'format:md:check',
    'format:json',
    'format:json:check',
    'format:js',
    'format:js:check',
    'format:yaml',
    'format:yaml:check'
  )
  $formatScriptDrift = @()
  foreach ($scriptName in $formatScripts) {
    $scriptValue = [string]$packageJson.scripts.PSObject.Properties[$scriptName].Value
    if ($scriptValue -notmatch 'node\s+\./scripts/run-prettier\.js') {
      $formatScriptDrift += "${scriptName}: ${scriptValue}"
    }
  }

  Write-TestResult `
    -TestName 'package format scripts use repo-local Prettier launcher' `
    -Passed ($formatScriptDrift.Count -eq 0) `
    -Message "Drifted scripts: $($formatScriptDrift -join '; ')"

  if (-not (Test-Path $prettierConfigPath)) {
    Write-TestResult `
      -TestName '.prettierrc.json exists for EOL parity contract' `
      -Passed $false `
      -Message "Missing file: $prettierConfigPath"
  }
  else {
    $prettierConfig = Get-Content -Path $prettierConfigPath -Raw | ConvertFrom-Json
    $lfOverrideFiles = New-Object System.Collections.Generic.List[string]
    foreach ($override in @($prettierConfig.overrides)) {
      $endOfLineProperty = $override.options.PSObject.Properties['endOfLine']
      if ($null -eq $endOfLineProperty -or [string]$endOfLineProperty.Value -ne 'lf') {
        continue
      }

      foreach ($filePattern in @($override.files)) {
        $lfOverrideFiles.Add([string]$filePattern) | Out-Null
      }
    }

    Write-TestResult `
      -TestName 'Prettier LF overrides include .github/** to match .gitattributes' `
      -Passed ($lfOverrideFiles -contains '.github/**') `
      -Message "LF override files: $($lfOverrideFiles -join ', ')"
  }

  $prettierRequiredFiles = @($formatStagedPath, $agentPreflightPath)
  $requiredFiles = @($formatStagedPath, $lintStagedMarkdownPath, $agentPreflightPath, $validateLintErrorCodesPath, $prePushPath)
  $launcherDrift = @()
  foreach ($file in $prettierRequiredFiles) {
    if (-not (Test-Path $file)) {
      $launcherDrift += "missing: $file"
      continue
    }

    $content = Get-Content -Path $file -Raw
    if ($content -notmatch 'run-prettier\.js|run_prettier') {
      $launcherDrift += $file
    }
  }

  Write-TestResult `
    -TestName 'Prettier validation surfaces route through repo-local launcher' `
    -Passed ($launcherDrift.Count -eq 0) `
    -Message "Missing launcher reference: $($launcherDrift -join '; ')"

  $nodeToolDrift = @()
  foreach ($file in $requiredFiles) {
    if (-not (Test-Path $file)) {
      $nodeToolDrift += "missing: $file"
      continue
    }

    $content = Get-Content -Path $file -Raw
    if ($content -match 'markdownlint|cspell' -and $content -notmatch 'run-node-bin\.js|run_node_tool') {
      $nodeToolDrift += $file
    }
  }

  Write-TestResult `
    -TestName 'preflight and non-hook helpers route cspell/markdownlint through repo-local launcher' `
    -Passed ($nodeToolDrift.Count -eq 0) `
    -Message "Missing node-tool launcher reference: $($nodeToolDrift -join '; ')"

  $markdownFenceFixDrift = @()
  foreach ($file in @($lintStagedMarkdownPath, $agentPreflightPath)) {
    if (-not (Test-Path $file)) {
      $markdownFenceFixDrift += "missing: $file"
      continue
    }

    $content = Get-Content -Path $file -Raw
    if ($content -notmatch 'fix-markdown-fence-languages\.ps1') {
      $markdownFenceFixDrift += $file
    }
  }

  Write-TestResult `
    -TestName 'Markdown preflight paths run fence-language auto-fix before markdownlint' `
    -Passed ($markdownFenceFixDrift.Count -eq 0) `
    -Message "Missing fence fixer reference: $($markdownFenceFixDrift -join '; ')"

  if (Test-Path $preCommitImplPath) {
    $preCommitContent = Get-Content -Path $preCommitImplPath -Raw
    $forbiddenPreCommitWork = @(
      'run-prettier\.js',
      'markdownlint',
      'cspell\s+(lint|--no-progress)',
      'run-doc-link-lint',
      'format-staged-csharp\.ps1',
      'dotnet\s+tool\s+run\s+csharpier',
      'lint-tests\.ps1',
      'lint-duplicate-usings\.ps1',
      'normalize-eol\.ps1'
    )
    $preCommitWorkHits = @()
    foreach ($pattern in $forbiddenPreCommitWork) {
      if ($preCommitContent -match $pattern) {
        $preCommitWorkHits += $pattern
      }
    }

    Write-TestResult `
      -TestName 'pre-commit implementation excludes slow formatter/spelling/doc-link work' `
      -Passed ($preCommitWorkHits.Count -eq 0) `
      -Message "Forbidden pre-commit work: $($preCommitWorkHits -join ', ')"

    Write-TestResult `
      -TestName 'pre-commit refuses whole-file auto-stage on pre-existing unstaged hunks' `
      -Passed ($preCommitContent -match 'InitiallyUnstagedPaths' -and $preCommitContent -match 'Refusing to auto-stage whole file') `
      -Message 'Expected a partial-staging guard before hook auto-restages generated fixes.'

    Write-TestResult `
      -TestName 'pre-commit avoids line-only bare-fence pre-scan' `
      -Passed ($preCommitContent -notmatch 'MARKDOWN_FENCE_FIX_REQUIRED') `
      -Message 'A line-only bare-fence pre-scan mistakes normal closing fences for missing-language openings.'

    $stagedBlobNeedle = "'grep', '--cached', '-n', '-I', '-E', '-z'"
    Write-TestResult `
      -TestName 'pre-commit C# region guard batch-reads staged blobs' `
      -Passed ($preCommitContent.Contains($stagedBlobNeedle)) `
      -Message 'Expected region validation to inspect staged index blobs through batched git grep, not per-file worktree reads.'
  }

  if (Test-Path $preCommitPath) {
    $preCommitLauncherContent = Get-Content -Path $preCommitPath -Raw
    $fastPathIndex = $preCommitLauncherContent.IndexOf('--diff-filter=ACMR')
    $implementationLoadIndex = $preCommitLauncherContent.IndexOf('hook_impl=')

    Write-TestResult `
      -TestName 'pre-commit launcher exits before implementation load when no paths are staged' `
      -Passed (
        $preCommitLauncherContent -match 'diff\s+--cached\s+--quiet\s+--diff-filter=ACMR' -and
        $preCommitLauncherContent -match 'No staged files to check' -and
        $fastPathIndex -ge 0 -and
        $implementationLoadIndex -ge 0 -and
        $fastPathIndex -lt $implementationLoadIndex
      ) `
      -Message 'Expected the extensionless launcher to avoid loading full pre-commit.ps1 for an empty index.'
  }
  else {
    Write-TestResult `
      -TestName 'pre-commit launcher exits before implementation load when no paths are staged' `
      -Passed $false `
      -Message "Missing file: $preCommitPath"
  }

  $forbiddenHits = @()
  foreach ($file in $requiredFiles + @($packageJsonPath)) {
    if (-not (Test-Path $file)) {
      continue
    }

    $lines = @(Get-Content -Path $file)
    for ($i = 0; $i -lt $lines.Count; $i++) {
      if ($lines[$i] -match 'npx\s+(--yes\s+)?(--no-install\s+)?(prettier|markdownlint|cspell)') {
        $forbiddenHits += "${file}:$($i + 1): $($lines[$i].Trim())"
      }
    }
  }

  Write-TestResult `
    -TestName 'local hooks/scripts do not invoke pinned Node tools through npx' `
    -Passed ($forbiddenHits.Count -eq 0) `
    -Message "Forbidden invocations: $($forbiddenHits -join '; ')"

  $llmForbiddenHits = @()
  $llmFiles = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot '.llm') -Recurse -File -Filter '*.md')
  foreach ($file in $llmFiles) {
    $lines = @(Get-Content -Path $file.FullName)
    for ($i = 0; $i -lt $lines.Count; $i++) {
      $line = $lines[$i]
      if ($line -match 'npx\s+(--yes\s+)?(--no-install\s+)?(prettier|markdownlint|cspell)') {
        $llmForbiddenHits += "$($file.FullName):$($i + 1): $($line.Trim())"
        continue
      }

      if ($line -match '^\s*(prettier|markdownlint|cspell)\s+(--|lint|stdin|--write|--check|--config)') {
        $llmForbiddenHits += "$($file.FullName):$($i + 1): $($line.Trim())"
        continue
      }

      if ($line -match '`(prettier|markdownlint|cspell)\s+(--|lint|stdin|--write|--check|--config)') {
        $llmForbiddenHits += "$($file.FullName):$($i + 1): $($line.Trim())"
      }
    }
  }

  Write-TestResult `
    -TestName 'LLM guidance does not teach host-PATH pinned Node tool invocations' `
    -Passed ($llmForbiddenHits.Count -eq 0) `
    -Message "Forbidden LLM guidance: $($llmForbiddenHits -join '; ')"
}

function Run-PrePushLastResortGuidanceContractTests {
  Write-Host ""
  Write-Host "Pre-push last-resort guidance contracts:" -ForegroundColor Magenta
  Write-Host ""

  $repoRoot = Get-RepoRoot
  $prePushPath = Join-Path $repoRoot '.githooks/pre-push'

  if (-not (Test-Path $prePushPath)) {
    Write-TestResult `
      -TestName 'pre-push hook exists for last-resort contract' `
      -Passed $false `
      -Message "Missing file: $prePushPath"
  }
  else {
    $prePushContent = Get-Content -Path $prePushPath -Raw

    Write-TestResult `
      -TestName 'pre-push declares last-resort fast-hook scope' `
      -Passed ($prePushContent -match 'last-resort' -and $prePushContent -match 'must stay fast') `
      -Message 'Expected the hook header to describe the last-resort fast-hook contract.'

    $forbiddenHookChecks = @(
      'audit-license-years\.sh',
      'run-prettier\.js',
      'cspell\s+lint',
      'run-doc-link-lint',
      'lint-meta-files',
      'run_conditional_tests',
      'test-wiki-generation',
      'test_wiki_scripts'
    )
    $hookViolations = @()
    foreach ($pattern in $forbiddenHookChecks) {
      if ($prePushContent -match $pattern) {
        $hookViolations += $pattern
      }
    }

    Write-TestResult `
      -TestName 'pre-push does not run routine lint, formatting, license, or regression suites' `
      -Passed ($hookViolations.Count -eq 0) `
      -Message "Forbidden hook checks: $($hookViolations -join ', ')"
  }

  $staleGuidancePatterns = @(
    'pre-push hooks?\s+(and\s+CI/CD\s+)?will\s+REJECT',
    'pre-push hooks?\s+REJECT',
    'pre-push hook:\s+Check\s+#[0-9]+\s+runs',
    'pre-push hook runs both'
  )
  $staleGuidanceHits = @()
  $llmFiles = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot '.llm') -Recurse -File -Filter '*.md')
  foreach ($file in $llmFiles) {
    $lines = @(Get-Content -Path $file.FullName)
    for ($i = 0; $i -lt $lines.Count; $i++) {
      foreach ($pattern in $staleGuidancePatterns) {
        if ($lines[$i] -match $pattern) {
          $relativePath = [System.IO.Path]::GetRelativePath($repoRoot, $file.FullName).Replace('\', '/')
          $staleGuidanceHits += "${relativePath}:$($i + 1): $($lines[$i].Trim())"
          break
        }
      }
    }
  }

  Write-TestResult `
    -TestName 'LLM guidance does not claim pre-push runs slow routine validators' `
    -Passed ($staleGuidanceHits.Count -eq 0) `
    -Message "Stale guidance: $($staleGuidanceHits -join '; ')"
}

function Run-ReleaseDrafterChangelogVersionContractTests {
  Write-Host ""
  Write-Host "Release drafter changelog version contracts:" -ForegroundColor Magenta
  Write-Host ""

  $repoRoot = Get-RepoRoot
  $workflowPath = Join-Path $repoRoot '.github/workflows/release-drafter.yml'

  if (-not (Test-Path $workflowPath)) {
    Write-TestResult -TestName 'release-drafter workflow exists for version extraction contracts' -Passed $false -Message "Missing file: $workflowPath"
    return
  }

  $workflowContent = Get-Content -Path $workflowPath -Raw
  $workflowLines = @($workflowContent -split "`r?`n")
  $onBlockLines = @()
  $insideOnBlock = $false
  foreach ($line in $workflowLines) {
    if ($line -ceq 'on:') {
      $insideOnBlock = $true
      continue
    }
    if ($insideOnBlock -and $line -match '^\S') {
      break
    }
    if ($insideOnBlock) {
      $onBlockLines += $line
    }
  }
  $hasPushTrigger = @($onBlockLines | Where-Object { $_ -match '^\s+push\s*:' }).Count -gt 0
  $hasWorkflowDispatchTrigger = @($onBlockLines | Where-Object { $_ -match '^\s+workflow_dispatch\s*:' }).Count -gt 0

  Write-TestResult `
    -TestName 'release-drafter is manual-only so release publishes cannot race draft updates' `
    -Passed ((-not $hasPushTrigger) -and $hasWorkflowDispatchTrigger) `
    -Message 'Expected release-drafter.yml to expose workflow_dispatch without an automatic push trigger.'

  Write-TestResult `
    -TestName 'release-drafter extracts latest changelog header before version selection' `
    -Passed ($workflowContent.Contains('CHANGELOG_FIRST_HEADER=')) `
    -Message 'Expected CHANGELOG_FIRST_HEADER assignment was not found.'

  Write-TestResult `
    -TestName 'release-drafter assigns VERSION from CHANGELOG_VERSION' `
    -Passed ($workflowContent.Contains('VERSION="${CHANGELOG_VERSION}"')) `
    -Message 'Expected VERSION assignment from CHANGELOG_VERSION was not found.'

  Write-TestResult `
    -TestName 'release-drafter uses semver-like release-drafter tag when first header is Unreleased' `
    -Passed ($workflowContent.Contains("First changelog header is Unreleased; using semver-like release-drafter tag")) `
    -Message 'Expected semver-like release-drafter tag preference for Unreleased changelog header was not found.'

  Write-TestResult `
    -TestName 'release-drafter normalizes semver-like drafter tag by stripping leading v/V' `
    -Passed ($workflowContent.Contains("DRAFTER_TAG_NORMALIZED") -and $workflowContent.Contains("sed -E 's/^[vV]//'") -and $workflowContent.Contains('CHANGELOG_VERSION="$DRAFTER_TAG_NORMALIZED"')) `
    -Message 'Expected normalization of semver-like release-drafter tag to unprefixed version was not found.'

  Write-TestResult `
    -TestName 'release-drafter semver-like drafter tag regex accepts optional v or V prefix' `
    -Passed ($workflowContent.Contains("DRAFTER_TAG_SEMVER_REGEX='^[vV]?[0-9]+")) `
    -Message 'Expected semver-like release-drafter tag regex with optional v/V prefix was not found.'

  Write-TestResult `
    -TestName 'release-drafter compares normalized drafter tag against VERSION before mismatch notice' `
    -Passed ($workflowContent.Contains('if [ -n "$DRAFTER_TAG" ] && [ "$DRAFTER_TAG_NORMALIZED" != "$VERSION" ]; then')) `
    -Message 'Expected mismatch comparison to use DRAFTER_TAG_NORMALIZED was not found.'

  $semverLikeTags = @(
    @{ Input = 'v1.2.3'; Expected = '1.2.3' },
    @{ Input = 'V1.2.3'; Expected = '1.2.3' },
    @{ Input = '1.2.3'; Expected = '1.2.3' },
    @{ Input = 'v1.2.3-beta'; Expected = '1.2.3-beta' }
  )
  $semverLikeRegex = $null
  $regexMatch = [regex]::Match($workflowContent, "DRAFTER_TAG_SEMVER_REGEX='([^']+)'")
  if ($regexMatch.Success) {
    $semverLikeRegex = $regexMatch.Groups[1].Value
  }
  $normalizationPasses = $true
  if ([string]::IsNullOrWhiteSpace($semverLikeRegex)) {
    $normalizationPasses = $false
  }
  foreach ($case in $semverLikeTags) {
    if ($case.Input -notmatch $semverLikeRegex) {
      $normalizationPasses = $false
      break
    }
    $normalizedTag = ($case.Input -replace '^[vV]', '')
    if ($normalizedTag -cne $case.Expected) {
      $normalizationPasses = $false
      break
    }
  }

  Write-TestResult `
    -TestName 'release-drafter semver-like tag normalization behavior strips leading v/V only' `
    -Passed $normalizationPasses `
    -Message 'Expected v/V-prefixed semver-like tags to normalize to unprefixed versions while preserving already-unprefixed tags.'

  Write-TestResult `
    -TestName 'release-drafter falls back to next semver changelog header when Unreleased and release-drafter tag is not semver-like' `
    -Passed ($workflowContent.Contains('if [ -n "$CHANGELOG_NEXT_SEMVER_HEADER" ]; then') -and $workflowContent.Contains("release-drafter tag is not semver-like; using next semver header")) `
    -Message 'Expected fallback to next semver changelog header was not found.'

  Write-TestResult `
    -TestName 'release-drafter errors when changelog is Unreleased and no semver version source exists' `
    -Passed ($workflowContent.Contains('no semver-like release-drafter tag or next semver header was found')) `
    -Message 'Expected hard failure for unresolved Unreleased changelog version was not found.'

  Write-TestResult `
    -TestName 'release-drafter refuses literal Unreleased for release tag/name version' `
    -Passed ($workflowContent.Contains("grep -Eqi '^unreleased$'") -and $workflowContent.Contains('Refusing to use literal Unreleased as release tag/name')) `
    -Message 'Expected explicit guard against literal Unreleased release tag/name was not found.'

  Write-TestResult `
    -TestName 'release-drafter parses semver from changelog header' `
    -Passed ($workflowContent.Contains('CHANGELOG_FIRST_HEADER') -and $workflowContent.Contains('sed -E')) `
    -Message 'Expected semver changelog header parsing command was not found.'

  Write-TestResult `
    -TestName 'release-drafter does not trust tag_name output for VERSION assignment' `
    -Passed (-not ($workflowContent -match 'VERSION=.*steps\.release_drafter\.outputs\.tag_name')) `
    -Message 'Found direct VERSION assignment from release-drafter tag_name output.'

  Write-TestResult `
    -TestName 'release-drafter updates release tag/name using changelog-derived version' `
    -Passed ($workflowContent -match '-F tag_name="\$VERSION"' -and $workflowContent -match '-F name="\$VERSION"') `
    -Message 'Expected release PATCH request to include tag_name/name fields from VERSION.'

  $earlyExitAfterChangelogNotice = $false
  for ($i = 0; $i -lt $workflowLines.Count; $i++) {
    if ($workflowLines[$i] -match 'Changelog section already exists') {
      $windowEnd = [Math]::Min($workflowLines.Count - 1, $i + 10)
      for ($j = $i + 1; $j -le $windowEnd; $j++) {
        if ($workflowLines[$j] -match '^\s*exit\s+0\s*$') {
          $earlyExitAfterChangelogNotice = $true
          break
        }
      }
      if ($earlyExitAfterChangelogNotice) {
        break
      }
    }
  }

  Write-TestResult `
    -TestName 'release-drafter does not early-exit before PATCH when changelog section already exists' `
    -Passed (-not $earlyExitAfterChangelogNotice) `
    -Message 'Found early-exit path that can skip tag/name PATCH when changelog section already exists.'

  Write-TestResult `
    -TestName 'release-drafter preserves existing release body when changelog section already exists' `
    -Passed ($workflowContent.Contains('cp "${RUNNER_TEMP}/current_body.md" "${RUNNER_TEMP}/new_body.md"')) `
    -Message 'Expected current release body to be preserved when changelog section already exists.'
}

function Run-ReleaseWorkflowChangelogContractTests {
  Write-Host ""
  Write-Host "Release workflow changelog heading contracts:" -ForegroundColor Magenta
  Write-Host ""

  $repoRoot = Get-RepoRoot
  $workflowPaths = @(
    Join-Path $repoRoot '.github/workflows/release-tag.yml'
    Join-Path $repoRoot '.github/workflows/release.yml'
  )
  $publishWorkflowPath = Join-Path $repoRoot '.github/workflows/release.yml'
  $publishWorkflowContent = Get-Content -Path $publishWorkflowPath -Raw

  $missingSectionHelper = @()
  $rawHeadingGrep = @()
  foreach ($workflowPath in $workflowPaths) {
    if (-not (Test-Path $workflowPath)) {
      $missingSectionHelper += "missing: $workflowPath"
      continue
    }

    $content = Get-Content -Path $workflowPath -Raw
    $relativePath = [System.IO.Path]::GetRelativePath($repoRoot, $workflowPath).Replace('\', '/')
    if ($content -notmatch 'Get-ChangelogSection') {
      $missingSectionHelper += $relativePath
    }
    if ($content -match 'grep\s+-Eq\s+"\^##\s+\\\[') {
      $rawHeadingGrep += $relativePath
    }
  }

  Write-TestResult `
    -TestName 'release tag/publish workflows validate changelog release-note content' `
    -Passed ($missingSectionHelper.Count -eq 0) `
    -Message "Missing Get-ChangelogSection usage: $($missingSectionHelper -join '; ')"

  Write-TestResult `
    -TestName 'release tag/publish workflows avoid raw changelog heading grep' `
    -Passed ($rawHeadingGrep.Count -eq 0) `
    -Message "Raw heading grep found in: $($rawHeadingGrep -join '; ')"

  $strictReleaseTagRegex = '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$'
  $acceptedReleaseTags = @('0.0.0', '1.2.3', '10.20.30')
  $rejectedReleaseTags = @(
    '01.2.3',
    '1.02.3',
    '1.2.03',
    'v1.2.3',
    '1.2',
    '1.2.3.4',
    '1.2.x',
    '1.2.3-alpha'
  )
  $verifierHasStrictReleaseTagRegex = $publishWorkflowContent.Contains("grep -Eq '$strictReleaseTagRegex'")
  $strictRegexAcceptsExpectedTags = @(
    $acceptedReleaseTags | Where-Object { $_ -notmatch $strictReleaseTagRegex }
  ).Count -eq 0
  $strictRegexRejectsExpectedTags = @(
    $rejectedReleaseTags | Where-Object { $_ -match $strictReleaseTagRegex }
  ).Count -eq 0

  $publishTriggerDelegatesStrictnessToVerifier = (
    $publishWorkflowContent.Contains('- "[0-9]*.[0-9]*.[0-9]*"') -and
    -not $publishWorkflowContent.Contains('- "[0-9]+.[0-9]+.[0-9]+"') -and
    $publishWorkflowContent.Contains('Release tags must use unprefixed X.Y.Z semver.') -and
    $verifierHasStrictReleaseTagRegex -and
    $strictRegexAcceptsExpectedTags -and
    $strictRegexRejectsExpectedTags
  )

  Write-TestResult `
    -TestName 'release publish workflow uses unambiguous tag glob before strict verification' `
    -Passed $publishTriggerDelegatesStrictnessToVerifier `
    -Message 'Expected release.yml tag filter to use an unambiguous digit-start glob while verify-tag enforces exact no-leading-zero semver.'
}

function Run-ReleaseWorkflowGitHubCliContractTests {
  Write-Host ""
  Write-Host "Release workflow GitHub CLI contracts:" -ForegroundColor Magenta
  Write-Host ""

  $repoRoot = Get-RepoRoot
  $workflowPath = Join-Path $repoRoot '.github/workflows/release.yml'
  $workflowContent = Get-Content -Path $workflowPath -Raw
  $repoEnvPattern = 'GH_REPO:\s*\$\{\{\s*github\.repository\s*\}\}'

  $publishHasRepo = $workflowContent -match "(?ms)- name: Publish GitHub Release.*?env:.*?${repoEnvPattern}.*?run:"
  $verifyHasRepo = $workflowContent -match "(?ms)- name: Verify GitHub Release assets.*?env:.*?${repoEnvPattern}.*?run:"

  Write-TestResult `
    -TestName 'release publish gh commands set repository context' `
    -Passed $publishHasRepo `
    -Message 'Expected Publish GitHub Release to set GH_REPO from github.repository.'

  Write-TestResult `
    -TestName 'release asset verification gh commands set repository context' `
    -Passed $verifyHasRepo `
    -Message 'Expected Verify GitHub Release assets to set GH_REPO from github.repository.'
}

function Run-ReleasePublishWorkflowBudgetContractTests {
  Write-Host ""
  Write-Host "Release publish workflow budget contracts:" -ForegroundColor Magenta
  Write-Host ""

  $repoRoot = Get-RepoRoot
  $workflowPath = Join-Path $repoRoot '.github/workflows/release.yml'
  $workflowContent = Get-Content -Path $workflowPath -Raw
  $exporterPath = Join-Path $repoRoot 'scripts/unity/export-unitypackage.sh'
  $exporterContent = Get-Content -Path $exporterPath -Raw

  $jobTimeoutMatch = [regex]::Match(
    $workflowContent,
    '(?ms)^\s*unitypackage:\s*.*?^\s*timeout-minutes:\s*(?<minutes>\d+)\s*$'
  )
  $lockTimeoutMatch = [regex]::Match(
    $workflowContent,
    '(?ms)^\s*- name: Acquire organization Unity lock\s*\r?\n(?:(?!^\s*- name:).)*?^\s+with:\s*\r?\n(?:(?!^\s*- name:).)*?^\s+timeout-minutes:\s*["'']?(?<minutes>\d+)["'']?\s*$'
  )
  $unityTimeoutMatch = [regex]::Match(
    $exporterContent,
    'UNITY_TIMEOUT="\$\{UNITY_TIMEOUT:-(?<seconds>\d+)\}"'
  )

  $jobTimeoutMinutes = if ($jobTimeoutMatch.Success) { [int]$jobTimeoutMatch.Groups['minutes'].Value } else { 0 }
  $lockTimeoutMinutes = if ($lockTimeoutMatch.Success) { [int]$lockTimeoutMatch.Groups['minutes'].Value } else { 0 }
  $unityTimeoutMinutes = if ($unityTimeoutMatch.Success) { [int][Math]::Ceiling(([int]$unityTimeoutMatch.Groups['seconds'].Value) / 60.0) } else { 0 }
  $minimumOverheadMinutes = 30
  $requiredJobTimeoutMinutes = $lockTimeoutMinutes + $unityTimeoutMinutes + $minimumOverheadMinutes
  $timeoutBudgetIsCoherent = (
    $jobTimeoutMatch.Success -and
    $lockTimeoutMatch.Success -and
    $unityTimeoutMatch.Success -and
    $jobTimeoutMinutes -ge $requiredJobTimeoutMinutes
  )

  Write-TestResult `
    -TestName 'release unitypackage job timeout covers lock wait and export budget' `
    -Passed $timeoutBudgetIsCoherent `
    -Message "Expected unitypackage job timeout to be at least lock timeout + Unity export timeout + ${minimumOverheadMinutes}m overhead. Job=${jobTimeoutMinutes}m, lock=${lockTimeoutMinutes}m, Unity=${unityTimeoutMinutes}m, required=${requiredJobTimeoutMinutes}m."
}

function Run-ReleasePrepareWorkflowContractTests {
  Write-Host ""
  Write-Host "Release prepare workflow contracts:" -ForegroundColor Magenta
  Write-Host ""

  $repoRoot = Get-RepoRoot
  $workflowPath = Join-Path $repoRoot '.github/workflows/release-prepare.yml'
  $workflowContent = Get-Content -Path $workflowPath -Raw

  $usesRobustGitBranchLookup = (
    $workflowContent.Contains('git ls-remote --exit-code --heads origin "${branch}"') -and
    $workflowContent.Contains('branch_lookup_exit=$?') -and
    $workflowContent.Contains('if [ "${branch_lookup_exit}" -ne 2 ]; then') -and
    $workflowContent.Contains('Failed to check whether branch ${branch} already exists.') -and
    -not $workflowContent.Contains('git/ref/heads/${branch}') -and
    -not $workflowContent.Contains('if git ls-remote --exit-code --heads origin "${branch}"')
  )
  $usesRobustGitTagLookup = (
    $workflowContent.Contains('tag_lookup_output="$(gh api -i "repos/${GITHUB_REPOSITORY}/git/ref/tags/${version}" 2>&1)"') -and
    $workflowContent.Contains('tag_lookup_exit=$?') -and
    $workflowContent.Contains('if [ "${tag_lookup_exit}" -eq 0 ]; then') -and
    $workflowContent.Contains('Failed to check whether tag ${version} already exists.') -and
    $workflowContent.Contains('"status":"404"') -and
    $workflowContent.Contains('grep -E ''(^HTTP/[0-9.]+ 404( |$)|"status":"404")'' >/dev/null') -and
    -not $workflowContent.Contains('gh api "repos/${GITHUB_REPOSITORY}/git/ref/tags/${version}" >/dev/null 2>&1') -and
    -not ($workflowContent -match 'gh api[^\r\n]+\|\|\s*true') -and
    -not ($workflowContent -match 'grep -Eq .*\bstatus')
  )
  $notesIndex = $workflowContent.IndexOf('scripts/release-tools/write-release-notes.ps1')
  $branchPushIndex = $workflowContent.IndexOf('push origin "HEAD:refs/heads/${BRANCH}"')
  $generatesNotesBeforePushingBranch = (
    $notesIndex -ge 0 -and
    $branchPushIndex -ge 0 -and
    $notesIndex -lt $branchPushIndex
  )

  Write-TestResult `
    -TestName 'release prepare checks existing release branches with robust git heads lookup' `
    -Passed $usesRobustGitBranchLookup `
    -Message 'Expected release-prepare.yml to treat git ls-remote exit 2 as absent while failing other lookup errors.'

  Write-TestResult `
    -TestName 'release prepare checks existing tags without hiding API failures' `
    -Passed $usesRobustGitTagLookup `
    -Message 'Expected release-prepare.yml to treat tag lookup 404 as absent while failing auth, rate-limit, and other API errors.'

  Write-TestResult `
    -TestName 'release prepare validates release notes before pushing branch' `
    -Passed $generatesNotesBeforePushingBranch `
    -Message 'Expected write-release-notes.ps1 to run before pushing release/X.Y.Z so failed note generation leaves no remote branch.'
}

function Run-ReleaseTagWorkflowContractTests {
  Write-Host ""
  Write-Host "Release tag workflow contracts:" -ForegroundColor Magenta
  Write-Host ""

  $repoRoot = Get-RepoRoot
  $workflowPath = Join-Path $repoRoot '.github/workflows/release-tag.yml'
  $workflowContent = Get-Content -Path $workflowPath -Raw

  $hasTagTargetCheck = (
    $workflowContent.Contains('tag_target="$(git rev-list -n 1 "${version}")"') -and
    $workflowContent.Contains('[ "${tag_target}" = "${GITHUB_SHA}" ]') -and
    $workflowContent.Contains('already exists at ${tag_target}, not release commit ${GITHUB_SHA}')
  )

  $credentialStepIndex = $workflowContent.IndexOf('- name: Check auto-commit GitHub App credentials', [StringComparison]::Ordinal)
  $tokenStepIndex = $workflowContent.IndexOf('- name: Generate auto-commit GitHub App token', [StringComparison]::Ordinal)
  $hasCredentialCheck = (
    $credentialStepIndex -ge 0 -and
    $tokenStepIndex -gt $credentialStepIndex -and
    $workflowContent.Contains('AUTO_COMMIT_APP_ID: ${{ secrets.AUTO_COMMIT_APP_ID }}') -and
    $workflowContent.Contains('AUTO_COMMIT_APP_PRIVATE_KEY: ${{ secrets.AUTO_COMMIT_APP_PRIVATE_KEY }}') -and
    $workflowContent.Contains('required to push release tags')
  )

  $hasDefaultBranchGate = (
    $workflowContent.Contains('github.ref_name == github.event.repository.default_branch') -and
    $workflowContent -notmatch "(?ms)on:\s*\r?\n\s*push:\s*\r?\n\s*branches:"
  )

  $checkoutStepIndex = $workflowContent.IndexOf('- name: Checkout', [StringComparison]::Ordinal)
  $checkoutFetchTagsIndex = if ($checkoutStepIndex -ge 0) {
    $workflowContent.IndexOf('fetch-tags: true', $checkoutStepIndex, [StringComparison]::Ordinal)
  } else {
    -1
  }
  $subjectCheckIndex = $workflowContent.IndexOf('subject="$(git log -1 --format=%s)"', [StringComparison]::Ordinal)
  $nonReleaseExitIndex = $workflowContent.IndexOf('Head commit is not a release commit; nothing to do.', [StringComparison]::Ordinal)
  $existingTagNoOpIndex = $workflowContent.IndexOf('git show-ref --verify --quiet "refs/tags/${version}"', [StringComparison]::Ordinal)
  $untaggedWarningIndex = $workflowContent.IndexOf('Version ${version} is untagged and CHANGELOG.md documents it', [StringComparison]::Ordinal)
  $nonReleaseProceedFalseIndex = if ($subjectCheckIndex -ge 0) {
    $workflowContent.IndexOf('echo "proceed=false" >> "${GITHUB_OUTPUT}"', $subjectCheckIndex, [StringComparison]::Ordinal)
  } else {
    -1
  }
  $nonReleaseExitZeroIndex = if ($nonReleaseProceedFalseIndex -ge 0) {
    $workflowContent.IndexOf('exit 0', $nonReleaseProceedFalseIndex, [StringComparison]::Ordinal)
  } else {
    -1
  }
  $releaseHeadingValidationIndex = if ($nonReleaseExitZeroIndex -ge 0) {
    $workflowContent.IndexOf('Release commit for ${version} has no CHANGELOG.md section with release-note content.', $nonReleaseExitZeroIndex, [StringComparison]::Ordinal)
  } else {
    -1
  }
  $tagLookupIndex = $workflowContent.IndexOf('tag_lookup_output="$(gh api -i "repos/${GITHUB_REPOSITORY}/git/ref/tags/${version}"', [StringComparison]::Ordinal)
  $tagMismatchIndex = $workflowContent.IndexOf('already exists at ${tag_target}, not release commit ${GITHUB_SHA}', [StringComparison]::Ordinal)
  $releaseSectionValidationIndex = if ($nonReleaseExitZeroIndex -ge 0) {
    $workflowContent.IndexOf('Get-ChangelogSection -Content $content -Version $env:CHANGELOG_VERSION', $nonReleaseExitZeroIndex, [StringComparison]::Ordinal)
  } else {
    -1
  }
  $checksTagsAfterReleaseDetection = (
    $subjectCheckIndex -ge 0 -and
    $nonReleaseExitIndex -gt $subjectCheckIndex -and
    $nonReleaseProceedFalseIndex -gt $nonReleaseExitIndex -and
    $nonReleaseExitZeroIndex -gt $nonReleaseProceedFalseIndex -and
    $releaseSectionValidationIndex -gt $nonReleaseExitZeroIndex -and
    $releaseHeadingValidationIndex -gt $nonReleaseExitZeroIndex -and
    $tagLookupIndex -gt $releaseHeadingValidationIndex -and
    $tagMismatchIndex -gt $tagLookupIndex
  )
  $checksLocalTagBeforeUntaggedWarning = (
    $subjectCheckIndex -ge 0 -and
    $existingTagNoOpIndex -gt $subjectCheckIndex -and
    $untaggedWarningIndex -gt $existingTagNoOpIndex -and
    $existingTagNoOpIndex -lt $nonReleaseProceedFalseIndex
  )
  $fetchesTagsBeforeLocalTaggedNoOp = (
    $checkoutStepIndex -ge 0 -and
    $checkoutFetchTagsIndex -gt $checkoutStepIndex -and
    $checkoutFetchTagsIndex -lt $existingTagNoOpIndex
  )
  $usesRobustTagLookup = (
    $tagLookupIndex -ge 0 -and
    $workflowContent.Contains('tag_lookup_exit=$?') -and
    $workflowContent.Contains('if [ "${tag_lookup_exit}" -eq 0 ]; then') -and
    $workflowContent.Contains('Failed to check whether tag ${version} already exists.') -and
    $workflowContent.Contains('"status":"404"') -and
    $workflowContent.Contains('grep -E ''(^HTTP/[0-9.]+ 404( |$)|"status":"404")'' >/dev/null') -and
    -not ($workflowContent -match 'gh api[^\r\n]+\|\|\s*true') -and
    -not ($workflowContent -match 'grep -Eq .*\bstatus')
  )

  Write-TestResult `
    -TestName 'release tag workflow fails when existing tag points elsewhere' `
    -Passed $hasTagTargetCheck `
    -Message 'Expected release-tag.yml to compare existing tag target with GITHUB_SHA and error on mismatches.'

  Write-TestResult `
    -TestName 'release tag workflow checks existing tags only after release detection' `
    -Passed $checksTagsAfterReleaseDetection `
    -Message 'Expected release-tag.yml to exit cleanly for non-release package/changelog pushes before checking existing tag targets.'

  Write-TestResult `
    -TestName 'release tag workflow validates changelog release-note content before tag lookup' `
    -Passed ($releaseSectionValidationIndex -gt $nonReleaseExitZeroIndex -and $releaseSectionValidationIndex -lt $tagLookupIndex) `
    -Message 'Expected release-tag.yml to call Get-ChangelogSection before checking or creating a release tag.'

  Write-TestResult `
    -TestName 'release tag workflow checks existing tags without hiding API failures' `
    -Passed $usesRobustTagLookup `
    -Message 'Expected release-tag.yml to treat tag lookup 404 as absent while failing auth, rate-limit, and other API errors.'

  Write-TestResult `
    -TestName 'release tag workflow suppresses untagged warning for locally-known tags' `
    -Passed $checksLocalTagBeforeUntaggedWarning `
    -Message 'Expected release-tag.yml to check refs/tags/${version} before warning that the documented version is untagged.'

  Write-TestResult `
    -TestName 'release tag workflow fetches tags before local tagged no-op check' `
    -Passed $fetchesTagsBeforeLocalTaggedNoOp `
    -Message 'Expected release-tag.yml checkout to fetch tags before using local refs/tags/${version} for the non-release no-op path.'

  Write-TestResult `
    -TestName 'release tag workflow checks app credentials before token action' `
    -Passed $hasCredentialCheck `
    -Message 'Expected release-tag.yml to validate AUTO_COMMIT_APP_* before create-github-app-token.'

  Write-TestResult `
    -TestName 'release tag workflow runs on repository default branch' `
    -Passed $hasDefaultBranchGate `
    -Message 'Expected release-tag.yml to avoid hard-coded main/master branch filters and gate on github.event.repository.default_branch.'
}

function Run-ReleasePackageContentContractTests {
  Write-Host ""
  Write-Host "Release package content contracts:" -ForegroundColor Magenta
  Write-Host ""

  $repoRoot = Get-RepoRoot
  $requiredUnityPackageEntries = @(
    'Editor',
    'Editor.meta',
    'Runtime',
    'Runtime.meta',
    'Samples~',
    'Shaders',
    'Shaders.meta',
    'Styles',
    'Styles.meta',
    'URP',
    'URP.meta',
    'link.xml',
    'link.xml.meta'
  )
  $requiredPackageFilesEntries = @(
    $requiredUnityPackageEntries
    'scripts.meta'
    'scripts/postinstall-hooks.js'
    'scripts/postinstall-hooks.js.meta'
  )
  $requiredUnityPackageFolders = @(
    'Editor',
    'Runtime',
    'Samples~',
    'Shaders',
    'Styles',
    'URP'
  )

  $packagePath = Join-Path $repoRoot 'package.json'
  $package = Get-Content -Path $packagePath -Raw | ConvertFrom-Json
  $packageFiles = @($package.files)
  $missingPackageFiles = @($requiredPackageFilesEntries | Where-Object { $_ -notin $packageFiles })

  Write-TestResult `
    -TestName 'package.json files allowlist includes all required release package entries' `
    -Passed ($missingPackageFiles.Count -eq 0) `
    -Message "Missing package.json files entries: $($missingPackageFiles -join ', ')"

  $validatorPath = Join-Path $repoRoot 'scripts/validate-npm-package.ps1'
  $validatorContent = Get-Content -Path $validatorPath -Raw
  $validatorRequiredEntries = Get-PowerShellSingleQuotedArrayEntries `
    -Content $validatorContent `
    -VariableName 'requiredPackageEntries'
  $validatorAllowedTopLevelEntries = Get-PowerShellSingleQuotedArrayEntries `
    -Content $validatorContent `
    -VariableName 'allowedTopLevelEntries'
  $validatorUnityFolders = Get-PowerShellSingleQuotedArrayEntries `
    -Content $validatorContent `
    -VariableName 'unityFolders'
  $validatorPackageContentRoots = Get-PowerShellSingleQuotedArrayEntries `
    -Content $validatorContent `
    -VariableName 'packageContentRoots'
  $missingValidatorRequiredEntries = @($requiredPackageFilesEntries | Where-Object { $_ -notin $validatorRequiredEntries })
  $requiredValidatorAllowedTopLevelEntries = @('scripts', 'scripts.meta')
  $missingValidatorAllowedTopLevelEntries = @($requiredValidatorAllowedTopLevelEntries | Where-Object { $_ -notin $validatorAllowedTopLevelEntries })
  $nestedValidatorAllowedTopLevelEntries = @($validatorAllowedTopLevelEntries | Where-Object { $_.Contains('/') -or $_.Contains('\') })
  $missingValidatorUnityFolders = @($requiredUnityPackageFolders | Where-Object { $_ -notin $validatorUnityFolders })
  $missingValidatorPackageContentRoots = @($requiredPackageFilesEntries | Where-Object { $_ -notin $validatorPackageContentRoots })
  $validatorUsesCaseSensitiveMembership = (
    $validatorContent.Contains('$entry -cnotin $allowedTopLevelEntries') -and
    $validatorContent.Contains('$entry -cnotin $allowedScriptsEntries') -and
    $validatorContent.Contains('$gitFile -cnotin $npmPackageFiles') -and
    $validatorContent.Contains('$npmFile -cnotin $gitPackageFiles')
  )
  $validatorPreservesCaseOnlyPathVariants = -not $validatorContent.Contains('Sort-Object -Unique')
  $validatorComparesWholePackagePayload = (
    $validatorContent.Contains('Get-TrackedPackageFiles -RepoRoot $repoRoot -PackageRoots $packageContentRoots') -and
    $validatorContent.Contains('Get-PackedPackageFiles -PackageDir $packageDir') -and
    $validatorContent.Contains('Get-ChildItem -LiteralPath $PackageDir -Recurse -File -Force') -and
    $validatorContent.Contains('git -C $RepoRoot ls-files -z -- @trackedRoots') -and
    -not $validatorContent.Contains('Test-Path -LiteralPath (Join-Path $RepoRoot $_)') -and
    $missingValidatorPackageContentRoots.Count -eq 0
  )
  $validatorChecksHiddenScriptEntries = $validatorContent.Contains('Get-ChildItem -LiteralPath $scriptsDir -Recurse -File -Force')
  $validatorChecksHiddenPackedCsFiles = $validatorContent.Contains("Get-ChildItem -LiteralPath `$packageDir -Recurse -File -Filter '*.cs' -Force")
  $validatorChecksHiddenUnityFolderEntries = $validatorContent.Contains('Get-ChildItem -LiteralPath $folderPath -Recurse -Force')
  $validatorUsesStructuredRelativePaths = (
    $validatorContent.Contains('function ConvertTo-PackageRelativePath') -and
    $validatorContent.Contains('[System.IO.Path]::GetRelativePath($rootPath, $childPath)') -and
    -not $validatorContent.Contains('.FullName.Replace(')
  )
  $productionPowerShellScriptsWithStringPathExtraction = @(
    Get-ChildItem -LiteralPath (Join-Path $repoRoot 'scripts') -Recurse -File -Filter '*.ps1' |
      Where-Object { $_.FullName -notmatch '[\\/](scripts[\\/])?tests[\\/]' } |
      Where-Object { (Get-Content -LiteralPath $_.FullName -Raw).Contains('.FullName.Replace(') } |
      ForEach-Object { [System.IO.Path]::GetRelativePath($repoRoot, $_.FullName).Replace('\', '/') }
  )

  Write-TestResult `
    -TestName 'npm package validator requires all release package entries' `
    -Passed ($missingValidatorRequiredEntries.Count -eq 0) `
    -Message "Missing validator required entries: $($missingValidatorRequiredEntries -join ', ')"

  Write-TestResult `
    -TestName 'npm package validator allows shipped scripts folder metadata' `
    -Passed ($missingValidatorAllowedTopLevelEntries.Count -eq 0) `
    -Message "Missing validator allowed top-level entries: $($missingValidatorAllowedTopLevelEntries -join ', ')"

  Write-TestResult `
    -TestName 'npm package validator top-level allowlist contains only top-level entries' `
    -Passed ($nestedValidatorAllowedTopLevelEntries.Count -eq 0) `
    -Message "Nested entries in top-level allowlist: $($nestedValidatorAllowedTopLevelEntries -join ', ')"

  Write-TestResult `
    -TestName 'npm package validator compares all shipped Unity roots against git' `
    -Passed ($missingValidatorUnityFolders.Count -eq 0) `
    -Message "Missing validator Unity folders: $($missingValidatorUnityFolders -join ', ')"

  Write-TestResult `
    -TestName 'npm package validator compares whole release payload against git' `
    -Passed $validatorComparesWholePackagePayload `
    -Message "Missing validator package content roots: $($missingValidatorPackageContentRoots -join ', ')"

  Write-TestResult `
    -TestName 'npm package validator checks hidden Unity folder entries for metadata' `
    -Passed $validatorChecksHiddenUnityFolderEntries `
    -Message 'Expected Unity folder metadata validation to enumerate with -LiteralPath and -Force, matching packed payload parity.'

  Write-TestResult `
    -TestName 'npm package validator checks hidden scripts entries against allowlist' `
    -Passed $validatorChecksHiddenScriptEntries `
    -Message 'Expected scripts folder allowlist validation to enumerate with -LiteralPath and -Force.'

  Write-TestResult `
    -TestName 'npm package validator checks hidden C# files for root restrictions' `
    -Passed $validatorChecksHiddenPackedCsFiles `
    -Message 'Expected packed C# root validation to enumerate with -LiteralPath and -Force.'

  Write-TestResult `
    -TestName 'npm package validator uses structured relative path extraction' `
    -Passed $validatorUsesStructuredRelativePaths `
    -Message 'Expected validate-npm-package.ps1 to use GetRelativePath instead of string replacement on FullName.'

  Write-TestResult `
    -TestName 'production PowerShell scripts avoid string-based FullName relative paths' `
    -Passed ($productionPowerShellScriptsWithStringPathExtraction.Count -eq 0) `
    -Message "Scripts still using string-based FullName relative path extraction: $($productionPowerShellScriptsWithStringPathExtraction -join ', ')"

  Write-TestResult `
    -TestName 'npm package validator uses case-sensitive package membership checks' `
    -Passed $validatorUsesCaseSensitiveMembership `
    -Message 'Expected validate-npm-package.ps1 to reject differently-cased package paths with -cnotin.'

  Write-TestResult `
    -TestName 'npm package validator preserves case-only path variants before membership checks' `
    -Passed $validatorPreservesCaseOnlyPathVariants `
    -Message 'Expected validate-npm-package.ps1 not to collapse case-only path variants with Sort-Object -Unique.'

  $exporterPath = Join-Path $repoRoot 'scripts/unity/export-unitypackage.sh'
  $exporterContent = Get-Content -Path $exporterPath -Raw
  $requiredLoop = [regex]::Match(
    $exporterContent,
    '(?ms)for entry in \\\s*(?<entries>.*?)\s*do\s*\r?\n\s*copy_package_entry "\$\{entry\}" required'
  )
  $requiredExportEntries = @()
  if ($requiredLoop.Success) {
    $requiredExportEntries = @(
      $requiredLoop.Groups['entries'].Value -split "`r?`n" |
        ForEach-Object { $_.Trim().TrimEnd('\').Trim() } |
        Where-Object { $_ }
    )
  }
  $missingExportEntries = @($requiredUnityPackageEntries | Where-Object { $_ -notin $requiredExportEntries })

  Write-TestResult `
    -TestName 'Unity package exporter stages all shipped Unity roots from npm pack' `
    -Passed ($requiredLoop.Success -and $missingExportEntries.Count -eq 0) `
    -Message "Missing exporter required entries: $($missingExportEntries -join ', ')"
}

function Print-SummaryAndExit {
  Write-Host ""
  Write-Host "Results:" -ForegroundColor Magenta
  Write-Host "  Passed: $script:TestsPassed"
  Write-Host "  Failed: $script:TestsFailed"

  if ($script:TestsFailed -gt 0) {
    Write-Host ""
    Write-Host "Failed tests:" -ForegroundColor Red
    foreach ($failedTest in $script:FailedTests) {
      Write-Host "  - $failedTest" -ForegroundColor Yellow
    }
    exit 1
  }

  exit 0
}

Run-SyncScriptContractTests
Run-CspellContractTests
Run-AgentValidationContractTests
Run-PowerShellPathBindingContractTests
Run-HookInstallContractTests
Run-RepoLocalPrettierContractTests
Run-PrePushLastResortGuidanceContractTests
Run-ReleaseDrafterChangelogVersionContractTests
Run-ReleaseWorkflowChangelogContractTests
Run-ReleaseWorkflowGitHubCliContractTests
Run-ReleasePublishWorkflowBudgetContractTests
Run-ReleasePrepareWorkflowContractTests
Run-ReleaseTagWorkflowContractTests
Run-ReleasePackageContentContractTests
Print-SummaryAndExit
