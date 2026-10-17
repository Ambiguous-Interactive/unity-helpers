#!/usr/bin/env pwsh
# cspell:ignore sxd sxe epr
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EditorPath,
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [Parameter(Mandatory = $true)][string]$ArtifactsPath,
    [Parameter(Mandatory = $true)][string]$AssemblyNames,
    [string]$TestFilter = 'WallstopStudios.UnityHelpers.Tests.Tools.OdinMigration.OdinMigrationApiTests.SourceAnalysisExposesReadOnlyChangesAndFindings',
    [string]$DebuggerDirectory,
    [switch]$CompareLoaderSnaps,
    [ValidateRange(1, 1800)][int]$TimeoutSeconds = 600
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/credential-redaction.ps1')
. (Join-Path $PSScriptRoot 'lib/accelerator.ps1')
. (Join-Path $PSScriptRoot 'lib/native-global-flags.ps1')
. (Join-Path $PSScriptRoot 'lib/native-loader-flag-lease.ps1')

function Invoke-NativeCaptureProcess {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$Directory,
        [int]$Seconds,
        [hashtable]$Observation,
        [string]$ReceiptPath
    )

    $info = [Diagnostics.ProcessStartInfo]::new($FilePath)
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.WorkingDirectory = $Directory
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.Environment['_NT_SYMBOL_PATH'] = $Directory
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    $tail = [Text.StringBuilder]::new()
    $Observation.StartedUtc = [DateTime]::UtcNow.ToString('o')
    $Observation.Pid = $null
    $Observation.RawExitCode = $null
    $Observation.KillReason = $null
    $Observation.Reaped = $false
    $Observation.StdoutCharacters = 0L
    $Observation.StderrCharacters = 0L
    $Observation.ReadOperations = 0L
    try {
        if (-not $process.Start()) { throw 'Native process did not start' }
        $Observation.Pid = $process.Id
        if ($ReceiptPath) { $Observation | ConvertTo-Json | Set-Content -LiteralPath $ReceiptPath -Encoding utf8 }
        $readers = @($process.StandardOutput, $process.StandardError)
        $buffers = @([char[]]::new(4096), [char[]]::new(4096))
        $tasks = @($readers[0].ReadAsync($buffers[0], 0, 4096), $readers[1].ReadAsync($buffers[1], 0, 4096))
        $done = @($false, $false)
        $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
        while (-not ($done[0] -and $done[1] -and $process.HasExited)) {
            $progressed = $false
            for ($index = 0; $index -lt 2; ++$index) {
                $reads = 0
                while (-not $done[$index] -and $tasks[$index].IsCompleted -and $reads -lt 64) {
                    ++$reads
                    $progressed = $true
                    $count = $tasks[$index].GetAwaiter().GetResult()
                    if ($count -eq 0) { $done[$index] = $true } else {
                        ++$Observation.ReadOperations
                        if ($index -eq 0) { $Observation.StdoutCharacters += $count } else { $Observation.StderrCharacters += $count }
                        [void]$tail.Append($buffers[$index], 0, $count)
                        if ($tail.Length -gt 524288) { [void]$tail.Remove(0, $tail.Length - 524288) }
                        $tasks[$index] = $readers[$index].ReadAsync($buffers[$index], 0, 4096)
                    }
                }
            }
            if ([DateTime]::UtcNow -ge $deadline) {
                $Observation.KillReason = 'wall-clock-timeout'
                break
            }
            if (-not $progressed) {
                $pending = @()
                for ($index = 0; $index -lt 2; ++$index) {
                    if (-not $done[$index]) { $pending += $tasks[$index] }
                }
                if ($pending.Count -gt 0) {
                    [void][Threading.Tasks.Task]::WaitAny([Threading.Tasks.Task[]]$pending, 100)
                } else { [void]$process.WaitForExit(100) }
            }
        }
    } finally {
        try {
            if ($null -ne $Observation.Pid) {
                if (-not $process.HasExited) {
                    if ($null -eq $Observation.KillReason) { $Observation.KillReason = 'controller-finally' }
                    try { $process.Kill($true) } catch { if (-not $process.HasExited) { throw } }
                }
                $Observation.Reaped = $process.WaitForExit(10000)
                if ($Observation.Reaped) { $Observation.RawExitCode = $process.ExitCode }
            }
        } finally {
            $Observation.EndedUtc = [DateTime]::UtcNow.ToString('o')
            $Observation.ConsoleTailTruncated = ($Observation.StdoutCharacters + $Observation.StderrCharacters) -gt 524288
            try {
                if ($ReceiptPath) { $Observation | ConvertTo-Json | Set-Content -LiteralPath $ReceiptPath -Encoding utf8 }
            } finally { $process.Dispose() }
        }
    }
    return $tail.ToString()
}

