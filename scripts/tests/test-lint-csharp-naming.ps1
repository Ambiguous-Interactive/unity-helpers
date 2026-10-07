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
    # All eight source roots, not just the one the cases write into: the linter now refuses a
    # missing root rather than skipping it, so a fixture repo that lacks Editor/, Tests/, or Generator~/ is not
    # a repository shape it should accept (#556).
    New-Item -ItemType Directory -Path (Join-Path $root 'Runtime') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $root 'Editor') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $root 'Tests') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $root 'Generator~') -Force | Out-Null
    foreach ($sourceRoot in @('Samples~', 'Styles', 'URP', 'Shaders')) {
        New-Item -ItemType Directory -Path (Join-Path $root $sourceRoot) -Force | Out-Null
    }
    # The lint script dot-sources BOTH git-staging-helpers AND comment-stripping
    # from $PSScriptRoot — every dependency must be staged into the fixture
    # `scripts/` directory or the script throws on dot-source.
    Copy-Item -LiteralPath $lintScriptPath   -Destination (Join-Path $root 'scripts/lint-csharp-naming.ps1')
    Copy-Item -LiteralPath $helperScriptPath -Destination (Join-Path $root 'scripts/comment-stripping.ps1')
    Copy-Item -LiteralPath $gitHelpersPath   -Destination (Join-Path $root 'scripts/git-staging-helpers.ps1')
    # Every fixture starts with all twenty valid frozen wire declarations.
    Add-FixtureFile $root 'Editor/CustomDrawers/PendingValueWrapper.cs' 'namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers { class PendingValueWrapper { [UnityEngine.SerializeReference] private object boxedValue; } }'
    Add-FixtureFile $root 'Runtime/Core/Helper/UnityMainThreadDispatcher.cs' 'namespace WallstopStudios.UnityHelpers.Core.Helper { class UnityMainThreadDispatcher { [UnityEngine.SerializeField] private int maxPendingActions; } }'
    Add-FixtureFile $root 'Samples~/Logging - Tag Formatter/Scripts/LoggingDemoController.cs' 'namespace Samples.UnityHelpers.Logging { class LoggingDemoController { [UnityEngine.SerializeField] private int logOnStart; [UnityEngine.SerializeField] private int startMuted; [UnityEngine.SerializeField] private int pretty; [UnityEngine.SerializeField] private int npcCallsign; [UnityEngine.SerializeField] private int statusLabel; [UnityEngine.SerializeField] private int reportMessage; [UnityEngine.SerializeField] private int sectorRange; } }'
    Add-FixtureFile $root 'Samples~/Random - PRNG/Scripts/RandomPrngDemo.cs' 'namespace Samples.UnityHelpers.Random.Prng { class RandomPrngDemo { [UnityEngine.SerializeField] private int seed; } }'
    Add-FixtureFile $root 'Samples~/Spatial Structures - 2D and 3D/Scripts/HullUsageDemo.cs' 'namespace Samples.UnityHelpers.SpatialStructures { class HullUsageDemo { [UnityEngine.SerializeField] private int gridlessBounds; [UnityEngine.SerializeField] private int gridlessEdgeSamplesPerSide; [UnityEngine.SerializeField] private int grid; [UnityEngine.SerializeField] private int gridFootprint; [UnityEngine.SerializeField] private int gridHullNeighbors; } }'
    Add-FixtureFile $root 'Samples~/Spatial Structures - 2D and 3D/Scripts/SpatialStructuresDemo.cs' 'namespace Samples.UnityHelpers.SpatialStructures { class SpatialStructuresDemo { [UnityEngine.SerializeField] private int pointCount; [UnityEngine.SerializeField] private int areaSize; [UnityEngine.SerializeField] private int queryRadius; } }'
    Add-FixtureFile $root 'Samples~/UGUI - EnhancedImage/Scripts/EnhancedImageDemo.cs' 'namespace Samples.UnityHelpers.UGUI.EnhancedImage { class EnhancedImageDemo { [UnityEngine.SerializeField] private int materialTemplate; } }'
    Add-FixtureFile $root 'Tests/Editor/TestTypes/PrivateCtorSetHost.cs' 'namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes { class PrivateCtorElement { [UnityEngine.SerializeField] private int magnitude; } }'
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
    param([string]$FixtureRoot, [switch]$Cli, [switch]$StagedOnly, [string]$WorkingDirectory)
    $lintCopy = Join-Path $FixtureRoot 'scripts/lint-csharp-naming.ps1'
    $location = if ($WorkingDirectory) { $WorkingDirectory } else { $FixtureRoot }
    if ($Cli) {
        Push-Location $location
        try {
            $arguments = @('-NoProfile', '-File', $lintCopy)
            if ($StagedOnly) { $arguments += '-StagedOnly' }
            $output = & pwsh @arguments *>&1
            return @{ ExitCode = $LASTEXITCODE; Output = ($output | Out-String) }
        } finally {
            Pop-Location
        }
    }

    $runner = [System.Management.Automation.PowerShell]::Create()
    try {
        $null = $runner.AddCommand('Set-Location').AddParameter('LiteralPath', $location)
        $null = $runner.AddStatement().AddCommand($lintCopy)
        if ($StagedOnly) { $null = $runner.AddParameter('StagedOnly') }
        $result = $runner.Invoke()
        $output = [System.Collections.Generic.List[string]]::new()
        foreach ($item in $result) { $output.Add($item.ToString()) }
        foreach ($item in $runner.Streams.Information) { $output.Add($item.MessageData.ToString()) }
        foreach ($item in $runner.Streams.Warning) { $output.Add($item.ToString()) }
        foreach ($item in $runner.Streams.Error) { $output.Add($item.ToString()) }
        if ($runner.Streams.Error.Count -gt 0) {
            throw "Linter emitted a PowerShell error: $($output -join [Environment]::NewLine)"
        }
        $exitCode = $runner.Runspace.SessionStateProxy.GetVariable('LASTEXITCODE')
        if ($null -eq $exitCode) {
            throw "Linter returned without an exit status. $($output -join [Environment]::NewLine)"
        }
        return @{ ExitCode = [int]$exitCode; Output = ($output -join [Environment]::NewLine) }
    } finally {
        $runner.Dispose()
    }
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
            $expected = [int]$Case.ExpectedExit
            if ($result.ExitCode -ne $expected) { $reasons += "expected exit $expected, got $($result.ExitCode)" }
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

    $harnessRoot = New-FixtureRoot
    $harnessScript = Join-Path $harnessRoot 'scripts/lint-csharp-naming.ps1'
    foreach ($exitCode in @(0, 1, 7)) {
        Set-Content -LiteralPath $harnessScript -Value "Write-Host 'harness-result'; exit $exitCode"
        $result = Invoke-LintInFixture $harnessRoot
        Write-TestResult "Harness_Exit$exitCode" ($result.ExitCode -eq $exitCode -and $result.Output -match 'harness-result')
    }
    Set-Content -LiteralPath $harnessScript -Value "& git --version; Write-Host 'harness-implicit-success'"
    $result = Invoke-LintInFixture $harnessRoot
    Write-TestResult 'Harness_NativeSuccessSuppliesExitStatus' ($result.ExitCode -eq 0 -and $result.Output -match 'harness-implicit-success')
    foreach ($case in @(
        @{ Name = 'NoExitStatus'; Script = "Write-Host 'harness-no-exit'"; Message = 'without an exit status' },
        @{ Name = 'PowerShellError'; Script = "Write-Error 'harness-error'; exit 0"; Message = 'PowerShell error' },
        @{ Name = 'Throw'; Script = "throw 'harness-throw'"; Message = 'harness-throw' }
    )) {
        Set-Content -LiteralPath $harnessScript -Value $case.Script
        $failed = $false
        try { $null = Invoke-LintInFixture $harnessRoot }
        catch { $failed = $_.Exception.Message -match $case.Message }
        Write-TestResult "Harness_$($case.Name)Fails" $failed
    }
    $root = New-FixtureRoot
    Add-FixtureFile -Root $root -RelativePath 'Runtime/Bar.cs' -Content 'public class Bar { }'
    $result = Invoke-LintInFixture $root -Cli
    Write-TestResult 'Cli_CleanFixture' ($result.ExitCode -eq 0)

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
            ExpectedExit = 1
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

    # Real declarations (not grep matches), all conditional assignments, and
    # readonly/implicit-private/multiple declarators share one field rule.
    $fieldCases = @(
        @{ Name = 'PassPrivateInstanceForms'; Source = 'class C { private int _count; readonly object _gate = new(); int _implicit; private (int X, int Y) _tuple; private System.Collections.Generic.Dictionary<string, int> _map = new(); private int _left, _right; }'; Exit = 0; Contains = @('27 private instance fields checked') }
        @{ Name = 'FailExplicitPrivateField'; Source = 'class C { private int count; }'; Exit = 1; Contains = @("Private instance field 'count'") }
        @{ Name = 'FailSerializedPrivateField'; Source = 'class C { [UnityEngine.SerializeField] private int oldName; }'; Exit = 1; Contains = @("Private instance field 'oldName'") }
        @{ Name = 'FailImplicitPrivateRecordField'; Source = 'record C { int bad; }'; Exit = 1; Contains = @("Private instance field 'bad'") }
        @{ Name = 'FailReadonlyPrivateField'; Source = 'class C { private readonly object gate = new(); }'; Exit = 1; Contains = @("Private instance field 'gate'") }
        @{ Name = 'FailImplicitPrivateStructField'; Source = 'struct C { int count; }'; Exit = 1; Contains = @("Private instance field 'count'") }
        @{ Name = 'FailEveryDeclarator'; Source = 'class C { private int _good, bad = 2, AlsoBad; }'; Exit = 1; Contains = @("field 'bad'", "field 'AlsoBad'") }
        @{ Name = 'FailUppercaseAndExtraUnderscores'; Source = 'class C { private int _Bad, __bad, _bad_name; }'; Exit = 1; Contains = @("field '_Bad'", "field '__bad'", "field '_bad_name'") }
        @{ Name = 'PassOtherMembersAndLocals'; Source = 'class C { public int Public; protected int Protected; private protected int PrivateProtected; protected internal int ProtectedInternal; internal int Internal; private static int Static; private static readonly int StaticReadonly; private const int Constant = 1; private int Property { get; set; } private event System.Action Event; void Good() { int local = 0; } }'; Exit = 0; Contains = @('20 private instance fields checked') }
        @{ Name = 'PassCommentsAndStrings'; Source = 'class C { string _source = "private int bad;"; /* private int other; */ }'; Exit = 0; Contains = @('21 private instance fields checked') }
        @{ Name = 'FailNestedTypeFields'; Source = 'class C { private class D { int bad; } }'; Exit = 1; Contains = @("field 'bad'") }
        @{ Name = 'FailCompoundConditional'; Source = "class C {`n#if A && B`nprivate int bad;`n#endif`n}"; Exit = 1; Contains = @("field 'bad'", 'line=3') }
        @{ Name = 'FailNestedElifElse'; Source = "class C {`n#if A`n#if B`nprivate int _ok;`n#elif C`nprivate int badElif;`n#else`nprivate int badElse;`n#endif`n#endif`n}"; Exit = 1; Contains = @("field 'badElif'", "field 'badElse'") }
        @{ Name = 'PassRepeatedBraceGuards'; Source = "class C { void Good() {`n#if A`nif (true) {`n#endif`n#if A`n}`n#endif`n} private int _value; }"; Exit = 0; Contains = @('21 private instance fields checked') }
        @{ Name = 'PassLiteralRawSource'; Source = 'class C { string _source = """' + "`n#if A`nprivate int bad;`n#endif`n" + '"""; }'; Exit = 0; Contains = @('21 private instance fields checked') }
    )
    foreach ($case in $fieldCases) {
        Invoke-TestCase -Case ([pscustomobject]@{
            Name = $case.Name
            Files = @([pscustomobject]@{ Path = 'Generator~/Fixture.cs'; Content = $case.Source })
            ExpectedExit = $case.Exit
            ExpectedOutputContains = $case.Contains
        })
    }
    $root = New-FixtureRoot
    Remove-Item -LiteralPath (Join-Path $root 'Generator~') -Recurse
    Add-FixtureFile $root 'Runtime/Fixture.cs' 'class C { private int _value; }'
    $result = Invoke-LintInFixture $root -Cli
    Write-TestResult 'FailMissingGeneratorRoot' ($result.ExitCode -ne 0 -and $result.Output.Contains('source root not found: Generator~')) $result.Output
    $root = New-FixtureRoot
    Get-ChildItem -LiteralPath $root -Filter '*.cs' -Recurse | Remove-Item -Force
    $result = Invoke-LintInFixture $root -Cli
    Write-TestResult 'FailEmptyRepository' ($result.ExitCode -ne 0 -and $result.Output.Contains('found no C# files')) $result.Output

    foreach ($staged in @($false, $true)) {
        foreach ($vendor in @($false, $true)) {
            $root = New-FixtureRoot
            Add-FixtureFile $root 'Runtime/Good.cs' 'class Good { int _value; }'
            $path = if ($vendor) { 'Runtime/Utils/SevenZip/Foreign.cs' } else { 'Tests/Owned/SevenZip/Bad.cs' }
            Add-FixtureFile $root $path 'class C { private int bad; }'
            Push-Location $root
            try { & git add -- . | Out-Null } finally { Pop-Location }
            $result = Invoke-LintInFixture $root -StagedOnly:$staged
            $expected = if ($vendor) { 0 } else { 1 }
            $pass = $result.ExitCode -eq $expected -and ($vendor -or $result.Output.Contains("Private instance field 'bad'"))
            Write-TestResult "VendorBoundaryStaged${staged}Vendor${vendor}" $pass $result.Output
        }
    }

    foreach ($case in @(
        @{ Name = 'StagedBadWorktreeGood'; Staged = 'class C { int bad; }'; Working = 'class C { int _good; }'; Exit = 1 }
        @{ Name = 'StagedGoodWorktreeBad'; Staged = 'class C { int _good; }'; Working = 'class C { int bad; }'; Exit = 0 }
        @{ Name = 'StagedDeletedWorktreeStillChecked'; Staged = 'class C { int bad; }'; Working = $null; Exit = 1 }
    )) {
        $root = New-FixtureRoot
        $path = 'Samples~/Name with spaces.cs'
        Add-FixtureFile $root $path $case.Staged
        Push-Location $root
        try { & git add -- $path | Out-Null } finally { Pop-Location }
        if ($null -eq $case.Working) { Remove-Item -LiteralPath (Join-Path $root $path) }
        else { Add-FixtureFile $root $path $case.Working }
        $result = Invoke-LintInFixture $root -StagedOnly
        Write-TestResult $case.Name ($result.ExitCode -eq $case.Exit) $result.Output
    }
    $root = New-FixtureRoot
    Remove-Item -LiteralPath (Join-Path $root '.git') -Recurse -Force
    Add-FixtureFile $root 'Runtime/Fixture.cs' 'class C { int _good; }'
    $result = Invoke-LintInFixture $root -StagedOnly -Cli
    Write-TestResult 'FailStagedGitRead' ($result.ExitCode -ne 0 -and $result.Output.Contains('Cannot read staged C# input')) $result.Output

    foreach ($vendor in @($false, $true)) {
        $root = New-FixtureRoot
        Add-FixtureFile $root 'Runtime/Good.cs' 'class Good { int _value; }'
        $path = if ($vendor) { 'Runtime/Utils/SevenZip/Foreign.cs' } else { 'Runtime/Bad.cs' }
        Add-FixtureFile $root $path 'class C { int bad; }'
        Push-Location $root
        try { & git add -- . | Out-Null } finally { Pop-Location }
        $result = Invoke-LintInFixture $root -StagedOnly -WorkingDirectory (Join-Path $root 'scripts')
        $expected = if ($vendor) { 0 } else { 1 }
        Write-TestResult "StagedSubdirectoryVendor${vendor}" ($result.ExitCode -eq $expected) $result.Output
    }
    # A real missing loose object lets diff enumerate a subject while show fails.
    $root = New-FixtureRoot
    Add-FixtureFile $root 'Runtime/Fixture.cs' 'class C { int _good; }'
    Push-Location $root
    try {
        & git add -- Runtime/Fixture.cs | Out-Null
        $blob = (& git rev-parse ':Runtime/Fixture.cs').Trim()
        Remove-Item -LiteralPath (Join-Path $root ('.git/objects/' + $blob.Substring(0, 2) + '/' + $blob.Substring(2))) -Force
        $enumerated = & git diff --cached --name-only --diff-filter=ACM -- '*.cs'
        $enumerationSucceeded = $LASTEXITCODE -eq 0 -and $enumerated -contains 'Runtime/Fixture.cs'
    } finally { Pop-Location }
    $result = Invoke-LintInFixture $root -StagedOnly -Cli
    Write-TestResult 'FailMissingStagedBlob' ($enumerationSucceeded -and $result.ExitCode -ne 0 -and $result.Output.Contains('Cannot read staged C# input')) $result.Output

    $root = New-FixtureRoot
    $source = "class C {`n" + ((1..40 | ForEach-Object { "private int _value$_;" }) -join "`n") + "`n}"
    Add-FixtureFile $root 'Runtime/Old.cs' $source
    Push-Location $root
    try {
        & git add -- . | Out-Null
        & git commit --quiet -m fixture | Out-Null
        & git mv -- Runtime/Old.cs Runtime/New.cs | Out-Null
        Add-FixtureFile $root 'Runtime/New.cs' ($source.Replace('_value40', 'bad'))
        & git add -- Runtime/New.cs | Out-Null
        $status = & git diff --cached --name-status
        $recognizedRename = $LASTEXITCODE -eq 0 -and ($status -match '^R[0-9]+\s+Runtime/Old.cs\s+Runtime/New.cs$')
    } finally { Pop-Location }
    $result = Invoke-LintInFixture $root -StagedOnly
    Write-TestResult 'FailRenamedEditedStagedBlob' ($recognizedRename -and $result.ExitCode -eq 1 -and $result.Output.Contains("field 'bad'")) $result.Output

    $outer = New-FixtureRoot
    Add-FixtureFile $outer 'Runtime/Fixture.cs' 'class C { int _good; }'
    Push-Location $outer
    try { & git add -- . | Out-Null } finally { Pop-Location }
    $nested = Join-Path $outer 'NestedPackage'
    New-Item -ItemType Directory -Path $nested | Out-Null
    Copy-Item -LiteralPath (Join-Path $outer 'scripts') -Destination (Join-Path $nested 'scripts') -Recurse
    $result = Invoke-LintInFixture $nested -StagedOnly -Cli
    Write-TestResult 'FailDifferentGitRepositoryRoot' ($result.ExitCode -ne 0 -and $result.Output.Contains('git repository root does not match')) $result.Output

    # Exact path/type/member/attribute protection; serialized neighbours get no exemption.
    $historicalCases = @(
        @{ Name = 'PassHistoricalSerializedBaseline'; Content = '[UnityEngine.SerializeReference] private object boxedValue;'; Type = 'PendingValueWrapper'; Exit = 0 }
        @{ Name = 'FailHistoricalUnderscoreRename'; Content = '[UnityEngine.SerializeReference] private object _boxedValue;'; Type = 'PendingValueWrapper'; Exit = 1 }
        @{ Name = 'FailHistoricalMissingMember'; Content = ''; Type = 'PendingValueWrapper'; Exit = 1 }
        @{ Name = 'FailHistoricalRemovedAttribute'; Content = 'private object boxedValue;'; Type = 'PendingValueWrapper'; Exit = 1 }
        @{ Name = 'FailHistoricalWrongAttribute'; Content = '[UnityEngine.SerializeField] private object boxedValue;'; Type = 'PendingValueWrapper'; Exit = 1 }
        @{ Name = 'FailHistoricalWrongDeclaringType'; Content = '[UnityEngine.SerializeReference] private object boxedValue;'; Type = 'OtherWrapper'; Exit = 1 }
        @{ Name = 'FailHistoricalBadSerializedNeighbour'; Content = '[UnityEngine.SerializeReference] private object boxedValue; [UnityEngine.SerializeField] private int neighbour;'; Type = 'PendingValueWrapper'; Exit = 1 }
        @{ Name = 'PassHistoricalValidNeighbour'; Content = '[UnityEngine.SerializeReference] private object boxedValue; [UnityEngine.SerializeField] private int _neighbour;'; Type = 'PendingValueWrapper'; Exit = 0 }
        @{ Name = 'FailHistoricalNestedWrongType'; Content = 'class Nested { [UnityEngine.SerializeReference] private object boxedValue; }'; Type = 'PendingValueWrapper'; Exit = 1 }
        @{ Name = 'FailHistoricalWrongNamespace'; Content = '[UnityEngine.SerializeReference] private object boxedValue;'; Type = 'PendingValueWrapper'; Namespace = 'Other'; Exit = 1 }
        @{ Name = 'FailHistoricalConditionalAttributeLoss'; Content = "`n#if A`n[UnityEngine.SerializeReference]`n#endif`nprivate object boxedValue;"; Type = 'PendingValueWrapper'; Exit = 1 }
        @{ Name = 'FailHistoricalMemberLostInConditionalBranch'; Content = "`n#if A`n[UnityEngine.SerializeReference] private object boxedValue;`n#else`n[UnityEngine.SerializeReference] private object _boxedValue;`n#endif"; Type = 'PendingValueWrapper'; Exit = 1 }
        @{ Name = 'FailHistoricalAttributeSpoof'; Content = '[Other.SerializeReference] private object boxedValue;'; Type = 'PendingValueWrapper'; Exit = 1 }
        @{ Name = 'FailHistoricalAttributeOnlyInComment'; Content = '/* [UnityEngine.SerializeReference] */ private object boxedValue;'; Type = 'PendingValueWrapper'; Exit = 1 }
        @{ Name = 'PassHistoricalShortUnityAttribute'; Content = '[SerializeReference] private object boxedValue;'; Type = 'PendingValueWrapper'; Exit = 0 }
        @{ Name = 'FailHistoricalNonSerializedMember'; Content = '[System.NonSerialized, UnityEngine.SerializeReference] private object boxedValue;'; Type = 'PendingValueWrapper'; Exit = 1 }
        @{ Name = 'FailHistoricalReadonlyMember'; Content = '[UnityEngine.SerializeReference] private readonly object boxedValue;'; Type = 'PendingValueWrapper'; Exit = 1 }
        @{ Name = 'FailHistoricalPublicMember'; Content = '[UnityEngine.SerializeReference] public object boxedValue;'; Type = 'PendingValueWrapper'; Exit = 1 }
    )
    foreach ($staged in @($false, $true)) {
        foreach ($case in $historicalCases) {
            $root = New-FixtureRoot
            $namespace = if ($case.ContainsKey('Namespace')) { $case.Namespace } else { 'WallstopStudios.UnityHelpers.Editor.CustomDrawers' }
            Add-FixtureFile $root 'Editor/CustomDrawers/PendingValueWrapper.cs' "namespace $namespace { using UnityEngine; class $($case.Type) {`n$($case.Content)`n} }"
            Push-Location $root
            try { & git add -- . | Out-Null } finally { Pop-Location }
            $result = Invoke-LintInFixture $root -StagedOnly:$staged
            Write-TestResult "$($case.Name)Staged$staged" ($result.ExitCode -eq $case.Exit) $result.Output
        }
        $root = New-FixtureRoot
        Add-FixtureFile $root 'Editor/OtherWrapper.cs' 'namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers { class PendingValueWrapper { [UnityEngine.SerializeReference] private object boxedValue; } }'
        Push-Location $root
        try { & git add -- . | Out-Null } finally { Pop-Location }
        $result = Invoke-LintInFixture $root -StagedOnly:$staged
        Write-TestResult "FailHistoricalWrongPathStaged$staged" ($result.ExitCode -eq 1 -and $result.Output.Contains("field 'boxedValue'")) $result.Output
    }
    foreach ($staged in @($false, $true)) {
        foreach ($validAvailability in @($false, $true)) {
            $root = New-FixtureRoot
            $symbol = if ($validAvailability) { 'UNITY_EDITOR' } else { 'OTHER' }
            Add-FixtureFile $root 'Editor/CustomDrawers/PendingValueWrapper.cs' ("namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers {`n#if $symbol`nclass PendingValueWrapper { [UnityEngine.SerializeReference] private object boxedValue; }`n#endif`n}")
            Push-Location $root
            try { & git add -- . | Out-Null } finally { Pop-Location }
            $result = Invoke-LintInFixture $root -StagedOnly:$staged
            $expected = if ($validAvailability) { 0 } else { 1 }
            Write-TestResult "HistoricalTypeAvailabilityStaged${staged}Valid${validAvailability}" ($result.ExitCode -eq $expected) $result.Output
        }
    }
    $root = New-FixtureRoot
    Remove-Item -LiteralPath (Join-Path $root 'Editor/CustomDrawers/PendingValueWrapper.cs')
    $result = Invoke-LintInFixture $root -Cli
    Write-TestResult 'FailHistoricalMissingWholeFile' ($result.ExitCode -ne 0 -and $result.Output.Contains('Historical serialized file is missing')) $result.Output
    foreach ($rename in @($false, $true)) {
        $root = New-FixtureRoot
        Push-Location $root
        try {
            & git add -- . | Out-Null
            & git commit --quiet -m fixture | Out-Null
            if ($rename) { & git mv -- Editor/CustomDrawers/PendingValueWrapper.cs Editor/CustomDrawers/OtherWrapper.cs | Out-Null }
            else { & git rm --quiet -- Editor/CustomDrawers/PendingValueWrapper.cs | Out-Null }
        } finally { Pop-Location }
        $result = Invoke-LintInFixture $root -StagedOnly -Cli
        Write-TestResult "FailHistoricalRemovedIndexPathRename$rename" ($result.ExitCode -ne 0 -and $result.Output.Contains('Cannot read staged C# input')) $result.Output
    }

    # Parser/resource failures must stop the CLI, never turn into an empty pass.
    foreach ($case in @(
        @{ Name = 'FailMalformedSyntax'; Source = 'class C { private int bad'; Needle = 'Cannot enforce field naming' }
        @{ Name = 'FailConditionalWorkLimit'; Source = "class C {`n#if " + ((1..13 | ForEach-Object { "S$_" }) -join ' || ') + "`nprivate int _value;`n#endif`n}"; Needle = 'Too many conditional symbols' }
    )) {
        $root = New-FixtureRoot
        Add-FixtureFile $root 'Runtime/Fixture.cs' $case.Source
        $result = Invoke-LintInFixture $root -Cli
        Write-TestResult $case.Name ($result.ExitCode -ne 0 -and $result.Output.Contains($case.Needle)) $result.Output
    }

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
