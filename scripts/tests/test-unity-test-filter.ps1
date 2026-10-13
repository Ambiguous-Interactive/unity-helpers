#!/usr/bin/env pwsh
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$runner = Join-Path $PSScriptRoot '../unity/run-ci-tests.ps1'
$source = Get-Content -LiteralPath $runner -Raw
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) { throw 'The Unity runner must parse.' }
foreach ($name in @('Test-NUnitResults', 'Get-UnityFailedNodeCount',
        'ConvertTo-UnsignedExitHex', 'Get-NativeExitCodeDescription',
        'Get-UnityCrashSignature', 'Write-UnityBenignExitWarning',
        'Write-UnityExecutionSymptomDiagnostics', 'New-UnityEditorTestPhases',
        'New-UnityEditorPhaseArguments',
        'Invoke-UnityEditorTestPhase')) {
    $function = $ast.Find({
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
        }, $true)
    if ($null -eq $function) { throw "Missing production function $name." }
    Invoke-Expression $function.Extent.Text
}

$nativeDescriptions = $ast.Find({
        param($node)
        $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
        $node.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
        $node.Left.VariablePath.UserPath -eq 'script:NativeExitCodeDescriptions'
    }, $true)
if ($null -eq $nativeDescriptions) { throw 'Missing native exit-code descriptions.' }
Invoke-Expression $nativeDescriptions.Extent.Text
. (Join-Path $PSScriptRoot '../unity/lib/credential-redaction.ps1')
. (Join-Path $PSScriptRoot '../unity/lib/compilation-fixture-results.ps1')

$verifier = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../../.github/actions/verify-unity-results/action.yml') -Raw
foreach ($diagnosticSource in @($source, $verifier)) {
    if ([regex]::Matches($diagnosticSource, [regex]::Escape("not(@site='Child')")).Count -ne 2) {
        throw 'Both Unity result readers must exclude rolled-up child suite failures from annotations and counts.'
    }
}

$requiredStart = $verifier.IndexOf('function Assert-RequiredNativeTest {')
if ($requiredStart -lt 0) { throw 'Missing required native-test gate.' }
$requiredAst = [System.Management.Automation.Language.Parser]::ParseInput(
    $verifier.Substring($requiredStart), [ref]$tokens, [ref]$parseErrors)
$requiredFunction = $requiredAst.Find({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq 'Assert-RequiredNativeTest'
    }, $true)
if ($null -eq $requiredFunction) { throw 'The native positive-control gate must parse.' }
Invoke-Expression $requiredFunction.Extent.Text
foreach ($fixture in @(
        @{ Required = ''; Body = ''; Pass = $true },
        @{ Required = 'Control'; Body = '<test-case fullname="Control" result="Passed" />'; Pass = $true },
        @{ Required = 'Control'; Body = ''; Pass = $false },
        @{ Required = 'Control'; Body = '<test-case fullname="control" result="Passed" />'; Pass = $false },
        @{ Required = 'Control'; Body = '<test-case fullname="Control" result="Skipped" />'; Pass = $false },
        @{ Required = 'Control'; Body = '<test-case fullname="Control" result="Failed" />'; Pass = $false },
        @{ Required = 'Control'; Body = '<test-case fullname="Control" result="Inconclusive" />'; Pass = $false },
        @{ Required = 'Control'; Body = '<test-case fullname="Control" result="Passed" /><test-case fullname="Control" result="Passed" />'; Pass = $false }
    )) {
    [xml]$requiredXml = '<test-run>' + $fixture.Body + '</test-run>'
    $accepted = $true
    try { Assert-RequiredNativeTest -Doc $requiredXml -RequiredTestName $fixture.Required }
    catch { $accepted = $false }
    if ($accepted -ne $fixture.Pass) { throw 'Required native-test positive/negative control mismatch.' }
}

# Diagnostics are not the subject; the actual XML gate and invocation sites are.
function Write-CiError { param([string]$Message) }
function Write-CiNotice { param([string]$Message) }
function Write-UnityFailedTestAnnotations { param([xml]$Xml, [string]$Label) }
function Write-UnityResultFailureDiagnostics { param([string]$LogPath, [string]$Project, [string]$Label) }