function Read-NativeCaptureTail {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return '[unavailable at capture]' }
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    try {
        $length = $stream.Length
        $start = [Math]::Max(0, $length - 524288)
        [void]$stream.Seek($start, [IO.SeekOrigin]::Begin)
        $buffer = [byte[]]::new(524288)
        $remaining = [int]($length - $start)
        $count = 0
        while ($remaining -gt 0) {
            $read = $stream.Read($buffer, $count, $remaining)
            if ($read -eq 0) { break }
            $count += $read
            $remaining -= $read
        }
        return [Text.Encoding]::UTF8.GetString($buffer, 0, $count)
    } finally { $stream.Dispose() }
}

function Get-NativeCaptureExitEvent {
    param([string]$Text)
    $events = [regex]::Matches($Text, '(?im)^Last event: ([0-9a-f]+)\.[0-9a-f]+: Exit process [^\r\n]*, code ([0-9a-f]+)\s*$')
    if ($events.Count -ne 1) { return $null }
    return @{
        Pid = [Convert]::ToInt32($events[0].Groups[1].Value, 16)
        ExitCodeHex = $events[0].Groups[2].Value.ToUpperInvariant().PadLeft(8, '0')
    }
}

function Get-NativeCaptureLayout {
    param([string]$Root)
    return @{
        Tools = Join-Path $Root 'tools'
        Direct = Join-Path $Root 'direct'
        Debugger = Join-Path $Root 'debugger'
    }
}

function Get-NativeCaptureXmlOutcome {
    param([string]$Path, [string]$ExpectedCase)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return @{ State = 'missing' } }
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.MaxCharactersInDocument = 4194304
    $reader = $null
    try {
        $reader = [Xml.XmlReader]::Create($Path, $settings)
        $document = [Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($reader)
        $cases = @($document.SelectNodes('//test-case'))
        if ($cases.Count -ne 1 -or $cases[0].GetAttribute('fullname') -cne $ExpectedCase) {
            return @{ State = 'invalid'; Reason = 'unexpected-case-selection'; CaseCount = $cases.Count }
        }
        return @{ State = 'observed'; FullName = $ExpectedCase; Result = $cases[0].GetAttribute('result') }
    } catch {
        return @{ State = 'invalid'; Reason = $_.Exception.GetType().Name }
    } finally { if ($null -ne $reader) { $reader.Dispose() } }
}

function Test-NativeCaptureInputsUnchanged {
    param([string]$EditorPath, [string]$PackageLockPath, [System.Collections.IDictionary]$Record)

    return (Test-Path -LiteralPath $EditorPath -PathType Leaf) -and (Test-Path -LiteralPath $PackageLockPath -PathType Leaf) -and $Record.Contains('PackagesLockSha256') -and (Get-FileHash -LiteralPath $EditorPath -Algorithm SHA256).Hash -ceq $Record.EditorSha256 -and (Get-FileHash -LiteralPath $PackageLockPath -Algorithm SHA256).Hash -ceq $Record.PackagesLockSha256
}

