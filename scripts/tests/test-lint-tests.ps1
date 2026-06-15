Param(
  [switch]$VerboseOutput
)

<#
.SYNOPSIS
    Test runner for lint-tests.ps1

.DESCRIPTION
    Tests that lint-tests.ps1 correctly:
    - Validates allowlisted helper file paths exist on disk
    - Detects UNH001 (direct destroy without Track)
    - Detects UNH002 (untracked Unity object allocation)
    - Detects UNH003 (missing CommonTestBase inheritance)
    - Passes clean files that follow all conventions
    - Allowlists known helper files correctly

.PARAMETER VerboseOutput
    Show detailed output during test execution

.EXAMPLE
    ./scripts/tests/test-lint-tests.ps1
    ./scripts/tests/test-lint-tests.ps1 -VerboseOutput
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:TestsPassed = 0
$script:TestsFailed = 0
$script:FailedTests = @()

function Write-Info($msg) {
  if ($VerboseOutput) { Write-Host "[test-lint-tests] $msg" -ForegroundColor Cyan }
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

$lintScriptPath = Join-Path $PSScriptRoot '..' 'lint-tests.ps1'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path

Write-Host "Testing lint-tests.ps1..." -ForegroundColor White

# ── Test 1: All allowlisted paths exist on disk ──────────────────────────────
Write-Host "`n  Section: Allowlist path validation" -ForegroundColor White

$lintContent = Get-Content $lintScriptPath -Raw
# Extract the $allowedHelperFiles array entries
$pathMatches = [regex]::Matches($lintContent, "'\s*([^']+\.cs)\s*'")
$allowlistPaths = @()
foreach ($m in $pathMatches) {
  $p = $m.Groups[1].Value
  # Only include paths that look like test helper files
  if ($p -match '^Tests/') {
    $allowlistPaths += $p
  }
}

foreach ($relPath in $allowlistPaths) {
  $fullPath = Join-Path $repoRoot $relPath
  $exists = Test-Path $fullPath
  Write-TestResult "AllowlistPathExists.$($relPath -replace '[/\\]','.')" $exists "File not found at: $fullPath"
}

# ── Test 2: Lint passes on a clean test file (known good) ────────────────────
Write-Host "`n  Section: Clean file acceptance" -ForegroundColor White

# Create a temporary clean test file
$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) "lint-tests-test-$(Get-Random)"
$tempTestDir = Join-Path $tempDir 'Tests' 'Editor'
New-Item -ItemType Directory -Path $tempTestDir -Force | Out-Null

try {

$cleanContent = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;

    public sealed class CleanTest : CommonTestBase
    {
        [Test]
        public void MyTest()
        {
            var go = Track(new GameObject("test"));
            Assert.IsTrue(go != null);
        }
    }
}
'@

$cleanFile = Join-Path $tempTestDir 'CleanTest.cs'
Set-Content -Path $cleanFile -Value $cleanContent -NoNewline

try {
  Push-Location $tempDir
  $output = & $lintScriptPath -Paths (Join-Path 'Tests' 'Editor' 'CleanTest.cs') *>&1
  $exitCode = $LASTEXITCODE
  Pop-Location
  Write-TestResult "CleanFile.PassesLint" ($exitCode -eq 0) "Expected exit 0, got $exitCode. Output: $($output | Out-String)"
} catch {
  Pop-Location
  Write-TestResult "CleanFile.PassesLint" $false "Exception: $_"
}

# ── Test 3: UNH001 detected (direct destroy without Track) ──────────────────
Write-Host "`n  Section: UNH001 detection" -ForegroundColor White

$unh001Content = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;
    using UnityEngine;

    public sealed class DestroyTest : CommonTestBase
    {
        [Test]
        public void MyTest()
        {
            var go = Track(new GameObject("test"));
            Object.DestroyImmediate(go);
        }
    }
}
'@

$unh001File = Join-Path $tempTestDir 'DestroyTest.cs'
Set-Content -Path $unh001File -Value $unh001Content -NoNewline

try {
  Push-Location $tempDir
  $output = & $lintScriptPath -Paths (Join-Path 'Tests' 'Editor' 'DestroyTest.cs') *>&1
  $exitCode = $LASTEXITCODE
  Pop-Location
  $outputStr = $output | Out-String
  $hasUNH001 = $outputStr -match 'UNH001'
  Write-TestResult "UNH001.DetectsDirectDestroy" ($exitCode -ne 0 -and $hasUNH001) "Expected non-zero exit with UNH001. Exit: $exitCode, Output: $outputStr"
} catch {
  Pop-Location
  Write-TestResult "UNH001.DetectsDirectDestroy" $false "Exception: $_"
}

# ── Test 4: UNH002 detected (untracked allocation) ──────────────────────────
Write-Host "`n  Section: UNH002 detection" -ForegroundColor White

$unh002Content = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;
    using UnityEngine;

    public sealed class UntrackedTest : CommonTestBase
    {
        [Test]
        public void MyTest()
        {
            Texture2D texture = new Texture2D(64, 64);
            Assert.IsTrue(texture != null);
        }
    }
}
'@

$unh002File = Join-Path $tempTestDir 'UntrackedTest.cs'
Set-Content -Path $unh002File -Value $unh002Content -NoNewline

try {
  Push-Location $tempDir
  $output = & $lintScriptPath -Paths (Join-Path 'Tests' 'Editor' 'UntrackedTest.cs') *>&1
  $exitCode = $LASTEXITCODE
  Pop-Location
  $outputStr = $output | Out-String
  $hasUNH002 = $outputStr -match 'UNH002'
  Write-TestResult "UNH002.DetectsUntrackedAlloc" ($exitCode -ne 0 -and $hasUNH002) "Expected non-zero exit with UNH002. Exit: $exitCode, Output: $outputStr"
} catch {
  Pop-Location
  Write-TestResult "UNH002.DetectsUntrackedAlloc" $false "Exception: $_"
}

# ── Test 5: UNH003 detected (missing CommonTestBase) ────────────────────────
Write-Host "`n  Section: UNH003 detection" -ForegroundColor White

$unh003Content = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;
    using UnityEngine;

    public sealed class NoBaseTest
    {
        [Test]
        public void MyTest()
        {
            Texture2D texture = new Texture2D(64, 64);
            Assert.IsTrue(texture != null);
        }
    }
}
'@