$selectionStart = $source.IndexOf('$filterArgs = @()')
$selectionEnd = $source.IndexOf('$resultsPath =', $selectionStart)
if ($selectionStart -lt 0 -or $selectionEnd -le $selectionStart) { throw 'Missing test selection construction.' }
$selection = $source.Substring($selectionStart, $selectionEnd - $selectionStart)
$expectedFilter = 'Package.Fixture.Method("literal value");Package.Other'
foreach ($TestFilter in @('', $expectedFilter)) {
    $TestCategory = 'FocusedAcceptance'
    Invoke-Expression $selection
    $ProjectPath = 'C:/owned project'
    $resultsPath = 'C:/result.xml'
    $AssemblyNames = 'Package.Tests'
    $acceleratorArgs = @()
    $graphicsArgs = @('-nographics')
    foreach ($TestMode in @('editmode', 'playmode', 'standalone')) {
        $testPlatform = $TestMode
        $variable = if ($TestMode -eq 'standalone') { 'buildArgs' } else { 'testArgs' }
        $assignments = @($ast.FindAll({
                    param($node)
                    $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
                    $node.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
                    $node.Left.VariablePath.UserPath -eq $variable
                }, $true))
        if ($assignments.Count -lt 1) { throw "Missing $variable construction." }
        foreach ($assignment in $assignments) { Invoke-Expression $assignment.Extent.Text }
        $arguments = Get-Variable -Name $variable -ValueOnly
        if ($TestMode -ne 'standalone') {
            $arguments = @(New-UnityEditorPhaseArguments -Arguments $arguments `
                -TestCategory $TestCategory -TestFilter $TestFilter)
        }
        $position = [Array]::IndexOf($arguments, '-testFilter')
        if ($TestFilter -eq '') {
            if ($position -ne -1 -or $requireFilteredPass) { throw "$TestMode default selection changed." }
        } else {
            if ($position -lt 0 -or $arguments[$position + 1] -cne $expectedFilter -or -not $requireFilteredPass) {
                throw "$TestMode lost or split the requested test filter."
            }
        }
        if ([Array]::IndexOf($arguments, '-testCategory') -lt 0) { throw "$TestMode lost its category filter." }
    }
}

$invocations = @($ast.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.CommandAst] -and
            $node.GetCommandName() -eq 'Test-NUnitResults'
        }, $true))
if ($invocations.Count -ne 2) { throw 'Both editor and standalone result invocation sites must be tested.' }

[xml]$failedChildSuite = @'
<test-run>
  <test-suite fullname="Parent" result="Failed" site="Child">
    <failure><message>One or more child tests had errors</message></failure>
    <test-case fullname="Broken" result="Failed" />
  </test-suite>
</test-run>
'@
if ((Get-UnityFailedNodeCount -Xml $failedChildSuite) -ne 1) {
    throw 'Failed child suites must not inflate the failed test count.'
}

[xml]$failedTearDownSuite = @'
<test-run>
  <test-suite fullname="Fixture" result="Failed" site="TearDown">
    <failure><message>Fixture cleanup failed</message></failure>
    <test-case fullname="Broken" result="Failed" />
  </test-suite>
</test-run>
'@
if ((Get-UnityFailedNodeCount -Xml $failedTearDownSuite) -ne 2) {
    throw 'A suite with its own lifecycle failure must remain actionable.'
}

$temporary = Join-Path ([IO.Path]::GetTempPath()) ('unity-filter-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
$checks = 0
try {
    $isolationChecks = 0
    $phases = @(New-UnityEditorTestPhases -ArtifactsPath $temporary `
        -TestCategory '!Performance;!Stress' -IsolateCompilationFixtures)
    if ($phases.Count -ne 2 -or $phases[0].TestCategory -cne 'CompilationIsolation' -or
        $phases[0].ArtifactsPath -ceq $phases[1].ArtifactsPath -or
        -not $phases[0].RequirePassedTests -or
        $phases[1].TestCategory -cne '!Performance;!Stress;!CompilationIsolation') {
        throw 'Compiler and normal tests must have separate complete selections and artifacts.'
    }
    $rejected = $false
    try { New-UnityEditorTestPhases -ArtifactsPath $temporary -TestFilter 'Caller.Filter' -IsolateCompilationFixtures }
    catch { $rejected = $true }
    if (-not $rejected) { throw 'Isolation must not silently widen a caller filter.' }
    $isolationChecks++
    foreach ($fixture in @(
            @{ Body = ''; Accepted = $false },
            @{ Body = '<test-case classname="Subject" result="Passed" />'; Accepted = $true },
            @{ Body = '<test-case classname="Other" result="Passed" />'; Accepted = $false },
            @{ Body = '<test-case classname="Subject" result="Skipped" />'; Accepted = $false },
            @{ Body = '<test-case classname="Subject" result="Inconclusive" />'; Accepted = $false },
            @{ Body = '<test-case classname="Subject" result="Failed" />'; Accepted = $false },
            @{ Body = '<test-case classname="Subject" result="Passed" /><test-case classname="Subject" result="Skipped" />'; Accepted = $false }
        )) {
        $fixtureXml = Join-Path $temporary 'fixture.xml'
        Set-Content -LiteralPath $fixtureXml -Value ('<test-run>' + $fixture.Body + '</test-run>')
        $accepted = $true
        try { Assert-UnityCompilationFixtureResults -Path $fixtureXml -FixtureName 'Subject' }
        catch { $accepted = $false }
        if ($accepted -ne $fixture.Accepted) { throw 'Isolated fixture evidence control mismatch.' }
        $isolationChecks++
    }

    $phaseLoop = $ast.Find({
        param($node)
        $node -is [System.Management.Automation.Language.ForEachStatementAst] -and
        $node.Variable.VariablePath.UserPath -eq 'phase'
    }, $true)
    if ($null -eq $phaseLoop) { throw 'The shipping phase orchestration must exist.' }
    function Invoke-UnityEditorTestsWithPackageManagerRetry {
        param($EditorPath, $Arguments, $Label, $LogPath, $ResultsPath, $Project)
        if (Test-Path -LiteralPath $ResultsPath) { throw 'A stale phase result reached the native runner.' }
        $script:phaseCalls.Add(@($Arguments))
        $first = $script:phaseCalls.Count -eq 1
        if (($script:phaseFailure -eq 'first process' -and $first) -or
            ($script:phaseFailure -eq 'second process' -and -not $first)) {
            throw 'Native process failure.'
        }
        $caseClass = if ($first) { $phases[0].RequiredFixture } else { 'Normal.Subject' }
        if ($script:phaseFailure -eq 'wrong subject' -and $first) { $caseClass = 'Wrong.Subject' }
        Set-Content -LiteralPath $ResultsPath -Value ("<test-run total='1' passed='1' failed='0' skipped='0'><test-case classname='$caseClass' result='Passed' /></test-run>")
        Set-Content -LiteralPath $LogPath -Value 'Native editor output.'
        return 0
    }
    function Write-AnalyzerSetupDiagnostics { param($Project, $LogPath, $Label) }
    function Assert-NoUnityCompilerWarnings {
        param($LogPath, $Label, $PackageRoot)
        if ($script:phaseFailure -eq 'compiler warning' -and $script:phaseCalls.Count -eq 1) {
            throw 'Compiler warning.'
        }
    }
    foreach ($phaseFailure in @('', 'first process', 'second process', 'wrong subject', 'compiler warning')) {
        $script:phaseFailure = $phaseFailure
        $script:phaseCalls = New-Object 'System.Collections.Generic.List[object]'
        foreach ($phase in $phases) {
            New-Item -ItemType Directory -Force -Path $phase.ArtifactsPath | Out-Null
            Set-Content -LiteralPath (Join-Path $phase.ArtifactsPath 'results.xml') -Value '<stale />'
        }
        $testArgs = @('-batchmode', '-projectPath', $temporary, '-runTests')
        $UnityEditorPath = 'native-editor'
        $UnityVersion = '2021.3.45f1'
        $TestMode = 'editmode'
        $ProjectPath = $temporary
        $RepoRoot = $temporary
        $accepted = $true
        try { Invoke-Expression $phaseLoop.Extent.Text }
        catch { $accepted = $false }
        if ($accepted -ne ($phaseFailure -eq '')) { throw 'A phase failure was hidden or a passing pair was rejected.' }
        $expectedCalls = if ($phaseFailure -eq '' -or $phaseFailure -eq 'second process') { 2 } else { 1 }
        if ($script:phaseCalls.Count -ne $expectedCalls) { throw 'A failed isolated phase must prevent the broad process.' }
        foreach ($arguments in $script:phaseCalls) {
            if (@($arguments | Where-Object { $_ -ceq '-testResults' }).Count -ne 1) {
                throw 'Each native process must receive one owned result path.'
            }
        }
        $isolationChecks++
    }
    $runMarker = '      run: |'
    $runStart = $verifier.IndexOf($runMarker)
    if ($runStart -lt 0) { throw 'The shipping result verifier must have a run block.' }
    $verifierProgram = $verifier.Substring($runStart + $runMarker.Length).TrimStart("`r", "`n")
    $verifierProgram = ($verifierProgram -split "`n" | ForEach-Object { $_ -replace '^        ', '' }) -join "`n"
    $verifierScript = Join-Path $temporary 'verify-action.ps1'
    Set-Content -LiteralPath $verifierScript -Value $verifierProgram
    $oldEnvironment = @{}
    foreach ($key in @('GITHUB_WORKSPACE', 'UH_RESULTS_DIR', 'UH_LABEL', 'UH_EXPECTED_EMPTY', 'UH_REQUIRED_TEST_NAME', 'UH_REQUIRED_COMPILATION_FIXTURE')) {
        $oldEnvironment[$key] = [Environment]::GetEnvironmentVariable($key)
    }
    try {
        $env:GITHUB_WORKSPACE = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
        $env:UH_LABEL = 'result ownership control'
        $env:UH_EXPECTED_EMPTY = 'false'
        $env:UH_REQUIRED_TEST_NAME = ''
        $env:UH_REQUIRED_COMPILATION_FIXTURE = 'Subject'
        foreach ($fixture in @(
                @{ Root = $null; Child = 'Passed'; Exit = 1 },
                @{ Root = 'Passed'; Child = $null; Exit = 1 },
                @{ Root = 'Failed'; Child = 'Passed'; Exit = 1 },
                @{ Root = 'Passed'; Child = 'Failed'; Exit = 1 },
                @{ Root = 'Passed'; Child = 'Skipped'; Exit = 1 },
                @{ Root = 'Passed'; Child = 'Passed'; Exit = 0 }
            )) {
            $env:UH_RESULTS_DIR = Join-Path $temporary ('ownership-' + [Guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path (Join-Path $env:UH_RESULTS_DIR 'compilation') -Force | Out-Null
            if ($null -ne $fixture.Root) {
                $failedCount = if ($fixture.Root -eq 'Failed') { 1 } else { 0 }
                Set-Content -LiteralPath (Join-Path $env:UH_RESULTS_DIR 'results.xml') -Value ("<test-run total='1' passed='1' failed='$failedCount' skipped='0'><test-case result='$($fixture.Root)' /></test-run>")
            }
            if ($null -ne $fixture.Child) {
                Set-Content -LiteralPath (Join-Path $env:UH_RESULTS_DIR 'compilation/results.xml') -Value ("<test-run total='1' passed='1' failed='0' skipped='0'><test-case classname='Subject' result='$($fixture.Child)' /></test-run>")
            }
            $output = & (Get-Process -Id $PID).Path -NoProfile -File $verifierScript 2>&1 | Out-String
            $actualExit = $LASTEXITCODE
            if (($actualExit -eq 0) -ne ($fixture.Exit -eq 0)) {
                throw "The actual verifier confused root and compilation evidence: $output"
            }
            $isolationChecks++
        }
        $dumpAction = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../../.github/actions/dump-unity-log-tail/action.yml') -Raw
        $dumpStart = $dumpAction.IndexOf($runMarker)
        if ($dumpStart -lt 0) { throw 'The shipping dump action must have a run block.' }
        $dumpProgram = $dumpAction.Substring($dumpStart + $runMarker.Length).TrimStart("`r", "`n")
        $dumpProgram = ($dumpProgram -split "`n" | ForEach-Object { $_ -replace '^        ', '' }) -join "`n"
        $dumpProgram = $dumpProgram.Replace('${{ inputs.results-dir }}', '$env:UH_RESULTS_DIR').Replace('${{ inputs.label }}', '$env:UH_LABEL').Replace('${{ inputs.tail-lines }}', '20')
        $dumpScript = Join-Path $temporary 'dump-action.ps1'
        Set-Content -LiteralPath $dumpScript -Value $dumpProgram
        Set-Content -LiteralPath (Join-Path $env:UH_RESULTS_DIR 'compilation/unity.log') -Value 'isolated compiler diagnostic marker'
        $dumpOutput = & (Get-Process -Id $PID).Path -NoProfile -File $dumpScript 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0 -or -not $dumpOutput.Contains('isolated compiler diagnostic marker')) {
            throw 'An isolated failure must retain its nested editor log tail.'
        }
        $isolationChecks++
    } finally {
        foreach ($key in $oldEnvironment.Keys) { [Environment]::SetEnvironmentVariable($key, $oldEnvironment[$key]) }
    }
    foreach ($fixture in @(
            @{ Name = 'passed'; Total = 1; Passed = 1; Failed = 0; Skipped = 0; Body = ''; Error = '' },
            @{ Name = 'empty'; Total = 0; Passed = 0; Failed = 0; Skipped = 0; Body = ''; Error = '0 tests ran' },
            @{ Name = 'explicit skipped'; Total = 1; Passed = 0; Failed = 0; Skipped = 1; Body = ''; Error = 'No selected tests passed' },
            @{ Name = 'inconclusive'; Total = 1; Passed = 0; Failed = 0; Skipped = 0; Body = ''; Error = 'No selected tests passed' },
            @{ Name = 'failed leaf'; Total = 1; Passed = 1; Failed = 0; Skipped = 0; Body = '<test-case fullname="Broken" result="Failed" />'; Error = 'tests failed' }
        )) {
        $resultsPath = Join-Path $temporary 'results.xml'
        Set-Content -LiteralPath $resultsPath -Value ("<test-run total='{0}' passed='{1}' failed='{2}' skipped='{3}'>{4}</test-run>" -f
            $fixture.Total, $fixture.Passed, $fixture.Failed, $fixture.Skipped, $fixture.Body)
        $UnityVersion = '2021.3.45f1'
        $TestMode = 'editmode'
        $logPath = ''
        $playerLogPath = ''
        $ProjectPath = $temporary
        $playerExitForValidation = 0
        $runExit = 0
        $phaseResults = $resultsPath
        $phaseLog = $logPath
        $Project = $ProjectPath
        $phaseExit = 0
        $Label = 'control'
        $RequirePassedTests = $true
        $requireFilteredPass = $true
        foreach ($invocation in $invocations) {
            $message = ''
            try { Invoke-Expression $invocation.Extent.Text } catch { $message = $_.Exception.Message }
            if ($fixture.Error -eq '') {
                if ($message -ne '') { throw "Passing fixture rejected: $message" }
            } elseif ($message -notlike ('*' + $fixture.Error + '*')) {
                throw "Expected rejection for '$($fixture.Name)', got '$message'."
            }
            $checks++
        }
    }
    $exitDiagnosticChecks = 0
    $logPath = Join-Path $temporary 'exit.log'
    foreach ($fixture in @(
            @{ Exit = 2; Timeout = $false; Log = 'Test run completed. Exiting with code 2 (Failed). One or more tests failed.'; Expected = 'Editor completion: Test run completed. Exiting with code 2 (Failed). One or more tests failed.'; Native = $false },
            @{ Exit = 2; Timeout = $false; Log = "Crash!!! DirectoryMonitor`nTest run completed. Exiting with code 2 (Failed). One or more tests failed."; Expected = 'Editor completion: Test run completed. Exiting with code 2 (Failed). One or more tests failed.'; Also = 'native crash marker and a DirectoryMonitor reference'; Native = $false },
            @{ Exit = 2; Timeout = $false; Log = "Test run completed. Exiting with code 2 (Failed). One or more tests failed.`nBatchmode quit successfully invoked"; Expected = 'Editor completion: Test run completed. Exiting with code 2 (Failed). One or more tests failed.'; Also = 'successful batch completion'; Native = $false },
            @{ Exit = 2; Timeout = $false; Log = 'DirectoryMonitor initialized'; Expected = 'exit/artifact discrepancy'; Native = $false },
            @{ Exit = 255; Timeout = $false; Log = 'Batchmode quit successfully invoked'; Expected = 'successful batch completion'; Native = $false },
            @{ Exit = -1073741819; Timeout = $false; Log = 'Crash!!! DirectoryMonitor'; Expected = 'native crash marker and a DirectoryMonitor reference'; Native = $true },
            @{ Exit = -1073741819; Timeout = $false; Log = ''; Expected = 'STATUS_ACCESS_VIOLATION'; Native = $true },
            @{ Exit = -1073741823; Timeout = $false; Log = ''; Expected = 'exit/artifact discrepancy'; Native = $false },
            @{ Exit = 137; Timeout = $false; Log = 'Crash!!!'; Expected = 'native crash marker; crash timing is unresolved'; Native = $false },
            @{ Exit = 124; Timeout = $true; Log = ''; Expected = 'tree-killed by the watchdog'; Native = $false },
            @{ Exit = 2; Timeout = $false; Log = $null; Expected = 'cause is unresolved'; Native = $false }
        )) {
        if ($null -eq $fixture.Log) { Remove-Item -LiteralPath $logPath -ErrorAction SilentlyContinue }
        else { Set-Content -LiteralPath $logPath -Value $fixture.Log }
        $output = Write-UnityBenignExitWarning -Label 'control' -ExitCode $fixture.Exit -TimedOut:$fixture.Timeout -LogPath $logPath 6>&1 | Out-String
        if (-not $output.Contains($fixture.Expected) -or
            $output.Contains('known native error/crash status') -ne $fixture.Native -or
            $output.Contains('benign post-work') -or $output.Contains('crash during shutdown') -or
            -not $output.Contains('validated result artifact is accepted')) {
            throw "Inaccurate accepted-artifact diagnostic for exit $($fixture.Exit): $output"
        }
        if ($fixture.ContainsKey('Also') -and -not $output.Contains($fixture.Also)) {
            throw "Combined exit log evidence was lost: $output"
        }
        if ($fixture.Log -eq 'DirectoryMonitor initialized' -and $output.Contains('Log evidence:')) {
            throw 'An ordinary DirectoryMonitor reference must not establish a crash.'
        }
        $exitDiagnosticChecks++
    }
    Set-Content -LiteralPath $logPath -Value "Crash!!! DirectoryMonitor`nTest run completed. Exiting with code 2 (Failed). access token: diagnostic-secret-value"
    $redactedOutput = Write-UnityBenignExitWarning -Label 'control' -ExitCode 2 -LogPath $logPath 6>&1 | Out-String
    if ($redactedOutput.Contains('diagnostic-secret-value') -or
        -not $redactedOutput.Contains('access token: <redacted:unity-access-token>') -or
        -not $redactedOutput.Contains('native crash marker and a DirectoryMonitor reference')) {
        throw 'Combined log observations must preserve evidence while redacting credentials.'
    }
    $exitDiagnosticChecks++
    foreach ($hexBare in $script:NativeExitCodeDescriptions.Keys) {
        $exitCode = [int]([Convert]::ToInt64($hexBare, 16) - 4294967296)
        $output = Write-UnityBenignExitWarning -Label 'control' -ExitCode $exitCode 6>&1 | Out-String
        if (-not $output.Contains('known native error/crash status') -or
            -not $output.Contains($script:NativeExitCodeDescriptions[$hexBare])) {
            throw "Known native status $hexBare lost its distinct description."
        }
        $exitDiagnosticChecks++
    }
    foreach ($exitCode in @(-1, [int]::MinValue, [int]::MaxValue, 125)) {
        $output = Write-UnityBenignExitWarning -Label 'control' -ExitCode $exitCode 6>&1 | Out-String
        if (-not $output.Contains("exited with code $exitCode") -or
            -not $output.Contains('exit/artifact discrepancy') -or
            $output.Contains('known native error/crash status')) {
            throw "Unknown exit $exitCode must retain its actual value and unresolved cause."
        }
        $exitDiagnosticChecks++
    }
    Set-Content -LiteralPath $logPath -Value 'Test run completed. Exiting with code 2 (Failed). One or more tests failed.'
    $acceptedXml = @'
<test-run total="7" passed="1" failed="0" skipped="0" inconclusive="6" result="Skipped:Ignored">
  <test-suite fullname="Assembly" result="Skipped" label="Ignored">
    <test-suite fullname="Fixture" result="Inconclusive">
      <test-case fullname="Passing" result="Passed" />
      <test-case fullname="Allocation1" result="Inconclusive"><reason><message>Measurement unavailable</message></reason></test-case>
      <test-case fullname="Allocation2" result="Inconclusive" />
      <test-case fullname="Allocation3" result="Inconclusive" />
      <test-case fullname="Allocation4" result="Inconclusive" />
      <test-case fullname="Allocation5" result="Inconclusive" />
      <test-case fullname="Allocation6" result="Inconclusive" />
    </test-suite>
  </test-suite>
</test-run>
'@
    foreach ($exitCode in @(0, 2, -1073741819)) {
        Set-Content -LiteralPath $resultsPath -Value $acceptedXml
        $digest = (Get-FileHash -LiteralPath $resultsPath).Hash
        $output = Test-NUnitResults -Path $resultsPath -Label 'control' -LogPath $logPath -UnityExitCode $exitCode -RequirePassedTests 6>&1 | Out-String
        if ((Get-FileHash -LiteralPath $resultsPath).Hash -ne $digest) { throw 'Accepted XML outcomes were rewritten.' }
        if ($exitCode -eq 0) {
            if ($output.Contains('::warning::')) { throw 'A successful exit must not produce an exit discrepancy warning.' }
        } elseif (-not $output.Contains("Accepted XML: total=7 passed=1 failed=0 skipped=0 inconclusive=6 root-result='Skipped:Ignored'") -or
            -not $output.Contains('One or more tests failed.')) {
            throw "Accepted XML counts and explicit editor completion must survive exit $exitCode diagnostics: $output"
        }
        $exitDiagnosticChecks++
    }
    foreach ($fixture in @(
            @{ Name = 'missing'; Xml = $null; Error = 'did not produce NUnit results' },
            @{ Name = 'malformed'; Xml = '<test-run'; Error = 'Cannot convert value' },
            @{ Name = 'missing root'; Xml = '<results />'; Error = 'Invalid NUnit results' },
            @{ Name = 'zero tests'; Xml = '<test-run total="0" passed="0" failed="0" skipped="0" />'; Error = '0 tests ran|zero-count NUnit XML' },
            @{ Name = 'zero passed'; Xml = '<test-run total="1" passed="0" failed="0" skipped="0"><test-case result="Inconclusive" /></test-run>'; Error = 'No selected tests passed' },
            @{ Name = 'failed count'; Xml = '<test-run total="2" passed="1" failed="1" skipped="0" />'; Error = 'tests failed' },
            @{ Name = 'failed leaf'; Xml = '<test-run total="2" passed="1" failed="0" skipped="0"><test-case result="Failed" /></test-run>'; Error = 'tests failed' },
            @{ Name = 'suite teardown failure'; Xml = '<test-run total="1" passed="1" failed="0" skipped="0"><test-suite result="Failed" site="TearDown"><failure><message>Cleanup failed</message></failure></test-suite></test-run>'; Error = 'tests failed' }
        )) {
        foreach ($exitCode in @(0, 2, -1073741819)) {
            if ($null -eq $fixture.Xml) { Remove-Item -LiteralPath $resultsPath -ErrorAction SilentlyContinue }
            else { Set-Content -LiteralPath $resultsPath -Value $fixture.Xml }
            $rejected = $false
            $rejectionMessage = ''
            $output = ''
            try { $output = Test-NUnitResults -Path $resultsPath -Label 'control' -LogPath $logPath -UnityExitCode $exitCode -RequirePassedTests 6>&1 | Out-String }
            catch { $rejected = $true; $rejectionMessage = $_.Exception.Message }
            if ($rejectionMessage -notmatch $fixture.Error) {
                throw "Incorrect rejection for '$($fixture.Name)': $rejectionMessage"
            }
            if (-not $rejected -or $output.Contains('validated result artifact is accepted')) {
                throw "The fail-closed XML gate accepted '$($fixture.Name)' with exit $exitCode."
            }
            $exitDiagnosticChecks++
        }
    }
    function Get-UnityDiagnosticLogFiles { param([string]$ResultsDir) Join-Path $ResultsDir 'unity.log' }
    function ConvertTo-UnitySafeLogText { param([string]$Text) $Text }
    function Write-CiError { param([string]$Message) Write-Host "::error::$Message" }
    $diagnosticChecks = 0
    foreach ($diagnosticSource in @($source, $verifier)) {
        $isRunner = $diagnosticSource -eq $source
        $functionName = 'Write-UnityExecutionSymptomDiagnostics'
        $start = $diagnosticSource.IndexOf("function $functionName {")
        if ($start -lt 0) { throw "Missing production diagnostic function $functionName." }
        $diagnosticAst = [System.Management.Automation.Language.Parser]::ParseInput(
            $diagnosticSource.Substring($start), [ref]$tokens, [ref]$parseErrors)
        $diagnosticFunction = $diagnosticAst.Find({
                param($node)
                $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                $node.Name -eq $functionName
            }, $true)
        if ($null -eq $diagnosticFunction) { throw "Cannot parse production diagnostic function $functionName." }
        Invoke-Expression $diagnosticFunction.Extent.Text
        foreach ($fixture in @(
                @{ Line = '0.2 kb 0.0% Packages/com.unity.test-framework/UnexpectedLogMessageException.cs'; Rejected = $false },
                @{ Line = 'UnexpectedLogMessageException: unexpected Error log'; Rejected = $true },
                @{ Line = 'Unhandled log message: unexpected Error log'; Rejected = $true }
            )) {
            $logPath = Join-Path $temporary 'unity.log'
            Set-Content -LiteralPath $logPath -Value $fixture.Line
            $output = if ($isRunner) {
                Write-UnityExecutionSymptomDiagnostics -LogPath $logPath -Label 'control' 6>&1 | Out-String
            } else {
                Write-UnityExecutionSymptomDiagnostics -ResultsDir $temporary -Label 'control' 6>&1 | Out-String
            }
            $rejected = $output.Contains('::error::Unity Test Framework rejected an unexpected log')
            if ($rejected -ne $fixture.Rejected) {
                throw "$functionName misclassified execution symptom: $($fixture.Line)"
            }
            $diagnosticChecks++
        }
    }
} finally {
    Remove-Item -LiteralPath $temporary -Recurse -Force
}
Write-Host "[test-unity-test-filter] Six argument controls, $checks result controls, $isolationChecks isolation controls, $diagnosticChecks diagnostic controls, and $exitDiagnosticChecks exit/XML controls passed."