if (-not $IsWindows) { throw 'Native capture requires Windows x64' }
if ([Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne 'X64') { throw 'Native capture requires an x64 controller' }
if (-not (Test-Path -LiteralPath $EditorPath -PathType Leaf)) { throw 'Editor is missing' }
if (-not (Test-Path -LiteralPath (Join-Path $ProjectPath 'ProjectSettings/ProjectVersion.txt') -PathType Leaf)) { throw 'Prepared Unity project is missing' }
$artifactRoot = [IO.Path]::GetFullPath($ArtifactsPath)
if (Test-Path -LiteralPath $artifactRoot) { throw 'Diagnostic artifact path must be fresh' }
[void](New-Item -ItemType Directory -Path $artifactRoot)
$privateRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) ('unity-helpers-native-debugger/' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $privateRoot)
$layout = Get-NativeCaptureLayout -Root $privateRoot
$record = [ordered]@{
    EvidenceOnly = $true
    State = 'preparing'
    StartedUtc = [DateTime]::UtcNow.ToString('o')
    Machine = $env:COMPUTERNAME
    OsVersion = [Environment]::OSVersion.VersionString
    ControllerPid = $PID
    GlobalFlagRegistry = Get-NativeGlobalFlagSnapshot
    LoaderFlagLease = $null
    LoaderFlagRestoreFailure = $null
    PrivateRoot = $privateRoot
    EditorPath = [IO.Path]::GetFullPath($EditorPath)
    EditorSha256 = (Get-FileHash -LiteralPath $EditorPath -Algorithm SHA256).Hash
    ProjectPath = [IO.Path]::GetFullPath($ProjectPath)
    ProjectVersion = [IO.File]::ReadAllText((Join-Path $ProjectPath 'ProjectSettings/ProjectVersion.txt'))
    TestFilter = $TestFilter
    Cases = @()
}
function Save-NativeCaptureRecord {
    $json = $record | ConvertTo-Json -Depth 12
    [IO.File]::WriteAllText((Join-Path $artifactRoot 'receipt.json'), (ConvertTo-UnitySafeLogText -Text $json))
}
$flagBase = $null
$flagKey = $null
try {
    Save-NativeCaptureRecord
    if ($CompareLoaderSnaps) {
        $flagBase = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
        $flagKey = $flagBase.OpenSubKey('SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\Unity.exe', $true)
        if ($null -eq $flagKey) { throw 'Unity.exe image flags are missing' }
        $record.LoaderFlagLease = New-NativeLoaderFlagLease -Key $flagKey
        Save-NativeLoaderFlagLease -Lease $record.LoaderFlagLease -Path ((Join-Path $artifactRoot 'loader-flag-lease.json') + '.prepared')
        Save-NativeCaptureRecord
    }
    if ([string]::IsNullOrWhiteSpace($DebuggerDirectory)) {
        $DebuggerDirectory = $layout.Tools
        $record.DownloadProcess = @{}
        $downloadOutput = Invoke-NativeCaptureProcess -FilePath (Get-Command python.exe -CommandType Application | Select-Object -First 1).Source `
            -Arguments @((Join-Path $PSScriptRoot 'download-native-debugger.py'), $DebuggerDirectory) `
            -Directory $privateRoot -Seconds 300 -Observation $record.DownloadProcess `
            -ReceiptPath (Join-Path $artifactRoot 'download-process.json')
        [IO.File]::WriteAllText((Join-Path $artifactRoot 'download-console.log'), (ConvertTo-UnitySafeLogText -Text $downloadOutput))
        Save-NativeCaptureRecord
        if ($record.DownloadProcess.RawExitCode -ne 0 -or $null -ne $record.DownloadProcess.KillReason -or -not $record.DownloadProcess.Reaped) {
            throw 'Native debugger download failed'
        }
    }
    $pins = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'native-debugger-files.json') | ConvertFrom-Json
    foreach ($pin in $pins.Files) {
        $path = Join-Path $DebuggerDirectory ($pin.Name.Substring('amd64/'.Length))
        if ((Get-Item -LiteralPath $path).Length -ne $pin.Bytes -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $pin.Sha256) {
            throw "Debugger file verification failed: $($pin.Name)"
        }
    }
    $debugger = Join-Path $DebuggerDirectory 'cdb.exe'
    $signature = Get-AuthenticodeSignature -LiteralPath $debugger
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
        throw 'Debugger signature verification failed'
    }
    $record.DebuggerSource = $pins.Source
    $record.DebuggerSha256 = (Get-FileHash -LiteralPath $debugger -Algorithm SHA256).Hash
    $versionMatch = [regex]::Match($record.ProjectVersion, '(?m)^m_EditorVersion: ([^\s]+)')
    if (-not $versionMatch.Success) { throw 'Prepared project has no editor version' }
    $acceleratorArguments = @(Get-AcceleratorArguments -Endpoint $env:UNITY_ACCELERATOR_ENDPOINT -Version $versionMatch.Groups[1].Value -Mode editmode)
    $lockPath = Join-Path $ProjectPath 'Packages/packages-lock.json'
    if (Test-Path -LiteralPath $lockPath -PathType Leaf) {
        if ((Get-Item -LiteralPath $lockPath).Length -gt 4194304) { throw 'Package lock exceeds diagnostic read bound' }
        $record.PackagesLockSha256 = (Get-FileHash -LiteralPath $lockPath -Algorithm SHA256).Hash
        $packageLock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
        $record.TestFramework = $packageLock.dependencies.'com.unity.test-framework'
    }
    $record.State = 'running'
    $modes = if ($CompareLoaderSnaps) { @('direct-before', 'direct-disabled', 'direct-restored') } else { @('direct', 'debugger') }
    foreach ($mode in $modes) {
        if ($CompareLoaderSnaps) {
            if (-not (Test-NativeCaptureInputsUnchanged -EditorPath $EditorPath -PackageLockPath $lockPath -Record $record)) { throw 'Comparison editor or package lock changed' }
            if ($mode -eq 'direct-disabled') { Disable-NativeLoaderFlag -Lease $record.LoaderFlagLease -Key $flagKey -LeasePath (Join-Path $artifactRoot 'loader-flag-lease.json') }
            if ($mode -eq 'direct-restored') { Restore-NativeLoaderFlag -Lease $record.LoaderFlagLease -Key $flagKey }
            Save-NativeCaptureRecord
        }
        $privateCase = if ($CompareLoaderSnaps) { Join-Path $privateRoot $mode } else { $layout[$mode] }
        [void](New-Item -ItemType Directory -Path $privateCase)
        $xmlPath = Join-Path $privateCase 'results.xml'
        $unityArguments = @('-batchmode', '-nographics', '-timestamps', '-releaseCodeOptimization', '-projectPath', $ProjectPath, '-runTests', '-testPlatform', 'editmode', '-assemblyNames', $AssemblyNames, '-testFilter', $TestFilter, '-testResults', $xmlPath, '-logFile', (Join-Path $privateCase 'unity.log'), '-crash-report-folder', $privateCase, '-upmLogFile', (Join-Path $privateCase 'upm.log')) + $acceleratorArguments
        $executable = $EditorPath
        $arguments = $unityArguments
        if ($mode -eq 'debugger') {
            $command = 'sxd -c2 ".exr -1;r;kv;lm;.dump /m fault.dmp;gn" av;sxe -c ".lastevent;q" epr;bu ntdll!RtlExitUserProcess ".echo WUH_EXIT_REQUEST;!gflag;r rcx;kv20;g";g'
            $executable = $debugger
            $arguments = @('-c', $command, $EditorPath) + $unityArguments
        }
        $observation = @{}
        $case = @{ Mode = $mode; Process = $observation; TargetExitEvent = $null; DumpPresent = $false }
        if ($CompareLoaderSnaps) { $case.GlobalFlagRegistry = Get-NativeGlobalFlagSnapshot }
        $record.Cases += $case
        Save-NativeCaptureRecord
        $output = Invoke-NativeCaptureProcess -FilePath $executable -Arguments $arguments -Directory $privateCase -Seconds $TimeoutSeconds -Observation $observation -ReceiptPath (Join-Path $artifactRoot "$mode-process.json")
        [IO.File]::WriteAllText((Join-Path $artifactRoot "$mode-console.log"), (ConvertTo-UnitySafeLogText -Text $output))
        if ($mode -eq 'debugger') { $case.TargetExitEvent = Get-NativeCaptureExitEvent -Text $output }
        foreach ($name in @('unity.log', 'upm.log')) {
            $tail = Read-NativeCaptureTail -Path (Join-Path $privateCase $name)
            [IO.File]::WriteAllText((Join-Path $artifactRoot "$mode-$name"), (ConvertTo-UnitySafeLogText -Text $tail))
        }
        $dump = Join-Path $privateCase 'fault.dmp'
        $case.DumpPresent = Test-Path -LiteralPath $dump -PathType Leaf
        if ($case.DumpPresent) {
            $case.DumpBytes = (Get-Item -LiteralPath $dump).Length
            if ($case.DumpBytes -le 268435456) { $case.DumpSha256 = (Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash }
        }
        $case.ConsoleTailLimitCharacters = 524288
        Save-NativeCaptureRecord
        $case.Xml = Get-NativeCaptureXmlOutcome -Path $xmlPath -ExpectedCase $TestFilter
        Save-NativeCaptureRecord
        if (-not $observation.Reaped -or $null -ne $observation.KillReason) { throw 'Diagnostic process did not exit naturally' }
        if ($mode -eq 'debugger' -and $null -eq $case.TargetExitEvent) { throw 'Debugger did not report a unique target exit event' }
        if ($CompareLoaderSnaps -and ($case.Xml.State -ne 'observed' -or $case.Xml.Result -ne 'Passed')) { throw 'Comparison requires exactly the selected passing case in every phase' }
        if ($CompareLoaderSnaps -and -not (Test-NativeCaptureInputsUnchanged -EditorPath $EditorPath -PackageLockPath $lockPath -Record $record)) { throw 'Comparison inputs changed during the phase' }
    }
    $record.State = 'completed'
    $record.EndedUtc = [DateTime]::UtcNow.ToString('o')
    Save-NativeCaptureRecord
    Write-Host 'Native diagnostic collection completed; these records cannot satisfy acceptance.'
} catch {
    $record.State = 'failed'
    $record.Failure = $_.Exception.Message
    Save-NativeCaptureRecord
    throw
} finally {
    try {
        if ($null -ne $flagKey -and $null -ne $record.LoaderFlagLease) {
            try { Complete-NativeLoaderFlagLease -Lease $record.LoaderFlagLease -Key $flagKey } catch {
                $record.State = 'restore-failed'
                $record.LoaderFlagRestoreFailure = $_.Exception.Message
                throw
            } finally { Save-NativeCaptureRecord }
        }
    } finally {
        if ($null -ne $flagKey) { $flagKey.Dispose() }
        if ($null -ne $flagBase) { $flagBase.Dispose() }
    }
}
