#!/usr/bin/env pwsh
# cspell:ignore sxd sxe epr
[CmdletBinding(DefaultParameterSetName = 'Capture', PositionalBinding = $false)]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Capture')]
    [Parameter(Mandatory = $true, ParameterSetName = 'Build')][string]$EditorPath,
    [Parameter(Mandatory = $true, ParameterSetName = 'Capture')]
    [Parameter(Mandatory = $true, ParameterSetName = 'Build')][string]$ProjectPath,
    [Parameter(Mandatory = $true)][string]$ArtifactsPath,
    [Parameter(Mandatory = $true, ParameterSetName = 'Capture')][string]$AssemblyNames,
    [Parameter(ParameterSetName = 'Capture')][string]$TestFilter = 'WallstopStudios.UnityHelpers.Tests.Tools.OdinMigration.OdinMigrationApiTests.SourceAnalysisExposesReadOnlyChangesAndFindings',
    [Parameter(ParameterSetName = 'Capture')]
    [Parameter(ParameterSetName = 'Build')][string]$DebuggerDirectory,
    [Parameter(Mandatory = $true, ParameterSetName = 'Build')][string[]]$BuildArguments,
    [Parameter(Mandatory = $true, ParameterSetName = 'Build')][string]$BuildLogPath,
    [Parameter(ParameterSetName = 'Build')][switch]$UseStandardHeap,
    [Parameter(ParameterSetName = 'Build')][switch]$AttachToDirect,
    [Parameter(ParameterSetName = 'Build')][switch]$AttachAfterStartup,
    [Parameter(ParameterSetName = 'Build')][switch]$AttachAfterShutdown,
    [Parameter(Mandatory = $true, ParameterSetName = 'Worker')][hashtable]$WorkerSpecification,
    [Parameter(ParameterSetName = 'Capture')][switch]$CompareLoaderSnaps,
    [Parameter(ParameterSetName = 'Capture')]
    [Parameter(ParameterSetName = 'Build')][ValidateRange(1, 1800)][int]$TimeoutSeconds = 600,
    [Parameter(Mandatory = $true, ParameterSetName = 'Readback')][string]$ExecutionDiagnosticsPath,
    [Parameter(Mandatory = $true, ParameterSetName = 'Readback')][string]$ExpectedMachine,
    [Parameter(ParameterSetName = 'Readback')][string]$TestFrameworkLogPath,
    [Parameter(Mandatory = $true, ParameterSetName = 'SelectReadback')][string]$SourceRunId,
    [Parameter(Mandatory = $true, ParameterSetName = 'SelectReadback')][string]$Repository,
    [Parameter(ParameterSetName = 'SelectReadback')][switch]$BuildReceipt,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$UnboundArguments
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($UnboundArguments -and $UnboundArguments.Count -gt 0) { throw 'Unbound arguments are not accepted; supply argument arrays from PowerShell.' }
. (Join-Path $PSScriptRoot 'lib/credential-redaction.ps1')
. (Join-Path $PSScriptRoot 'lib/native-evidence.ps1')
. (Join-Path $PSScriptRoot 'lib/accelerator.ps1')
. (Join-Path $PSScriptRoot 'lib/native-global-flags.ps1')
. (Join-Path $PSScriptRoot 'lib/native-loader-flag-lease.ps1')
. (Join-Path $PSScriptRoot 'lib/native-attach.ps1')

