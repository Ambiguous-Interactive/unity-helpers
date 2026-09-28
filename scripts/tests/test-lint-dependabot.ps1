Param(
  [switch]$VerboseOutput
)

<#
.SYNOPSIS
    Test runner for lint-dependabot.ps1

.DESCRIPTION
    Tests that lint-dependabot.ps1 correctly:
    - Passes a fully valid Dependabot v2 config
    - Passes a config with multiple named groups
    - Passes when validating multiple valid files at once
    - Fails when one of multiple provided files is invalid
    - Detects DEP001 (missing version: 2)
    - Detects DEP001 when version: 2 appears after updates: instead of before
    - Passes when a non-version top-level key (e.g. registries:) precedes version: 2 (no false DEP001)
    - Does NOT pass when version: 2 is nested inside another key (Fail_NestedVersionDoesNotSatisfyDEP001)
    - Passes valid YAML scalar forms: trailing comment, double-quoted, single-quoted version: 2
    - Reports the group item's declaration line (not the parser's current line) in DEP006 messages
    - Detects DEP002 (multi-ecosystem-groups: top-level key)
    - Detects DEP003 (multi-ecosystem-group: inside an entry)
    - Detects DEP004 (patterns: at entry level instead of inside groups)
    - Detects DEP005 (missing schedule: from an entry)
    - Passes a config where groups: block contains inline comments (no false DEP006)
    - Detects DEP006 (groups entry missing patterns:)
    - Detects DEP007 (updates: section absent entirely)
    - Fails on the exact broken config that was previously shipped

.PARAMETER VerboseOutput
    Show detailed output during test execution

.EXAMPLE
    ./scripts/tests/test-lint-dependabot.ps1
    ./scripts/tests/test-lint-dependabot.ps1 -VerboseOutput
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:TestsPassed = 0
$script:TestsFailed = 0
$script:FailedTests = @()

