Param(
  [switch]$VerboseOutput
)

<#
.SYNOPSIS
    Tests for generate-skills-index.ps1 and lint-llm-instructions.ps1.

.DESCRIPTION
    Validates the file-based, cross-OS-deterministic skills index:
    - The generator is deterministic (two runs => identical bytes).
    - .llm/skills/index.md matches the generator and is UTF-8 no-BOM / LF.
    - Generated output has the expected shape (single H1, category sections,
      ./<name>.md links) and is ordinally sorted within each section.
    - .llm/context.md links to ./skills/index.md, carries no stale embedded-index
      markers, and has exactly one H1.
    - The linter PASSES on the clean repo and FAILS (red) on a non-ASCII trigger
      and on index drift (each mutation is restored in a finally block).
    - Get-MarkdownH1Lines remains code-block-aware.

.EXAMPLE
    ./scripts/tests/test-llm-instructions-lint.ps1 -VerboseOutput
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:TestsPassed = 0
$script:TestsFailed = 0
$script:FailedTests = @()

function Write-Info($msg) {
  if ($VerboseOutput) { Write-Host "[test-llm-instructions-lint] $msg" -ForegroundColor Cyan }
}

function Write-TestResult {
  param([string]$TestName, [bool]$Passed, [string]$Message = "")
  if ($Passed) {
    Write-Host "  [PASS] $TestName" -ForegroundColor Green
    $script:TestsPassed++
  }
  else {
    Write-Host "  [FAIL] $TestName" -ForegroundColor Red
    if ($Message) { Write-Host "         $Message" -ForegroundColor Yellow }
    $script:TestsFailed++
    $script:FailedTests += $TestName
  }
}

$markdownHelpersPath = Join-Path -Path $PSScriptRoot -ChildPath '..' -AdditionalChildPath 'markdown-helpers.ps1'
. $markdownHelpersPath

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$generateScript = Join-Path $repoRoot 'scripts' 'generate-skills-index.ps1'
$lintScript = Join-Path $repoRoot 'scripts' 'lint-llm-instructions.ps1'
$contextFile = Join-Path $repoRoot '.llm' 'context.md'
$skillsDir = Join-Path $repoRoot '.llm' 'skills'
$indexFile = Join-Path $skillsDir 'index.md'

Write-Host "Testing generate-skills-index.ps1 and lint-llm-instructions.ps1..." -ForegroundColor White

function Get-NormalizedText {
  param([string]$Path)
  return ([System.IO.File]::ReadAllText($Path) -replace "`r`n", "`n")
}

