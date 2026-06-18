#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Test runner for lint-workflow-run-expression-length.ps1.

.DESCRIPTION
    Tests that lint-workflow-run-expression-length.ps1 correctly:
    - Exits 0 against a clean synthetic repo.
    - Exits 1 and emits WFL001 for a composite action.yml whose `run:` block is
      over 20000 characters AND contains `${{` (the verify-unity-results bug).
    - Exits 1 and emits WFL001 for the same condition in a workflow .yml file.
    - Exits 1 for the folded `>` block-scalar form and the `- run: |` step form.
    - Does NOT flag a large block (>20000 chars) that contains NO `${{`.
    - Does NOT flag a small block (<20000 chars) that DOES contain `${{`.
    - Does NOT flag the env:-mapping pattern (inputs in env:, body reads $env:*).
    - Does NOT treat a `run:` token inside a quoted YAML string as a run block.
    - Handles a block whose length is exactly at / just over the 20000 boundary.
    - Handles multiple run blocks in one file (one offending, one clean).
    - Handles run blocks indented at various depths.

.PARAMETER VerboseOutput
    Show verbose per-test diagnostics.

.EXAMPLE
    pwsh -NoProfile -File scripts/tests/test-lint-workflow-run-expression-length.ps1
    pwsh -NoProfile -File scripts/tests/test-lint-workflow-run-expression-length.ps1 -VerboseOutput