function Invoke-NativeCaptureProcess {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$Directory,
        [int]$Seconds,
        [hashtable]$Observation,
        [string]$ReceiptPath,
        [switch]$PreserveSymbolPath,
        [hashtable]$Coordination,
        [string]$HeadPath,
        [string]$TailPath
    )

    $info = [Diagnostics.ProcessStartInfo]::new($FilePath)
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.WorkingDirectory = $Directory
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    if (-not $PreserveSymbolPath) { $info.Environment['_NT_SYMBOL_PATH'] = $Directory }
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    $tail = [Text.StringBuilder]::new()
    $head = [Text.StringBuilder]::new()
    $startup = $null
    if ($null -ne $Coordination -and $Coordination.ContainsKey('StartupMarkerPrefix')) {
        $startup = @{ Pending = ''; Discarding = $false; Completed = $false }
    }
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
        if ($null -ne $Coordination) { Write-NativeAttachIdentity -Process $process -Coordination $Coordination }
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
                        if ($HeadPath -and $head.Length -lt 524288) {
                            [void]$head.Append($buffers[$index], 0, [Math]::Min($count, 524288 - $head.Length))
                        }
                        if ($index -eq 0 -and $null -ne $startup) {
                            Add-NativeAttachStartupOutput -Coordination $Coordination -State $startup -Buffer $buffers[$index] -Count $count -ProcessId $Observation.Pid -DeliveredCharacters $Observation.StdoutCharacters
                        }
                        if ($tail.Length -gt 524288) { [void]$tail.Remove(0, $tail.Length - 524288) }
                        $tasks[$index] = $readers[$index].ReadAsync($buffers[$index], 0, 4096)
                    }
                }
            }
            if ($null -ne $Coordination -and (Test-Path -LiteralPath $Coordination.CancelPath -PathType Leaf)) {
                $Observation.KillReason = 'coordinator-cancellation'
                break
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
                if ($HeadPath) { [IO.File]::WriteAllText($HeadPath, (ConvertTo-UnitySafeLogText -Text $head.ToString())) }
                if ($TailPath) { [IO.File]::WriteAllText($TailPath, (ConvertTo-UnitySafeLogText -Text $tail.ToString())) }
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

function Get-NativeCaptureDebuggerCommandBudget {
    param([string]$Command)
    return [ordered]@{ Maximum = 4095; Actual = $Command.Length; Admitted = ($Command.Length -le 4095) }
}

function Get-NativeCaptureDebuggerCommand {
    param([string]$DumpPath = 'fault.dmp')
    if ($DumpPath.IndexOfAny([char[]]@([char]34, [char]10, [char]13)) -ge 0) { throw 'Invalid debugger dump path.' }
    $command = 'sxd -c2 ".exr -1;r;kv;lm;.dump /m fault.dmp;gn" av;r @$t0=0;sx- -c ".if (@$t0 < 8) { .echo WUH_FIRST_CHANCE_AV;.exr -1;r;kv20 };r @$t0=@$t0+1" av;sx;.echo WUH_EXCEPTION_FILTERS_END;sxe -c ".lastevent;q" epr;bu ntdll!NtTerminateProcess ".echo WUH_TERMINATE_REQUEST;r rcx,rdx;kv20;g";bu ntdll!RtlExitUserProcess ".echo WUH_EXIT_REQUEST;!gflag;r rcx;kv20;g";bu ntdll!RtlExitUserThread ".echo WUH_THREAD_EXIT;?@$tid;r rcx;kv20;~;.echo WUH_THREAD_EXIT_END;g";bu ntdll!NtTerminateThread ".echo WUH_THREAD_TERMINATE;?@$tid;r rcx,rdx;kv20;~;.echo WUH_THREAD_TERMINATE_END;g";.echo WUH_BREAKPOINTS_BEGIN;bl /L;.echo WUH_BREAKPOINTS_END;g'
    if ($DumpPath -cne 'fault.dmp') { return $command.Replace('fault.dmp', '\"' + $DumpPath.Replace('\', '\\') + '\"') }
    return $command
}

function Invoke-NativeStandaloneBuildCapture {
    param(
        [string]$EditorPath, [string]$ProjectPath, [string[]]$Arguments,
        [string]$DebuggerPath, [string]$PrivateDirectory, [string]$ArtifactsPath,
        [string]$LogPath, [int]$Seconds, [System.Collections.IDictionary]$Record,
        [switch]$UseStandardHeap,
        [switch]$AttachToDirect,
        [switch]$AttachAfterStartup,
        [switch]$AttachAfterShutdown
    )

    if ($AttachToDirect -and $UseStandardHeap) { throw 'Direct attachment cannot request debugger launch heap policy.' }
    if ($AttachAfterStartup -and -not $AttachToDirect) { throw 'Startup-delayed attachment requires Direct attachment.' }
    if ($AttachAfterShutdown -and (-not $AttachToDirect -or $AttachAfterStartup)) { throw 'Shutdown-delayed attachment requires Direct attachment without startup selection.' }
    foreach ($pair in @(@('-projectPath', $ProjectPath), @('-testPlatform', 'StandaloneWindows64'), @('-logFile', '-'))) {
        $positions = @(for ($index = 0; $index -lt $Arguments.Count; ++$index) { if ($Arguments[$index] -ceq $pair[0]) { $index } })
        if ($positions.Count -ne 1 -or $positions[0] + 1 -ge $Arguments.Count -or $Arguments[$positions[0] + 1] -cne $pair[1]) {
            throw 'Build capture requires the unchanged Standalone build argument array.'
        }
    }
    if (@($Arguments | Where-Object { $_ -ceq '-runTests' }).Count -ne 1 -or $Arguments -ccontains '-quit') {
        throw 'Build capture requires one test-player build without an early quit.'
    }
    if ($Seconds -lt 1 -or $Seconds -gt 1800) { throw 'Build capture requires a bounded timeout.' }
    [void](New-Item -ItemType Directory -Path $PrivateDirectory -ErrorAction Stop)
    $actual = @($Arguments) + @('-crash-report-folder', $PrivateDirectory, '-upmLogFile', (Join-Path $PrivateDirectory 'upm.log'))
    $case = [ordered]@{ Mode = 'standalone-build'; Process = @{}; TargetExitEvent = $null; DumpPresent = $false }
    $Record.Mode = 'standalone-build'
    $Record.TestFilter = $null
    $Record.WorkingDirectory = [Environment]::CurrentDirectory
    $Record.ConsoleScope = 'bounded-debugger-and-Unity-console-tail'
    $Record.InputArguments = @($Arguments)
    $Record.ActualArguments = $actual
    $Record.BuildEnvironment = [ordered]@{}
    $Record.DebuggerHeap = [ordered]@{
        Requested = $(if ($UseStandardHeap) { 'standard' } else { 'debugger-default' })
        InheritedNoDebugHeap = $(if ($null -eq [Environment]::GetEnvironmentVariable('_NO_DEBUG_HEAP', 'Process')) { 'absent' } else { 'present' })
        TargetState = 'unverified'
    }
    foreach ($name in @('UH_PLAYER_BUILD_PATH', 'UH_PERF_COMMIT', 'UH_PERF_BASE_COMMIT', 'UH_PERF_OPTIMIZATION')) {
        $Record.BuildEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    }
    $Record.Qualification = 'One existing build under a debugger; controller and target exits are distinct. No player launch, timing or acceptance qualification. Symbol-path environment is inherited unchanged.'
    $Record.Cases += $case
    $command = Get-NativeCaptureDebuggerCommand -DumpPath (Join-Path $PrivateDirectory 'fault.dmp')
    $debuggerArguments = @('-c', $command, $EditorPath) + $actual
    if ($UseStandardHeap) { $debuggerArguments = @('-hd') + $debuggerArguments }
    $Record.DebuggerArguments = $debuggerArguments
    if (-not $AttachToDirect) { $Record.DebuggerCommandBudget = Get-NativeCaptureDebuggerCommandBudget -Command $command }
    Save-NativeCaptureRecord
    if (-not $AttachToDirect -and -not $Record.DebuggerCommandBudget.Admitted) { throw 'Debugger command exceeds the explicit 4095-character budget.' }
    if ($AttachToDirect) {
        $Record.ConsoleScope = 'separate-bounded-direct-target-and-debugger-console-windows'
        $Record.Qualification = 'Direct startup followed by debugger attachment; target, worker and debugger outcomes remain separate. No player launch, timing or acceptance qualification.'
        $output = Invoke-NativeAttachCapture -CaptureScript (Join-Path $PSScriptRoot 'capture-native-fault.ps1') `
            -EditorPath $EditorPath -Arguments $actual -DebuggerPath $DebuggerPath -PrivateDirectory $PrivateDirectory `
            -ArtifactsPath $ArtifactsPath -Seconds $Seconds -Record $Record -AfterStartup:$AttachAfterStartup -AfterShutdown:$AttachAfterShutdown
    } else {
        $output = Invoke-NativeCaptureProcess -FilePath $DebuggerPath `
            -Arguments $debuggerArguments `
            -Directory $Record.WorkingDirectory -Seconds $Seconds -Observation $case.Process `
            -ReceiptPath (Join-Path $ArtifactsPath 'build-debugger-process.json') -PreserveSymbolPath
    }
    $safeOutput = ConvertTo-UnitySafeLogText -Text $output
    $buildOutput = if ($AttachToDirect) { [IO.File]::ReadAllText((Join-Path $ArtifactsPath 'build-target-console.log')) + [Environment]::NewLine + $safeOutput } else { $safeOutput }
    [IO.File]::WriteAllText($LogPath, $buildOutput)
    [IO.File]::WriteAllText((Join-Path $ArtifactsPath 'build-debugger-console.log'), $safeOutput)
    $case.TargetExitEvent = Get-NativeCaptureExitEvent -Text $output
    $case.ControllerExitRelation = 'unexplained'
    if ($null -ne $case.TargetExitEvent) {
        $targetBits = [Convert]::ToUInt32($case.TargetExitEvent.ExitCodeHex, 16)
        $case.TargetExitCode = [BitConverter]::ToInt32([BitConverter]::GetBytes($targetBits), 0)
        if ($case.Process.RawExitCode -eq 0) { $case.ControllerExitRelation = 'zero' }
        elseif ($case.Process.RawExitCode -eq $case.TargetExitCode) { $case.ControllerExitRelation = 'matches-observed-target' }
    }
    $case.ConsoleTailLimitCharacters = 524288
    foreach ($name in @('upm.log')) {
        $tail = Read-NativeCaptureTail -Path (Join-Path $PrivateDirectory $name)
        [IO.File]::WriteAllText((Join-Path $ArtifactsPath ('build-' + $name)), (ConvertTo-UnitySafeLogText -Text $tail))
    }
    $dump = Join-Path $PrivateDirectory 'fault.dmp'
    $case.DumpPresent = Test-Path -LiteralPath $dump -PathType Leaf
    if ($case.DumpPresent) {
        $case.DumpBytes = (Get-Item -LiteralPath $dump).Length
        if ($case.DumpBytes -le 268435456) { $case.DumpSha256 = (Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash }
    }
    Save-NativeCaptureRecord
    if (-not $case.Process.Reaped -or $null -ne $case.Process.KillReason) { throw 'Diagnostic build did not exit naturally' }
    if ($null -eq $case.TargetExitEvent -or $case.ControllerExitRelation -ceq 'unexplained') { throw 'Debugger did not complete with one observed target exit' }
    $Record.State = 'completed'
    $Record.EndedUtc = [DateTime]::UtcNow.ToString('o')
    Save-NativeCaptureRecord
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

function Invoke-NativeEvidenceReadback {
    param(
        [Parameter(Mandatory = $true)][string]$ExecutionDiagnosticsPath,
        [Parameter(Mandatory = $true)][string]$ExpectedMachine,
        [Parameter(Mandatory = $true)][string]$ArtifactsPath,
        [string]$TestFrameworkLogPath
    )

    $inputPath = [IO.Path]::GetFullPath($ExecutionDiagnosticsPath)
    $inputItem = Get-Item -LiteralPath $inputPath -Force -ErrorAction Stop
    if ($inputItem.PSIsContainer -or ($inputItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Execution diagnostics must be a regular file.'
    }
    $stream = [IO.File]::Open($inputPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        if ($stream.Length -lt 1 -or $stream.Length -gt 4194304) { throw 'Execution diagnostics must contain at most 4 MiB.' }
        $bytes = [byte[]]::new([int]$stream.Length)
        $offset = 0
        while ($offset -lt $bytes.Length) {
            $count = $stream.Read($bytes, $offset, $bytes.Length - $offset)
            if ($count -eq 0) { throw 'Execution diagnostics changed while reading.' }
            $offset += $count
        }
        if ($stream.ReadByte() -ne -1) { throw 'Execution diagnostics changed while reading.' }
    } finally { $stream.Dispose() }
    $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes).TrimStart([char]0xfeff)
    $document = [Text.Json.JsonDocument]::Parse($text)
    try {
        $process = $document.RootElement.GetProperty('Process')
        $observation = @{}
        foreach ($name in @('MachineName', 'ExecutablePath', 'LaunchUtc', 'EndObservedUtc', 'State')) {
            $value = $process.GetProperty($name).GetString()
            if ([string]::IsNullOrWhiteSpace($value) -or $value.Length -gt 4096 -or $value -match '[\x00-\x1f]') {
                throw "Execution diagnostics has an invalid $name."
            }
            $observation[$name] = $value
        }
        $observation.ProcessId = $process.GetProperty('ProcessId').GetInt32()
        $observation.RawExitCode = $process.GetProperty('RawExitCode').GetInt32()
        if ($observation.ProcessId -le 0 -or $observation.State -cne 'observed') { throw 'Execution diagnostics has no observed process identity.' }
        $times = @{}
        foreach ($name in @('LaunchUtc', 'EndObservedUtc')) {
            $parsed = [DateTimeOffset]::MinValue
            if ($observation[$name] -cnotmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?Z$' -or
                -not [DateTimeOffset]::TryParse($observation[$name], [Globalization.CultureInfo]::InvariantCulture,
                    [Globalization.DateTimeStyles]::None, [ref]$parsed)) { throw "Execution diagnostics has an invalid $name." }
            $times[$name] = $parsed
        }
        if ($times.EndObservedUtc -lt $times.LaunchUtc -or $times.EndObservedUtc -gt [DateTimeOffset]::UtcNow) {
            throw 'Execution diagnostics process times are reversed or in the future.'
        }
        $actualMachine = [Environment]::MachineName
        if ([string]::IsNullOrWhiteSpace($ExpectedMachine) -or
            -not [string]::Equals($ExpectedMachine, $observation.MachineName, [StringComparison]::OrdinalIgnoreCase) -or
            -not [string]::Equals($actualMachine, $observation.MachineName, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Historical readback requires expected, saved, and actual machine identities to match.'
        }
        if (-not [IO.Path]::IsPathRooted($observation.ExecutablePath)) { throw 'Execution diagnostics executable path must be absolute.' }
        $native = $process.GetProperty('NativeEvidence')
        $nativeState = $native.GetProperty('State').GetString()
        if ([string]::IsNullOrWhiteSpace($nativeState)) { throw 'Execution diagnostics native capture state is missing.' }
        $nativeFields = @{}
        foreach ($name in @('CaptureId', 'CrashDirectory', 'UpmLogPath')) {
            $element = [Text.Json.JsonElement]::new()
            if ($native.TryGetProperty($name, [ref]$element)) { $nativeFields[$name] = $element.GetString() }
        }
        $capture = $null
        if ($nativeFields.Count -ne 0) {
            if ($nativeFields.Count -ne 3 -or $nativeFields.CaptureId -cnotmatch '^[a-f0-9]{32}$' -or
                [string]::IsNullOrWhiteSpace($nativeFields.CrashDirectory) -or [string]::IsNullOrWhiteSpace($nativeFields.UpmLogPath) -or
                $nativeFields.CrashDirectory -match '[\x00-\x1f]' -or $nativeFields.UpmLogPath -match '[\x00-\x1f]' -or
                -not [IO.Path]::IsPathRooted($nativeFields.CrashDirectory) -or -not [IO.Path]::IsPathRooted($nativeFields.UpmLogPath)) {
                throw 'Execution diagnostics native capture identity is invalid.'
            }
            $crashDirectory = [IO.Path]::GetFullPath($nativeFields.CrashDirectory)
            if ([IO.Path]::GetFileName($crashDirectory) -cne $nativeFields.CaptureId -or
                -not [string]::Equals([IO.Path]::GetFullPath($nativeFields.UpmLogPath), (Join-Path $crashDirectory 'upm.log'), [StringComparison]::OrdinalIgnoreCase)) {
                throw 'Execution diagnostics native capture paths disagree.'
            }
            $capture = @{ State = 'configured'; CaptureId = $nativeFields.CaptureId; CrashDirectory = $crashDirectory; UpmLogPath = $nativeFields.UpmLogPath }
        } elseif ($nativeState -notin @('capture-unavailable', 'capture-setup-failed', 'unsupported-platform')) {
            throw 'Execution diagnostics native capture paths are missing.'
        }
    } finally { $document.Dispose() }

    $artifactRoot = [IO.Path]::GetFullPath($ArtifactsPath)
    if ($null -ne $capture) {
        $capturePrefix = $capture.CrashDirectory.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if ($artifactRoot.StartsWith($capturePrefix, [StringComparison]::OrdinalIgnoreCase) -or
            [string]::Equals($artifactRoot, $capture.CrashDirectory, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Historical readback output must be outside the original native capture directory.'
        }
    }
    if (Test-Path -LiteralPath $artifactRoot) { throw 'Diagnostic artifact path must be fresh.' }
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { $inputHash = [BitConverter]::ToString($algorithm.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant() }
    finally { $algorithm.Dispose() }
    $null = New-Item -ItemType Directory -Path $artifactRoot -ErrorAction Stop
    [IO.File]::WriteAllBytes((Join-Path $artifactRoot 'execution-diagnostics.original.json'), $bytes)
    $record = [ordered]@{
        SchemaVersion = 1; EvidenceOnly = $true; Mode = 'historical-readback'; State = 'collecting'
        OriginalPath = $inputPath; OriginalSha256 = $inputHash; OriginalBytes = $bytes.Length
        Machine = $actualMachine; ExpectedMachine = $ExpectedMachine; OriginalProcess = $observation
        OriginalNativeCaptureState = $nativeState; StartedUtc = [DateTime]::UtcNow.ToString('o')
        WindowsEvents = $null; NativeEvidence = $null; TestFrameworkSources = $null; EndedUtc = $null
        Qualification = 'Historical process evidence only. Missing events or files do not resolve the original failure; no new Unity invocation or acceptance evidence.'
    }
    [IO.File]::WriteAllText((Join-Path $artifactRoot 'receipt.json'), (ConvertTo-UnitySafeLogText -Text ($record | ConvertTo-Json -Depth 12)))
    $record.WindowsEvents = Get-UnityProcessFailureEvents -ProcessObservation $observation
    [IO.File]::WriteAllText((Join-Path $artifactRoot 'receipt.json'), (ConvertTo-UnitySafeLogText -Text ($record | ConvertTo-Json -Depth 12)))
    $record.NativeEvidence = if ($null -eq $capture) {
        @{ State = 'capture-unavailable'; Reason = 'original-capture-identity-unavailable'; Files = @() }
    } else { Get-UnityNativeEvidence -Capture $capture -LogPath (Join-Path $artifactRoot 'readback.log') }
    if (-not [string]::IsNullOrWhiteSpace($TestFrameworkLogPath)) {
        try { $record.TestFrameworkSources = Get-NativeTestFrameworkSources -LogPath $TestFrameworkLogPath -ProcessObservation $observation -ArtifactsPath $artifactRoot }
        catch { $record.TestFrameworkSources = @{ State = 'rejected'; ErrorType = $_.Exception.GetType().Name; Error = $_.Exception.Message; Scope = 'current-persistent-package-sources'; OriginalOwnedProjectBytesProven = $false } }
    }
    $record.EndedUtc = [DateTime]::UtcNow.ToString('o')
    $record.State = 'completed'
    [IO.File]::WriteAllText((Join-Path $artifactRoot 'receipt.json'), (ConvertTo-UnitySafeLogText -Text ($record | ConvertTo-Json -Depth 12)))
    return [pscustomobject]$record
}

if ($PSCmdlet.ParameterSetName -ceq 'Worker') {
    Invoke-NativeAttachWorker -Specification $WorkerSpecification -ArtifactsPath $ArtifactsPath
    return
}

if ($AttachToDirect -and $UseStandardHeap) { throw 'Direct attachment cannot request debugger launch heap policy.' }
if ($AttachAfterStartup -and -not $AttachToDirect) { throw 'Startup-delayed attachment requires Direct attachment.' }
if ($AttachAfterShutdown -and (-not $AttachToDirect -or $AttachAfterStartup)) { throw 'Shutdown-delayed attachment requires Direct attachment without startup selection.' }

if ($PSCmdlet.ParameterSetName -ceq 'SelectReadback') {
    Get-NativeReadbackArtifactSelection -Repository $Repository -SourceRunId $SourceRunId -ArtifactsPath $ArtifactsPath -Token $env:GITHUB_TOKEN -BuildReceipt:$BuildReceipt
    return
}

if ($PSCmdlet.ParameterSetName -ceq 'Readback') {
    Invoke-NativeEvidenceReadback -ExecutionDiagnosticsPath $ExecutionDiagnosticsPath -ExpectedMachine $ExpectedMachine -ArtifactsPath $ArtifactsPath -TestFrameworkLogPath $TestFrameworkLogPath
    return
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
        $flagPath = 'SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\Unity.exe'
        $readOnlyFlagKey = $flagBase.OpenSubKey($flagPath, $false)
        try {
            if ($null -eq $readOnlyFlagKey) { throw 'Unity.exe image flags are missing' }
            $record.LoaderFlagLease = New-NativeLoaderFlagLease -Key $readOnlyFlagKey
            Save-NativeLoaderFlagLease -Lease $record.LoaderFlagLease -Path ((Join-Path $artifactRoot 'loader-flag-lease.json') + '.prepared')
            Save-NativeCaptureRecord
        } finally {
            if ($null -ne $readOnlyFlagKey) { $readOnlyFlagKey.Dispose() }
        }
        $flagKey = $flagBase.OpenSubKey($flagPath, $true)
        if ($null -eq $flagKey) { throw 'Unity.exe image flags are missing' }
    }
    if ([string]::IsNullOrWhiteSpace($DebuggerDirectory)) {
        $DebuggerDirectory = $layout.Tools
        $record.DownloadProcess = @{}
        $downloadOutput = Invoke-NativeCaptureProcess -FilePath (Get-Command uv -CommandType Application | Select-Object -First 1).Source `
            -Arguments @('run', '--project', (Join-Path $PSScriptRoot '../python~'), '--locked', '--no-dev', 'python', (Join-Path $PSScriptRoot 'download-native-debugger.py'), $DebuggerDirectory) `
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
    if ($PSCmdlet.ParameterSetName -eq 'Build') {
        Invoke-NativeStandaloneBuildCapture -EditorPath $EditorPath -ProjectPath $ProjectPath `
            -Arguments $BuildArguments -DebuggerPath $debugger -PrivateDirectory (Join-Path $privateRoot 'build') `
            -ArtifactsPath $artifactRoot -LogPath $BuildLogPath -Seconds $TimeoutSeconds -Record $record -UseStandardHeap:$UseStandardHeap -AttachToDirect:$AttachToDirect -AttachAfterStartup:$AttachAfterStartup -AttachAfterShutdown:$AttachAfterShutdown
        return
    }
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
            $command = Get-NativeCaptureDebuggerCommand
            $executable = $debugger
            $arguments = @('-c', $command, $EditorPath) + $unityArguments
        }
        $observation = @{}
        $case = @{ Mode = $mode; Process = $observation; TargetExitEvent = $null; DumpPresent = $false }
        if ($CompareLoaderSnaps) { $case.GlobalFlagRegistry = Get-NativeGlobalFlagSnapshot }
        $record.Cases += $case
        if ($mode -eq 'debugger') {
            $case.DebuggerArguments = $arguments
            $case.DebuggerCommandBudget = Get-NativeCaptureDebuggerCommandBudget -Command $command
        }
        Save-NativeCaptureRecord
        if ($mode -eq 'debugger' -and -not $case.DebuggerCommandBudget.Admitted) { throw 'Debugger command exceeds the explicit 4095-character budget.' }
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
