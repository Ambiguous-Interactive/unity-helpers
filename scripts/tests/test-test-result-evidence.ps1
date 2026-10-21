#!/usr/bin/env pwsh
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../unity/lib/test-result-evidence.ps1')
$checks = 0
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('test result evidence ' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null

function Assert-Control {
    param([bool]$Passed, [string]$Name)
    if (-not $Passed) { throw "Control failed: $Name" }
    ++$script:checks
}

function Read-Control {
    param([string]$Content, [string]$Format)
    $path = Join-Path $temporary ('results.' + $Format)
    [IO.File]::WriteAllText($path, $Content)
    Read-TestResultEvidence -Path $path -Format $Format
}

function Assert-Rejected {
    param([string]$Content, [string]$Format, [string]$Name)
    $rejected = $false
    try { Read-Control $Content $Format | Out-Null } catch { $rejected = $true }
    Assert-Control $rejected $Name
}

$nunit = @'
<test-run id="run-nunit" start-time="2026-10-18T12:00:00Z" end-time="2026-10-18T12:00:01Z" result="Passed" testcasecount="2" total="2" passed="2" failed="0" skipped="0" inconclusive="0">
  <test-suite type="Assembly" fullname="/compiled/tests.dll" result="Passed">
    <test-suite type="TestFixture" fullname="Sample.Fixture" result="Passed">
      <test-case id="case-a" classname="Sample.Fixture" fullname="Sample.Fixture.Read(1)" result="Passed"/>
      <test-case id="case-b" classname="Sample.Fixture" fullname="Sample.Fixture.Custom display" result="Passed"/>
    </test-suite>
  </test-suite>
</test-run>
'@
$trx = @'
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010" id="run-trx">
  <Times start="2026-10-18T12:00:00Z" finish="2026-10-18T12:00:01Z"/>
  <Results>
    <UnitTestResult testId="case-a" executionId="execution-a" testName="Read(1)" outcome="Passed"/>
    <UnitTestResult testId="case-b" executionId="execution-b" testName="Custom display" outcome="Passed"/>
  </Results>
  <TestDefinitions>
    <UnitTest id="case-a" name="Read(1)"><Execution id="execution-a"/><TestMethod codeBase="/compiled/tests.dll" className="Sample.Fixture" name="Read(1)"/></UnitTest>
    <UnitTest id="case-b" name="Custom display"><Execution id="execution-b"/><TestMethod codeBase="/compiled/tests.dll" className="Sample.Fixture" name="OtherMethod"/></UnitTest>
  </TestDefinitions>
  <TestEntries>
    <TestEntry testId="case-a" executionId="execution-a"/>
    <TestEntry testId="case-b" executionId="execution-b"/>
  </TestEntries>
  <ResultSummary outcome="Completed">
    <Counters total="2" executed="2" passed="2" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0"/>
  </ResultSummary>
</TestRun>
'@

try {
    foreach ($format in @('NUnit', 'Trx')) {
        $content = if ($format -eq 'NUnit') { $nunit } else { $trx }
        $result = Read-Control $content $format
        Assert-Control ($result.Total -eq 2 -and $result.Passed -eq 2 -and $result.Cases.Count -eq 2 -and
            $result.Failed -eq 0 -and $result.Skipped -eq 0 -and $result.Inconclusive -eq 0) "$format exact passed counts"
        Assert-Control ($result.StartedUtc -ceq '2026-10-18T12:00:00Z' -and $result.CompletedUtc -ceq '2026-10-18T12:00:01Z') "$format original timestamps"
        Assert-Control ($result.Cases[0].Fixture -ceq 'Sample.Fixture' -and $result.Cases[0].AssemblyPath -ceq '/compiled/tests.dll') "$format actual fixture and assembly"
        Assert-Control ($result.Cases[0].TestId -ceq 'case-a') "$format test ID"
        Assert-Rejected '' $format "$format empty file"
        Assert-Rejected '<broken' $format "$format malformed XML"
        Assert-Rejected ('<!DOCTYPE test-run [<!ENTITY value "unused">]>' + $content) $format "$format DTD is refused"
        Assert-Rejected ($content.Replace('total="2"', 'total="1"')) $format "$format inconsistent total"
        Assert-Rejected ($content.Replace('passed="2"', 'passed="1"')) $format "$format inconsistent passed count"
        Assert-Rejected ($content.Replace('inconclusive="0"', 'inconclusive="1"')) $format "$format inconclusive summary"
        Assert-Rejected ($content.Replace('failed="0"', '')) $format "$format missing summary counter"
        Assert-Rejected ($content.Replace('case-b', 'case-a')) $format "$format duplicate test ID"
    }
    $result = Read-Control $nunit NUnit
    Assert-Control ($result.RunId -ceq 'run-nunit' -and $result.Cases[0].FullName -ceq 'Sample.Fixture.Read(1)' -and
        $result.Cases[1].FullName -ceq 'Sample.Fixture.Custom display' -and $result.Cases[0].ExecutionId -ceq '') 'NUnit preserves exact full names and absent execution ID'
    $legacy = '<test-run result="Passed" testcasecount="1" total="1" passed="1" failed="0" skipped="0" inconclusive="0"><test-case fullname="Legacy.Case" result="Passed"/></test-run>'
    $result = Read-Control $legacy NUnit
    Assert-Control ($result.Cases[0].Fixture -ceq '' -and $result.Cases[0].AssemblyPath -ceq '' -and
        $result.Cases[0].TestId -ceq '' -and $result.RunId -ceq '' -and $result.StartedUtc -ceq '' -and $result.CompletedUtc -ceq '') 'NUnit legacy absent metadata remains absent'
    Assert-Rejected ($nunit.Replace('Sample.Fixture.Custom display', 'Sample.Fixture.Read(1)')) NUnit 'NUnit duplicate full name'
    Assert-Rejected ($nunit.Replace('fullname="Sample.Fixture.Read(1)"', '')) NUnit 'NUnit missing full name'
    Assert-Rejected ($nunit.Replace('result="Passed" testcasecount', 'result="Failed" testcasecount')) NUnit 'NUnit failed root with passed leaves'
    Assert-Rejected ($nunit.Replace('fullname="Sample.Fixture" result="Passed"', 'fullname="Sample.Fixture" result="Failed"')) NUnit 'NUnit failed fixture with passed leaves'
    Assert-Rejected ($nunit.Replace('</test-suite>', '<failure><message>teardown</message></failure></test-suite>')) NUnit 'NUnit teardown failure with passed leaves'
    foreach ($outcome in @('Failed', 'Skipped', 'Inconclusive', 'Warning')) {
        Assert-Rejected ($nunit.Replace('fullname="Sample.Fixture.Read(1)" result="Passed"', "fullname=`"Sample.Fixture.Read(1)`" result=`"$outcome`"")) NUnit "NUnit $outcome leaf"
    }
    Assert-Rejected ($nunit.Replace('<test-case id="case-a" classname="Sample.Fixture" fullname="Sample.Fixture.Read(1)" result="Passed"/>', '')) NUnit 'NUnit missing leaf'
    Assert-Rejected ($legacy.Replace('testcasecount="1" total="1" passed="1"', 'testcasecount="0" total="0" passed="0"').Replace('<test-case fullname="Legacy.Case" result="Passed"/>', '')) NUnit 'NUnit empty run'
    $result = Read-Control ($nunit.Replace('Read(1)', 'Read(2)')) NUnit
    Assert-Control ($result.Cases[0].FullName -ceq 'Sample.Fixture.Read(2)') 'NUnit changed argument remains visible for exact binding'

    $result = Read-Control $trx Trx
    Assert-Control ($result.RunId -ceq 'run-trx' -and $result.Cases[0].ExecutionId -ceq 'execution-a' -and
        $result.Cases[0].FullName -ceq 'Read(1)' -and $result.Cases[1].FullName -ceq 'Custom display') 'TRX preserves raw display and execution IDs without method inference'
    Assert-Rejected ($trx.Replace('http://microsoft.com/schemas/VisualStudio/TeamTest/2010', 'other-namespace')) Trx 'TRX wrong namespace'
    Assert-Rejected ($trx.Replace('outcome="Completed"', 'outcome="Failed"')) Trx 'TRX failed root summary with passed leaves'
    Assert-Rejected ($trx.Replace('</ResultSummary>', '<RunInfos><RunInfo outcome="Error"/></RunInfos></ResultSummary>')) Trx 'TRX lifecycle run error'
    Assert-Rejected ($trx.Replace('</ResultSummary>', '<Output><ErrorInfo><Message>teardown</Message></ErrorInfo></Output></ResultSummary>')) Trx 'TRX teardown error with passed leaves'
    foreach ($outcome in @('Failed', 'NotExecuted', 'Inconclusive', 'Aborted')) {
        Assert-Rejected ($trx.Replace('testName="Read(1)" outcome="Passed"', "testName=`"Read(1)`" outcome=`"$outcome`"")) Trx "TRX $outcome leaf"
    }
    $emptyTrx = [regex]::Replace($trx, '<Results>.*?</Results>|<TestDefinitions>.*?</TestDefinitions>|<TestEntries>.*?</TestEntries>', '', [Text.RegularExpressions.RegexOptions]::Singleline).Replace('total="2" executed="2" passed="2"', 'total="0" executed="0" passed="0"')
    Assert-Rejected $emptyTrx Trx 'TRX empty run'
    Assert-Rejected ($trx.Replace('Read(1)', 'Custom display')) Trx 'TRX duplicate fixture display identity'
    Assert-Rejected ($trx.Replace('execution-b', 'execution-a')) Trx 'TRX duplicate execution ID'
    Assert-Rejected ($trx.Replace('<Execution id="execution-a"/>', '<Execution id="different"/>')) Trx 'TRX definition execution mismatch'
    Assert-Rejected ($trx.Replace('<TestEntry testId="case-a" executionId="execution-a"/>', '<TestEntry testId="case-b" executionId="execution-a"/>')) Trx 'TRX execution entry test mismatch'
    Assert-Rejected ($trx.Replace('<TestEntry testId="case-a" executionId="execution-a"/>', '')) Trx 'TRX missing execution entry'
    Assert-Rejected ($trx.Replace('<UnitTestResult testId="case-a" executionId="execution-a" testName="Read(1)" outcome="Passed"/>', '')) Trx 'TRX missing result'
    Assert-Rejected ($trx.Replace('id="case-a" name="Read(1)"', 'id="different" name="Read(1)"')) Trx 'TRX unjoined definition'
    Assert-Rejected ($trx.Replace('id="case-a" name="Read(1)"', 'id="case-a" name="Read(2)"')) Trx 'TRX definition display mismatch'
    Assert-Rejected ($trx.Replace('className="Sample.Fixture"', '')) Trx 'TRX missing fixture'
    Assert-Rejected ($trx.Replace('codeBase="/compiled/tests.dll"', '')) Trx 'TRX missing assembly'
    Assert-Rejected ($trx.Replace('</Results>', '<OtherResult outcome="Passed"/></Results>')) Trx 'TRX unknown result element'
    $result = Read-Control ($trx.Replace('Read(1)', 'Read(2)')) Trx
    Assert-Control ($result.Cases[0].FullName -ceq 'Read(2)') 'TRX changed argument remains visible for exact binding'
    $otherFixture = $trx.Replace('Custom display', 'Read(1)').Replace('className="Sample.Fixture" name="OtherMethod"', 'className="Other.Fixture" name="OtherMethod"')
    $result = Read-Control $otherFixture Trx
    Assert-Control ($result.Cases.Count -eq 2 -and $result.Cases[1].Fixture -ceq 'Other.Fixture') 'TRX same display in distinct fixtures remains distinct'
    Assert-Rejected $nunit Trx 'NUnit artifact cannot pass as TRX'
    Assert-Rejected $trx NUnit 'TRX artifact cannot pass as NUnit'
    $oversizedPath = Join-Path $temporary 'oversized.xml'
    $stream = [IO.File]::Create($oversizedPath)
    try { $stream.SetLength(33554433) } finally { $stream.Dispose() }
    foreach ($path in @($oversizedPath, $temporary, (Join-Path $temporary 'missing.xml'))) {
        $rejected = $false
        try { Read-TestResultEvidence -Path $path -Format NUnit | Out-Null } catch { $rejected = $true }
        Assert-Control $rejected 'reject oversized, directory and missing artifact paths'
    }
    Write-Host "Test result evidence controls passed: $checks"
} finally {
    Remove-Item -LiteralPath $temporary -Recurse -Force
}