function Write-Info($msg) {
    if ($VerboseOutput) { Write-Host "[test-lint-dependabot] $msg" -ForegroundColor Cyan }
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

$lintScriptPath = (Resolve-Path (Join-Path $PSScriptRoot '..' 'lint-dependabot.ps1')).Path

# Use a temp directory for fixture files
$tempBase = if ($env:TEMP) { $env:TEMP } elseif ($env:TMPDIR) { $env:TMPDIR } else { '/tmp' }
$tempDir = Join-Path $tempBase "test-lint-dependabot-$(Get-Random)"
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

# Content fixtures run the full script in fresh runspaces; CLI fixtures below retain
# pwsh -File argument binding, which the in-process call operator cannot reproduce.
function Invoke-IsolatedScript {
    param([string]$ScriptPath, [string]$FixturePath)
    $runner = [System.Management.Automation.PowerShell]::Create()
    try {
        $null = $runner.AddCommand($ScriptPath)
        if ($FixturePath) { $null = $runner.AddParameter('Paths', $FixturePath) }
        $result = $runner.Invoke()
        $output = [System.Collections.Generic.List[string]]::new()
        foreach ($item in $result) { $output.Add($item.ToString()) }
        foreach ($item in $runner.Streams.Information) { $output.Add($item.MessageData.ToString()) }
        foreach ($item in $runner.Streams.Error) { $output.Add($item.ToString()) }
        if ($runner.Streams.Error.Count -gt 0) {
            throw "Linter emitted a PowerShell error: $($output -join [Environment]::NewLine)"
        }
        $exitCode = $runner.Runspace.SessionStateProxy.GetVariable('LASTEXITCODE')
        if ($null -eq $exitCode) {
            throw "Linter returned without an exit code. $($output -join [Environment]::NewLine)"
        }
        return @{ ExitCode = [int]$exitCode; Output = ($output -join [Environment]::NewLine) }
    } finally {
        $runner.Dispose()
    }
}

function Invoke-LintOnContent {
    param([string]$YamlContent, [switch]$UseCli)
    $fixturePath = Join-Path $tempDir "dependabot-$(Get-Random).yml"
    Set-Content -Path $fixturePath -Value $YamlContent -NoNewline
    Write-Info "Testing fixture: $fixturePath"
    if ($UseCli) {
        $output = & pwsh -NoProfile -File $lintScriptPath -Paths $fixturePath *>&1
        return @{ ExitCode = $LASTEXITCODE; Output = ($output | Out-String) }
    }
    return Invoke-IsolatedScript -ScriptPath $lintScriptPath -FixturePath $fixturePath
}

# Helper: run the lint script against two fixture strings, via pwsh -File
function Invoke-LintOnTwoContents {
    param([string]$YamlContent1, [string]$YamlContent2)
    $fixturePath1 = Join-Path $tempDir "dependabot-$(Get-Random).yml"
    $fixturePath2 = Join-Path $tempDir "dependabot-$(Get-Random).yml"
    Set-Content -Path $fixturePath1 -Value $YamlContent1 -NoNewline
    Set-Content -Path $fixturePath2 -Value $YamlContent2 -NoNewline
    Write-Info "Testing fixtures: $fixturePath1 and $fixturePath2"
    try {
        $output = & pwsh -NoProfile -File $lintScriptPath -Paths $fixturePath1 $fixturePath2 *>&1
        $exitCode = $LASTEXITCODE
        return @{ ExitCode = $exitCode; Output = ($output | Out-String) }
    } catch {
        return @{ ExitCode = -1; Output = "Exception invoking linter: $_" }
    }
}

Write-Host "Testing lint-dependabot.ps1..." -ForegroundColor White

try {

$harnessPath = Join-Path $tempDir 'harness.ps1'
foreach ($expectedExit in @(0, 1, 7)) {
    Set-Content -LiteralPath $harnessPath -Value "Write-Host 'harness-output'; exit $expectedExit"
    $result = Invoke-IsolatedScript -ScriptPath $harnessPath
    Write-TestResult "Harness_Exit$expectedExit" ($result.ExitCode -eq $expectedExit -and $result.Output -match 'harness-output') "Expected exact exit $expectedExit with captured host output."
}

foreach ($control in @(
    @{ Name = 'MissingExit'; Content = "Write-Host 'no-exit'"; ExpectedError = 'without an exit code' },
    @{ Name = 'TerminatingError'; Content = "throw 'harness-terminating-error'"; ExpectedError = 'harness-terminating-error' },
    @{ Name = 'NonTerminatingError'; Content = "Write-Error 'harness-stream-error' -ErrorAction Continue; exit 0"; ExpectedError = 'harness-stream-error' }
)) {
    Set-Content -LiteralPath $harnessPath -Value $control.Content
    $rejected = $false
    try {
        $null = Invoke-IsolatedScript -ScriptPath $harnessPath
    } catch {
        $rejected = $_.Exception.Message -match $control.ExpectedError
    }
    Write-TestResult "Harness_$($control.Name)Fails" $rejected 'Harness failures must not satisfy linter expectations.'
}

Set-Content -LiteralPath $harnessPath -Value '$global:dependabotHarnessState = 1; exit 1'
$null = Invoke-IsolatedScript -ScriptPath $harnessPath
Set-Content -LiteralPath $harnessPath -Value "if (Get-Variable dependabotHarnessState -ErrorAction SilentlyContinue) { exit 1 }; exit 0"
$result = Invoke-IsolatedScript -ScriptPath $harnessPath
Write-TestResult 'Harness_RunspacesAreIsolated' ($result.ExitCode -eq 0) 'Each fixture must start with fresh session state.'

# ── Pass_ValidConfig ─────────────────────────────────────────────────────────
Write-Host "`n  Section: Valid configurations" -ForegroundColor White

$validConfig = @'
version: 2
updates:
  - package-ecosystem: "github-actions"
    directory: "/"
    schedule:
      interval: "weekly"
    groups:
      all-dependencies:
        patterns:
          - "*"
    assignees:
      - wallstop
    reviewers:
      - wallstop
'@

$result = Invoke-LintOnContent $validConfig -UseCli
Write-TestResult "Pass_ValidConfig" ($result.ExitCode -eq 0) "Expected exit 0, got $($result.ExitCode). Output: $($result.Output)"

# ── Pass_GroupsWithPatterns ───────────────────────────────────────────────────
$groupsConfig = @'
version: 2
updates:
  - package-ecosystem: "nuget"
    directory: "/"
    schedule:
      interval: "daily"
    groups:
      production-dependencies:
        patterns:
          - "Microsoft.*"
      development-dependencies:
        patterns:
          - "xunit*"
    assignees:
      - wallstop
'@

$result = Invoke-LintOnContent $groupsConfig
Write-TestResult "Pass_GroupsWithPatterns" ($result.ExitCode -eq 0) "Expected exit 0, got $($result.ExitCode). Output: $($result.Output)"

# ── Pass_MultipleValidFiles ───────────────────────────────────────────────────
$validConfig2 = @'
version: 2
updates:
  - package-ecosystem: "npm"
    directory: "/"
    schedule:
      interval: "weekly"
    groups:
      all-dependencies:
        patterns:
          - "*"
'@

$result = Invoke-LintOnTwoContents $validConfig $validConfig2
Write-TestResult "Pass_MultipleValidFiles" ($result.ExitCode -eq 0) "Expected exit 0 for two valid files, got $($result.ExitCode). Output: $($result.Output)"

# ── Fail_MultipleFilesOneInvalid ──────────────────────────────────────────────
$invalidForMulti = @'
version: 2
updates:
  - package-ecosystem: "nuget"
    directory: "/"
    assignees:
      - wallstop
'@

$result = Invoke-LintOnTwoContents $validConfig $invalidForMulti
$hasDEP005multi = $result.Output -match 'DEP005'
Write-TestResult "Fail_MultipleFilesOneInvalid" ($result.ExitCode -eq 1 -and $hasDEP005multi) "Expected exit 1 + DEP005 when one of two files is invalid. Exit: $($result.ExitCode), Output: $($result.Output)"

# ── Fail_MissingVersion ───────────────────────────────────────────────────────
Write-Host "`n  Section: Error detection" -ForegroundColor White

$noVersionConfig = @'
updates:
  - package-ecosystem: "github-actions"
    directory: "/"
    schedule:
      interval: "weekly"
'@

$result = Invoke-LintOnContent $noVersionConfig
$hasDEP001 = $result.Output -match 'DEP001'
Write-TestResult "Fail_MissingVersion" ($result.ExitCode -eq 1 -and $hasDEP001) "Expected exit 1 + DEP001. Exit: $($result.ExitCode), Output: $($result.Output)"

# ── Fail_VersionAfterUpdates ──────────────────────────────────────────────────
# version: 2 must appear BEFORE updates:, not after
$versionAfterUpdatesConfig = @'
updates:
  - package-ecosystem: "github-actions"
    directory: "/"
    schedule:
      interval: "weekly"
version: 2
'@

$result = Invoke-LintOnContent $versionAfterUpdatesConfig
$hasDEP001pos = $result.Output -match 'DEP001'
Write-TestResult "Fail_VersionAfterUpdates" ($result.ExitCode -eq 1 -and $hasDEP001pos) "Expected exit 1 + DEP001 when version: 2 is after updates:. Exit: $($result.ExitCode), Output: $($result.Output)"

# ── Pass_VersionAfterOtherTopLevelKey ─────────────────────────────────────────
# A config with another top-level key (e.g. 'registries:') before 'version: 2'
# must NOT trigger DEP001 — only reaching 'updates:' without 'version: 2' fails.
$versionAfterRegistriesConfig = @'
registries:
  dockerhub:
    type: docker-registry
    url: https://registry.hub.docker.com
version: 2
updates:
  - package-ecosystem: "github-actions"
    directory: "/"
    schedule:
      interval: "weekly"
'@

$result = Invoke-LintOnContent $versionAfterRegistriesConfig
Write-TestResult "Pass_VersionAfterOtherTopLevelKey" ($result.ExitCode -eq 0) "Expected exit 0 when registries: precedes version: 2. Exit: $($result.ExitCode), Output: $($result.Output)"

# ── Pass_VersionWithTrailingComment ───────────────────────────────────────────
# YAML allows trailing inline comments; `version: 2  # comment` is valid and must
# NOT trigger DEP001.  Quoted `version: "2"` is also valid YAML.
$versionTrailingCommentConfig = @'
version: 2  # required by Dependabot v2
updates:
  - package-ecosystem: "github-actions"
    directory: "/"
    schedule:
      interval: "weekly"
'@

$result = Invoke-LintOnContent $versionTrailingCommentConfig
Write-TestResult "Pass_VersionWithTrailingComment" ($result.ExitCode -eq 0) "Expected exit 0 when version: 2 has trailing inline comment. Exit: $($result.ExitCode), Output: $($result.Output)"

# ── Pass_VersionQuoted ────────────────────────────────────────────────────────
$versionQuotedConfig = @'
version: "2"
updates:
  - package-ecosystem: "github-actions"
    directory: "/"
    schedule:
      interval: "weekly"
'@

$result = Invoke-LintOnContent $versionQuotedConfig
Write-TestResult "Pass_VersionQuoted" ($result.ExitCode -eq 0) "Expected exit 0 when version: is quoted as \"2\". Exit: $($result.ExitCode), Output: $($result.Output)"

# ── Pass_VersionSingleQuoted ──────────────────────────────────────────────────
$versionSingleQuotedConfig = @'
version: '2'
updates:
  - package-ecosystem: "github-actions"
    directory: "/"
    schedule:
      interval: "weekly"
'@

$result = Invoke-LintOnContent $versionSingleQuotedConfig
Write-TestResult "Pass_VersionSingleQuoted" ($result.ExitCode -eq 0) "Expected exit 0 when version: is single-quoted as '2'. Exit: $($result.ExitCode), Output: $($result.Output)"

# ── Fail_NestedVersionDoesNotSatisfyDEP001 ────────────────────────────────────
# A 'version: 2' nested inside another top-level section (e.g. registries:)
# must NOT satisfy DEP001.  Only a root-level (zero-indent) 'version: 2' counts.
$nestedVersionConfig = @'
registries:
  my-reg:
    type: npm-registry
    url: https://registry.npmjs.org
    version: 2
updates:
  - package-ecosystem: "github-actions"
    directory: "/"
    schedule:
      interval: "weekly"
'@

$result = Invoke-LintOnContent $nestedVersionConfig
$hasDEP001nested = $result.Output -match 'DEP001'
Write-TestResult "Fail_NestedVersionDoesNotSatisfyDEP001" ($result.ExitCode -eq 1 -and $hasDEP001nested) "Expected exit 1 + DEP001 when version: 2 is nested. Exit: $($result.ExitCode), Output: $($result.Output)"

# ── Fail_MissingUpdatesSection ────────────────────────────────────────────────
# A file with version: 2 but no 'updates:' section must fail with DEP007.
$noUpdatesSectionConfig = @'
version: 2
'@

$result = Invoke-LintOnContent $noUpdatesSectionConfig
$hasDEP007 = $result.Output -match 'DEP007'
Write-TestResult "Fail_MissingUpdatesSection" ($result.ExitCode -eq 1 -and $hasDEP007) "Expected exit 1 + DEP007 when updates: section is absent. Exit: $($result.ExitCode), Output: $($result.Output)"

# ── Pass_DEP006LineNumberAccuracy ─────────────────────────────────────────────
# DEP006 error must reference the group item's declaration line, not the
# parser's current line (which could be the start of the next entry or EOF).
$dep006LineNumConfig = @'
version: 2
updates:
  - package-ecosystem: "github-actions"
    directory: "/"
    schedule:
      interval: "weekly"
    groups:
      bad-group:
        apply-security-updates-only: true
'@

$result = Invoke-LintOnContent $dep006LineNumConfig
$hasDEP006 = $result.Output -match 'DEP006'
# The group item 'bad-group:' is on line 8 of the fixture.
# Ensure the reported line is NOT 0 (uninitialized) and the error text says DEP006.
$lineNumIsNonZero = $result.Output -match 'DEP006: .* \(near line [1-9]\d*\)'
Write-TestResult "Pass_DEP006LineNumberAccuracy" ($result.ExitCode -eq 1 -and $hasDEP006 -and $lineNumIsNonZero) "Expected exit 1 + DEP006 with non-zero line number. Exit: $($result.ExitCode), Output: $($result.Output)"

# ── Fail_MultiEcosystemGroups ─────────────────────────────────────────────────
$multiGroupsConfig = @'
version: 2

multi-ecosystem-groups:
  all-dependencies:
    schedule:
      interval: weekly

updates:
  - package-ecosystem: "github-actions"
    directory: "/"
    schedule:
      interval: "weekly"
'@

$result = Invoke-LintOnContent $multiGroupsConfig
$hasDEP002 = $result.Output -match 'DEP002'
Write-TestResult "Fail_MultiEcosystemGroups" ($result.ExitCode -eq 1 -and $hasDEP002) "Expected exit 1 + DEP002. Exit: $($result.ExitCode), Output: $($result.Output)"

# ── Fail_MultiEcosystemGroupKey ───────────────────────────────────────────────
$multiGroupKeyConfig = @'
version: 2
updates:
  - package-ecosystem: "github-actions"
    directory: "/"
    schedule:
      interval: "weekly"
    multi-ecosystem-group: all-dependencies
'@

$result = Invoke-LintOnContent $multiGroupKeyConfig
$hasDEP003 = $result.Output -match 'DEP003'
Write-TestResult "Fail_MultiEcosystemGroupKey" ($result.ExitCode -eq 1 -and $hasDEP003) "Expected exit 1 + DEP003. Exit: $($result.ExitCode), Output: $($result.Output)"

# ── Fail_PatternsAtWrongLevel ─────────────────────────────────────────────────
$patternsWrongConfig = @'
version: 2
updates:
  - package-ecosystem: "github-actions"
    directory: "/"
    schedule:
      interval: "weekly"
    patterns:
      - "*"
'@

$result = Invoke-LintOnContent $patternsWrongConfig
$hasDEP004 = $result.Output -match 'DEP004'
Write-TestResult "Fail_PatternsAtWrongLevel" ($result.ExitCode -eq 1 -and $hasDEP004) "Expected exit 1 + DEP004. Exit: $($result.ExitCode), Output: $($result.Output)"

# ── Fail_MissingSchedule ──────────────────────────────────────────────────────
$noScheduleConfig = @'
version: 2
updates:
  - package-ecosystem: "github-actions"
    directory: "/"
    assignees:
      - wallstop
'@

$result = Invoke-LintOnContent $noScheduleConfig
$hasDEP005 = $result.Output -match 'DEP005'
Write-TestResult "Fail_MissingSchedule" ($result.ExitCode -eq 1 -and $hasDEP005) "Expected exit 1 + DEP005. Exit: $($result.ExitCode), Output: $($result.Output)"

# ── Fail_GroupsMissingPatterns ────────────────────────────────────────────────
$groupsNoPatternsConfig = @'
version: 2
updates:
  - package-ecosystem: "github-actions"
    directory: "/"
    schedule:
      interval: "weekly"
    groups:
      all-dependencies:
        apply-security-updates-only: true
'@

$result = Invoke-LintOnContent $groupsNoPatternsConfig
$hasDEP006 = $result.Output -match 'DEP006'
Write-TestResult "Fail_GroupsMissingPatterns" ($result.ExitCode -eq 1 -and $hasDEP006) "Expected exit 1 + DEP006. Exit: $($result.ExitCode), Output: $($result.Output)"

# ── Pass_GroupsWithComments ────────────────────────────────────────────────────
# Inline comments and blank lines inside groups: must NOT trigger false DEP006.
$groupsWithCommentsConfig = @'
version: 2
updates:
  - package-ecosystem: "github-actions"
    directory: "/"
    schedule:
      interval: "weekly"
    groups:
      # This comment should not be treated as a group item
      all-dependencies:
        # Another comment
        patterns:
          - "*"
'@

$result = Invoke-LintOnContent $groupsWithCommentsConfig
Write-TestResult "Pass_GroupsWithComments" ($result.ExitCode -eq 0) "Expected exit 0 when groups block contains comments. Exit: $($result.ExitCode), Output: $($result.Output)"

# ── Regression_OriginalBrokenConfig ──────────────────────────────────────────
Write-Host "`n  Section: Regression" -ForegroundColor White

$brokenConfig = @'
version: 2

multi-ecosystem-groups:
  all-dependencies:
    schedule:
      interval: weekly

updates:
  # GitHub Actions, NuGet, and npm updates are grouped into one large PR.
  - package-ecosystem: github-actions
    directory: /
    assignees:
      - wallstop
    reviewers:
      - wallstop
    patterns:
      - "*"
    multi-ecosystem-group: all-dependencies

  - package-ecosystem: nuget
    directory: /
    assignees:
      - wallstop
    reviewers:
      - wallstop
    patterns:
      - "*"
    multi-ecosystem-group: all-dependencies

  - package-ecosystem: npm
    directory: /
    versioning-strategy: increase
    assignees:
      - wallstop
    reviewers:
      - wallstop
    patterns:
      - "*"
    multi-ecosystem-group: all-dependencies
'@

$result = Invoke-LintOnContent $brokenConfig
$hasDEP002r = $result.Output -match 'DEP002'
$hasDEP003r = $result.Output -match 'DEP003'
$hasDEP004r = $result.Output -match 'DEP004'
$hasDEP005r = $result.Output -match 'DEP005'
$allDetected = $hasDEP002r -and $hasDEP003r -and $hasDEP004r -and $hasDEP005r
Write-TestResult "Regression_OriginalBrokenConfig" ($result.ExitCode -eq 1 -and $allDetected) (
    "Expected exit 1 with DEP002/DEP003/DEP004/DEP005. Exit: $($result.ExitCode). " +
    "DEP002=$hasDEP002r DEP003=$hasDEP003r DEP004=$hasDEP004r DEP005=$hasDEP005r. Output: $($result.Output)"
)

} finally {
    Remove-Item -Recurse -Force $tempDir -ErrorAction SilentlyContinue
}

# ── Summary ──────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host ("Tests passed: {0}" -f $script:TestsPassed) -ForegroundColor Green
Write-Host ("Tests failed: {0}" -f $script:TestsFailed) -ForegroundColor $(if ($script:TestsFailed -gt 0) { "Red" } else { "Green" })

if ($script:FailedTests.Count -gt 0) {
    Write-Host "Failed tests:" -ForegroundColor Red
    foreach ($t in $script:FailedTests) {
        Write-Host "  - $t" -ForegroundColor Red
    }
}

exit $script:TestsFailed
