#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Test runner for lint-csharp-naming.ps1.

.DESCRIPTION
    Verifies that lint-csharp-naming.ps1 correctly:
    - Reports the actual method-declaration line number when a violation
      is preceded by a multi-line `///` XML doc comment.
    - Does NOT flag a properly-named method preceded by an XML doc comment.

    Load-bearing on the `^[ \t]*` indent prefix in the method-declaration
    regex: with `\s*`, `\s` would match newline characters, allowing the
    regex engine to anchor at a line earlier in the doc-comment block (the
    masked `///` lines collapse to whitespace) and report the wrong line
    number. `[ \t]*` keeps each match within a single source line.

.PARAMETER VerboseOutput
    Show verbose per-test diagnostics.

.EXAMPLE
    pwsh -NoProfile -File scripts/tests/test-lint-csharp-naming.ps1
#>
param(
    [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:TestsPassed = 0
$script:TestsFailed = 0
$script:FailedTests = @()

function Write-TestResult {
    param(
        [string]$TestName,
        [bool]$Passed,
        [string]$Message = ''
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

$lintScriptPath   = (Resolve-Path (Join-Path $PSScriptRoot '..' 'lint-csharp-naming.ps1')).Path
$helperScriptPath = (Resolve-Path (Join-Path $PSScriptRoot '..' 'comment-stripping.ps1')).Path
$gitHelpersPath   = (Resolve-Path (Join-Path $PSScriptRoot '..' 'git-staging-helpers.ps1')).Path

$tempBase = if ($env:TEMP) { $env:TEMP } elseif ($env:TMPDIR) { $env:TMPDIR } else { '/tmp' }
$tempRoot = Join-Path $tempBase "test-lint-csharp-naming-$(Get-Random)"

function New-FixtureRoot {
    $root = Join-Path $tempRoot "repo-$(Get-Random)"
    New-Item -ItemType Directory -Path (Join-Path $root 'scripts') -Force | Out-Null
    # All three source roots, not just the one the cases write into: the linter now refuses a
    # missing root rather than skipping it, so a fixture repo that lacks Editor/ and Tests/ is not
    # a repository shape it should accept (#556).
    New-Item -ItemType Directory -Path (Join-Path $root 'Runtime') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $root 'Editor') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $root 'Tests') -Force | Out-Null
    # The lint script dot-sources BOTH git-staging-helpers AND comment-stripping
    # from $PSScriptRoot — every dependency must be staged into the fixture
    # `scripts/` directory or the script throws on dot-source.
    Copy-Item -LiteralPath $lintScriptPath   -Destination (Join-Path $root 'scripts/lint-csharp-naming.ps1')
    Copy-Item -LiteralPath $helperScriptPath -Destination (Join-Path $root 'scripts/comment-stripping.ps1')
    Copy-Item -LiteralPath $gitHelpersPath   -Destination (Join-Path $root 'scripts/git-staging-helpers.ps1')
    Push-Location $root
    try {
        & git init --quiet 2>$null | Out-Null
        & git config user.email 'test@example.com' 2>$null | Out-Null
        & git config user.name 'Test' 2>$null | Out-Null
    } finally {
        Pop-Location
    }
    return $root
}

function Add-FixtureFile {
    param(
        [string]$Root,
        [string]$RelativePath,
        [string]$Content
    )
    $full = Join-Path $Root $RelativePath
    $dir = Split-Path -Parent $full
    if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    # Use WriteAllText to keep newline content exactly as authored — the
    # method-declaration line number is the entire point of these fixtures
    # so we must not let Set-Content rewrite line endings.
    [IO.File]::WriteAllText($full, $Content)
}

function Invoke-LintInFixture {
    param([string]$FixtureRoot)
    $lintCopy = Join-Path $FixtureRoot 'scripts/lint-csharp-naming.ps1'
    Push-Location $FixtureRoot
    try {
        $output = & pwsh -NoProfile -File $lintCopy *>&1
        $exitCode = $LASTEXITCODE
    } finally {
        Pop-Location
    }
    return @{ ExitCode = $exitCode; Output = ($output | Out-String) }
}

function Invoke-TestCase {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$Case
    )

    try {
        $root = New-FixtureRoot
        foreach ($file in $Case.Files) {
            Add-FixtureFile -Root $root -RelativePath $file.Path -Content $file.Content
        }
        $result = Invoke-LintInFixture $root

        $reasons = @()

        if ($Case.PSObject.Properties['ExpectedExit']) {
            $expected = $Case.ExpectedExit
            if ($expected -is [string] -and $expected -eq 'nonzero') {
                if ($result.ExitCode -eq 0) { $reasons += "expected nonzero exit, got 0" }
            } else {
                if ($result.ExitCode -ne [int]$expected) { $reasons += "expected exit $expected, got $($result.ExitCode)" }
            }
        }

        if ($Case.PSObject.Properties['ExpectedOutputContains']) {
            foreach ($needle in $Case.ExpectedOutputContains) {
                if ($result.Output -notmatch [regex]::Escape($needle)) {
                    $reasons += "output missing required substring: $needle"
                }
            }
        }

        if ($Case.PSObject.Properties['ExpectedOutputNotContains']) {
            foreach ($needle in $Case.ExpectedOutputNotContains) {
                if ($result.Output -match [regex]::Escape($needle)) {
                    $reasons += "output contains forbidden substring: $needle"
                }
            }
        }

        if ($reasons.Count -eq 0) {
            Write-TestResult $Case.Name $true
        } else {
            $msg = ($reasons -join '; ') + " | Output: $($result.Output)"
            Write-TestResult $Case.Name $false $msg
        }
    } catch {
        Write-TestResult $Case.Name $false ("exception: " + $_.Exception.Message)
    }
}

try {
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

    Write-Host "Testing lint-csharp-naming.ps1..." -ForegroundColor White
    Write-Host "`n  Section A: XML doc comment + method line-number reporting" -ForegroundColor White

    # Fixture lines (1-indexed):
    #   1: namespace Foo {
    #   2:     /// <summary>
    #   3:     /// Multi-line XML doc comment.
    #   4:     /// </summary>
    #   5:     public class Bar {
    #   6:         /// <summary>
    #   7:         /// Doc on the offending method.
    #   8:         /// </summary>
    #   9:         public void Bad_Name() { }
    #  10:     }
    #  11: }
    #
    # Under the regex `^[ \t]*...`, the engine anchors at line 9 and reports
    # `line=9`. Under the buggy `^\s*...`, `\s` matches newlines, so the engine
    # could anchor at the start of line 6 (the doc-comment block) — masked
    # `///` lines collapse to whitespace and the `\s*` would consume those
    # newlines, producing `line=6` (or earlier). The reported line MUST equal
    # the method-declaration line, so we assert on `line=9` exactly.
    $sectionACases = @(
        [pscustomobject]@{
            Name = 'Fail_DocCommentBeforeBadlyNamedMethod_LineNumberPointsToMethod'
            Files = @(
                [pscustomobject]@{
                    Path = 'Runtime/Bar.cs'
                    Content = @'
namespace Foo {
    /// <summary>
    /// Multi-line XML doc comment.
    /// </summary>
    public class Bar {
        /// <summary>
        /// Doc on the offending method.
        /// </summary>
        public void Bad_Name() { }
    }
}
'@
                }
            )
            ExpectedExit = 'nonzero'
            ExpectedOutputContains = @('Bad_Name', 'line=9', 'Runtime/Bar.cs')
            # The line number must be 9 (method decl), NOT an earlier line in
            # the doc-comment block (lines 6, 7, 8) or the class line (5).
            ExpectedOutputNotContains = @('line=5', 'line=6', 'line=7', 'line=8')
        }
        [pscustomobject]@{
            Name = 'Pass_DocCommentBeforeCorrectlyNamedMethod'
            Files = @(
                [pscustomobject]@{
                    Path = 'Runtime/Bar.cs'
                    Content = @'
namespace Foo {
    /// <summary>
    /// Multi-line XML doc comment with a Bad_Name reference.
    /// </summary>
    public class Bar {
        /// <summary>
        /// Doc on the well-named method, also mentions Bad_Name.
        /// </summary>
        public void GoodName() { }
    }
}
'@
                }
            )
            ExpectedExit = 0
            ExpectedOutputContains = @('All C# method names follow naming conventions')
            # Even though the doc-comment text mentions `Bad_Name`, comment masking
            # means the linter must NOT flag it.
            ExpectedOutputNotContains = @('UNH004', 'Bad_Name')
        }
    )

    foreach ($c in $sectionACases) { Invoke-TestCase -Case $c }

} finally {
    Remove-Item -Recurse -Force $tempRoot -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host ("Tests passed: {0}" -f $script:TestsPassed) -ForegroundColor Green
Write-Host ("Tests failed: {0}" -f $script:TestsFailed) -ForegroundColor $(if ($script:TestsFailed -gt 0) { 'Red' } else { 'Green' })
if ($script:FailedTests.Count -gt 0) {
    Write-Host 'Failed tests:' -ForegroundColor Red
    foreach ($t in $script:FailedTests) {
        Write-Host "  - $t" -ForegroundColor Red
    }
}

exit $script:TestsFailed