$unh003File = Join-Path $tempTestDir 'NoBaseTest.cs'
Set-Content -Path $unh003File -Value $unh003Content -NoNewline

try {
  Push-Location $tempDir
  $output = & $lintScriptPath -Paths (Join-Path 'Tests' 'Editor' 'NoBaseTest.cs') *>&1
  $exitCode = $LASTEXITCODE
  Pop-Location
  $outputStr = $output | Out-String
  $hasUNH003 = $outputStr -match 'UNH003'
  Write-TestResult "UNH003.DetectsMissingBase" ($exitCode -ne 0 -and $hasUNH003) "Expected non-zero exit with UNH003. Exit: $exitCode, Output: $outputStr"
} catch {
  Pop-Location
  Write-TestResult "UNH003.DetectsMissingBase" $false "Exception: $_"
}

# ── Test 6: Allowlisted file is skipped ──────────────────────────────────────
Write-Host "`n  Section: Allowlist filtering" -ForegroundColor White

# Extract the Is-AllowlistedFile function
$funcPattern = '(?s)(function Is-AllowlistedFile\([^)]*\)\s*\{.*?\n\})'
if ($lintContent -match $funcPattern) {
  Invoke-Expression $Matches[1]

  # Also extract $allowedHelperFiles
  $arrayPattern = '(?s)\$allowedHelperFiles\s*=\s*@\((.*?)\)'
  if ($lintContent -match $arrayPattern) {
    $arrayExpr = '@(' + $Matches[1] + ')'
    $allowedHelperFiles = Invoke-Expression $arrayExpr
  }
} else {
  Write-Host "  FATAL: Could not extract Is-AllowlistedFile function" -ForegroundColor Red
}

if ($allowlistPaths.Count -gt 0) {
  # Test that an exact-match path returns true
  $testPath = $allowlistPaths[0]
  $result = Is-AllowlistedFile $testPath
  Write-TestResult "Allowlist.ExactMatch" $result "Expected true for '$testPath'"

  # Test that a backslash-normalized path returns true
  $backslashPath = $testPath -replace '/', '\'
  $result2 = Is-AllowlistedFile $backslashPath
  Write-TestResult "Allowlist.BackslashNormalized" $result2 "Expected true for '$backslashPath'"

  # Test that a path with leading ./ is handled
  $dotSlashPath = "./$testPath"
  $result3 = Is-AllowlistedFile $dotSlashPath
  Write-TestResult "Allowlist.DotSlashPrefix" $result3 "Expected true for '$dotSlashPath'"

  # Test that a non-allowlisted path returns false
  $result4 = Is-AllowlistedFile 'Tests/Editor/SomeRandomTest.cs'
  Write-TestResult "Allowlist.NonMatchReturnsFalse" (-not $result4) "Expected false for non-allowlisted path"
}

# ── Test 7: UNH-SUPPRESS comment skips violation ────────────────────────────
Write-Host "`n  Section: UNH-SUPPRESS handling" -ForegroundColor White

$suppressContent = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;
    using UnityEngine;

    public sealed class SuppressTest : CommonTestBase
    {
        [Test]
        public void MyTest()
        {
            var go = Track(new GameObject("test"));
            Object.DestroyImmediate(go); // UNH-SUPPRESS
        }
    }
}
'@

$suppressFile = Join-Path $tempTestDir 'SuppressTest.cs'
Set-Content -Path $suppressFile -Value $suppressContent -NoNewline

try {
  Push-Location $tempDir
  $output = & $lintScriptPath -Paths (Join-Path 'Tests' 'Editor' 'SuppressTest.cs') *>&1
  $exitCode = $LASTEXITCODE
  Pop-Location
  Write-TestResult "UNH-SUPPRESS.SkipsViolation" ($exitCode -eq 0) "Expected exit 0 with suppress comment. Exit: $exitCode, Output: $($output | Out-String)"
} catch {
  Pop-Location
  Write-TestResult "UNH-SUPPRESS.SkipsViolation" $false "Exception: $_"
}

# ── Test 8: UNH004 detected (underscores in test names) ─────────────────────
Write-Host "`n  Section: UNH004 detection" -ForegroundColor White

$unh004Content = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;
    using System.Collections.Generic;

    public sealed class NamingTest : CommonTestBase
    {
        private static IEnumerable<TestCaseData> MyTestData()
        {
            yield return new TestCaseData(1).SetName("Some_Bad_Name");
        }

        [TestCaseSource(nameof(MyTestData))]
        public void MyTest(int value)
        {
            Assert.IsTrue(value > 0);
        }
    }
}
'@

$unh004File = Join-Path $tempTestDir 'NamingTest.cs'
Set-Content -Path $unh004File -Value $unh004Content -NoNewline

try {
  Push-Location $tempDir
  $output = & $lintScriptPath -Paths (Join-Path 'Tests' 'Editor' 'NamingTest.cs') *>&1
  $exitCode = $LASTEXITCODE
  Pop-Location
  $outputStr = $output | Out-String
  $hasUNH004 = $outputStr -match 'UNH004'
  Write-TestResult "UNH004.DetectsUnderscoreInSetName" ($exitCode -ne 0 -and $hasUNH004) "Expected non-zero exit with UNH004. Exit: $exitCode, Output: $outputStr"
} catch {
  Pop-Location
  Write-TestResult "UNH004.DetectsUnderscoreInSetName" $false "Exception: $_"
}

# ── Test 9: UNH005 detected (Assert.IsNull / Assert.IsNotNull) ──────────────
Write-Host "`n  Section: UNH005 detection" -ForegroundColor White

$unh005Content = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;
    using UnityEngine;

    public sealed class NullAssertTest : CommonTestBase
    {
        [Test]
        public void MyTest()
        {
            var go = Track(new GameObject("test"));
            Assert.IsNotNull(go);
        }
    }
}
'@

$unh005File = Join-Path $tempTestDir 'NullAssertTest.cs'
Set-Content -Path $unh005File -Value $unh005Content -NoNewline

try {
  Push-Location $tempDir
  $output = & $lintScriptPath -Paths (Join-Path 'Tests' 'Editor' 'NullAssertTest.cs') *>&1
  $exitCode = $LASTEXITCODE
  Pop-Location
  $outputStr = $output | Out-String
  $hasUNH005 = $outputStr -match 'UNH005'
  Write-TestResult "UNH005.DetectsAssertIsNotNull" ($exitCode -ne 0 -and $hasUNH005) "Expected non-zero exit with UNH005. Exit: $exitCode, Output: $outputStr"
} catch {
  Pop-Location
  Write-TestResult "UNH005.DetectsAssertIsNotNull" $false "Exception: $_"
}

