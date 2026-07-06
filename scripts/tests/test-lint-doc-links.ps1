#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Test runner for lint-doc-links.ps1.

.DESCRIPTION
    Verifies that lint-doc-links.ps1 correctly:
    - Ignores fictional path references inside language comments and docstrings
      (PowerShell, C#, Python, Shell, YAML, CMD).
    - Still catches live (non-comment) references to nonexistent markdown files.
    - Catches case-sensitivity drift in markdown links on every platform.
    - Preserves existing behaviors (bare .md mentions, missing relative prefix,
      absolute GitHub Pages paths, valid relative links).

.PARAMETER VerboseOutput
    Show verbose per-test diagnostics.

.EXAMPLE
    pwsh -NoProfile -File scripts/tests/test-lint-doc-links.ps1
    pwsh -NoProfile -File scripts/tests/test-lint-doc-links.ps1 -VerboseOutput
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

$lintScriptPath = (Resolve-Path (Join-Path $PSScriptRoot '..' 'lint-doc-links.ps1')).Path
$helperScriptPath = (Resolve-Path (Join-Path $PSScriptRoot '..' 'comment-stripping.ps1')).Path

$tempBase = if ($env:TEMP) { $env:TEMP } elseif ($env:TMPDIR) { $env:TMPDIR } else { '/tmp' }
$tempRoot = Join-Path $tempBase "test-lint-doc-links-$(Get-Random)"

function New-FixtureRoot {
    $root = Join-Path $tempRoot "repo-$(Get-Random)"
    New-Item -ItemType Directory -Path (Join-Path $root 'scripts') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $root 'docs') -Force | Out-Null
    Copy-Item -LiteralPath $lintScriptPath   -Destination (Join-Path $root 'scripts/lint-doc-links.ps1')
    Copy-Item -LiteralPath $helperScriptPath -Destination (Join-Path $root 'scripts/comment-stripping.ps1')
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
        [string]$Content,
        [switch]$NoNewline
    )
    $full = Join-Path $Root $RelativePath
    $dir = Split-Path -Parent $full
    if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    if ($NoNewline) {
        # Use WriteAllText so the file has NO trailing newline at all —
        # critical for the BLOCKER 1 regression fixture which exercises
        # the bare-scalar return from Get-Content under Set-StrictMode.
        [IO.File]::WriteAllText($full, $Content)
    } else {
        Set-Content -LiteralPath $full -Value $Content -NoNewline:$false
    }
}

function Save-FixtureFiles {
    param([string]$Root)
    Push-Location $Root
    try {
        & git add -A 2>$null | Out-Null
    } finally {
        Pop-Location
    }
}