# Generate to a temp file once and reuse the "expected" content/bytes.
$expectedTemp = [System.IO.Path]::GetTempFileName()
$determinismTemp = [System.IO.Path]::GetTempFileName()
try {
  & pwsh -NoProfile -File $generateScript -OutputPath $expectedTemp | Out-Null
  $genExit1 = $LASTEXITCODE
  & pwsh -NoProfile -File $generateScript -OutputPath $determinismTemp | Out-Null
  $genExit2 = $LASTEXITCODE

  # ===========================================================================
  Write-Host "`n  Section: Generator determinism + encoding" -ForegroundColor White

  Write-TestResult "Generator.ExitsZero" (($genExit1 -eq 0) -and ($genExit2 -eq 0)) `
    "Generator exit codes: $genExit1 / $genExit2"

  $bytesA = [System.IO.File]::ReadAllBytes($expectedTemp)
  $bytesB = [System.IO.File]::ReadAllBytes($determinismTemp)
  $identical = ($bytesA.Length -eq $bytesB.Length)
  if ($identical) {
    for ($i = 0; $i -lt $bytesA.Length; $i++) { if ($bytesA[$i] -ne $bytesB[$i]) { $identical = $false; break } }
  }
  Write-TestResult "Generator.Deterministic" $identical `
    "Two generator runs produced different bytes ($($bytesA.Length) vs $($bytesB.Length))."

  # ===========================================================================
  Write-Host "`n  Section: index.md presence, match, encoding" -ForegroundColor White

  $indexExists = Test-Path -LiteralPath $indexFile
  Write-TestResult "Index.Exists" $indexExists "Expected $indexFile to exist"

  if ($indexExists) {
    $expectedContent = Get-NormalizedText -Path $expectedTemp
    $currentContent = Get-NormalizedText -Path $indexFile
    Write-TestResult "Index.MatchesGenerator" `
      ([string]::Equals($expectedContent, $currentContent, [System.StringComparison]::Ordinal)) `
      "index.md does not match generator output; run generate-skills-index.ps1"

    $indexBytes = [System.IO.File]::ReadAllBytes($indexFile)
    $hasBom = $indexBytes.Length -ge 3 -and $indexBytes[0] -eq 0xEF -and $indexBytes[1] -eq 0xBB -and $indexBytes[2] -eq 0xBF
    Write-TestResult "Index.NoBom" (-not $hasBom) "index.md must not start with a UTF-8 BOM"
    Write-TestResult "Index.LfOnly" (-not ($indexBytes -contains 0x0D)) "index.md must use LF line endings (no CR)"
  }

  # ===========================================================================
  Write-Host "`n  Section: Generated output shape" -ForegroundColor White

  $genLines = (Get-NormalizedText -Path $expectedTemp) -split "`n"

  $h1 = @($genLines | Where-Object { $_ -match '^# ' })
  Write-TestResult "Output.ExactlyOneH1" ($h1.Count -eq 1) "Expected 1 H1, found $($h1.Count): $($h1 -join ' | ')"

  $invalid = @()
  foreach ($line in $genLines) {
    $t = $line.TrimEnd()
    if ($t -eq '') { continue }
    if ($t -match '^<!--.*-->$') { continue }
    if ($t -match '^#{1,2} ') { continue }
    if ($t -match '^\|') { continue }
    if ($t -eq 'Invoke these skills for specific tasks.') { continue }  # the single intro paragraph
    $invalid += $t
  }
  Write-TestResult "Output.AllLinesValid" ($invalid.Count -eq 0) "Invalid line(s): $($invalid -join ' | ')"

  # All link rows must be [name](./name.md) with link text == filename.
  $linkRows = @($genLines | Where-Object { $_ -match '^\| \[' })
  $badLinks = @($linkRows | Where-Object { $_ -notmatch '^\| \[([a-z0-9][a-z0-9-]*)\]\(\./\1\.md\) \| .+ \|$' })
  Write-TestResult "Output.LinksAreLocalAndConsistent" ($badLinks.Count -eq 0) `
    "Rows with malformed links: $($badLinks -join ' | ')"

  Write-TestResult "Output.HasCoreSection" (@($genLines | Where-Object { $_ -eq '## Core Skills (Always Consider)' }).Count -eq 1) "Missing Core section"
  Write-TestResult "Output.NoStaleMarkers" (@($genLines | Where-Object { $_ -match 'GENERATED SKILLS INDEX' }).Count -eq 0) "Output still emits old BEGIN/END markers"

  # Ordinal sort within EVERY section: collect each section's filenames, then emit a
  # single affirmative pass/fail covering all sections (no fail-only-never-pass path).
  $sectionNames = @()
  $names = New-Object System.Collections.Generic.List[string]
  foreach ($line in $genLines) {
    if ($line -match '^## ') {
      if ($names.Count -gt 0) { $sectionNames += , ($names.ToArray()) }
      $names = New-Object System.Collections.Generic.List[string]
    }
    elseif ($line -match '^\| \[([a-z0-9][a-z0-9-]*)\]') {
      $names.Add($Matches[1])
    }
  }
  if ($names.Count -gt 0) { $sectionNames += , ($names.ToArray()) }

  $allOrdered = $true
  $offender = ''
  foreach ($arr in $sectionNames) {
    $sorted = [string[]]$arr.Clone()
    [Array]::Sort($sorted, [System.StringComparer]::Ordinal)
    for ($i = 0; $i -lt $arr.Count; $i++) {
      if ($arr[$i] -ne $sorted[$i]) { $allOrdered = $false; $offender = ($arr -join ', '); break }
    }
    if (-not $allOrdered) { break }
  }
  Write-TestResult "Output.OrdinalSortAllSections" (($sectionNames.Count -ge 3) -and $allOrdered) `
    "Expected >=3 ordinally-sorted sections; found $($sectionNames.Count), ordered=$allOrdered, offender=[$offender]"

  # ===========================================================================
  Write-Host "`n  Section: context.md" -ForegroundColor White

  $contextRaw = Get-Content -LiteralPath $contextFile -Raw
  Write-TestResult "Context.LinksToIndex" ($contextRaw -match '\]\(\./skills/index\.md\)') "context.md must link to ./skills/index.md"
  Write-TestResult "Context.NoBeginMarker" (-not $contextRaw.Contains('<!-- BEGIN GENERATED SKILLS INDEX -->')) "context.md still has a stale BEGIN marker"
  Write-TestResult "Context.NoEndMarker" (-not $contextRaw.Contains('<!-- END GENERATED SKILLS INDEX -->')) "context.md still has a stale END marker"

  $contextH1 = @(Get-MarkdownH1Lines -Lines @(Get-Content -LiteralPath $contextFile))
  Write-TestResult "Context.ExactlyOneH1" ($contextH1.Count -eq 1) "Expected 1 H1, found $($contextH1.Count)"

  # ===========================================================================
  Write-Host "`n  Section: Linter green path" -ForegroundColor White

  & pwsh -NoProfile -File $lintScript | Out-Null
  Write-TestResult "Lint.PassesOnCleanRepo" ($LASTEXITCODE -eq 0) "Expected exit 0 from lint-llm-instructions.ps1"

  # ===========================================================================
  Write-Host "`n  Section: Linter red paths (mutate + restore)" -ForegroundColor White

  # Red 1: a non-ASCII character in a trigger comment must fail the lint.
  $victim = Join-Path $skillsDir 'serialization-safety.md'
  if (Test-Path -LiteralPath $victim) {
    $backup = [System.IO.File]::ReadAllBytes($victim)
    try {
      $text = [System.IO.File]::ReadAllText($victim)
      $emDash = [char]0x2014
      $mutated = $text -replace 'exception contract - every', "exception contract $emDash every"
      [System.IO.File]::WriteAllText($victim, $mutated, (New-Object System.Text.UTF8Encoding($false)))
      & pwsh -NoProfile -File $lintScript | Out-Null
      Write-TestResult "Lint.FailsOnNonAsciiTrigger" ($LASTEXITCODE -ne 0) "Lint should fail when a trigger has a non-ASCII character"
    }
    finally {
      [System.IO.File]::WriteAllBytes($victim, $backup)
    }
  }
  else {
    Write-TestResult "Lint.FailsOnNonAsciiTrigger" $false "Expected fixture serialization-safety.md to exist"
  }

  # Red 2: drift in index.md must fail the lint.
  if ($indexExists) {
    $idxBackup = [System.IO.File]::ReadAllBytes($indexFile)
    try {
      Add-Content -LiteralPath $indexFile -Value '| [bogus](./bogus.md) | injected drift |'
      & pwsh -NoProfile -File $lintScript | Out-Null
      Write-TestResult "Lint.FailsOnIndexDrift" ($LASTEXITCODE -ne 0) "Lint should fail when index.md drifts from generator output"
    }
    finally {
      [System.IO.File]::WriteAllBytes($indexFile, $idxBackup)
    }
  }

  # Red 3: an unknown / typo'd category must fail the lint.
  if (Test-Path -LiteralPath $victim) {
    $backup3 = [System.IO.File]::ReadAllBytes($victim)
    try {
      $text3 = [System.IO.File]::ReadAllText($victim)
      $mutated3 = $text3 -replace '\| Core -->', '| Core Skills -->'
      [System.IO.File]::WriteAllText($victim, $mutated3, (New-Object System.Text.UTF8Encoding($false)))
      & pwsh -NoProfile -File $lintScript | Out-Null
      Write-TestResult "Lint.FailsOnUnknownCategory" ($LASTEXITCODE -ne 0) "Lint should fail on an unknown/typo'd category"
    }
    finally {
      [System.IO.File]::WriteAllBytes($victim, $backup3)
    }
  }

  # Confirm clean again after restores.
  & pwsh -NoProfile -File $lintScript | Out-Null
  Write-TestResult "Lint.GreenAfterRestore" ($LASTEXITCODE -eq 0) "Lint should pass again after restoring mutated files"
}
finally {
  Remove-Item -LiteralPath $expectedTemp, $determinismTemp -Force -ErrorAction SilentlyContinue
}