# ── Test 10: Stale allowlist path causes failure ─────────────────────────────
Write-Host "`n  Section: Stale allowlist validation" -ForegroundColor White

# Create a modified copy of lint-tests.ps1 with a fake allowlist entry
$staleTempDir = Join-Path ([System.IO.Path]::GetTempPath()) "lint-tests-stale-$(Get-Random)"
New-Item -ItemType Directory -Path $staleTempDir -Force | Out-Null
# Create package.json to trigger the allowlist validation
Set-Content -Path (Join-Path $staleTempDir 'package.json') -Value '{}' -NoNewline
# Create Tests dir structure
$staleTestDir = Join-Path $staleTempDir 'Tests' 'Editor'
New-Item -ItemType Directory -Path $staleTestDir -Force | Out-Null

# Copy lint script and inject a fake path
$staleLintContent = $lintContent -replace [regex]::Escape("'Tests/Core/TextureTestHelper.cs',"), "'Tests/Core/TextureTestHelper.cs',`n  'Tests/NonExistent/FakeFile.cs',"
$staleLintPath = Join-Path $staleTempDir 'lint-tests-stale.ps1'
Set-Content -Path $staleLintPath -Value $staleLintContent -NoNewline

# lint-tests.ps1 now dot-sources scripts/comment-stripping.ps1 relative to
# $PSScriptRoot. Copy the helper next to the staged copy so the dot-source
# resolves inside the tempdir. Without this, the dot-source fails with
# "term is not recognized" before the allowlist validation runs.
$helperSrc = Join-Path $PSScriptRoot '..' 'comment-stripping.ps1'
Copy-Item -LiteralPath $helperSrc -Destination (Join-Path $staleTempDir 'comment-stripping.ps1') -Force

$staleCleanFile = Join-Path $staleTestDir 'Clean.cs'
Set-Content -Path $staleCleanFile -Value $cleanContent -NoNewline

try {
  Push-Location $staleTempDir
  $output = & $staleLintPath -Paths (Join-Path 'Tests' 'Editor' 'Clean.cs') *>&1
  $exitCode = $LASTEXITCODE
  Pop-Location
  $outputStr = $output | Out-String
  $hasError = $outputStr -match 'Allowlisted helper file not found'
  Write-TestResult "StaleAllowlist.FailsOnMissingPath" ($exitCode -ne 0 -and $hasError) "Expected non-zero exit with error message. Exit: $exitCode, Output: $outputStr"
} catch {
  Pop-Location
  Write-TestResult "StaleAllowlist.FailsOnMissingPath" $false "Exception: $_"
}

Remove-Item -Recurse -Force $staleTempDir -ErrorAction SilentlyContinue

# ── Test 11: UNH004 method-name variants ─────────────────────────────────────
Write-Host "`n  Section: UNH004 method-name detection" -ForegroundColor White

function Invoke-LintOnFixture {
  param(
    [string]$FixtureRelativePath,
    [string]$FixtureContent
  )
  $path = Join-Path $tempTestDir $FixtureRelativePath
  Set-Content -Path $path -Value $FixtureContent -NoNewline
  try {
    Push-Location $tempDir
    $out = & $lintScriptPath -Paths (Join-Path 'Tests' 'Editor' $FixtureRelativePath) *>&1
    $exit = $LASTEXITCODE
    Pop-Location
    return [pscustomobject]@{ ExitCode = $exit; Output = ($out | Out-String) }
  } catch {
    Pop-Location
    return [pscustomobject]@{ ExitCode = -1; Output = "Exception: $_" }
  }
}