function Invoke-LintInFixture {
    param(
        [string]$FixtureRoot,
        [string]$WorkingDirectory = $FixtureRoot,
        [string[]]$Paths = @(),
        [string]$Mode = ''
    )

    $lintCopy = Join-Path $FixtureRoot 'scripts/lint-doc-links.ps1'
    Save-FixtureFiles -Root $FixtureRoot
    Push-Location $WorkingDirectory
    try {
        $arguments = @('-NoProfile', '-File', $lintCopy)
        if ($Paths -and $Paths.Count -gt 0) {
            $arguments += '-Paths'
            $arguments += $Paths
        }
        if (-not [string]::IsNullOrWhiteSpace($Mode)) {
            $arguments += '-Mode'
            $arguments += $Mode
        }

        $output = & pwsh @arguments *>&1
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

    # Wrap the whole body so a thrown exception from one fixture doesn't halt
    # the entire suite — the test gets recorded as failed and we move on.
    try {
        $root = New-FixtureRoot
        foreach ($file in $Case.Files) {
            $noNewline = $false
            if ($file.PSObject.Properties['NoNewline']) { $noNewline = [bool]$file.NoNewline }
            Add-FixtureFile -Root $root -RelativePath $file.Path -Content $file.Content -NoNewline:$noNewline
        }
        $workingDirectory = $root
        if ($Case.PSObject.Properties['WorkingDirectoryRelativePath']) {
            $workingDirectory = Join-Path $root $Case.WorkingDirectoryRelativePath
            if (-not (Test-Path -LiteralPath $workingDirectory)) {
                New-Item -ItemType Directory -Path $workingDirectory -Force | Out-Null
            }
        }

        $paths = @()
        if ($Case.PSObject.Properties['Paths']) {
            $paths = @($Case.Paths)
        }
        $mode = ''
        if ($Case.PSObject.Properties['Mode']) {
            $mode = [string]$Case.Mode
        }

        $result = Invoke-LintInFixture -FixtureRoot $root -WorkingDirectory $workingDirectory -Paths $paths -Mode $mode

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

    Write-Host "Testing lint-doc-links.ps1..." -ForegroundColor White
    Write-Host "`n  Section A: Comment-masking regressions" -ForegroundColor White

    $sectionACases = @(
        [pscustomobject]@{
            Name = 'Pass_PsDocstringExampleIgnored'
            Files = @(
                [pscustomobject]@{ Path = 'docs/readme.md'; Content = "# Readme`n" }
                [pscustomobject]@{ Path = 'scripts/example.ps1'; Content = @'
<#
.EXAMPLE
    Some-Command -Path 'C:\repo\Docs\readme.md'
#>
param()
Write-Host 'ok'
'@ }
            )
            ExpectedExit = 0
            ExpectedOutputContains = @('Markdown link lint passed')
        }
        [pscustomobject]@{
            Name = 'Pass_CsXmlDocIgnored'
            Files = @(
                [pscustomobject]@{ Path = 'src/foo.cs'; Content = @'
class Foo {
    /// <example>docs/foo.md</example>
    public void Bar() { }
}
'@ }
            )
            ExpectedExit = 0
            ExpectedOutputContains = @('Markdown link lint passed')
        }
        [pscustomobject]@{
            Name = 'Pass_CsBlockCommentIgnored'
            Files = @(
                [pscustomobject]@{ Path = 'src/foo.cs'; Content = @'
class Foo {
    /* see docs/foo.md */
    public void Bar() { }
}
'@ }
            )
            ExpectedExit = 0
            ExpectedOutputContains = @('Markdown link lint passed')
        }
        [pscustomobject]@{
            Name = 'Pass_PythonTripleQuoteDocstringIgnored'
            Files = @(
                [pscustomobject]@{ Path = 'src/foo.py'; Content = @'
"""
Module docs.
See docs/foo.md for details.
"""
def bar():
    pass
'@ }
            )
            ExpectedExit = 0
            ExpectedOutputContains = @('Markdown link lint passed')
        }
        [pscustomobject]@{
            Name = 'Pass_PythonHashCommentIgnored'
            Files = @(
                [pscustomobject]@{ Path = 'src/foo.py'; Content = @'
# docs/foo.md placeholder reference in comment
def bar():
    pass
'@ }
            )
            ExpectedExit = 0
            ExpectedOutputContains = @('Markdown link lint passed')
        }
        [pscustomobject]@{
            Name = 'Pass_ShHashCommentIgnored'
            Files = @(
                [pscustomobject]@{ Path = 'src/foo.sh'; Content = @'
#!/usr/bin/env bash
# docs/foo.md historical reference
echo ok
'@ }
            )
            ExpectedExit = 0
            ExpectedOutputContains = @('Markdown link lint passed')
        }
        [pscustomobject]@{
            Name = 'Pass_YamlHashCommentIgnored'
            Files = @(
                [pscustomobject]@{ Path = '.github/workflows/x.yml'; Content = @'
name: X
# docs/foo.md sample reference
on: [push]
jobs:
  ok:
    runs-on: ubuntu-latest
    steps:
      - run: echo ok
'@ }
            )
            ExpectedExit = 0
            ExpectedOutputContains = @('Markdown link lint passed')
        }
        [pscustomobject]@{
            Name = 'Pass_CmdRemIgnored'
            Files = @(
                [pscustomobject]@{ Path = 'src/foo.cmd'; Content = @'
@echo off
REM docs/foo.md historical reference
echo ok
'@ }
            )
            ExpectedExit = 0
            ExpectedOutputContains = @('Markdown link lint passed')
        }
        # Load-bearing canary: comment masking is ON for .ps1 AND only masks comments,
        # not live strings. If someone removes the dot-source or breaks the language map
        # for .ps1, this fixture will both miss the comment masking (live ref OK) AND
        # the live-reference scan (which would skip the file entirely), so the test
        # would fail one way or the other.
        [pscustomobject]@{
            Name = 'Pass_DocstringIgnoredButLiveReferenceCaught'
            Files = @(
                [pscustomobject]@{ Path = 'scripts/example.ps1'; Content = @'
<#
.EXAMPLE
    See docs/fictional-comment.md for details.
#>
$p = 'docs/fictional-live.md'
Write-Host $p
'@ }
            )
            ExpectedExit = 'nonzero'
            ExpectedOutputContains = @('docs/fictional-live.md')
            ExpectedOutputNotContains = @('docs/fictional-comment.md')
        }
    )

    foreach ($c in $sectionACases) { Invoke-TestCase -Case $c }

    Write-Host "`n  Section B: Comments must NOT swallow real references" -ForegroundColor White

    $sectionBCases = @(
        [pscustomobject]@{
            Name = 'Fail_LiveCsReferenceCaught'
            Files = @(
                [pscustomobject]@{ Path = 'src/foo.cs'; Content = @'
class Foo {
    public void Bar() {
        var path = "docs/nonexistent.md";
    }
}
'@ }
            )
            ExpectedExit = 'nonzero'
            ExpectedOutputContains = @('docs/nonexistent.md', 'Source reference')
            ExpectedOutputNotContains = @('Bare .md mention', 'jekyll-relative-links', 'Absolute GitHub Pages path')
        }
        [pscustomobject]@{
            Name = 'Fail_PsLiveReferenceCaught'
            Files = @(
                [pscustomobject]@{ Path = 'src/foo.ps1'; Content = @'
$x = 'docs/nonexistent.md'
Write-Host $x
'@ }
            )
            ExpectedExit = 'nonzero'
            ExpectedOutputContains = @('docs/nonexistent.md', 'Source reference')
            ExpectedOutputNotContains = @('Bare .md mention', 'jekyll-relative-links', 'Absolute GitHub Pages path')
        }
        [pscustomobject]@{
            Name = 'Fail_HashInQuotedStringStillLive'
            Files = @(
                [pscustomobject]@{ Path = 'src/foo.py'; Content = @'
x = "docs/nonexistent.md # not a comment"
'@ }
            )
            ExpectedExit = 'nonzero'
            ExpectedOutputContains = @('docs/nonexistent.md', 'Source reference')
            ExpectedOutputNotContains = @('Bare .md mention', 'jekyll-relative-links', 'Absolute GitHub Pages path')
        }
        [pscustomobject]@{
            Name = 'Fail_ReferenceAfterClosingComment'
            Files = @(
                [pscustomobject]@{ Path = 'src/foo.cs'; Content = @'
class Foo {
    public void Bar() {
        /* intro */ var p = "docs/nonexistent.md";
    }
}
'@ }
            )
            ExpectedExit = 'nonzero'
            ExpectedOutputContains = @('docs/nonexistent.md', 'Source reference')
            ExpectedOutputNotContains = @('Bare .md mention', 'jekyll-relative-links', 'Absolute GitHub Pages path')
        }
    )

    foreach ($c in $sectionBCases) { Invoke-TestCase -Case $c }

    Write-Host "`n  Section C: Case sensitivity (cross-platform regression for Bug B)" -ForegroundColor White

    # On case-insensitive FS (Windows/macOS), Test-Path alone would accept Docs/readme.md
    # against on-disk docs/readme.md. Test-PathWithCase walks the parent listing with
    # ordinal -ceq compare so case drift is caught on every platform.
    #
    # NOTE on Linux load-bearing: this fixture exercises the END-TO-END pipeline
    # (lint script reads the markdown, calls Test-PathWithCase, reports). On
    # Linux specifically, the case mismatch is caught by `Test-Path -LiteralPath`
    # itself (case-sensitive fs), so this fixture proves the lint script
    # ROUTES through the right rejection path but does NOT prove the case
    # walker's `-ceq` logic in isolation. Section E adds a direct unit test
    # of `Test-PathWithCase` that exercises the case-walker even on Linux by
    # using two real directories with different cases.
    $sectionCCases = @(
        [pscustomobject]@{
            # Fixture uses `./Docs/readme.md` (with `./` prefix) so this test
            # CANNOT pass via the missing-relative-prefix or jekyll-relative-links
            # rules — the only way it can fail is if Test-PathWithCase rejects
            # the `Docs/` segment for case mismatch against on-disk `docs/`.
            Name = 'Fail_CaseMismatchCaught_CrossPlatform'
            Files = @(
                [pscustomobject]@{ Path = 'docs/readme.md'; Content = "# Readme`n" }
                [pscustomobject]@{ Path = 'README.md'; Content = "See [docs](./Docs/readme.md).`n" }
            )
            ExpectedExit = 'nonzero'
            ExpectedOutputContains = @('does not resolve to an existing markdown file')
            ExpectedOutputNotContains = @('missing relative prefix', 'jekyll-relative-links', 'Bare .md mention', 'Absolute GitHub Pages path')
        }
        [pscustomobject]@{
            Name = 'Pass_ExactCaseMatches'
            Files = @(
                [pscustomobject]@{ Path = 'docs/readme.md'; Content = "# Readme`n" }
                [pscustomobject]@{ Path = 'README.md'; Content = "See [docs](./docs/readme.md).`n" }
            )
            ExpectedExit = 0
            ExpectedOutputContains = @('Markdown link lint passed')
        }
    )

    foreach ($c in $sectionCCases) { Invoke-TestCase -Case $c }

    Write-Host "`n  Section D: Existing behaviors preserved" -ForegroundColor White

    $sectionDCases = @(
        [pscustomobject]@{
            Name = 'Fail_BareMdMention_InMarkdown'
            Files = @(
                [pscustomobject]@{ Path = 'README.md'; Content = "See foo.md please.`n" }
            )
            ExpectedExit = 'nonzero'
            ExpectedOutputContains = @('Bare .md mention')
            ExpectedOutputNotContains = @('jekyll-relative-links', 'Absolute GitHub Pages path', 'does not resolve to an existing markdown file')
        }
        [pscustomobject]@{
            Name = 'Fail_InlineCodeMarkdownReference_InMarkdown'
            Files = @(
                [pscustomobject]@{ Path = 'README.md'; Content = 'See `docs/readme.md` please.' }
            )
            ExpectedExit = 'nonzero'
            ExpectedOutputContains = @('Inline code mentions markdown file', 'docs/readme.md')
            ExpectedOutputNotContains = @('Bare .md mention', 'jekyll-relative-links', 'Absolute GitHub Pages path', 'does not resolve to an existing markdown file')
        }
        [pscustomobject]@{
            Name = 'Pass_InlineCodeMarkdownGlobsAllowed'
            Files = @(
                [pscustomobject]@{ Path = 'README.md'; Content = 'Patterns: `.md`, `*.md`, and `**/*.md`.' }
            )
            ExpectedExit = 0
            ExpectedOutputContains = @('Markdown link lint passed')
        }
        [pscustomobject]@{
            Name = 'Pass_InlineCodeMdxMentionAllowed'
            Files = @(
                [pscustomobject]@{ Path = 'README.md'; Content = 'Use `foo.mdx` for MDX examples.' }
            )
            ExpectedExit = 0
            ExpectedOutputContains = @('Markdown link lint passed')
        }
        [pscustomobject]@{
            Name = 'Fail_MissingRelativePrefix_InMarkdown'
            Files = @(
                [pscustomobject]@{ Path = 'docs/readme.md'; Content = "# Readme`n" }
                [pscustomobject]@{ Path = 'README.md'; Content = "See [readme](docs/readme.md).`n" }
            )
            ExpectedExit = 'nonzero'
            ExpectedOutputContains = @('jekyll-relative-links')
            ExpectedOutputNotContains = @('Bare .md mention', 'Absolute GitHub Pages path', 'does not resolve to an existing markdown file')
        }
        [pscustomobject]@{
            Name = 'Fail_AbsoluteGitHubPagesPath_InMarkdown'
            Files = @(
                [pscustomobject]@{ Path = 'README.md'; Content = "See [home](/unity-helpers/foo).`n" }
            )
            ExpectedExit = 'nonzero'
            ExpectedOutputContains = @('Absolute GitHub Pages path')
            ExpectedOutputNotContains = @('Bare .md mention', 'jekyll-relative-links', 'does not resolve to an existing markdown file')
        }
        [pscustomobject]@{
            # Round-3 BLOCKER 1 regression test: a markdown file with a broken
            # link and no trailing newline. Prior to wrapping Get-Content with
            # @(), Set-StrictMode caused $lines to bind as a bare scalar and
            # the loop never iterated — the file was silently skipped. Use
            # WriteAllText (NoNewline equivalent) so the fixture has exactly
            # one line and no terminator.
            Name = 'Fail_SingleLineMarkdownNoTrailingNewline'
            Files = @(
                [pscustomobject]@{ Path = 'README.md'; Content = 'See [docs](./Docs/missing.md).' ; NoNewline = $true }
            )
            ExpectedExit = 'nonzero'
            ExpectedOutputContains = @('Docs/missing.md', 'does not resolve to an existing markdown file')
            ExpectedOutputNotContains = @('Bare .md mention', 'jekyll-relative-links', 'Absolute GitHub Pages path')
        }
        [pscustomobject]@{
            Name = 'Pass_ValidMarkdownLink'
            Files = @(
                [pscustomobject]@{ Path = 'docs/readme.md'; Content = "# Readme`n" }
                [pscustomobject]@{ Path = 'README.md'; Content = "See [readme](./docs/readme.md).`n" }
            )
            ExpectedExit = 0
            ExpectedOutputContains = @('Markdown link lint passed')
        }
        [pscustomobject]@{
            Name = 'Pass_ValidNonMarkdownLocalFileLink'
            Files = @(
                [pscustomobject]@{ Path = 'LICENSE'; Content = "MIT`n" }
                [pscustomobject]@{ Path = 'README.md'; Content = "See the [license](./LICENSE).`n" }
            )
            ExpectedExit = 0
            ExpectedOutputContains = @('Markdown link lint passed')
        }
        [pscustomobject]@{
            Name = 'Pass_ValidLocalDirectoryLink'
            Files = @(
                [pscustomobject]@{ Path = 'docs/readme.md'; Content = "# Readme`n" }
                [pscustomobject]@{ Path = 'README.md'; Content = "See the [docs directory](./docs).`n" }
            )
            ExpectedExit = 0
            ExpectedOutputContains = @('Markdown link lint passed')
        }
        [pscustomobject]@{
            Name = 'Pass_ValidLocalDirectoryLinkWithTrailingSlash'
            Files = @(
                [pscustomobject]@{ Path = 'docs/readme.md'; Content = "# Readme`n" }
                [pscustomobject]@{ Path = 'README.md'; Content = "See the [docs directory](./docs/).`n" }
            )
            ExpectedExit = 0
            ExpectedOutputContains = @('Markdown link lint passed')
        }
        [pscustomobject]@{
            Name = 'Pass_SourceDirectorySelfLink'
            Files = @(
                [pscustomobject]@{ Path = 'docs/readme.md'; Content = "See this [directory](./).`n" }
            )
            ExpectedExit = 0
            ExpectedOutputContains = @('Markdown link lint passed')
        }
        [pscustomobject]@{
            Name = 'Pass_RepoRootDirectorySelfLink'
            Files = @(
                [pscustomobject]@{ Path = 'README.md'; Content = "See this [repository root](./).`n" }
            )
            ExpectedExit = 0
            ExpectedOutputContains = @('Markdown link lint passed')
        }
        [pscustomobject]@{
            Name = 'Pass_ParentDirectoryRepoRootLink'
            Files = @(
                [pscustomobject]@{ Path = 'docs/readme.md'; Content = "See the [repository root](../).`n" }
            )
            ExpectedExit = 0
            ExpectedOutputContains = @('Markdown link lint passed')
        }
        [pscustomobject]@{
            Name = 'Pass_ValidSamplesReadmeLink'
            Files = @(
                [pscustomobject]@{ Path = 'Samples~/DI - VContainer/README.md'; Content = "# VContainer Sample`n" }
                [pscustomobject]@{ Path = 'README.md'; Content = "See [sample](./Samples~/DI%20-%20VContainer/README.md).`n" }
            )
            ExpectedExit = 0
            ExpectedOutputContains = @('Markdown link lint passed')
        }
        [pscustomobject]@{
            Name = 'Pass_InlineCodePseudoLinkIgnoredByLocalTargetValidation'
            Files = @(
                [pscustomobject]@{ Path = 'README.md'; Content = "Use format ``[Display Text](Page)`` in examples.`n" }
            )
            ExpectedExit = 0
            ExpectedOutputContains = @('Markdown link lint passed')
        }
        [pscustomobject]@{
            Name = 'Fail_BrokenNonMarkdownLocalFileLink'
            Files = @(
                [pscustomobject]@{ Path = 'README.md'; Content = "See the [license](./LICENSE).`n" }
            )
            ExpectedExit = 'nonzero'
            ExpectedOutputContains = @('LICENSE', 'local file or directory')
            ExpectedOutputNotContains = @('Bare .md mention', 'jekyll-relative-links', 'Absolute GitHub Pages path')
        }
        [pscustomobject]@{
            Name = 'Fail_BrokenReferenceDefinitionNonMarkdownLink'
            Files = @(
                [pscustomobject]@{ Path = 'README.md'; Content = "See the [license][license].`n`n[license]: ./LICENSE`n" }
            )
            ExpectedExit = 'nonzero'
            ExpectedOutputContains = @('Reference link target', 'LICENSE', 'local file or directory')
            ExpectedOutputNotContains = @('Bare .md mention', 'jekyll-relative-links', 'Absolute GitHub Pages path')
        }
        [pscustomobject]@{
            Name = 'Fail_BrokenSamplesReadmeLink'
            Files = @(
                [pscustomobject]@{ Path = 'README.md'; Content = "See [sample](./Samples~/Missing/README.md).`n" }
            )
            ExpectedExit = 'nonzero'
            ExpectedOutputContains = @('Samples~/Missing/README.md', 'does not resolve to an existing markdown file')
            ExpectedOutputNotContains = @('Bare .md mention', 'jekyll-relative-links', 'Absolute GitHub Pages path')
        }
        [pscustomobject]@{
            Name = 'Pass_SubdirectoryInvocationUsesRepoRootAnchoring'
            Files = @(
                [pscustomobject]@{ Path = 'docs/readme.md'; Content = "# Readme`n" }
                [pscustomobject]@{ Path = 'README.md'; Content = "See [readme](./docs/readme.md).`n" }
            )
            WorkingDirectoryRelativePath = 'docs'
            ExpectedExit = 0
            ExpectedOutputContains = @('Markdown link lint passed')
        }
        [pscustomobject]@{
            Name = 'Fail_PathScopedListedMarkdownViolation'
            Files = @(
                [pscustomobject]@{ Path = 'README.md'; Content = "See foo.md please.`n" }
                [pscustomobject]@{ Path = 'docs/readme.md'; Content = "# Readme`n" }
            )
            Paths = @('README.md')
            ExpectedExit = 'nonzero'
            ExpectedOutputContains = @('Bare .md mention')
        }
        [pscustomobject]@{
            Name = 'Pass_PathScopedUnlistedMarkdownViolationIgnored'
            Files = @(
                [pscustomobject]@{ Path = 'README.md'; Content = "See foo.md please.`n" }
                [pscustomobject]@{ Path = 'docs/readme.md'; Content = "# Readme`n" }
            )
            Paths = @('docs/readme.md')
            ExpectedExit = 0
            ExpectedOutputContains = @('Markdown link lint passed')
        }
        [pscustomobject]@{
            Name = 'Fail_PathScopedSecondPathViolationCaught'
            Files = @(
                [pscustomobject]@{ Path = 'docs/readme.md'; Content = "# Readme`n" }
                [pscustomobject]@{ Path = 'README.md'; Content = "See foo.md please.`n" }
            )
            Paths = @('docs/readme.md', 'README.md')
            ExpectedExit = 'nonzero'
            ExpectedOutputContains = @('Bare .md mention')
        }
        [pscustomobject]@{
            Name = 'Pass_FormatModeSkipsBrokenLocalTarget'
            Mode = 'Format'
            Files = @(
                [pscustomobject]@{ Path = 'README.md'; Content = "See [missing](./docs/missing.md).`n" }
            )
            ExpectedExit = 0
            ExpectedOutputContains = @('Markdown link lint passed')
            ExpectedOutputNotContains = @('does not resolve to an existing markdown file')
        }
        [pscustomobject]@{
            Name = 'Pass_TargetModeSkipsMissingRelativePrefix'
            Mode = 'Targets'
            Files = @(
                [pscustomobject]@{ Path = 'docs/readme.md'; Content = "# Readme`n" }
                [pscustomobject]@{ Path = 'README.md'; Content = "See [readme](docs/readme.md).`n" }
            )
            ExpectedExit = 0
            ExpectedOutputContains = @('Markdown link lint passed')
            ExpectedOutputNotContains = @('jekyll-relative-links')
        }
        [pscustomobject]@{
            Name = 'Fail_TargetModeCatchesBrokenLocalTarget'
            Mode = 'Targets'
            Files = @(
                [pscustomobject]@{ Path = 'README.md'; Content = "See [missing](./docs/missing.md).`n" }
            )
            ExpectedExit = 'nonzero'
            ExpectedOutputContains = @('docs/missing.md', 'does not resolve to an existing markdown file')
            ExpectedOutputNotContains = @('jekyll-relative-links')
        }
    )

    foreach ($c in $sectionDCases) { Invoke-TestCase -Case $c }

    Write-Host "`n  Section E: Direct unit tests for Test-PathWithCase" -ForegroundColor White

    # The lint script is not designed to be dot-sourced (it has top-level
    # execution), so we use the language parser to extract just the
    # `Test-PathWithCase` function definition and inject it into the current
    # scope. This lets us call it directly in-process — load-bearing for the
    # `RepoRoot.TrimEnd` and Linux case-walker assertions below.
    $lintSource = Get-Content -LiteralPath $lintScriptPath -Raw
    $tokens = $null
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseInput(
        $lintSource, [ref]$tokens, [ref]$errors
    )
    if ($errors -and $errors.Count -gt 0) {
        Write-TestResult 'PathCase_AstParse' $false ("parser errors: {0}" -f ($errors -join '; '))
    } else {
        $fnAst = $ast.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -eq 'Test-PathWithCase'
        }, $true) | Select-Object -First 1

        if (-not $fnAst) {
            Write-TestResult 'PathCase_AstFunctionFound' $false 'Test-PathWithCase function not found in lint script'
        } else {
            # Re-define the function in the test scope by Invoke-Expression on its
            # textual form. This is intentional — we want the EXACT production
            # implementation, not a copy that could drift.
            Invoke-Expression $fnAst.Extent.Text

            # Build a fresh fixture root for the unit tests to query against.
            $unitRoot = Join-Path $tempRoot "unit-$(Get-Random)"
            New-Item -ItemType Directory -Path (Join-Path $unitRoot 'docs') -Force | Out-Null
            $actualFile = Join-Path $unitRoot 'docs/actualcase.md'
            [IO.File]::WriteAllText($actualFile, "x`n")

            # ----- Test 1: RepoRoot with TRAILING separator must short-circuit
            # at the repo-root boundary (load-bearing: prove the `-ceq`
            # comparison fires).
            #
            # Strategy: pass a RepoRoot ending in `/` that points at a
            # SUBDIRECTORY of the actual file's parent chain. Capture the
            # post-call sequence of `Get-ChildItem` parent enumerations by
            # arranging that walking PAST `$RepoRoot` would visit a directory
            # whose listing fails (read-protected). If the walk short-circuits
            # at `$RepoRoot`, the protected dir is never touched. If it does
            # NOT short-circuit (the bug), the walk hits the protected dir
            # and the function returns $false.
            #
            # We use a counter-based instrumentation: replace `Get-ChildItem`
            # with a tracking shim inside the function's execution. Because
            # `Test-PathWithCase` is dot-sourced into THIS scope, we can
            # shadow the cmdlet with a function in the parent scope and the
            # function call will pick it up.
            $sep = [IO.Path]::DirectorySeparatorChar
            $rootWithTrailingSep = $unitRoot + $sep

            # Track every directory listed during the walk. If the walk
            # short-circuits at $unitRoot, the listing of $unitRoot's PARENT
            # ($tempRoot or higher) must NEVER occur.
            $script:VisitedDirs = [System.Collections.Generic.List[string]]::new()
            $tempRootResolved = [IO.Path]::GetFullPath($tempRoot).TrimEnd($sep, '/')
            function Get-ChildItem {
                [CmdletBinding()]
                param(
                    [Parameter(Position = 0)] $LiteralPath,
                    [switch]$Force,
                    [Parameter(ValueFromRemainingArguments = $true)] $Rest
                )
                $script:VisitedDirs.Add([string]$LiteralPath) | Out-Null
                # Forward common parameters (especially -ErrorAction Stop, which
                # the production caller relies on to convert missing-path errors
                # into a catchable exception). [CmdletBinding()] swallows the
                # common parameters into $PSBoundParameters; without explicit
                # forwarding the inner cmdlet would silently use the default
                # ErrorAction and the shim would not behave transparently.
                $forwarded = @{ LiteralPath = $LiteralPath; Force = [bool]$Force }
                if ($PSBoundParameters.ContainsKey('ErrorAction')) {
                    $forwarded['ErrorAction'] = $PSBoundParameters['ErrorAction']
                }
                Microsoft.PowerShell.Management\Get-ChildItem @forwarded
            }

            try {
                $resultWithTrail = Test-PathWithCase -FullPath $actualFile -RepoRoot $rootWithTrailingSep
                $walkStoppedAtRoot = -not ($script:VisitedDirs | Where-Object {
                    $resolvedVisited = [IO.Path]::GetFullPath($_).TrimEnd($sep, '/')
                    # Visiting any dir that is a strict ancestor of $unitRoot
                    # means the walk did NOT short-circuit at RepoRoot.
                    $resolvedVisited -eq $tempRootResolved
                })
                $passed = ($resultWithTrail -eq $true) -and $walkStoppedAtRoot
                Write-TestResult 'PathCase_TrailingSeparatorOnRepoRootShortCircuits' $passed `
                    ("expected `$true and walk stopped at unitRoot. result=$resultWithTrail, visited=$($script:VisitedDirs -join ',')")
            } catch {
                Write-TestResult 'PathCase_TrailingSeparatorOnRepoRootShortCircuits' $false ("threw: " + $_.Exception.Message)
            } finally {
                # Remove the shim so subsequent tests see the real cmdlet.
                Remove-Item -LiteralPath function:Get-ChildItem -ErrorAction SilentlyContinue
            }

            # Sanity baseline: same query without the trailing separator must also resolve.
            try {
                $resultWithoutTrail = Test-PathWithCase -FullPath $actualFile -RepoRoot $unitRoot
                Write-TestResult 'PathCase_NoTrailingSeparatorBaselineResolves' ($resultWithoutTrail -eq $true) `
                    ("expected `$true with bare RepoRoot, got: $resultWithoutTrail")
            } catch {
                Write-TestResult 'PathCase_NoTrailingSeparatorBaselineResolves' $false ("threw: " + $_.Exception.Message)
            }

            # ----- Test 2: case-walker rejects parent-dir case drift even
            # when both the wrong-case and right-case names are simultaneously
            # present on disk (so Test-Path alone cannot disambiguate).
            #
            # Setup: create two REAL directories `docs/` and `Docs/` with the
            # same leaf file `actualcase.md` in both. On Linux these are
            # distinct directories; on Windows/macOS the second New-Item would
            # fail or alias the first, so this leg of the test is skipped on
            # case-insensitive filesystems.
            #
            # Then query `docs/actualcase.md` (exact case for the on-disk
            # `docs/` dir). Both `Test-Path` and the case-walker should
            # return $true. Then query `Docs/actualcase.md` — under the
            # case walker, `-ceq` against the parent listing finds BOTH
            # `Docs` and `docs` entries; the `Docs` segment must match
            # because we asked for `Docs` and the listing contains a
            # case-exact `Docs` entry. This proves the walker uses ordinal
            # case-sensitive equality (not OrdinalIgnoreCase) — load-bearing
            # on every platform.
            $caseSensitiveFs = $true
            try {
                New-Item -ItemType Directory -Path (Join-Path $unitRoot 'Docs') -Force -ErrorAction Stop | Out-Null
            } catch {
                $caseSensitiveFs = $false
            }
            if (-not $caseSensitiveFs -or -not (Test-Path -LiteralPath (Join-Path $unitRoot 'Docs'))) {
                Write-Host '         (skipped on case-insensitive filesystem)' -ForegroundColor DarkGray
            } else {
                $upperCaseFile = Join-Path (Join-Path $unitRoot 'Docs') 'actualcase.md'
                [IO.File]::WriteAllText($upperCaseFile, "y`n")

                # Sanity: both directories really exist with distinct cases.
                $entries = Get-ChildItem -LiteralPath $unitRoot -Force | Where-Object { $_.Name -in @('docs', 'Docs') } | ForEach-Object { $_.Name }
                $bothExist = ($entries -contains 'docs') -and ($entries -contains 'Docs')

                if (-not $bothExist) {
                    Write-Host ('         (filesystem collapsed Docs and docs into one entry: {0})' -f ($entries -join ',')) -ForegroundColor DarkGray
                } else {
                    # Lower-case query resolves with exact case match.
                    try {
                        $resA = Test-PathWithCase -FullPath (Join-Path (Join-Path $unitRoot 'docs') 'actualcase.md') -RepoRoot $unitRoot
                        Write-TestResult 'PathCase_LowerCaseSegmentResolves' ($resA -eq $true) `
                            ("expected `$true for `docs/actualcase.md`, got: $resA")
                    } catch {
                        Write-TestResult 'PathCase_LowerCaseSegmentResolves' $false ("threw: " + $_.Exception.Message)
                    }

                    # Upper-case query also resolves (real `Docs/` dir exists).
                    try {
                        $resB = Test-PathWithCase -FullPath (Join-Path (Join-Path $unitRoot 'Docs') 'actualcase.md') -RepoRoot $unitRoot
                        Write-TestResult 'PathCase_UpperCaseSegmentResolves' ($resB -eq $true) `
                            ("expected `$true for `Docs/actualcase.md`, got: $resB")
                    } catch {
                        Write-TestResult 'PathCase_UpperCaseSegmentResolves' $false ("threw: " + $_.Exception.Message)
                    }

                    # Crucial: a NON-EXISTENT mixed-case path must be rejected
                    # — `Test-Path` returns false (segment doesn't exist on
                    # case-sensitive FS), so the function returns false at the
                    # early bail. Document this explicitly so a future change
                    # to `Test-Path -LiteralPath` semantics doesn't silently
                    # weaken the case check.
                    $nonExistentPath = Join-Path (Join-Path $unitRoot 'DoCs') 'actualcase.md'
                    try {
                        $resC = Test-PathWithCase -FullPath $nonExistentPath -RepoRoot $unitRoot
                        Write-TestResult 'PathCase_NonExistentMixedCaseRejected' ($resC -eq $false) `
                            ("expected `$false for `DoCs/actualcase.md` (no such dir), got: $resC")
                    } catch {
                        Write-TestResult 'PathCase_NonExistentMixedCaseRejected' $false ("threw: " + $_.Exception.Message)
                    }
                }
            }

            # And the corresponding positive control: exact-case query resolves.
            $correctCasePath = Join-Path (Join-Path $unitRoot 'docs') 'actualcase.md'
            try {
                $resultCorrect = Test-PathWithCase -FullPath $correctCasePath -RepoRoot $unitRoot
                Write-TestResult 'PathCase_ExactCaseResolves' ($resultCorrect -eq $true) `
                    ("expected `$true for exact-case path, got: $resultCorrect")
            } catch {
                Write-TestResult 'PathCase_ExactCaseResolves' $false ("threw: " + $_.Exception.Message)
            }
        }
    }

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