# =============================================================================
# Get-MarkdownH1Lines remains code-block-aware (helper used by the linter).
# =============================================================================
Write-Host "`n  Section: Code-block-aware H1 detection" -ForegroundColor White

$codeBlockCases = @(
  @{ Name = 'BashCommentsInCodeBlock'; ExpectedCount = 1; Lines = @('# Real H1', '', '```bash', '# bash comment', '```', '', 'x') }
  @{ Name = 'TildeCodeBlock'; ExpectedCount = 1; Lines = @('# Real H1', '', '~~~sh', '# not a heading', '~~~') }
  @{ Name = 'NoCodeBlocksTwoH1'; ExpectedCount = 2; Lines = @('# First', '', '# Second') }
  @{ Name = 'H1AfterCodeBlock'; ExpectedCount = 1; Lines = @('```', '# fenced', '```', '# Real H1') }
  @{ Name = 'UnclosedFenceSwallowsRemainder'; ExpectedCount = 1; Lines = @('# Real H1', '```bash', '# swallowed') }
  @{ Name = 'NestedFourBacktickFence'; ExpectedCount = 1; Lines = @('# Real H1', '````', '```', '# inside', '```', '````') }
)
foreach ($case in $codeBlockCases) {
  $h1Results = @(Get-MarkdownH1Lines -Lines $case.Lines)
  Write-TestResult "CodeBlockAware.$($case.Name)" ($h1Results.Count -eq $case.ExpectedCount) `
    "Expected $($case.ExpectedCount) H1, found $($h1Results.Count)"
}

# ── Summary ──────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host ("=" * 60)
Write-Host ("Tests passed: {0}" -f $script:TestsPassed) -ForegroundColor Green
Write-Host ("Tests failed: {0}" -f $script:TestsFailed) -ForegroundColor $(if ($script:TestsFailed -gt 0) { "Red" } else { "Green" })
if ($script:FailedTests.Count -gt 0) {
  Write-Host "Failed tests:" -ForegroundColor Red
  foreach ($t in $script:FailedTests) { Write-Host "  - $t" -ForegroundColor Red }
}
Write-Host ("=" * 60)

exit $script:TestsFailed
