Param(
  [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:TestsPassed = 0
$script:TestsFailed = 0
$script:FailedTests = @()

function Write-Info($msg) {
  if ($VerboseOutput) {
    Write-Host "[test-lint-duplicate-usings] $msg" -ForegroundColor Cyan
  }
}

function Write-TestResult {
  param(
    [string]$TestName,
    [bool]$Passed,
    [string]$Message = ''
  )

  if ($Passed) {
    Write-Host "  [PASS] $TestName" -ForegroundColor Green
    $script:TestsPassed++
  }
  else {
    Write-Host "  [FAIL] $TestName" -ForegroundColor Red
    if (-not [string]::IsNullOrWhiteSpace($Message)) {
      Write-Host "         $Message" -ForegroundColor Yellow
    }
    $script:TestsFailed++
    $script:FailedTests += $TestName
  }
}

function Invoke-LinterForContent {
  param(
    [Parameter(Mandatory = $true)]
    [AllowEmptyString()]
    [string]$Content
  )

  $tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("lint-duplicate-usings-" + [System.Guid]::NewGuid().ToString('N'))
  New-Item -ItemType Directory -Path $tempDir | Out-Null

  $fixturePath = Join-Path $tempDir 'Fixture.cs'
  Set-Content -Path $fixturePath -Value $Content -NoNewline

  $linterPath = Join-Path $PSScriptRoot '..' 'lint-duplicate-usings.ps1'

  try {
    $output = & pwsh -NoProfile -File $linterPath -Paths $fixturePath 2>&1
    $exitCode = $LASTEXITCODE

    return @{
      ExitCode = $exitCode
      Output = $output | Out-String
    }
  }
  finally {
    Remove-Item -Recurse -Force $tempDir -ErrorAction SilentlyContinue
  }
}

Write-Host ''
Write-Host '========================================' -ForegroundColor White
Write-Host 'Duplicate Using Linter Tests' -ForegroundColor White
Write-Host '========================================' -ForegroundColor White

Write-Host ''
Write-Host '  Section: Duplicate detection' -ForegroundColor White

$duplicateDirectiveContent = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using System;
    using System;

    internal sealed class Sample
    {
    }
}
'@

$result = Invoke-LinterForContent -Content $duplicateDirectiveContent
$hasUNH007 = $result.Output -match 'UNH007'
Write-TestResult 'UNH007.DetectsDuplicateImport' (($result.ExitCode -ne 0) -and $hasUNH007) "Exit: $($result.ExitCode), Output: $($result.Output)"

$duplicateAliasContent = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using IO = System.IO;
    using IO = System.IO;

    internal sealed class Sample
    {
    }
}
'@

$result = Invoke-LinterForContent -Content $duplicateAliasContent
$hasUNH007 = $result.Output -match 'UNH007'
Write-TestResult 'UNH007.DetectsDuplicateAlias' (($result.ExitCode -ne 0) -and $hasUNH007) "Exit: $($result.ExitCode), Output: $($result.Output)"

Write-Host ''
Write-Host '  Section: False-positive resistance' -ForegroundColor White

$localUsingDeclarationContent = @'
namespace WallstopStudios.UnityHelpers.Tests
{
    using System;
    using System.IO;

    internal sealed class Sample
    {
        public void Execute()
        {
            using MemoryStream stream = new();
            stream.WriteByte(1);
        }
    }
}
'@

$result = Invoke-LinterForContent -Content $localUsingDeclarationContent
Write-TestResult 'LocalUsingDeclaration.IsIgnored' ($result.ExitCode -eq 0) "Exit: $($result.ExitCode), Output: $($result.Output)"

$crossNamespaceContent = @'
namespace A
{
    using System;

    internal sealed class First
    {
    }
}

namespace B
{
    using System;

    internal sealed class Second
    {
    }
}
'@

$result = Invoke-LinterForContent -Content $crossNamespaceContent
Write-TestResult 'SameImportAcrossNamespaces.IsAllowed' ($result.ExitCode -eq 0) "Exit: $($result.ExitCode), Output: $($result.Output)"

$fileScopedContent = @'
namespace WallstopStudios.UnityHelpers.Tests;

using System;
using System;

internal sealed class FileScopedSample
{
}
'@

$result = Invoke-LinterForContent -Content $fileScopedContent
$hasUNH007 = $result.Output -match 'UNH007'
Write-TestResult 'UNH007.DetectsFileScopedDuplicate' (($result.ExitCode -ne 0) -and $hasUNH007) "Exit: $($result.ExitCode), Output: $($result.Output)"

$nestedNamespaceContent = @'
namespace Outer
{
  namespace Inner
  {
    using System;
    using System;

    internal sealed class NestedSample
    {
    }
  }
}
'@

$result = Invoke-LinterForContent -Content $nestedNamespaceContent
$hasUNH007 = $result.Output -match 'UNH007'
Write-TestResult 'UNH007.DetectsNestedNamespaceDuplicate' (($result.ExitCode -ne 0) -and $hasUNH007) "Exit: $($result.ExitCode), Output: $($result.Output)"

$braceInStringContent = @'
namespace First
{
  using System;

  internal sealed class FirstSample
  {
    public void Execute()
    {
      string format = "{0}";
      Console.WriteLine(format);
    }
  }
}

namespace Second
{
  using System;
  using System;

  internal sealed class SecondSample
  {
  }
}
'@

$result = Invoke-LinterForContent -Content $braceInStringContent
$hasUNH007 = $result.Output -match 'UNH007'
Write-TestResult 'UNH007.BraceInStringDoesNotCorruptScopeTracking' (($result.ExitCode -ne 0) -and $hasUNH007) "Exit: $($result.ExitCode), Output: $($result.Output)"

Write-Host ''
Write-Host '  Section: Degenerate files' -ForegroundColor White

# The reads here are [IO.File]::ReadAllLines rather than Get-Content, which are not the same
# function on a file with no content: Get-Content answers $null and ReadAllLines answers an empty
# array. A linter that indexed the result would throw on one and not the other, so the degenerate
# shapes are asserted rather than assumed.
$emptyResult = Invoke-LinterForContent -Content ''
Write-TestResult 'UNH007.AnEmptyFileIsClean' ($emptyResult.ExitCode -eq 0) "Exit: $($emptyResult.ExitCode), Output: $($emptyResult.Output)"

$singleLineResult = Invoke-LinterForContent -Content 'using System;'
Write-TestResult 'UNH007.ASingleLineFileIsClean' ($singleLineResult.ExitCode -eq 0) "Exit: $($singleLineResult.ExitCode), Output: $($singleLineResult.Output)"

Write-Host ''
Write-Host '========================================' -ForegroundColor White
Write-Host 'Test Summary' -ForegroundColor White
Write-Host '========================================' -ForegroundColor White
Write-Host ''

$totalTests = $script:TestsPassed + $script:TestsFailed

if ($script:TestsFailed -eq 0) {
  Write-Host "All $totalTests tests passed!" -ForegroundColor Green
  exit 0
}

Write-Host "Passed: $($script:TestsPassed) / $totalTests" -ForegroundColor Yellow
Write-Host "Failed: $($script:TestsFailed) / $totalTests" -ForegroundColor Red
Write-Host ''
Write-Host 'Failed tests:' -ForegroundColor Red
foreach ($testName in $script:FailedTests) {
  Write-Host "  - $testName" -ForegroundColor Red
}

exit 1
