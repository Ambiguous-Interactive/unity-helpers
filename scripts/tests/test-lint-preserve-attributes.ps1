Param(
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

# The linter pairs a reflective read with a declaration, so a fixture needs both. The read lives in
# one file and the attribute in another, exactly as it does in the package.
function Invoke-LinterForDeclaration {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Declaration
  )

  $tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("lint-preserve-attributes-" + [System.Guid]::NewGuid().ToString('N'))
  New-Item -ItemType Directory -Path $tempDir | Out-Null

  $reader = @'
namespace Fixture
{
    using System;

    public static class Reader
    {
        public static object Read(Type type)
        {
            return Attribute.GetCustomAttribute(type, typeof(FixtureAttribute));
        }
    }
}
'@

  Set-Content -Path (Join-Path $tempDir 'Reader.cs') -Value $reader -NoNewline
  Set-Content -Path (Join-Path $tempDir 'FixtureAttribute.cs') -Value $Declaration -NoNewline

  $linterPath = Join-Path $PSScriptRoot '..' 'lint-preserve-attributes.js'

  try {
    $previous = $env:PRESERVE_ATTRIBUTES_ROOT
    $env:PRESERVE_ATTRIBUTES_ROOT = $tempDir
    $output = & node $linterPath 2>&1
    $exitCode = $LASTEXITCODE

    return @{
      ExitCode = $exitCode
      Output   = $output | Out-String
    }
  }
  finally {
    $env:PRESERVE_ATTRIBUTES_ROOT = $previous
    Remove-Item -Recurse -Force $tempDir -ErrorAction SilentlyContinue
  }
}

Write-Host ''
Write-Host '========================================' -ForegroundColor White
Write-Host 'Preserve-Attributes Linter Tests' -ForegroundColor White
Write-Host '========================================' -ForegroundColor White
Write-Host ''

# A missing [Preserve] is the whole point of the check, so it is asserted first.
$missing = @'
namespace Fixture
{
    using System;

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class FixtureAttribute : Attribute { }
}
'@

$result = Invoke-LinterForDeclaration -Declaration $missing
Write-TestResult -TestName 'A reflected attribute with no [Preserve] fails' -Passed ($result.ExitCode -eq 1) -Message $result.Output

$adjacent = @'
namespace Fixture
{
    using System;
    using UnityEngine.Scripting;

    [Preserve]
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class FixtureAttribute : Attribute { }
}
'@

$result = Invoke-LinterForDeclaration -Declaration $adjacent
Write-TestResult -TestName '[Preserve] directly above the class passes' -Passed ($result.ExitCode -eq 0) -Message $result.Output

# The two shapes that made this check report a missing [Preserve] on a declaration carrying one.
$separatedByComment = @'
namespace Fixture
{
    using System;
    using UnityEngine.Scripting;

    [Preserve]
    // A comment explaining the targets below, which is ordinary C# and must not hide [Preserve].
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class FixtureAttribute : Attribute { }
}
'@

$result = Invoke-LinterForDeclaration -Declaration $separatedByComment
Write-TestResult -TestName '[Preserve] separated from the class by a comment passes' -Passed ($result.ExitCode -eq 0) -Message $result.Output

$wrappedAttribute = @'
namespace Fixture
{
    using System;
    using UnityEngine.Scripting;

    [Preserve]
    [AttributeUsage(
        AttributeTargets.Class | AttributeTargets.Field,
        AllowMultiple = false,
        Inherited = true
    )]
    public sealed class FixtureAttribute : Attribute { }
}
'@

$result = Invoke-LinterForDeclaration -Declaration $wrappedAttribute
Write-TestResult -TestName '[Preserve] above a csharpier-wrapped attribute passes' -Passed ($result.ExitCode -eq 0) -Message $result.Output

$commentAndWrapped = @'
namespace Fixture
{
    using System;
    using UnityEngine.Scripting;

    /// <summary>Documented, preserved, and wrapped all at once.</summary>
    [Preserve]
    // Why the targets are what they are.
    [AttributeUsage(
        AttributeTargets.Class,
        AllowMultiple = false
    )]
    public sealed class FixtureAttribute : Attribute { }
}
'@

$result = Invoke-LinterForDeclaration -Declaration $commentAndWrapped
Write-TestResult -TestName 'Doc comment, [Preserve], comment and a wrapped attribute together pass' -Passed ($result.ExitCode -eq 0) -Message $result.Output

# A [Preserve] that belongs to something else must not be borrowed by the declaration below it.
$preserveOnAnotherMember = @'
namespace Fixture
{
    using System;
    using UnityEngine.Scripting;

    public sealed class Unrelated
    {
        [Preserve]
        public int Kept;
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class FixtureAttribute : Attribute { }
}
'@

$result = Invoke-LinterForDeclaration -Declaration $preserveOnAnotherMember
Write-TestResult -TestName 'A [Preserve] on an unrelated member is not borrowed' -Passed ($result.ExitCode -eq 1) -Message $result.Output

Write-Host ''
Write-Host '========================================' -ForegroundColor White
Write-Host "Passed: $script:TestsPassed  Failed: $script:TestsFailed" -ForegroundColor White
Write-Host '========================================' -ForegroundColor White

if ($script:TestsFailed -gt 0) {
  foreach ($name in $script:FailedTests) {
    Write-Host "  - $name" -ForegroundColor Red
  }
  exit 1
}

exit 0
