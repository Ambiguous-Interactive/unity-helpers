#!/usr/bin/env pwsh
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../verify-wproto-evidence.ps1')
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('wproto evidence controls ' + [Guid]::NewGuid().ToString('N'))
$checks = 0
$failures = [Collections.Generic.List[string]]::new()

function Write-ControlFile {
    param([string]$Path, [string]$Content)
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path))
    [IO.File]::WriteAllText($Path, $Content)
}

function Write-ControlJson {
    param([string]$Path, $Value)
    Write-ControlFile $Path (ConvertTo-Json -InputObject $Value -Depth 30)
}

function New-ControlProcess {
    param([int]$Id, [string]$Started, [string]$Ended)
    return [ordered]@{
        completed = $true; exitCode = 0; pid = $Id; timedOut = $false; terminationReason = $null
        exception = $null; startedUtc = $Started; endedUtc = $Ended
        observationScope = 'dotnet-parent-exit-and-captured-output'
        timestampSource = 'parent-utc-launch-and-wait-bounds'
    }
}

function Get-ControlArtifact {
    param([string]$Path)
    return [ordered]@{ path = $Path; sha256 = Get-WProtoFileHash $Path }
}

function New-ControlTrx {
    param($Fixture, [switch]$Extra)
    $names = @('Read(1)', 'Other', 'LoadedOracleHasTheExpectedPhysicalIdentity')
    $fixtures = @('Sample.Fixture', 'Sample.Fixture', 'WallstopStudios.UnityHelpers.Proto.Generator.Tests.OracleIdentityTests')
    if ($Extra) { $names += 'Additional'; $fixtures += 'Extra.Fixture' }
    $results = [Text.StringBuilder]::new()
    $definitions = [Text.StringBuilder]::new()
    $entries = [Text.StringBuilder]::new()
    $assembly = [System.Security.SecurityElement]::Escape($Fixture.Assembly)
    for ($index = 0; $index -lt $names.Count; ++$index) {
        [void]$results.Append("<UnitTestResult testId=`"test-$index`" executionId=`"execution-$index`" testName=`"$($names[$index])`" outcome=`"Passed`"/>")
        [void]$definitions.Append("<UnitTest id=`"test-$index`" name=`"$($names[$index])`"><Execution id=`"execution-$index`"/><TestMethod adapterTypeName=`"executor://nunit3testexecutor/`" codeBase=`"$assembly`" className=`"$($fixtures[$index])`" name=`"$($names[$index])`"/></UnitTest>")
        [void]$entries.Append("<TestEntry testId=`"test-$index`" executionId=`"execution-$index`"/>")
    }
    return @"
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010" id="ordinary-trx-run">
<Times start="2026-10-18T12:03:01Z" finish="2026-10-18T12:03:10Z"/>
<Results>$results</Results><TestDefinitions>$definitions</TestDefinitions><TestEntries>$entries</TestEntries>
<ResultSummary outcome="Completed"><Counters total="$($names.Count)" executed="$($names.Count)" passed="$($names.Count)" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0"/></ResultSummary>
</TestRun>
"@
}

function Save-ControlTrx {
    param($Fixture, [string]$Content)
    Write-ControlFile $Fixture.Results $Content
    $Fixture.Record.results.sha256 = Get-WProtoFileHash $Fixture.Results
}

function Set-ControlProcessOutput {
    param($Fixture, [string]$ProcessField, [string]$StandardOutput, [string]$StandardError)
    $process = $Fixture.Record[$ProcessField]
    Write-ControlFile $process.stdout.path $StandardOutput
    Write-ControlFile $process.stderr.path $StandardError
    Write-ControlFile $process.log.path ("STDOUT (raw stream saved separately)`n" + $StandardOutput + "`nSTDERR (raw stream saved separately; no cross-stream ordering claimed)`n" + $StandardError)
    foreach ($stream in @('stdout', 'stderr', 'log')) { $process[$stream].sha256 = Get-WProtoFileHash $process[$stream].path }
    $rootField = if ($ProcessField -eq 'sdkProcess') { 'sdkLog' } elseif ($ProcessField -eq 'process') { 'log' } else { 'buildLog' }
    $Fixture.Record[$rootField] = if ($ProcessField -eq 'sdkProcess') { $process.stdout } else { $process.log }
}

function New-ControlFixture {
    $root = Join-Path $temporary 'repository'
    [void][IO.Directory]::CreateDirectory($root)
    & git -C $root init --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Cannot initialize isolated control repository.' }
    $hooks = Join-Path $temporary 'empty-hooks'
    [void][IO.Directory]::CreateDirectory($hooks)
    & git -C $root -c user.name=EvidenceControls -c user.email=controls@example.invalid -c "core.hooksPath=$hooks" -c commit.gpgsign=false commit --quiet --allow-empty -m 'Empty evidence control fixture'
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create isolated empty control commit.' }
    $revision = (& git -C $root rev-parse HEAD) -join ''
    Write-ControlFile (Join-Path $root '.gitignore') "**/bin/`nIgnoredByGit.cs`n"
    Write-ControlFile (Join-Path $root 'Runtime/Core/Serialization/WallstopProto/WProtoReader.cs') 'internal class WProtoReader {}'
    Write-ControlFile (Join-Path $root 'Runtime/PreservedInput.cs') 'internal class PreservedInput {}'
    Write-ControlFile (Join-Path $root 'Tests/Fixture.cs') 'namespace Sample { public class Fixture { [NUnit.Framework.TestCase(1)] public void Read(int value) {} [NUnit.Framework.Test] public void Other() {} } }'
    Write-ControlFile (Join-Path $root 'Tests/OracleIdentityTests.cs') 'namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests { public class OracleIdentityTests { [NUnit.Framework.Test] public void LoadedOracleHasTheExpectedPhysicalIdentity() {} } }'
    $guide = Join-Path $root 'docs/guide.md'
    Write-ControlFile $guide '# Synthetic receipt controls'
    $directory = Join-Path $root 'Generator~/WallstopStudios.UnityHelpers.Proto.Generator.Tests'
    $project = Join-Path $directory 'WallstopStudios.UnityHelpers.Proto.Generator.Tests.csproj'
    Write-ControlFile $project '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>'
    $binaryRoot = Join-Path $directory 'bin/v3/Release/net9.0'
    $assembly = Join-Path $binaryRoot 'WallstopStudios.UnityHelpers.Proto.Generator.Tests.dll'
    Write-ControlFile $assembly 'synthetic test assembly; never loaded or executed'
    Write-ControlFile (Join-Path $binaryRoot 'PreservedDependency.dll') 'synthetic optional output dependency'
    foreach ($name in @('WallstopStudios.UnityHelpers.Proto.Generator.dll', 'WallstopStudios.UnityHelpers.Analyzers.dll', 'WallstopStudios.UnityHelpers.OdinMigration.Generator.dll')) {
        Write-ControlFile (Join-Path $root "Runtime/Analyzers/$name") "synthetic analyzer $name; never loaded or executed"
    }
    foreach ($name in @('protobuf-net.dll', 'protobuf-net.Core.dll')) {
        $pinned = Join-Path $root "Runtime/Protobuf-Net/$name"
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($pinned))
        [IO.File]::Copy((Join-Path $repository "Runtime/Protobuf-Net/$name"), $pinned)
        [IO.File]::Copy($pinned, (Join-Path $binaryRoot $name))
    }
    $selectors = @(
        [ordered]@{ id = 'Fixture.Read'; sourcePath = 'Tests/Fixture.cs'; fixture = 'Sample.Fixture' },
        [ordered]@{ id = 'Fixture.Other'; sourcePath = 'Tests/Fixture.cs'; fixture = 'Sample.Fixture' },
        [ordered]@{ id = 'OracleIdentityTests.LoadedOracleHasTheExpectedPhysicalIdentity'; sourcePath = 'Tests/OracleIdentityTests.cs'; fixture = 'WallstopStudios.UnityHelpers.Proto.Generator.Tests.OracleIdentityTests' }
    )
    $catalog = [ordered]@{
        schemaVersion = 1; guidePath = 'docs/guide.md'; guideSha256 = Get-WProtoFileHash $guide
        lanes = @([ordered]@{ id = 'host-v3'; backend = 'CoreCLR'; profile = 'Release-net9.0-oracle-v3'; oracle = '3.2.56' })
        claims = @(
            [ordered]@{
                id = 'sample'; claim = 'Synthetic behavior witness'; guideAnchor = 'docs/guide.md'; classification = 'behavior'
                status = 'supported-with-witness'; readerDirections = @('current-write/current-read'); selectors = @($selectors.id); gapIds = @()
                laneRequirements = @([ordered]@{ lane = 'host-v3'; status = 'witnesses'; scope = 'complete'; selectors = @($selectors.id) })
            },
            [ordered]@{
                id = 'pending'; claim = 'Explicit remaining gap'; guideAnchor = 'docs/guide.md'; classification = 'behavior'
                status = 'evidence-gap'; readerDirections = @('current-write/current-read'); selectors = @(); gapIds = @('G1')
                laneRequirements = @([ordered]@{ lane = 'host-v3'; status = 'not-applicable'; scope = 'none'; selectors = @() })
            }
        )
        gaps = @([ordered]@{ id = 'G1'; status = 'open'; claimIds = @('pending') }); diagnostics = @()
    }
    $bindings = [ordered]@{
        schemaVersion = 1; selectors = $selectors
        fixtures = @(
            [ordered]@{ fixture = 'Sample.Fixture'; sourcePath = 'Tests/Fixture.cs'; lanes = @([ordered]@{ lane = 'host-v3'; status = 'discovered'; caseNames = @('Sample.Fixture.Read(1)', 'Sample.Fixture.Other') }) },
            [ordered]@{ fixture = 'WallstopStudios.UnityHelpers.Proto.Generator.Tests.OracleIdentityTests'; sourcePath = 'Tests/OracleIdentityTests.cs'; lanes = @([ordered]@{ lane = 'host-v3'; status = 'discovered'; caseNames = @('WallstopStudios.UnityHelpers.Proto.Generator.Tests.OracleIdentityTests.LoadedOracleHasTheExpectedPhysicalIdentity') }) }
        )
    }
    $fixture = [pscustomobject]@{
        Root = $root; BinaryRoot = $binaryRoot; Assembly = $assembly; Guide = $guide
        CatalogPath = (Join-Path $directory 'CompatibilityClaimCatalog.v1.json'); Catalog = $catalog
        BindingsPath = (Join-Path $directory 'CompatibilityCaseBindings.v1.json'); Bindings = $bindings
        Results = (Join-Path $root 'artifacts/results.trx'); Receipt = (Join-Path $root 'artifacts/receipt.json'); Record = $null
    }
    Write-ControlJson $fixture.CatalogPath $catalog
    Write-ControlJson $fixture.BindingsPath $bindings
    Write-ControlFile $fixture.Results (New-ControlTrx $fixture)
    foreach ($name in @('sdk', 'build', 'test')) {
        $content = if ($name -eq 'sdk') { '9.0.203' } else { 'Synthetic successful process output; no process was run.' }
        Write-ControlFile (Join-Path $root "artifacts/$name.stdout.log") $content
        Write-ControlFile (Join-Path $root "artifacts/$name.stderr.log") ''
        Write-ControlFile (Join-Path $root "artifacts/$name.log") ("STDOUT (raw stream saved separately)`n" + $content + "`nSTDERR (raw stream saved separately; no cross-stream ordering claimed)`n")
    }
    $executable = Join-Path $root 'artifacts/dotnet'
    Write-ControlFile $executable 'synthetic executable; never launched'
    $sources = @(Get-WProtoSourceInventory $root)
    $binaries = @(Get-WProtoBinaryInventory $root $binaryRoot)
    $record = [ordered]@{
        schemaVersion = 1; runId = 'synthetic-receipt-control'; lane = 'host-v3'; exception = $null
        catalogSha256 = Get-WProtoFileHash $fixture.CatalogPath; bindingsSha256 = Get-WProtoFileHash $fixture.BindingsPath
        sourceRevision = $revision; sourceFiles = $sources; sourceFilesAfterRun = $sources
        binaryRoot = $binaryRoot; binaries = $binaries; binariesAfterRun = $binaries; resultRunId = 'ordinary-trx-run'
        environment = [ordered]@{ backend = 'CoreCLR'; profile = 'Release-net9.0-oracle-v3'; oracle = '3.2.56'; configuration = 'Release'; sdkVersion = '9.0.203' }
        sdkProcess = New-ControlProcess 101 '2026-10-18T12:00:00Z' '2026-10-18T12:01:00Z'
        build = New-ControlProcess 102 '2026-10-18T12:01:00Z' '2026-10-18T12:02:00Z'
        process = New-ControlProcess 103 '2026-10-18T12:03:00Z' '2026-10-18T12:04:00Z'
        results = [ordered]@{ path = $fixture.Results; sha256 = Get-WProtoFileHash $fixture.Results; format = 'Trx' }
        sdkLog = Get-ControlArtifact (Join-Path $root 'artifacts/sdk.stdout.log')
        buildLog = Get-ControlArtifact (Join-Path $root 'artifacts/build.log')
        log = Get-ControlArtifact (Join-Path $root 'artifacts/test.log')
    }
    foreach ($field in @('sdkProcess', 'build', 'process')) {
        $record[$field].executable = $executable
        $record[$field].executableSha256 = Get-WProtoFileHash $executable
        $record[$field].workingDirectory = $root
        $prefix = if ($field -eq 'sdkProcess') { 'sdk' } elseif ($field -eq 'process') { 'test' } else { 'build' }
        foreach ($stream in @('stdout', 'stderr', 'log')) {
            $suffix = if ($stream -eq 'log') { '.log' } else { ".$stream.log" }
            $record[$field][$stream] = Get-ControlArtifact (Join-Path $root ("artifacts/$prefix" + $suffix))
        }
    }
    $record.sdkProcess.arguments = @('--version')
    $record.build.arguments = @('build', $project, '-c', 'Release', '-p:ProtobufNetOracle=v3', '--no-incremental', '-p:UseSharedCompilation=false')
    $record.process.arguments = @('test', $project, '-c', 'Release', '-p:ProtobufNetOracle=v3', '--no-build', '--no-restore', '--logger', 'trx;LogFileName=results.trx', '--results-directory', (Split-Path $fixture.Results -Parent))
    $fixture.Record = $record
    Write-ControlJson $fixture.Receipt $record
    return $fixture
}

try {
    $fixture = New-ControlFixture
    $baseline = [Collections.Generic.Dictionary[string,byte[]]]::new([StringComparer]::Ordinal)
    foreach ($file in Get-ChildItem -LiteralPath $fixture.Root -File -Recurse -Force) {
        if ($file.FullName.StartsWith((Join-Path $fixture.Root '.git') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) { continue }
        $baseline.Add($file.FullName, [IO.File]::ReadAllBytes($file.FullName))
    }
    function Restore-ControlFixture {
        foreach ($file in Get-ChildItem -LiteralPath $fixture.Root -File -Recurse -Force) {
            if ($file.FullName.StartsWith((Join-Path $fixture.Root '.git') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) { continue }
            if (-not $baseline.ContainsKey($file.FullName)) { [IO.File]::Delete($file.FullName) }
        }
        foreach ($entry in $baseline.GetEnumerator()) { [IO.File]::WriteAllBytes($entry.Key, $entry.Value) }
        $fixture.Catalog = Read-WProtoJson $fixture.CatalogPath
        $fixture.Bindings = Read-WProtoJson $fixture.BindingsPath
        $fixture.Record = Read-WProtoJson $fixture.Receipt
    }
    $accepted = Assert-WProtoEvidence $fixture.Root $fixture.Receipt 'synthetic-receipt-control'
    if ($accepted.fixtureEvidenceAccepted -ne 2 -or $accepted.completeCatalogQualified -ne $false -or $accepted.unboundFixtures.Count -ne 0) { throw 'Positive synthetic receipt control did not preserve partial qualification.' }
    ++$checks
    $mutations = @(
        @{ Name = 'wrong invocation'; Change = { param($f) $f.Record.runId = 'another-run' } },
        @{ Name = 'wrong build command'; Change = { param($f) $f.Record.build.arguments = @('--version') } },
        @{ Name = 'wrong test command'; Change = { param($f) $f.Record.process.arguments = @('--version') } },
        @{ Name = 'wrong SDK command'; Change = { param($f) $f.Record.sdkProcess.arguments = @('test') } },
        @{ Name = 'wrong working directory'; Change = { param($f) $f.Record.process.workingDirectory = $temporary } },
        @{ Name = 'changed executable bytes'; Change = { param($f) Write-ControlFile $f.Record.process.executable 'different executable' } },
        @{ Name = 'missing executable hash'; Change = { param($f) $f.Record.process.Remove('executableSha256') } },
        @{ Name = 'wrong source revision'; Change = { param($f) $f.Record.sourceRevision = 'different' } },
        @{ Name = 'stale catalog hash'; Change = { param($f) $f.Record.catalogSha256 = 'different' } },
        @{ Name = 'stale binding hash'; Change = { param($f) $f.Record.bindingsSha256 = 'different' } },
        @{ Name = 'wrong profile'; Change = { param($f) $f.Record.environment.profile = 'Debug' } },
        @{ Name = 'wrong oracle'; Change = { param($f) $f.Record.environment.oracle = '2.4.9' } },
        @{ Name = 'unobserved native lane'; Change = { param($f) $f.Record.environment.backend = 'Unity-Editor-Mono' } },
        @{ Name = 'wrong SDK observation'; Change = { param($f) $f.Record.environment.sdkVersion = '9.0.999' } },
        @{ Name = 'missing process PID'; Change = { param($f) $f.Record.process.Remove('pid') } },
        @{ Name = 'missing process exception metadata'; Change = { param($f) $f.Record.process.Remove('exception') } },
        @{ Name = 'missing process termination metadata'; Change = { param($f) $f.Record.process.Remove('terminationReason') } },
        @{ Name = 'missing build observation'; Change = { param($f) $f.Record.Remove('build') } },
        @{ Name = 'nonzero test exit'; Change = { param($f) $f.Record.process.exitCode = 1 } },
        @{ Name = 'nonzero build exit'; Change = { param($f) $f.Record.build.exitCode = 1 } },
        @{ Name = 'nonzero SDK exit'; Change = { param($f) $f.Record.sdkProcess.exitCode = 1 } },
        @{ Name = 'timed out process'; Change = { param($f) $f.Record.process.timedOut = $true } },
        @{ Name = 'terminated process'; Change = { param($f) $f.Record.process.terminationReason = 'timeout' } },
        @{ Name = 'process exception'; Change = { param($f) $f.Record.process.exception = 'observation failed' } },
        @{ Name = 'collector exception'; Change = { param($f) $f.Record.exception = 'collection failed' } },
        @{ Name = 'unknown observation scope'; Change = { param($f) $f.Record.process.observationScope = 'unobserved' } },
        @{ Name = 'missing timestamp source'; Change = { param($f) $f.Record.process.Remove('timestampSource') } },
        @{ Name = 'mixed operating system timestamp source'; Change = { param($f) $f.Record.process.timestampSource = 'process-start-and-exit-properties' } },
        @{ Name = 'schema boolean'; Change = { param($f) $f.Record.schemaVersion = $true } },
        @{ Name = 'schema string'; Change = { param($f) $f.Record.schemaVersion = '1' } },
        @{ Name = 'string PID'; Change = { param($f) $f.Record.process.pid = '103' } },
        @{ Name = 'string completion'; Change = { param($f) $f.Record.process.completed = 'true' } },
        @{ Name = 'string timeout'; Change = { param($f) $f.Record.process.timedOut = 'false' } },
        @{ Name = 'string exit'; Change = { param($f) $f.Record.process.exitCode = '0' } },
        @{ Name = 'array backend'; Change = { param($f) $f.Record.environment.backend = @('CoreCLR') } },
        @{ Name = 'array binary role'; Change = { param($f) $f.Record.binaries[0].role = @($f.Record.binaries[0].role) } },
        @{ Name = 'object source inventory'; Change = { param($f) $f.Record.sourceFiles = @{} } },
        @{ Name = 'reversed process interval'; Change = { param($f) $f.Record.process.endedUtc = '2026-10-18T12:02:00Z' } },
        @{ Name = 'overlapping build and test'; Change = { param($f) $f.Record.build.endedUtc = '2026-10-18T12:03:30Z' } },
        @{ Name = 'source census addition'; Change = { param($f) Write-ControlFile (Join-Path $f.Root 'Tests/Additional.cs') 'class Additional {}' } },
        @{ Name = 'source census deletion'; Change = { param($f) [IO.File]::Delete((Join-Path $f.Root 'Runtime/PreservedInput.cs')) } },
        @{ Name = 'ignored source census addition'; Change = { param($f) Write-ControlFile (Join-Path $f.Root 'Generator~/WallstopStudios.UnityHelpers.Proto.Generator.Tests/IgnoredByGit.cs') 'class IgnoredByGit {}' } },
        @{ Name = 'source byte change'; Change = { param($f) Write-ControlFile (Join-Path $f.Root 'Runtime/Core/Serialization/WallstopProto/WProtoReader.cs') 'class ChangedReader {}' } },
        @{ Name = 'source after snapshot change'; Change = { param($f) $f.Record.sourceFilesAfterRun[0].sha256 = 'different' } },
        @{ Name = 'binary byte change'; Change = { param($f) Write-ControlFile $f.Assembly 'changed assembly' } },
        @{ Name = 'binary census addition'; Change = { param($f) Write-ControlFile (Join-Path $f.BinaryRoot 'Additional.dll') 'added dependency' } },
        @{ Name = 'uppercase binary census addition'; Change = { param($f) Write-ControlFile (Join-Path $f.BinaryRoot 'Uppercase.DLL') 'added uppercase dependency' } },
        @{ Name = 'binary census deletion'; Change = { param($f) [IO.File]::Delete((Join-Path $f.BinaryRoot 'PreservedDependency.dll')) } },
        @{ Name = 'missing analyzer'; Change = { param($f) [IO.File]::Delete((Join-Path $f.Root 'Runtime/Analyzers/WallstopStudios.UnityHelpers.Analyzers.dll')) } },
        @{ Name = 'wrong pinned output oracle'; Change = { param($f) Write-ControlFile (Join-Path $f.BinaryRoot 'protobuf-net.dll') 'wrong oracle'; $f.Record.binaries = @(Get-WProtoBinaryInventory $f.Root $f.BinaryRoot); $f.Record.binariesAfterRun = $f.Record.binaries } },
        @{ Name = 'wrong result run'; Change = { param($f) $f.Record.resultRunId = 'other-result' } },
        @{ Name = 'missing result artifact'; Change = { param($f) [IO.File]::Delete($f.Results) } },
        @{ Name = 'stale result hash'; Change = { param($f) $f.Record.results.sha256 = 'different' } },
        @{ Name = 'missing test log'; Change = { param($f) [IO.File]::Delete($f.Record.log.path) } },
        @{ Name = 'missing case'; Change = { param($f) Save-ControlTrx $f ([IO.File]::ReadAllText($f.Results).Replace('<UnitTestResult testId="test-0" executionId="execution-0" testName="Read(1)" outcome="Passed"/>', '')) } },
        @{ Name = 'duplicate result identity'; Change = { param($f) Save-ControlTrx $f ([IO.File]::ReadAllText($f.Results).Replace('test-1', 'test-0')) } },
        @{ Name = 'changed case argument'; Change = { param($f) Save-ControlTrx $f ([IO.File]::ReadAllText($f.Results).Replace('Read(1)', 'Read(2)')) } },
        @{ Name = 'skipped case'; Change = { param($f) Save-ControlTrx $f ([IO.File]::ReadAllText($f.Results).Replace('testName="Read(1)" outcome="Passed"', 'testName="Read(1)" outcome="NotExecuted"')) } },
        @{ Name = 'inconclusive case'; Change = { param($f) Save-ControlTrx $f ([IO.File]::ReadAllText($f.Results).Replace('testName="Read(1)" outcome="Passed"', 'testName="Read(1)" outcome="Inconclusive"')) } },
        @{ Name = 'teardown failure'; Change = { param($f) Save-ControlTrx $f ([IO.File]::ReadAllText($f.Results).Replace('</ResultSummary>', '<Output><ErrorInfo><Message>teardown failed</Message></ErrorInfo></Output></ResultSummary>')) } },
        @{ Name = 'reversed result interval'; Change = { param($f) Save-ControlTrx $f ([IO.File]::ReadAllText($f.Results).Replace('12:03:01Z', '12:05:00Z').Replace('12:03:10Z', '12:02:00Z')) } },
        @{ Name = 'wrong assembly on extra case'; Change = { param($f) Save-ControlTrx $f ((New-ControlTrx $f -Extra).Replace("codeBase=`"$([System.Security.SecurityElement]::Escape($f.Assembly))`" className=`"Extra.Fixture`"", 'codeBase="/other/assembly.dll" className="Extra.Fixture"')) } },
        @{ Name = 'wrong adapter identity'; Change = { param($f) Save-ControlTrx $f ([IO.File]::ReadAllText($f.Results).Replace('executor://nunit3testexecutor/', 'executor://different/')) } },
        @{ Name = 'missing adapter identity'; Change = { param($f) Save-ControlTrx $f ([IO.File]::ReadAllText($f.Results).Replace('adapterTypeName="executor://nunit3testexecutor/" ', '')) } },
        @{ Name = 'missing oracle identity witness'; Change = { param($f) Save-ControlTrx $f ([IO.File]::ReadAllText($f.Results).Replace('LoadedOracleHasTheExpectedPhysicalIdentity', 'OtherIdentity')) } },
        @{ Name = 'native fault in captured build log'; Change = { param($f) Set-ControlProcessOutput $f 'build' 'Got a UNKNOWN while executing native code' '' } },
        @{ Name = 'native fault in captured test log'; Change = { param($f) Set-ControlProcessOutput $f 'process' 'Native Crash Reporting' '' } },
        @{ Name = 'native fault in captured stderr'; Change = { param($f) Set-ControlProcessOutput $f 'process' 'Successful test results' 'Native Crash Reporting' } },
        @{ Name = 'missing stdout artifact'; Change = { param($f) [IO.File]::Delete($f.Record.process.stdout.path) } },
        @{ Name = 'raw stdout hash drift'; Change = { param($f) Write-ControlFile $f.Record.process.stdout.path 'Changed raw output' } },
        @{ Name = 'root log not bound to test process'; Change = { param($f) $f.Record.log = $f.Record.build.log } }
    )
    foreach ($mutation in $mutations) {
        Restore-ControlFixture
        & $mutation.Change $fixture | Out-Null
        Write-ControlJson $fixture.Receipt $fixture.Record
        $rejected = $false
        try { Assert-WProtoEvidence $fixture.Root $fixture.Receipt 'synthetic-receipt-control' | Out-Null } catch { $rejected = $true }
        if (-not $rejected) { $failures.Add("Unexpected acceptance: $($mutation.Name)") }
        ++$checks
    }
    $catalogMutations = @(
        @{ Name = 'duplicate claim ID'; Change = { param($f) $f.Catalog.claims[1].id = 'sample' } },
        @{ Name = 'missing selector'; Change = { param($f) $f.Catalog.claims[0].selectors = @('Fixture.Missing') } },
        @{ Name = 'zero witness lane'; Change = { param($f) $f.Catalog.claims[0].laneRequirements[0].selectors = @() } },
        @{ Name = 'nonreciprocal gap'; Change = { param($f) $f.Catalog.gaps[0].claimIds = @('sample') } },
        @{ Name = 'unknown gap'; Change = { param($f) $f.Catalog.claims[1].gapIds = @('Missing') } },
        @{ Name = 'unknown classification'; Change = { param($f) $f.Catalog.claims[0].classification = 'same-everywhere' } },
        @{ Name = 'catalog typed schema'; Change = { param($f) $f.Catalog.schemaVersion = $true } },
        @{ Name = 'catalog array replaced by object'; Change = { param($f) $f.Catalog.claims = @{} } },
        @{ Name = 'duplicate selector ID'; Change = { param($f) $f.Bindings.selectors[1].id = $f.Bindings.selectors[0].id } },
        @{ Name = 'missing source file'; Change = { param($f) $f.Bindings.selectors[0].sourcePath = 'Tests/Missing.cs' } },
        @{ Name = 'wrong source fixture'; Change = { param($f) $f.Bindings.selectors[0].fixture = 'Other.Fixture' } },
        @{ Name = 'duplicate bound case'; Change = { param($f) $f.Bindings.fixtures[0].lanes[0].caseNames[1] = $f.Bindings.fixtures[0].lanes[0].caseNames[0] } },
        @{ Name = 'empty discovered fixture'; Change = { param($f) $f.Bindings.fixtures[0].lanes[0].caseNames = @() } },
        @{ Name = 'nonempty undiscovered fixture'; Change = { param($f) $f.Bindings.fixtures[0].lanes[0].status = 'missing-discovery' } },
        @{ Name = 'wrong fixture case namespace'; Change = { param($f) $f.Bindings.fixtures[0].lanes[0].caseNames[0] = 'Other.Fixture.Read(1)' } },
        @{ Name = 'stale guide'; Change = { param($f) Write-ControlFile $f.Guide '# Changed guide' } }
    )
    foreach ($mutation in $catalogMutations) {
        Restore-ControlFixture
        & $mutation.Change $fixture | Out-Null
        Write-ControlJson $fixture.CatalogPath $fixture.Catalog
        Write-ControlJson $fixture.BindingsPath $fixture.Bindings
        $rejected = $false
        try { Read-WProtoCatalog $fixture.Root | Out-Null } catch { $rejected = $true }
        if (-not $rejected) { $failures.Add("Unexpected catalog acceptance: $($mutation.Name)") }
        ++$checks
    }
    Restore-ControlFixture
    Save-ControlTrx $fixture (New-ControlTrx $fixture -Extra)
    Write-ControlJson $fixture.Receipt $fixture.Record
    $accepted = Assert-WProtoEvidence $fixture.Root $fixture.Receipt 'synthetic-receipt-control'
    if ($accepted.unboundFixtures.Count -ne 1 -or $accepted.unboundFixtures[0] -cne 'Extra.Fixture' -or $accepted.completeCatalogQualified -ne $false) { $failures.Add('Unbound positive fixture was not reported separately.') }
    ++$checks
    Restore-ControlFixture
    $text = [IO.File]::ReadAllText($fixture.Receipt).Replace('"schemaVersion": 1,', '"schemaVersion": 1, "SchemaVersion": 1,')
    Write-ControlFile $fixture.Receipt $text
    $rejected = $false
    try { Assert-WProtoEvidence $fixture.Root $fixture.Receipt 'synthetic-receipt-control' | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { $failures.Add('Duplicate JSON member was accepted.') }
    ++$checks
    if ($failures.Count -gt 0) { throw ($failures -join "`n") }
    Write-Host "WProto evidence receipt-graph controls passed: $checks; synthetic artifacts do not qualify a build or serializer."
} finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}