# Case 1: Underscore in [Test] method name
$case1 = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;

    public sealed class Case1 : CommonTestBase
    {
        [Test]
        public void Snake_Case_Test() { }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'Case1.cs' -FixtureContent $case1
$ok = ($r.ExitCode -ne 0) -and ($r.Output -match 'UNH004') -and ($r.Output -match 'Snake_Case_Test')
Write-TestResult "UNH004.MethodName.DetectsUnderscoreInTestMethod" $ok "Exit: $($r.ExitCode), Output: $($r.Output)"

# Case 2: Stacked attributes
$case2 = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;

    public sealed class Case2 : CommonTestBase
    {
        [Test]
        [Category("Fast")]
        public void Stacked_Test() { }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'Case2.cs' -FixtureContent $case2
$ok = ($r.ExitCode -ne 0) -and ($r.Output -match 'UNH004') -and ($r.Output -match 'Stacked_Test')
Write-TestResult "UNH004.MethodName.DetectsInStackedAttributes" $ok "Exit: $($r.ExitCode), Output: $($r.Output)"

# Case 3: Same-line attribute
$case3 = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;

    public sealed class Case3 : CommonTestBase
    {
        [Test] public void Inline_Test() { }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'Case3.cs' -FixtureContent $case3
$ok = ($r.ExitCode -ne 0) -and ($r.Output -match 'UNH004') -and ($r.Output -match 'Inline_Test')
Write-TestResult "UNH004.MethodName.DetectsInSameLineAttribute" $ok "Exit: $($r.ExitCode), Output: $($r.Output)"

# Case 4: Multi-line attribute args
$case4 = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;

    public sealed class Case4 : CommonTestBase
    {
        [TestCase(
            1,
            2)]
        public void Multi_Line_Test(int a, int b) { }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'Case4.cs' -FixtureContent $case4
$ok = ($r.ExitCode -ne 0) -and ($r.Output -match 'UNH004') -and ($r.Output -match 'Multi_Line_Test')
Write-TestResult "UNH004.MethodName.DetectsWithMultiLineAttributeArgs" $ok "Exit: $($r.ExitCode), Output: $($r.Output)"

# Case 5: // comment between attribute and signature
$case5 = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;

    public sealed class Case5 : CommonTestBase
    {
        [Test]
        // why
        public void Comment_Between() { }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'Case5.cs' -FixtureContent $case5
$ok = ($r.ExitCode -ne 0) -and ($r.Output -match 'UNH004') -and ($r.Output -match 'Comment_Between')
Write-TestResult "UNH004.MethodName.DetectsWithCommentBetweenAttrAndSignature" $ok "Exit: $($r.ExitCode), Output: $($r.Output)"

# Case 6: Trailing // reason on attribute line
$case6 = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;

    public sealed class Case6 : CommonTestBase
    {
        [Test]
        [Category("Fast")] // reason
        public void Trailing_Comment() { }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'Case6.cs' -FixtureContent $case6
$ok = ($r.ExitCode -ne 0) -and ($r.Output -match 'UNH004') -and ($r.Output -match 'Trailing_Comment')
Write-TestResult "UNH004.MethodName.DetectsWithTrailingCommentOnAttr" $ok "Exit: $($r.ExitCode), Output: $($r.Output)"

# Case 7: Non-test method with underscore is NOT flagged
$case7 = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;

    public sealed class Case7 : CommonTestBase
    {
        private void Helper_Method() { }

        [Test]
        public void LegitTest()
        {
            Helper_Method();
        }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'Case7.cs' -FixtureContent $case7
$ok = ($r.ExitCode -eq 0)
Write-TestResult "UNH004.MethodName.DoesNotFlagNonTestMethods" $ok "Exit: $($r.ExitCode), Output: $($r.Output)"

# Case 8: UNH-SUPPRESS honors
$case8 = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;

    public sealed class Case8 : CommonTestBase
    {
        [Test] // UNH-SUPPRESS
        public void Suppressed_Underscore() { }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'Case8.cs' -FixtureContent $case8
$ok = ($r.ExitCode -eq 0)
Write-TestResult "UNH004.MethodName.HonorsUnhSuppress" $ok "Exit: $($r.ExitCode), Output: $($r.Output)"

# Case 9: PascalCase not flagged
$case9 = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;

    public sealed class Case9 : CommonTestBase
    {
        [Test] public void PascalCaseIsFine() { }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'Case9.cs' -FixtureContent $case9
$ok = ($r.ExitCode -eq 0)
Write-TestResult "UNH004.MethodName.DoesNotFlagPascalCase" $ok "Exit: $($r.ExitCode), Output: $($r.Output)"

# Case 10: Empty file does not crash
$emptyFile = Join-Path $tempTestDir 'EmptyFixture.cs'
Set-Content -Path $emptyFile -Value '' -NoNewline
try {
  Push-Location $tempDir
  $out = & $lintScriptPath -Paths (Join-Path 'Tests' 'Editor' 'EmptyFixture.cs') *>&1
  $exit = $LASTEXITCODE
  Pop-Location
  $outStr = $out | Out-String
  $ok = ($exit -eq 0) -and ($outStr -notmatch 'cannot be found on this object')
  Write-TestResult "StrictMode.EmptyFileDoesNotCrashLinter" $ok "Exit: $exit, Output: $outStr"
} catch {
  Pop-Location
  Write-TestResult "StrictMode.EmptyFileDoesNotCrashLinter" $false "Exception: $_"
}

# Case 11: Single-line file does not crash
$singleFile = Join-Path $tempTestDir 'SingleLineFixture.cs'
Set-Content -Path $singleFile -Value '// a comment' -NoNewline
try {
  Push-Location $tempDir
  $out = & $lintScriptPath -Paths (Join-Path 'Tests' 'Editor' 'SingleLineFixture.cs') *>&1
  $exit = $LASTEXITCODE
  Pop-Location
  $outStr = $out | Out-String
  $ok = ($exit -eq 0) -and ($outStr -notmatch 'cannot be found on this object')
  Write-TestResult "StrictMode.SingleLineFileDoesNotCrashLinter" $ok "Exit: $exit, Output: $outStr"
} catch {
  Pop-Location
  Write-TestResult "StrictMode.SingleLineFileDoesNotCrashLinter" $false "Exception: $_"
}

# ── Test 12: UNH004 string-literal bypass + long-form/qualified attributes ────
Write-Host "`n  Section: UNH004 method-name bypass resistance" -ForegroundColor White

# Case 12a: Trailing-comment stripper corruption via "//" in string literal
$caseSlashSlash = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;

    public sealed class CaseSlashSlash : CommonTestBase
    {
        [Test]
        [Category("http://example.com")]
        public void Url_Category_Underscore() { }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'CaseSlashSlash.cs' -FixtureContent $caseSlashSlash
$ok = ($r.ExitCode -ne 0) -and ($r.Output -match 'UNH004') -and ($r.Output -match 'Url_Category_Underscore')
Write-TestResult "UNH004.MethodName.DetectsWithStringContainingSlashSlash" $ok "Exit: $($r.ExitCode), Output: $($r.Output)"

# Case 12b: Open bracket inside string literal
$caseOpenBracket = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;

    public sealed class CaseOpenBracket : CommonTestBase
    {
        [TestCase("[bracket")]
        public void Open_Bracket_In_String(string s) { }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'CaseOpenBracket.cs' -FixtureContent $caseOpenBracket
$ok = ($r.ExitCode -ne 0) -and ($r.Output -match 'UNH004') -and ($r.Output -match 'Open_Bracket_In_String')
Write-TestResult "UNH004.MethodName.DetectsWithStringContainingOpenBracket" $ok "Exit: $($r.ExitCode), Output: $($r.Output)"

# Case 12c: Close bracket inside string literal
$caseCloseBracket = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;

    public sealed class CaseCloseBracket : CommonTestBase
    {
        [TestCase("bracket]")]
        public void Close_Bracket_In_String(string s) { }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'CaseCloseBracket.cs' -FixtureContent $caseCloseBracket
$ok = ($r.ExitCode -ne 0) -and ($r.Output -match 'UNH004') -and ($r.Output -match 'Close_Bracket_In_String')
Write-TestResult "UNH004.MethodName.DetectsWithStringContainingCloseBracket" $ok "Exit: $($r.ExitCode), Output: $($r.Output)"

# Case 12d: Open paren inside string literal
$caseOpenParen = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;

    public sealed class CaseOpenParen : CommonTestBase
    {
        [TestCase("(paren")]
        public void Open_Paren_In_String(string s) { }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'CaseOpenParen.cs' -FixtureContent $caseOpenParen
$ok = ($r.ExitCode -ne 0) -and ($r.Output -match 'UNH004') -and ($r.Output -match 'Open_Paren_In_String')
Write-TestResult "UNH004.MethodName.DetectsWithStringContainingOpenParen" $ok "Exit: $($r.ExitCode), Output: $($r.Output)"

# Case 12e: Close paren inside string literal
$caseCloseParen = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;

    public sealed class CaseCloseParen : CommonTestBase
    {
        [TestCase("paren)")]
        public void Close_Paren_In_String(string s) { }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'CaseCloseParen.cs' -FixtureContent $caseCloseParen
$ok = ($r.ExitCode -ne 0) -and ($r.Output -match 'UNH004') -and ($r.Output -match 'Close_Paren_In_String')
Write-TestResult "UNH004.MethodName.DetectsWithStringContainingCloseParen" $ok "Exit: $($r.ExitCode), Output: $($r.Output)"

# Case 12f: Long-form [TestAttribute]
$caseLongForm = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;

    public sealed class CaseLongForm : CommonTestBase
    {
        [TestAttribute]
        public void Long_Form_Test() { }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'CaseLongForm.cs' -FixtureContent $caseLongForm
$ok = ($r.ExitCode -ne 0) -and ($r.Output -match 'UNH004') -and ($r.Output -match 'Long_Form_Test')
Write-TestResult "UNH004.MethodName.DetectsLongFormTestAttribute" $ok "Exit: $($r.ExitCode), Output: $($r.Output)"

# Case 12g: Fully-qualified [NUnit.Framework.Test]
$caseQualified = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    public sealed class CaseQualified : CommonTestBase
    {
        [NUnit.Framework.Test]
        public void Qualified_Test() { }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'CaseQualified.cs' -FixtureContent $caseQualified
$ok = ($r.ExitCode -ne 0) -and ($r.Output -match 'UNH004') -and ($r.Output -match 'Qualified_Test')
Write-TestResult "UNH004.MethodName.DetectsQualifiedTestAttribute" $ok "Exit: $($r.ExitCode), Output: $($r.Output)"

# Case 12h: Fully-qualified [NUnit.Framework.TestCase(1, 2)]
$caseQualifiedTestCase = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    public sealed class CaseQualifiedTestCase : CommonTestBase
    {
        [NUnit.Framework.TestCase(1, 2)]
        public void Qualified_TestCase(int a, int b) { }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'CaseQualifiedTestCase.cs' -FixtureContent $caseQualifiedTestCase
$ok = ($r.ExitCode -ne 0) -and ($r.Output -match 'UNH004') -and ($r.Output -match 'Qualified_TestCase')
Write-TestResult "UNH004.MethodName.DetectsQualifiedTestCaseAttribute" $ok "Exit: $($r.ExitCode), Output: $($r.Output)"

# Case 12i: Character literal containing '['
$caseCharBracket = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;

    public sealed class CaseCharBracket : CommonTestBase
    {
        [TestCase('[')]
        public void Char_Bracket(char c) { }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'CaseCharBracket.cs' -FixtureContent $caseCharBracket
$ok = ($r.ExitCode -ne 0) -and ($r.Output -match 'UNH004') -and ($r.Output -match 'Char_Bracket')
Write-TestResult "UNH004.MethodName.DetectsCharLiteralBracket" $ok "Exit: $($r.ExitCode), Output: $($r.Output)"

# Case 12j: Inline comma form: "[Test, Category("Fast")] public void Foo()"
$caseInlineComma = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;

    public sealed class CaseInlineComma : CommonTestBase
    {
        [Test, Category("Fast")] public void Inline_Comma_Test() { }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'CaseInlineComma.cs' -FixtureContent $caseInlineComma
$ok = ($r.ExitCode -ne 0) -and ($r.Output -match 'UNH004') -and ($r.Output -match 'Inline_Comma_Test')
Write-TestResult "UNH004.MethodName.DetectsInlineCommaStackedAttribute" $ok "Exit: $($r.ExitCode), Output: $($r.Output)"

# Case 12k: Stacked inline attributes where [Test] is NOT first
$caseStackedInline = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;

    public sealed class CaseStackedInline : CommonTestBase
    {
        [Category("Fast")][Test] public void Stacked_Inline_Test() { }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'CaseStackedInline.cs' -FixtureContent $caseStackedInline
$ok = ($r.ExitCode -ne 0) -and ($r.Output -match 'UNH004') -and ($r.Output -match 'Stacked_Inline_Test')
Write-TestResult "UNH004.MethodName.DetectsStackedInlineAttribute" $ok "Exit: $($r.ExitCode), Output: $($r.Output)"

# Bug 1 regression: parameter-level [Test] must NOT be treated as a test-method
# attribute. C# allows parameter attributes like "void Foo(int x, [Test] int y)"
# — the method is not a test, so UNH004 must not fire. The anywhere pattern
# would otherwise match the "[Test]" inside the parameter list.
$caseParamAttr = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;

    public sealed class CaseParamAttr : CommonTestBase
    {
        public void Not_A_Test(int x, [Test] int y) { }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'CaseParamAttr.cs' -FixtureContent $caseParamAttr
$ok = ($r.ExitCode -eq 0) -and ($r.Output -notmatch 'UNH004')
Write-TestResult "UNH004.MethodName.DoesNotFlagMethodWithParameterAttribute" $ok "Exit: $($r.ExitCode), Output: $($r.Output)"

# Bug 2 regression: stacked attributes on a single line ABOVE the signature
# where [Test] is NOT first. The walker previously used only the anchored
# pattern on the reconstructed attribute block, so "[Category(\"Fast\")][Test]"
# above the signature slipped through. Must now be flagged.
$caseStackedNonInline = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;

    public sealed class CaseStackedNonInline : CommonTestBase
    {
        [Category("Fast")][Test]
        public void stacked_non_inline_bad_name() { }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'CaseStackedNonInline.cs' -FixtureContent $caseStackedNonInline
$ok = ($r.ExitCode -ne 0) -and ($r.Output -match 'UNH004') -and ($r.Output -match 'stacked_non_inline_bad_name')
Write-TestResult "UNH004.MethodName.DetectsStackedNonInlineTestAttribute" $ok "Exit: $($r.ExitCode), Output: $($r.Output)"

# Case 12l: global::-qualified attribute
$caseGlobalQualified = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    public sealed class CaseGlobalQualified : CommonTestBase
    {
        [global::NUnit.Framework.Test] public void Qualified_Global_Test() { }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'CaseGlobalQualified.cs' -FixtureContent $caseGlobalQualified
$ok = ($r.ExitCode -ne 0) -and ($r.Output -match 'UNH004') -and ($r.Output -match 'Qualified_Global_Test')
Write-TestResult "UNH004.MethodName.DetectsGlobalQualifiedAttribute" $ok "Exit: $($r.ExitCode), Output: $($r.Output)"

# Case 12m: global::-qualified TestCase attribute
$caseGlobalQualifiedTestCase = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    public sealed class CaseGlobalQualifiedTestCase : CommonTestBase
    {
        [global::NUnit.Framework.TestCase(1)] public void Global_Inline_TestCase_Name(int a) { }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'CaseGlobalQualifiedTestCase.cs' -FixtureContent $caseGlobalQualifiedTestCase
$ok = ($r.ExitCode -ne 0) -and ($r.Output -match 'UNH004') -and ($r.Output -match 'Global_Inline_TestCase_Name')
Write-TestResult "UNH004.MethodName.DetectsGlobalQualifiedInlineTestCase" $ok "Exit: $($r.ExitCode), Output: $($r.Output)"

# ── Test 13: Multi-line /* */ block comment is now fully masked ──────────────
# Round 6 migration: lint-tests.ps1 now dot-sources comment-stripping.ps1,
# so multi-line block comments are correctly masked across line boundaries.
#
# Load-bearing: under the pre-migration scrubber, per-line scrubbing could
# not track "inside a block comment" state across physical lines, so the
# body of a multi-line `/* ... */` block comment was visible to downstream
# regex matching. A comment containing `Object.Destroy(x)` would trip the
# UNH001 destroy pattern even though the call is commented out. Under the
# fix, the helper masks the full comment span and the linter does NOT
# flag the commented-out Destroy call.
Write-Host "`n  Section: Multi-line block comment masking (helper migration)" -ForegroundColor White

$caseMultiLineBlock = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;
    using UnityEngine;

    public sealed class CaseMultiLineBlock : CommonTestBase
    {
        [Test]
        public void MyTest()
        {
            var go = Track(new GameObject("test"));
            /*
             * Historical note: we used to destroy directly here.
             * Object.DestroyImmediate(go);
             * Do NOT re-introduce — teardown via Track handles it.
             */
            Assert.IsTrue(go != null);
        }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'CaseMultiLineBlock.cs' -FixtureContent $caseMultiLineBlock
$ok = ($r.ExitCode -eq 0) -and ($r.Output -notmatch 'UNH001')
Write-TestResult "MultiLineBlockComment.DoesNotFlagCommentedOutDestroy" $ok "Exit: $($r.ExitCode), Output: $($r.Output)"

# ── Test 14: UNH006 UnityTest TestCaseSource return metadata ─────────────────
Write-Host "`n  Section: UNH006 UnityTest TestCaseSource return metadata" -ForegroundColor White

$caseUnityTestMissingReturnsNull = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine.TestTools;

    public sealed class CaseUnityTestMissingReturnsNull : CommonTestBase
    {
        private static IEnumerable<TestCaseData> MissingReturnsNullCases()
        {
            yield return new TestCaseData(250).SetName("Extreme.DrawLoop.TwoHundredFiftyIterations.Stable");
            yield return new TestCaseData(1000).Returns(null).SetName("Extreme.DrawLoop.ThousandIterations.Stable");
        }

        [UnityTest]
        [TestCaseSource(nameof(MissingReturnsNullCases))]
        public IEnumerator ParameterizedCoroutineTest(int count)
        {
            yield return null;
        }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'CaseUnityTestMissingReturnsNull.cs' -FixtureContent $caseUnityTestMissingReturnsNull
$ok = ($r.ExitCode -ne 0) -and ($r.Output -match 'UNH006') -and ($r.Output -match 'MissingReturnsNullCases')
Write-TestResult "UNH006.DetectsMissingReturnsNullForUnityTestCaseSource" $ok "Exit: $($r.ExitCode), Output: $($r.Output)"

$caseUnityTestWithReturnsNull = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine.TestTools;

    public sealed class CaseUnityTestWithReturnsNull : CommonTestBase
    {
        private static IEnumerable<TestCaseData> WithReturnsNullCases()
        {
            yield return new TestCaseData(250).Returns(null).SetName("Extreme.DrawLoop.TwoHundredFiftyIterations.Stable");
            yield return new TestCaseData(1000).Returns(null).SetName("Extreme.DrawLoop.ThousandIterations.Stable");
        }

        [UnityTest]
        [TestCaseSource(nameof(WithReturnsNullCases))]
        public IEnumerator ParameterizedCoroutineTest(int count)
        {
            yield return null;
        }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'CaseUnityTestWithReturnsNull.cs' -FixtureContent $caseUnityTestWithReturnsNull
$ok = ($r.ExitCode -eq 0) -and ($r.Output -notmatch 'UNH006')
Write-TestResult "UNH006.AllowsReturnsNullForUnityTestCaseSource" $ok "Exit: $($r.ExitCode), Output: $($r.Output)"

$caseSynchronousTestCaseSourceWithoutReturnsNull = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using System.Collections.Generic;
    using NUnit.Framework;

    public sealed class CaseSynchronousTestCaseSourceWithoutReturnsNull : CommonTestBase
    {
        private static IEnumerable<TestCaseData> SynchronousCases()
        {
            yield return new TestCaseData(250).SetName("Extreme.DrawLoop.TwoHundredFiftyIterations.Stable");
        }

        [Test]
        [TestCaseSource(nameof(SynchronousCases))]
        public void SynchronousTest(int count)
        {
            Assert.IsTrue(count > 0);
        }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'CaseSynchronousTestCaseSourceWithoutReturnsNull.cs' -FixtureContent $caseSynchronousTestCaseSourceWithoutReturnsNull
$ok = ($r.ExitCode -eq 0) -and ($r.Output -notmatch 'UNH006')
Write-TestResult "UNH006.DoesNotRequireReturnsNullForSynchronousTestCaseSource" $ok "Exit: $($r.ExitCode), Output: $($r.Output)"

$caseUnityTestReversedAttributeOrder = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine.TestTools;

    public sealed class CaseUnityTestReversedAttributeOrder : CommonTestBase
    {
        private static IEnumerable<TestCaseData> ReversedAttributeOrderCases()
        {
            yield return new TestCaseData(250).SetName("Extreme.DrawLoop.TwoHundredFiftyIterations.Stable");
        }

        [TestCaseSource(nameof(ReversedAttributeOrderCases))]
        [UnityTest]
        public IEnumerator ParameterizedCoroutineTest(int count)
        {
            yield return null;
        }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'CaseUnityTestReversedAttributeOrder.cs' -FixtureContent $caseUnityTestReversedAttributeOrder
$ok = ($r.ExitCode -ne 0) -and ($r.Output -match 'UNH006') -and ($r.Output -match 'ReversedAttributeOrderCases')
Write-TestResult "UNH006.DetectsMissingReturnsNullWithReversedAttributeOrder" $ok "Exit: $($r.ExitCode), Output: $($r.Output)"

$caseUnityTestArraySource = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using System.Collections;
    using NUnit.Framework;
    using UnityEngine.TestTools;

    public sealed class CaseUnityTestArraySource : CommonTestBase
    {
        private static readonly TestCaseData[] ArrayCases =
        {
            new TestCaseData(250).SetName("Extreme.DrawLoop.TwoHundredFiftyIterations.Stable"),
            new TestCaseData(1000).Returns(null).SetName("Extreme.DrawLoop.ThousandIterations.Stable"),
        };

        [UnityTest]
        [TestCaseSource(nameof(ArrayCases))]
        public IEnumerator ParameterizedCoroutineTest(int count)
        {
            yield return null;
        }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'CaseUnityTestArraySource.cs' -FixtureContent $caseUnityTestArraySource
$ok = ($r.ExitCode -ne 0) -and ($r.Output -match 'UNH006') -and ($r.Output -match 'ArrayCases')
Write-TestResult "UNH006.DetectsMissingReturnsNullForArraySource" $ok "Exit: $($r.ExitCode), Output: $($r.Output)"

$caseUnityTestLongFormAttribute = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine.TestTools;

    public sealed class CaseUnityTestLongFormAttribute : CommonTestBase
    {
        private static IEnumerable<TestCaseData> LongFormAttributeCases()
        {
            yield return new TestCaseData(250).SetName("Extreme.DrawLoop.TwoHundredFiftyIterations.Stable");
        }

        [UnityTestAttribute]
        [TestCaseSourceAttribute(nameof(LongFormAttributeCases))]
        public IEnumerator ParameterizedCoroutineTest(int count)
        {
            yield return null;
        }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'CaseUnityTestLongFormAttribute.cs' -FixtureContent $caseUnityTestLongFormAttribute
$ok = ($r.ExitCode -ne 0) -and ($r.Output -match 'UNH006') -and ($r.Output -match 'LongFormAttributeCases')
Write-TestResult "UNH006.DetectsMissingReturnsNullForLongFormAttributes" $ok "Exit: $($r.ExitCode), Output: $($r.Output)"

$caseUnityTestWrappedSourceAttribute = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine.TestTools;

    public sealed class CaseUnityTestWrappedSourceAttribute : CommonTestBase
    {
        private static IEnumerable<TestCaseData> WrappedSourceAttributeCases()
        {
            yield return new TestCaseData(250).SetName("Extreme.DrawLoop.TwoHundredFiftyIterations.Stable");
        }

        [UnityTest]
        [TestCaseSource(
            nameof(WrappedSourceAttributeCases)
        )]
        public IEnumerator ParameterizedCoroutineTest(int count)
        {
            yield return null;
        }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'CaseUnityTestWrappedSourceAttribute.cs' -FixtureContent $caseUnityTestWrappedSourceAttribute
$ok = ($r.ExitCode -ne 0) -and ($r.Output -match 'UNH006') -and ($r.Output -match 'WrappedSourceAttributeCases')
Write-TestResult "UNH006.DetectsMissingReturnsNullForWrappedSourceAttribute" $ok "Exit: $($r.ExitCode), Output: $($r.Output)"

$caseNearbyUnityTestDoesNotBleedIntoNonUnityCoroutine = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine.TestTools;

    public sealed class CaseNearbyUnityTestDoesNotBleedIntoNonUnityCoroutine : CommonTestBase
    {
        private static IEnumerable<TestCaseData> NonUnityCoroutineCases()
        {
            yield return new TestCaseData(250).SetName("Extreme.DrawLoop.TwoHundredFiftyIterations.Stable");
        }

        [UnityTest]
        public IEnumerator PlainUnityCoroutine()
        {
            yield return null;
        }

        [TestCaseSource(nameof(NonUnityCoroutineCases))]
        public IEnumerator NonUnityParameterizedCoroutineTest(int count)
        {
            yield return null;
        }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'CaseNearbyUnityTestDoesNotBleedIntoNonUnityCoroutine.cs' -FixtureContent $caseNearbyUnityTestDoesNotBleedIntoNonUnityCoroutine
$ok = ($r.ExitCode -eq 0) -and ($r.Output -notmatch 'UNH006')
Write-TestResult "UNH006.DoesNotUseNearbyUnityTestAttribute" $ok "Exit: $($r.ExitCode), Output: $($r.Output)"

# ── Test: UNH007 (giant literal loop bound in a non-perf test) ────────────────
Write-Host "`n  Section: UNH007 detection" -ForegroundColor White

$unh007Pos = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;

    public sealed class BigLoopTest : CommonTestBase
    {
        [Test]
        public void Loop()
        {
            int sum = 0;
            for (int i = 0; i < 60000; i++)
            {
                sum += i;
            }
            Assert.IsTrue(sum >= 0);
        }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'BigLoopTest.cs' -FixtureContent $unh007Pos
Write-TestResult "UNH007.DetectsGiantLoop" (($r.ExitCode -ne 0) -and ($r.Output -match 'UNH007')) "Exit: $($r.ExitCode), Output: $($r.Output)"

$unh007Neg = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;

    [Category("Stress")]
    public sealed class BigLoopStressTest : CommonTestBase
    {
        [Test]
        public void Loop()
        {
            int sum = 0;
            for (int i = 0; i < 60000; i++)
            {
                sum += i;
            }
            Assert.IsTrue(sum >= 0);
        }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'BigLoopStressTest.cs' -FixtureContent $unh007Neg
Write-TestResult "UNH007.AllowsGiantLoopInStressFixture" (($r.ExitCode -eq 0) -and ($r.Output -notmatch 'UNH007')) "Exit: $($r.ExitCode), Output: $($r.Output)"

# ── Test: UNH008 (perf-named fixture must carry Performance/Stress category) ──
Write-Host "`n  Section: UNH008 detection" -ForegroundColor White

$unh008Pos = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;

    public sealed class WidgetPerformanceTests : CommonTestBase
    {
        [Test]
        public void Bench()
        {
            Assert.IsTrue(true);
        }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'WidgetPerformanceTests.cs' -FixtureContent $unh008Pos
Write-TestResult "UNH008.DetectsUntaggedPerfFixture" (($r.ExitCode -ne 0) -and ($r.Output -match 'UNH008')) "Exit: $($r.ExitCode), Output: $($r.Output)"

$unh008Neg = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;

    [Category("Performance")]
    public sealed class WidgetPerformanceTaggedTests : CommonTestBase
    {
        [Test]
        public void Bench()
        {
            Assert.IsTrue(true);
        }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'WidgetPerformanceTaggedTests.cs' -FixtureContent $unh008Neg
Write-TestResult "UNH008.AllowsTaggedPerfFixture" (($r.ExitCode -eq 0) -and ($r.Output -notmatch 'UNH008')) "Exit: $($r.ExitCode), Output: $($r.Output)"

# Fully-qualified [NUnit.Framework.Category("Performance")] must also satisfy the rule.
$unh008NegFq = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;

    [NUnit.Framework.Category("Performance")]
    public sealed class WidgetPerformanceFqTests : CommonTestBase
    {
        [Test]
        public void Bench()
        {
            Assert.IsTrue(true);
        }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'WidgetPerformanceFqTests.cs' -FixtureContent $unh008NegFq
Write-TestResult "UNH008.AllowsFullyQualifiedCategory" (($r.ExitCode -eq 0) -and ($r.Output -notmatch 'UNH008')) "Exit: $($r.ExitCode), Output: $($r.Output)"

# ── Test: UNH009 (advisory, non-blocking AssetDatabase churn) ────────────────
Write-Host "`n  Section: UNH009 advisory" -ForegroundColor White

$unh009Pos = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;
    using UnityEditor;

    public sealed class RefreshTest : CommonTestBase
    {
        [Test]
        public void DoRefresh()
        {
            AssetDatabase.Refresh();
        }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'RefreshTest.cs' -FixtureContent $unh009Pos
# UNH009 is ADVISORY: it must appear in output but MUST NOT fail the build.
Write-TestResult "UNH009.AdvisoryNonBlocking" (($r.ExitCode -eq 0) -and ($r.Output -match 'UNH009')) "Exit: $($r.ExitCode), Output: $($r.Output)"

$unh009Neg = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using NUnit.Framework;
    using UnityEditor;

    public sealed class RefreshBatchedTest : BatchedEditorTestBase
    {
        [Test]
        public void DoRefresh()
        {
            AssetDatabase.Refresh();
        }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'RefreshBatchedTest.cs' -FixtureContent $unh009Neg
Write-TestResult "UNH009.SkipsBatchedBase" (($r.ExitCode -eq 0) -and ($r.Output -notmatch 'UNH009')) "Exit: $($r.ExitCode), Output: $($r.Output)"

# ── Test: UNH010 (advisory, non-blocking real-time waits) ────────────────────
Write-Host "`n  Section: UNH010 advisory" -ForegroundColor White

$unh010Wait = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using System.Collections;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class WaitFixture : CommonTestBase
    {
        [UnityTest]
        public IEnumerator Waits()
        {
            yield return new WaitForSeconds(0.5f);
        }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'WaitFixture.cs' -FixtureContent $unh010Wait
Write-TestResult "UNH010.FlagsWaitForSeconds" (($r.ExitCode -eq 0) -and ($r.Output -match 'UNH010')) "Exit: $($r.ExitCode), Output: $($r.Output)"

$unh010Delay = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using System.Threading.Tasks;
    using NUnit.Framework;

    public sealed class DelayFixture : CommonTestBase
    {
        [Test]
        public async Task Delays()
        {
            await Task.Delay(50);
        }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'DelayFixture.cs' -FixtureContent $unh010Delay
Write-TestResult "UNH010.FlagsTaskDelayLiteral" (($r.ExitCode -eq 0) -and ($r.Output -match 'UNH010')) "Exit: $($r.ExitCode), Output: $($r.Output)"

# Cancellation fodder: Task.Delay(n, ct) is NOT a blocking wait and must NOT flag.
$unh010Cancel = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;

    public sealed class CancelFixture : CommonTestBase
    {
        [Test]
        public async Task Cancels()
        {
            CancellationToken cancellationToken = default;
            await Task.Delay(5000, cancellationToken);
        }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'CancelFixture.cs' -FixtureContent $unh010Cancel
Write-TestResult "UNH010.SkipsCancellationDelay" (($r.ExitCode -eq 0) -and ($r.Output -notmatch 'UNH010')) "Exit: $($r.ExitCode), Output: $($r.Output)"

# UNH-SUPPRESS on the wait line suppresses the advisory.
$unh010Suppress = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using System.Threading;
    using NUnit.Framework;

    public sealed class SleepFixture : CommonTestBase
    {
        [Test]
        public void Sleeps()
        {
            Thread.Sleep(100); // UNH-SUPPRESS: intentional poll yield
        }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'SleepFixture.cs' -FixtureContent $unh010Suppress
Write-TestResult "UNH010.HonorsSuppress" (($r.ExitCode -eq 0) -and ($r.Output -notmatch 'UNH010')) "Exit: $($r.ExitCode), Output: $($r.Output)"

# Performance-category fixtures may use real-time waits (excluded from main matrix).
$unh010Perf = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using System.Collections;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    [Category("Performance")]
    public sealed class WaitPerfFixture : CommonTestBase
    {
        [UnityTest]
        public IEnumerator Waits()
        {
            yield return new WaitForSeconds(0.5f);
        }
    }
}
'@
$r = Invoke-LintOnFixture -FixtureRelativePath 'WaitPerfFixture.cs' -FixtureContent $unh010Perf
Write-TestResult "UNH010.SkipsPerfCategory" (($r.ExitCode -eq 0) -and ($r.Output -notmatch 'UNH010')) "Exit: $($r.ExitCode), Output: $($r.Output)"

} finally {
  # ── Cleanup ──────────────────────────────────────────────────────────────────
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
