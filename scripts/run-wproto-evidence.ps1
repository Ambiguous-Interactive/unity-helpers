#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('v2', 'v3')][string]$Oracle,
    [guid]$RunId = [guid]::NewGuid(),
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$OutputRoot,
    [ValidateRange(1, 86400)][int]$BuildTimeoutSeconds = 900,
    [ValidateRange(1, 86400)][int]$TestTimeoutSeconds = 900
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'verify-wproto-evidence.ps1') -RepositoryRoot $RepositoryRoot

function Write-WProtoNewFile {
    param([string]$Path, [string]$Text)
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    $writer = [IO.StreamWriter]::new($stream, [Text.UTF8Encoding]::new($false))
    try { $writer.Write($Text) } finally { $writer.Dispose() }
}

function Get-WProtoArtifact {
    param([string]$Path)
    return [ordered]@{ path = [IO.Path]::GetFullPath($Path); sha256 = Get-WProtoFileHash $Path }
}

function Invoke-WProtoObservedProcess {
    param([string]$Executable, [string[]]$Arguments, [string]$WorkingDirectory, [string]$Prefix, [int]$TimeoutSeconds)
    $record = [ordered]@{
        executable = $Executable; executableSha256 = $null; arguments = $Arguments; workingDirectory = $WorkingDirectory
        observationScope = 'dotnet-parent-exit-and-captured-output'; pid = $null
        timestampSource = 'parent-utc-launch-and-wait-bounds'
        startedUtc = $null; endedUtc = $null; completed = $false; exitCode = $null
        timedOut = $false; terminationReason = $null; exception = $null
        osStartedUtc = $null; osEndedUtc = $null; osTimestampException = $null
    }
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $Executable
    $start.WorkingDirectory = $WorkingDirectory
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    $stdoutPath = $Prefix + '.stdout.log'
    $stderrPath = $Prefix + '.stderr.log'
    $stdout = [IO.File]::Open($stdoutPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    $stderr = [IO.File]::Open($stderrPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try {
        $record.executableSha256 = Get-WProtoFileHash $Executable
        $launchObservedUtc = [DateTimeOffset]::UtcNow
        if (-not $process.Start()) { throw 'Process did not start.' }
        $record.pid = $process.Id
        $record.startedUtc = $launchObservedUtc.ToString('o')
        try { $record.osStartedUtc = $process.StartTime.ToUniversalTime().ToString('o') } catch { $record.osTimestampException = $_.Exception.ToString() }
        $outCopy = $process.StandardOutput.BaseStream.CopyToAsync($stdout)
        $errCopy = $process.StandardError.BaseStream.CopyToAsync($stderr)
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $record.timedOut = $true
            $record.terminationReason = 'timeout-kill-process-tree'
            $process.Kill($true)
            if (-not $process.WaitForExit(10000)) { throw 'Process tree did not terminate after timeout.' }
        }
        $record.endedUtc = [DateTimeOffset]::UtcNow.ToString('o')
        $record.completed = $process.HasExited
        $record.exitCode = $process.ExitCode
        try { $record.osEndedUtc = $process.ExitTime.ToUniversalTime().ToString('o') } catch { $record.osTimestampException = [string]$record.osTimestampException + "`n" + $_.Exception.ToString() }
        if (-not [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($outCopy, $errCopy), 10000)) {
            $record.terminationReason = 'output-drain-timeout'
            throw 'Captured process streams did not close.'
        }
    } catch {
        $record.exception = $_.Exception.ToString()
        if ($null -ne $record.pid -and -not $process.HasExited) {
            $record.terminationReason = 'collector-error-kill-process-tree'
            try { $process.Kill($true); [void]$process.WaitForExit(10000) } catch { $record.exception += "`n" + $_.Exception.ToString() }
        }
    } finally {
        $stdout.Dispose()
        $stderr.Dispose()
        $process.Dispose()
    }
    $record.stdout = Get-WProtoArtifact $stdoutPath
    $record.stderr = Get-WProtoArtifact $stderrPath
    $logPath = $Prefix + '.log'
    Write-WProtoNewFile $logPath ("STDOUT (raw stream saved separately)`n" + [IO.File]::ReadAllText($stdoutPath) + "`nSTDERR (raw stream saved separately; no cross-stream ordering claimed)`n" + [IO.File]::ReadAllText($stderrPath))
    $record.log = Get-WProtoArtifact $logPath
    return $record
}

function Invoke-WProtoEvidenceRun {
    param([string]$Root, [string]$Destination, [string]$SelectedOracle, [guid]$InvocationId, [int]$BuildSeconds, [int]$TestSeconds)
    $Root = [IO.Path]::GetFullPath($Root)
    if ([string]::IsNullOrWhiteSpace($Destination)) { $Destination = Join-Path $Root 'progress/.artifacts/wproto-evidence' }
    $Destination = [IO.Path]::GetFullPath($Destination)
    [void][IO.Directory]::CreateDirectory($Destination)
    $identity = $InvocationId.ToString('D')
    if ($InvocationId -eq [guid]::Empty) { throw 'Invocation identity cannot be empty.' }
    $directory = Join-Path $Destination $identity
    if (Test-Path -LiteralPath $directory) { throw 'Invocation output directory already exists.' }
    Write-WProtoNewFile (Join-Path $Destination ($identity + '.reservation')) $directory
    [void][IO.Directory]::CreateDirectory($directory)
    $receipt = [ordered]@{
        schemaVersion = 1; runId = $identity; lane = 'host-' + $SelectedOracle
        sourceRevision = $null; sourceFiles = @(); sourceFilesAfterRun = @()
        catalogSha256 = $null; bindingsSha256 = $null; environment = $null
        sdkProcess = $null; build = $null; process = $null; binaryRoot = $null
        binaries = @(); binariesAfterRun = @(); results = $null; resultRunId = $null
        log = $null; buildLog = $null; sdkLog = $null; exception = $null
    }
    $receiptPath = Join-Path $directory 'receipt.json'
    $sourceInventoryCaptured = $false
    try {
        $model = Read-WProtoCatalog $Root
        $lane = @($model.catalog.lanes | Where-Object { $_.id -ceq $receipt.lane })
        if ($lane.Count -ne 1 -or $lane[0].backend -cne 'CoreCLR') { throw 'Only catalogued CoreCLR host lanes can be invoked.' }
        $receipt.catalogSha256 = Get-WProtoFileHash $model.catalogPath
        $receipt.bindingsSha256 = Get-WProtoFileHash $model.bindingsPath
        $receipt.sourceRevision = (& git -C $Root rev-parse HEAD) -join ''
        if ($LASTEXITCODE -ne 0) { throw 'Cannot identify source revision.' }
        $receipt.sourceFiles = @(Get-WProtoSourceInventory $Root)
        $sourceInventoryCaptured = $true
        $dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
        $receipt.sdkProcess = Invoke-WProtoObservedProcess $dotnet @('--version') $Root (Join-Path $directory 'sdk') 60
        $receipt.sdkLog = $receipt.sdkProcess.stdout
        Assert-WProtoProcess $receipt.sdkProcess 'SDK observation'
        $sdk = [IO.File]::ReadAllText($receipt.sdkProcess.stdout.path).Trim()
        if ($sdk -cnotmatch '^\d+\.\d+\.\d+[^\r\n]*$') { throw 'SDK version output is not a single version.' }
        $receipt.environment = [ordered]@{ backend = $lane[0].backend; profile = $lane[0].profile; oracle = $lane[0].oracle; configuration = 'Release'; sdkVersion = $sdk }
        $projectDirectory = Join-Path $Root 'Generator~/WallstopStudios.UnityHelpers.Proto.Generator.Tests'
        $project = Join-Path $projectDirectory 'WallstopStudios.UnityHelpers.Proto.Generator.Tests.csproj'
        $receipt.build = Invoke-WProtoObservedProcess $dotnet @('build', $project, '-c', 'Release', ('-p:ProtobufNetOracle=' + $SelectedOracle), '--no-incremental', '-p:UseSharedCompilation=false') $Root (Join-Path $directory 'build') $BuildSeconds
        $receipt.buildLog = $receipt.build.log
        Assert-WProtoProcess $receipt.build 'Release build'
        $receipt.binaryRoot = Join-Path $projectDirectory ('bin/' + $SelectedOracle + '/Release/net9.0')
        $receipt.binaries = @(Get-WProtoBinaryInventory $Root $receipt.binaryRoot)
        $receipt.process = Invoke-WProtoObservedProcess $dotnet @('test', $project, '-c', 'Release', ('-p:ProtobufNetOracle=' + $SelectedOracle), '--no-build', '--no-restore', '--logger', 'trx;LogFileName=results.trx', '--results-directory', $directory) $Root (Join-Path $directory 'test') $TestSeconds
        $receipt.log = $receipt.process.log
        $receipt.binariesAfterRun = @(Get-WProtoBinaryInventory $Root $receipt.binaryRoot)
        $resultPath = Join-Path $directory 'results.trx'
        if (Test-Path -LiteralPath $resultPath -PathType Leaf) {
            $receipt.results = Get-WProtoArtifact $resultPath
            $receipt.results.format = 'Trx'
        }
        Assert-WProtoProcess $receipt.process 'Ordinary host tests'
        if ($null -eq $receipt.results) { throw 'Test process produced no TRX.' }
        if ((ConvertTo-Json -InputObject $receipt.binaries -Depth 5 -Compress) -cne (ConvertTo-Json -InputObject $receipt.binariesAfterRun -Depth 5 -Compress)) { throw 'Binary inventory changed during tests.' }
        $result = Read-TestResultEvidence -Path $resultPath -Format Trx
        $receipt.resultRunId = $result.RunId
        $oracleCase = @($result.Cases | Where-Object { $_.Fixture -ceq 'WallstopStudios.UnityHelpers.Proto.Generator.Tests.OracleIdentityTests' -and $_.FullName -ceq 'LoadedOracleHasTheExpectedPhysicalIdentity' -and $_.Result -ceq 'Passed' })
        if ($oracleCase.Count -ne 1) { throw 'The physical oracle identity test did not pass exactly once.' }
    } catch { $receipt.exception = $_.Exception.ToString() }
    finally {
        try {
            if ($sourceInventoryCaptured) {
                $receipt.sourceFilesAfterRun = @(Get-WProtoSourceInventory $Root)
                if ((ConvertTo-Json -InputObject $receipt.sourceFiles -Depth 5 -Compress) -cne (ConvertTo-Json -InputObject $receipt.sourceFilesAfterRun -Depth 5 -Compress)) { throw 'Source inventory changed during invocation.' }
            }
        } catch { $receipt.exception = [string]$receipt.exception + "`n" + $_.Exception.ToString() }
        Write-WProtoNewFile $receiptPath (ConvertTo-Json -InputObject $receipt -Depth 20)
    }
    if ($null -ne $receipt.exception) { throw "Invocation did not qualify. Evidence retained at ${receiptPath}: $($receipt.exception)" }
    try {
        $verification = Assert-WProtoEvidence $Root $receiptPath $identity
        $verification.receiptPath = $receiptPath
        Write-WProtoNewFile (Join-Path $directory 'verification.json') (ConvertTo-Json -InputObject $verification -Depth 10)
        return $verification
    } catch {
        Write-WProtoNewFile (Join-Path $directory 'verification-failure.json') (ConvertTo-Json -InputObject ([ordered]@{ runId = $identity; receiptPath = $receiptPath; exception = $_.Exception.ToString() }) -Depth 5)
        throw
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    try { Invoke-WProtoEvidenceRun $RepositoryRoot $OutputRoot $Oracle $RunId $BuildTimeoutSeconds $TestTimeoutSeconds | ConvertTo-Json }
    catch { [Console]::Error.WriteLine($_.Exception.ToString()); exit 1 }
}
