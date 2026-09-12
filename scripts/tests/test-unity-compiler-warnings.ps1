#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Tests the zero-warning gate used by every Unity CI compile path.
#>
[CmdletBinding()]
param(
    [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../unity/lib/credential-redaction.ps1')

$scriptRoot = Split-Path -Parent $PSScriptRoot
$target = Join-Path $scriptRoot 'unity/run-ci-tests.ps1'
$tokens = $null
$errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    $target,
    [ref]$tokens,
    [ref]$errors
)
if (0 -lt @($errors).Count) {
    throw "run-ci-tests.ps1 has parse errors: $($errors -join '; ')"
}

foreach ($functionName in @('Write-CiError', 'Assert-NoUnityCompilerWarnings')) {
    $definition = $ast.Find(
        {
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -eq $functionName
        },
        $true
    )
    if ($null -eq $definition) {
        throw "Function '$functionName' not found in run-ci-tests.ps1."
    }
    Invoke-Expression $definition.Extent.Text
}

$passed = 0
$failed = 0

function Assert-That {
    param([string]$Description, [bool]$Condition)

    if ($Condition) {
        if ($VerboseOutput) {
            Write-Host "  PASS: $Description"
        }
        $script:passed++
        return
    }

    Write-Host "  FAIL: $Description"
    $script:failed++
}

function Invoke-Gate {
    param(
        [string[]]$Lines,
        [int]$MaximumReported = 25
    )

    $logPath = Join-Path (
        [System.IO.Path]::GetTempPath()
    ) ("uh-compiler-warning-" + [System.Guid]::NewGuid().ToString('N') + '.log')
    try {
        Set-Content -LiteralPath $logPath -Value $Lines -Encoding utf8
        $output = @()
        $script:gateThrew = $false
        $output = @(
            & {
                try {
                Assert-NoUnityCompilerWarnings -LogPath $logPath -Label 'fixture' -PackageRoot $scriptRoot -MaximumReported $MaximumReported 6>&1 |
                    ForEach-Object { "$_" }
                } catch {
                    $script:gateThrew = $true
                    Write-Output $_.Exception.Message
                }
            } 6>&1
        )
        return @{ Output = $output; Threw = $script:gateThrew }
    } finally {
        if (Test-Path -LiteralPath $logPath -PathType Leaf) {
            Remove-Item -LiteralPath $logPath -Force
        }
    }
}

$clean = Invoke-Gate -Lines @(
    'Compilation completed successfully',
    '[uh-test-stream] TEST-STARTED Fixture("warning WUH015: intentional test data")',
    'A plain warning from Unity that is not a compiler diagnostic',
    '0 warnings'
)
Assert-That 'non-diagnostic warning text stays green' (-not $clean.Threw)
Assert-That 'a clean scan reports its subject count' (
    @($clean.Output | Where-Object { $_ -like '*Scanned 4 fixture log line(s)*0 unique*' }).Count -eq 1
)

$warningLines = @(
    ((Join-Path $scriptRoot 'Runtime/Thing.cs') + '(8,13): warning CS0618: obsolete'),
    'Packages/com.wallstop-studios.unity-helpers/Editor/Thing.cs(9): warning WUH003: expensive equality',
    '  warning CS8032: An instance of analyzer WallstopStudios.UnityHelpers.Analyzers cannot be created',
    '*** Tundra build success***Assets/WallstopStudios/UnityHelpers/Styles/Thing.cs(4,1,4,8): warning UAC0006: Unity diagnostic',
    'Samples~/Example/Thing.cs(5): warning WPROTO001: serialization contract warning',
    'Library/PackageCache/com.wallstop-studios.unity-helpers@abc123/Runtime/Cached.cs(6): warning CS0612: cached package warning',
    'CSC : warning CS8032: An instance of analyzer WallstopStudios.UnityHelpers.Proto cannot be created',
    '[Worker0] CSC : warning CS1685: The predefined type is defined in WallstopStudios.UnityHelpers.Runtime and another assembly',
    "warning AD0001: Analyzer 'WallstopStudios.UnityHelpers.Analyzers.UnityObjectNullAnalyzer' threw an exception",
    'Library/Bee/artifacts/1900b0aE.dag/WallstopStudios.UnityHelpers.Proto.Generator/Generated.cs(7): warning WPROTO031: generated warning'
)
$warnings = Invoke-Gate -Lines $warningLines -MaximumReported 2
Assert-That 'compiler and analyzer warnings fail the gate' $warnings.Threw
Assert-That 'the failure reports the unique warning count' (
    @($warnings.Output | Where-Object { $_ -like '*emitted 10 unique package, sample, or CI-harness compiler warning(s)*' }).Count -ge 1
)
Assert-That 'the report limit is honored' (
    @($warnings.Output | Where-Object { $_ -like '::error::fixture emitted a compiler warning:*' }).Count -eq 2
)
Assert-That 'omitted warnings are counted' (
    @($warnings.Output | Where-Object { $_ -like '*emitted 8 additional unique*' }).Count -eq 1
)

$unowned = Invoke-Gate -Lines @(
    'Packages/com.example/Runtime/Dependency.cs(4,1): warning CS0618: dependency warning',
    ((Join-Path $scriptRoot 'Tests/Editor/Fixture.cs') + '(9): warning CS0168: test warning'),
    'warning WPROTO001: source-less analyzer prose',
    'warning CS8032: An instance of analyzer Example.Analyzers cannot be created',
    "warning AD0001: Analyzer 'Example.Analyzers.Rule' threw an exception",
    'warning CS8032: An instance of analyzer WallstopStudios.UnityHelpersAdjacent cannot be created',
    'Library/Bee/artifacts/example/Generated.cs(7): warning CS0618: unrelated generated warning'
)
Assert-That 'dependency, test, and unrelated source-less warnings stay outside the client gate' (-not $unowned.Threw)

$harness = Invoke-Gate -Lines @(
    'Assets/Editor/UhCiTestConfigurator.cs(12): warning CS0618: generated configurator warning',
    'Assets/Editor/UnityHelpersPackageExporter.cs(23): warning CS0618: generated exporter warning'
)
Assert-That 'generated CI harness warnings fail the gate' $harness.Threw

$duplicate = Invoke-Gate -Lines @($warningLines[0], $warningLines[0])
Assert-That 'duplicate compiler lines are reported once' (
    @($duplicate.Output | Where-Object { $_ -like '*emitted 1 unique package, sample, or CI-harness compiler warning(s)*' }).Count -ge 1
)

$missing = $false
try {
    Assert-NoUnityCompilerWarnings -LogPath (
        Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString('N'))
    ) -Label 'missing' -PackageRoot $scriptRoot
} catch {
    $missing = $_.Exception.Message -like '*could not read*'
}
Assert-That 'a missing log cannot masquerade as a clean scan' $missing

$source = Get-Content -LiteralPath $target -Raw
foreach ($requiredCall in @(
    'Assert-NoUnityCompilerWarnings -LogPath $LogPath -Label $Label -PackageRoot $RepoRoot',
    'Assert-NoUnityCompilerWarnings -LogPath $LogPath -Label ''Unity package export'' -PackageRoot $PackageRoot',
    '-Label "Unity $UnityVersion standalone build"',
    '-Label "Unity $UnityVersion $TestMode"'
)) {
    Assert-That "runner includes warning gate call: $requiredCall" $source.Contains($requiredCall)
}
Assert-That 'runner scans a preserved configure attempt after a retry' $source.Contains(
    'Assert-NoUnityCompilerWarnings -LogPath $firstAttemptLogPath -Label "$Label (first attempt)" -PackageRoot $RepoRoot'
)

Write-Host "Unity compiler warnings: $passed passed, $failed failed."
if (0 -lt $failed) {
    exit 1
}
exit 0
