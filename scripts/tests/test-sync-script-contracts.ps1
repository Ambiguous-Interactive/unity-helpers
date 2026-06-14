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
  Write-Host "Agent/pre-push spelling contract checks:" -ForegroundColor Magenta
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

    Write-TestResult `
      -TestName "$($scriptInfo.Name) captures trailing -Paths arguments under pwsh -File" `
      -Passed ($hasRemainingArgs -and $usesEffectivePaths) `
      -Message 'Expected ValueFromRemainingArguments AdditionalPaths and effective path merging.'
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

  $prettierRequiredFiles = @($formatStagedPath, $agentPreflightPath, $preCommitPath, $prePushPath)
  $requiredFiles = @($formatStagedPath, $lintStagedMarkdownPath, $agentPreflightPath, $validateLintErrorCodesPath, $preCommitPath, $prePushPath)
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
    -TestName 'hooks and preflight route Prettier through repo-local launcher' `
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
    -TestName 'hooks and preflight route cspell/markdownlint through repo-local launcher' `
    -Passed ($nodeToolDrift.Count -eq 0) `
    -Message "Missing node-tool launcher reference: $($nodeToolDrift -join '; ')"

  $markdownFenceFixDrift = @()
  foreach ($file in @($lintStagedMarkdownPath, $agentPreflightPath, $preCommitPath)) {
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
    -TestName 'Markdown hook/preflight paths run fence-language auto-fix before markdownlint' `
    -Passed ($markdownFenceFixDrift.Count -eq 0) `
    -Message "Missing fence fixer reference: $($markdownFenceFixDrift -join '; ')"

  if (Test-Path $preCommitPath) {
    $preCommitContent = Get-Content -Path $preCommitPath -Raw
    Write-TestResult `
      -TestName 'pre-commit runs fence-language fixer from markdownlint MD040 diagnostics' `
      -Passed ($preCommitContent -match 'MD040/fenced-code-language' -and $preCommitContent -match 'MARKDOWNLINT_FIX_CAPTURE') `
      -Message 'Expected pre-commit to invoke the fence fixer only after markdownlint reports MD040.'

    Write-TestResult `
      -TestName 'pre-commit avoids line-only bare-fence pre-scan' `
      -Passed ($preCommitContent -notmatch 'MARKDOWN_FENCE_FIX_REQUIRED') `
      -Message 'A line-only bare-fence pre-scan mistakes normal closing fences for missing-language openings.'
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

  $workflowLines = @($workflowContent -split "`r?`n")
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
Run-ReleaseDrafterChangelogVersionContractTests
Print-SummaryAndExit