#>
param(
    [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:TestsPassed = 0
$script:TestsFailed = 0
$script:FailedTests = @()

function Write-Info($msg) {
    if ($VerboseOutput) { Write-Host "[test-lint-workflow-run-expression-length] $msg" -ForegroundColor Cyan }
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

$lintScriptPath = (Resolve-Path (Join-Path $PSScriptRoot '..' 'lint-workflow-run-expression-length.ps1')).Path

$tempBase = if ($env:TEMP) { $env:TEMP } elseif ($env:TMPDIR) { $env:TMPDIR } else { '/tmp' }
$tempRoot = Join-Path $tempBase "test-lint-workflow-run-expression-length-$(Get-Random)"

# Build a synthetic repo layout in a tempdir and invoke the lint scoped to it.
# The lint resolves scan targets relative to $PSScriptRoot/.. (the repo root), so
# we copy the lint script into the tempdir under the same scripts/ layout.
function New-FixtureRoot {
    $root = Join-Path $tempRoot "repo-$(Get-Random)"
    New-Item -ItemType Directory -Path (Join-Path $root 'scripts') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $root '.github/workflows') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $root '.github/actions') -Force | Out-Null
    Copy-Item -LiteralPath $lintScriptPath -Destination (Join-Path $root 'scripts/lint-workflow-run-expression-length.ps1')
    return $root
}

function Invoke-LintInFixture {
    param([string]$FixtureRoot)
    $lintCopy = Join-Path $FixtureRoot 'scripts/lint-workflow-run-expression-length.ps1'
    $output = & pwsh -NoProfile -File $lintCopy -VerboseOutput *>&1
    $exitCode = $LASTEXITCODE
    return @{ ExitCode = $exitCode; Output = ($output | Out-String) }
}

# Produce a run-block body string of approximately $TargetChars characters by
# repeating a padding line. Each padding line is emitted at the supplied indent.
# Returns the multi-line string (NOT including the `run: |` header).
function New-PaddingBody {
    param(
        [int]$TargetChars,
        [string]$Indent = '          ',
        [string]$FirstLine = $null
    )
    $sb = [System.Text.StringBuilder]::new()
    if ($FirstLine) {
        [void]$sb.AppendLine($Indent + $FirstLine)
    }
    $pad = $Indent + '# padding aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
    while ($sb.Length -lt $TargetChars) {
        [void]$sb.AppendLine($pad)
    }
    return $sb.ToString().TrimEnd("`r", "`n")
}

# Write a composite action.yml whose single `run:` block has the given body.
function Write-ActionWithRunBody {
    param(
        [string]$Root,
        [string]$ActionName,
        [string]$Body,
        [string]$ScalarIndicator = '|'
    )
    $dir = Join-Path $Root ".github/actions/$ActionName"
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    $header = @"
name: $ActionName
runs:
  using: composite
  steps:
    - name: Step
      shell: pwsh
      run: $ScalarIndicator
"@
    Set-Content -LiteralPath (Join-Path $dir 'action.yml') -Value ($header + "`n" + $Body)
}

try {
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

    Write-Host "Testing lint-workflow-run-expression-length.ps1..." -ForegroundColor White
    Write-Host "`n  Section: Negative (clean) fixtures" -ForegroundColor White

    # --- Pass_CleanRepo ---
    # A small, well-formed action with a tiny run block and an env: mapping.
    $root = New-FixtureRoot
    New-Item -ItemType Directory -Path (Join-Path $root '.github/actions/ok') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $root '.github/actions/ok/action.yml') -Value @'
name: OK
runs:
  using: composite
  steps:
    - name: Step
      shell: pwsh
      env:
        UH_DIR: ${{ inputs.results-dir }}
      run: |
        $dir = $env:UH_DIR
        Write-Host "ok $dir"
'@
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_CleanRepo" ($result.ExitCode -eq 0) "Expected exit 0 on clean fixture. Exit: $($result.ExitCode). Output: $($result.Output)"

    Write-Host "`n  Section: Positive (should-fail) fixtures" -ForegroundColor White

    # --- Fail_LargeBlockWithExpressionInAction ---
    # The motivating bug: a composite action.yml with a >20000-char run block
    # that ALSO interpolates ${{ inputs.* }}. MUST flag WFL001.
    $root = New-FixtureRoot
    $body = New-PaddingBody -TargetChars 21000 -FirstLine '$dir = "${{ inputs.results-dir }}"'
    Write-ActionWithRunBody -Root $root -ActionName 'oversized' -Body $body
    $result = Invoke-LintInFixture $root
    $hasWfl = ($result.Output -match 'WFL001') -and ($result.Output -match 'oversized')
    Write-TestResult "Fail_LargeBlockWithExpressionInAction" ($result.ExitCode -ne 0 -and $hasWfl) "Expected exit != 0 + WFL001 on >20000-char action run block with `${{ }}. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_LargeBlockWithExpressionInWorkflow ---
    # Same condition but in a .github/workflows/*.yml file (the existing pwsh
    # lint scans workflows; this lint must too, AND measure length).
    $root = New-FixtureRoot
    $wfBody = New-PaddingBody -TargetChars 21000 -Indent '          ' -FirstLine '$x = "${{ github.sha }}"'
    Set-Content -LiteralPath (Join-Path $root '.github/workflows/big.yml') -Value (@'
name: Big
on: [push]
jobs:
  big:
    runs-on: ubuntu-latest
    steps:
      - shell: pwsh
        run: |
'@ + "`n" + $wfBody)
    $result = Invoke-LintInFixture $root
    $hasWfl = ($result.Output -match 'WFL001') -and ($result.Output -match 'big\.yml')
    Write-TestResult "Fail_LargeBlockWithExpressionInWorkflow" ($result.ExitCode -ne 0 -and $hasWfl) "Expected exit != 0 + WFL001 on >20000-char workflow run block with `${{ }}. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_FoldedBlockScalarForm ---
    # `run: >` folded scalar over 20000 chars with ${{ }}. MUST flag WFL001.
    $root = New-FixtureRoot
    $foldedBody = New-PaddingBody -TargetChars 21000 -FirstLine 'echo "${{ inputs.label }}"'
    Write-ActionWithRunBody -Root $root -ActionName 'folded' -Body $foldedBody -ScalarIndicator '>'
    $result = Invoke-LintInFixture $root
    $hasWfl = ($result.Output -match 'WFL001') -and ($result.Output -match 'folded')
    Write-TestResult "Fail_FoldedBlockScalarForm" ($result.ExitCode -ne 0 -and $hasWfl) "Expected exit != 0 + WFL001 on folded `run: >` block. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_DashRunStepFormDeepIndent ---
    # The true `- run: |` step form, where `run:` is the FIRST key of a sequence
    # item (preceded by the `- ` indicator). Body must be measured relative to
    # the `run` key column, not the dash, and the block must still flag.
    $root = New-FixtureRoot
    $dashBody = New-PaddingBody -TargetChars 21000 -Indent '          ' -FirstLine '$v = "${{ inputs.y }}"'
    Set-Content -LiteralPath (Join-Path $root '.github/workflows/dashrun.yml') -Value (@'
name: DashRun
on: [push]
jobs:
  j:
    runs-on: ubuntu-latest
    steps:
      - run: |
'@ + "`n" + $dashBody)
    $result = Invoke-LintInFixture $root
    $hasWfl = ($result.Output -match 'WFL001') -and ($result.Output -match 'dashrun\.yml')
    Write-TestResult "Fail_DashRunStepFormDeepIndent" ($result.ExitCode -ne 0 -and $hasWfl) "Expected exit != 0 + WFL001 on `- run: |` step form. Exit: $($result.ExitCode). Output: $($result.Output)"

    Write-Host "`n  Section: False-positive guards" -ForegroundColor White

    # --- Pass_LargeBlockNoExpression ---
    # A >20000-char run block with NO `${{` must NOT flag (this is exactly the
    # fixed verify-unity-results shape: huge body, inputs read via $env:*).
    $root = New-FixtureRoot
    $bigNoExpr = New-PaddingBody -TargetChars 21000 -FirstLine '$dir = $env:UH_RESULTS_DIR'
    Write-ActionWithRunBody -Root $root -ActionName 'bignoexpr' -Body $bigNoExpr
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_LargeBlockNoExpression" ($result.ExitCode -eq 0) "Expected exit 0 on >20000-char block WITHOUT `${{ }}. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Pass_SmallBlockWithExpression ---
    # A small block (<20000 chars) WITH `${{` must NOT flag — short expressions
    # are fine; only oversized ones break the template compiler.
    $root = New-FixtureRoot
    New-Item -ItemType Directory -Path (Join-Path $root '.github/actions/smallexpr') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $root '.github/actions/smallexpr/action.yml') -Value @'
name: SmallExpr
runs:
  using: composite
  steps:
    - name: Step
      shell: pwsh
      run: |
        Write-Host "label is ${{ inputs.label }}"
        Write-Host "dir is ${{ inputs.results-dir }}"
'@
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_SmallBlockWithExpression" ($result.ExitCode -eq 0) "Expected exit 0 on small block WITH `${{ }}. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Pass_EnvMappingPattern ---
    # The prescribed fix: a large body that reads $env:* with inputs piped in
    # via an env: mapping. No `${{` in the run body -> must NOT flag even though
    # the env: mapping itself uses ${{ }} (that's a separate scalar, not run:).
    $root = New-FixtureRoot
    $envBody = New-PaddingBody -TargetChars 21000 -FirstLine '$dir = $env:UH_RESULTS_DIR; $label = $env:UH_LABEL'
    $dir = Join-Path $root '.github/actions/envmapping'
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $dir 'action.yml') -Value (@'
name: EnvMapping
runs:
  using: composite
  steps:
    - name: Step
      shell: pwsh
      env:
        UH_RESULTS_DIR: ${{ inputs.results-dir }}
        UH_LABEL: ${{ inputs.label }}
      run: |
'@ + "`n" + $envBody)
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_EnvMappingPattern" ($result.ExitCode -eq 0) "Expected exit 0 on the env:-mapping fix pattern. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Pass_RunTokenInQuotedString ---
    # A `run:`-looking token inside a quoted YAML string (e.g. a description)
    # must NOT be treated as a run block. Combined with a benign real run block.
    $root = New-FixtureRoot
    $dir = Join-Path $root '.github/actions/quoted'
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $dir 'action.yml') -Value @'
name: Quoted
description: "This action explains how the run: key behaves when ${{ inputs.x }} is large"
runs:
  using: composite
  steps:
    - name: Step
      shell: bash
      run: echo hello
'@
    $result = Invoke-LintInFixture $root
    Write-TestResult "Pass_RunTokenInQuotedString" ($result.ExitCode -eq 0) "Expected exit 0 — `run:` inside a quoted string is not a run block. Exit: $($result.ExitCode). Output: $($result.Output)"

    Write-Host "`n  Section: Boundary & multi-block" -ForegroundColor White

    # --- Pass_JustUnderBoundary ---
    # A block with `${{` whose body is comfortably UNDER 20000 chars must pass.
    $root = New-FixtureRoot
    $underBody = New-PaddingBody -TargetChars 18000 -FirstLine '$x = "${{ inputs.x }}"'
    Write-ActionWithRunBody -Root $root -ActionName 'under' -Body $underBody
    # Sanity: confirm the body we generated is actually under the threshold so
    # this test is meaningful and not accidentally over.
    $result = Invoke-LintInFixture $root
    $underMeasured = [regex]::Match($result.Output, 'under/action\.yml: run block at line \d+ -> length=(\d+)')
    $underOk = $underMeasured.Success -and ([int]$underMeasured.Groups[1].Value -lt 20000) -and ([int]$underMeasured.Groups[1].Value -gt 15000)
    Write-TestResult "Pass_JustUnderBoundary" ($result.ExitCode -eq 0 -and $underOk) "Expected exit 0 and a measured length 15000-20000. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_JustOverBoundary ---
    # A block with `${{` whose body is just OVER 20000 chars must flag.
    $root = New-FixtureRoot
    $overBody = New-PaddingBody -TargetChars 20200 -FirstLine '$x = "${{ inputs.x }}"'
    Write-ActionWithRunBody -Root $root -ActionName 'over' -Body $overBody
    $result = Invoke-LintInFixture $root
    $overMeasured = [regex]::Match($result.Output, 'over/action\.yml: run block at line \d+ -> length=(\d+)')
    $overOk = $overMeasured.Success -and ([int]$overMeasured.Groups[1].Value -gt 20000)
    $hasWfl = ($result.Output -match 'WFL001') -and ($result.Output -match 'over/action\.yml')
    Write-TestResult "Fail_JustOverBoundary" ($result.ExitCode -ne 0 -and $hasWfl -and $overOk) "Expected exit != 0 + WFL001 and measured length > 20000. Exit: $($result.ExitCode). Output: $($result.Output)"

    # --- Fail_MultipleRunBlocksOneOffending ---
    # A file with two run blocks: one clean small block, one oversized block
    # with `${{`. Only the oversized one should flag, and the lint must fail.
    $root = New-FixtureRoot
    $offending = New-PaddingBody -TargetChars 21000 -Indent '          ' -FirstLine '$x = "${{ inputs.x }}"'
    Set-Content -LiteralPath (Join-Path $root '.github/workflows/multi.yml') -Value (@'
name: Multi
on: [push]
jobs:
  j:
    runs-on: ubuntu-latest
    steps:
      - name: Clean small step
        shell: pwsh
        run: |
          Write-Host "small ${{ github.ref }}"
      - name: Oversized step
        shell: pwsh
        run: |
'@ + "`n" + $offending)
    $result = Invoke-LintInFixture $root
    $hasWfl = ($result.Output -match 'WFL001') -and ($result.Output -match 'multi\.yml')
    # Exactly one violation line expected.
    $violationCount = ([regex]::Matches($result.Output, 'WFL001')).Count
    Write-TestResult "Fail_MultipleRunBlocksOneOffending" ($result.ExitCode -ne 0 -and $hasWfl -and $violationCount -eq 1) "Expected exit != 0 + exactly one WFL001 (the oversized block). Exit: $($result.ExitCode). WFL001 count: $violationCount. Output: $($result.Output)"

    Write-Host "`n  Section: Edge-case input files" -ForegroundColor White

    # --- Pass_EmptyAndNoRunFiles ---
    # An empty action.yml and a workflow with no run: blocks must not crash and
    # must pass.
    $root = New-FixtureRoot
    $dir = Join-Path $root '.github/actions/emptyaction'
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    New-Item -ItemType File -Path (Join-Path $dir 'action.yml') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $root '.github/workflows/norun.yml') -Value @'
name: NoRun
on: [push]
jobs:
  j:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
'@
    $result = Invoke-LintInFixture $root
    $notCrashed = ($result.Output -notmatch 'ParentContainsErrorRecordException') -and ($result.Output -notmatch "cannot be found")
    Write-TestResult "Pass_EmptyAndNoRunFiles" ($result.ExitCode -eq 0 -and $notCrashed) "Expected exit 0 and no crash on empty/no-run files. Exit: $($result.ExitCode). Output: $($result.Output)"

} finally {
    Remove-Item -Recurse -Force $tempRoot -ErrorAction SilentlyContinue
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
