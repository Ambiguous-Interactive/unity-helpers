#!/usr/bin/env pwsh

function Write-NativeAttachIdentity {
    param([Diagnostics.Process]$Process, [hashtable]$Coordination)

    $owner = [Diagnostics.Process]::GetCurrentProcess()
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(1)
        while ($true) {
            try {
                if ($Process.HasExited) { throw 'Direct target exited before its launch identity could be observed.' }
                $createdTicks = $Process.StartTime.ToUniversalTime().Ticks
                $executablePath = $Process.MainModule.FileName
                break
            } catch [ComponentModel.Win32Exception], [InvalidOperationException], [ArgumentOutOfRangeException] {
                if ($Process.HasExited -or [DateTime]::UtcNow -ge $deadline) { throw }
                Start-Sleep -Milliseconds 20
                $Process.Refresh()
            }
        }
        $identity = [ordered]@{
            CaptureId = $Coordination.CaptureId
            Machine = [Environment]::MachineName
            ControllerPid = $Coordination.ControllerPid
            WorkerPid = $PID
            WorkerCreatedTicks = $owner.StartTime.ToUniversalTime().Ticks
            Pid = $Process.Id
            CreatedTicks = $createdTicks
            ExecutablePath = $executablePath
            ExecutableSha256 = (Get-FileHash -LiteralPath $executablePath -Algorithm SHA256).Hash
        }
        $temporary = $Coordination.IdentityPath + '.writing'
        [IO.File]::WriteAllText($temporary, ($identity | ConvertTo-Json))
        [IO.File]::Move($temporary, $Coordination.IdentityPath)
    } finally { $owner.Dispose() }
}

function Read-NativeAttachDocument {
    param([string]$IdentityPath, [string[]]$Names)

    $item = Get-Item -LiteralPath $IdentityPath -Force -ErrorAction Stop
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $item.Length -gt 32768 -or $item.Length -lt 1) {
        throw 'Attach identity must be a bounded regular file.'
    }
    $stream = [IO.File]::Open($IdentityPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        if ($stream.Length -lt 1 -or $stream.Length -gt 32768) { throw 'Attach identity exceeds its read bound.' }
        $bytes = [byte[]]::new([int]$stream.Length)
        $offset = 0
        while ($offset -lt $bytes.Length) {
            $read = $stream.Read($bytes, $offset, $bytes.Length - $offset)
            if ($read -eq 0) { throw 'Attach identity changed while reading.' }
            $offset += $read
        }
        if ($stream.ReadByte() -ne -1) { throw 'Attach identity changed while reading.' }
    } finally { $stream.Dispose() }
    $document = [Text.Json.JsonDocument]::Parse([Text.UTF8Encoding]::new($false, $true).GetString($bytes))
    try {
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($property in $document.RootElement.EnumerateObject()) {
            if ($property.Name -cnotin $Names -or -not $seen.Add($property.Name)) { throw 'Attach identity contains unknown or duplicate members.' }
        }
        if ($seen.Count -ne $Names.Count) { throw 'Attach identity is incomplete.' }
        return $document
    } catch { $document.Dispose(); throw }
}

function Open-NativeAttachTarget {
    param([string]$IdentityPath, [hashtable]$Expected)

    $document = Read-NativeAttachDocument -IdentityPath $IdentityPath -Names @('CaptureId', 'Machine', 'ControllerPid', 'WorkerPid', 'WorkerCreatedTicks', 'Pid', 'CreatedTicks', 'ExecutablePath', 'ExecutableSha256')
    try {
        $identity = @{}
        foreach ($name in @('CaptureId', 'Machine', 'ExecutablePath', 'ExecutableSha256')) {
            $identity[$name] = $document.RootElement.GetProperty($name).GetString()
        }
        foreach ($name in @('ControllerPid', 'WorkerPid', 'Pid')) { $identity[$name] = $document.RootElement.GetProperty($name).GetInt32() }
        foreach ($name in @('WorkerCreatedTicks', 'CreatedTicks')) { $identity[$name] = $document.RootElement.GetProperty($name).GetInt64() }
    } finally { $document.Dispose() }
    if ($identity.CaptureId -cnotmatch '^[a-f0-9]{32}$' -or $identity.CaptureId -cne $Expected.CaptureId -or
        $identity.Machine -cne [Environment]::MachineName -or $identity.ControllerPid -ne $PID -or
        $identity.WorkerPid -le 0 -or $identity.Pid -le 0 -or $identity.Pid -eq $PID -or $identity.Pid -eq $identity.WorkerPid -or
        $identity.ExecutablePath -cne $Expected.ExecutablePath -or $identity.ExecutableSha256 -cne $Expected.ExecutableSha256 -or
        $identity.CreatedTicks -lt $Expected.EarliestTicks -or $identity.CreatedTicks -gt [DateTime]::UtcNow.Ticks -or
        $identity.WorkerCreatedTicks -lt $Expected.EarliestTicks -or $identity.WorkerCreatedTicks -gt $identity.CreatedTicks) {
        throw 'Attach identity does not match the owned launch.'
    }
    $target = $null
    $worker = $null
    try {
        $target = [Diagnostics.Process]::GetProcessById($identity.Pid)
        $worker = [Diagnostics.Process]::GetProcessById($identity.WorkerPid)
        $null = $target.Handle
        $null = $worker.Handle
        if ($target.HasExited -or $worker.HasExited -or $target.StartTime.ToUniversalTime().Ticks -ne $identity.CreatedTicks -or
            $worker.StartTime.ToUniversalTime().Ticks -ne $identity.WorkerCreatedTicks -or
            $target.MainModule.FileName -cne $identity.ExecutablePath -or
            (Get-FileHash -LiteralPath $target.MainModule.FileName -Algorithm SHA256).Hash -cne $identity.ExecutableSha256) {
            throw 'Attach target or worker identity changed or exited.'
        }
        if (-not $IsWindows) { throw 'Attach ownership validation requires Windows.' }
        $targetOwner = Get-CimInstance -ClassName Win32_Process -Filter "ProcessId=$($identity.Pid)" -OperationTimeoutSec 3 -ErrorAction Stop
        $workerOwner = Get-CimInstance -ClassName Win32_Process -Filter "ProcessId=$($identity.WorkerPid)" -OperationTimeoutSec 3 -ErrorAction Stop
        if ($null -eq $targetOwner -or $null -eq $workerOwner -or $targetOwner.ParentProcessId -ne $identity.WorkerPid -or $workerOwner.ParentProcessId -ne $PID) {
            throw 'Attach target is not owned by this collector worker.'
        }
        if ($target.HasExited -or $worker.HasExited) { throw 'Attach target or worker exited during validation.' }
        return @{ Target = $target; Worker = $worker; Identity = $identity; VerifiedUtc = [DateTime]::UtcNow.ToString('o'); ObservedCommandLine = [string]$targetOwner.CommandLine }
    } catch {
        if ($null -ne $target) { $target.Dispose() }
        if ($null -ne $worker) { $worker.Dispose() }
        throw
    }
}

function Add-NativeAttachStartupOutput {
    param([hashtable]$Coordination, [hashtable]$State, [char[]]$Buffer, [int]$Count, [int]$ProcessId, [long]$DeliveredCharacters)

    if ($State.Completed) { return }
    $text = [string]::new($Buffer, 0, $Count)
    if ($State.Discarding) {
        $newline = $text.IndexOf([char]10)
        if ($newline -lt 0) { return }
        $text = $text.Substring($newline + 1)
        $State.Discarding = $false
    }
    $text = $State.Pending + $text
    $last = $text.LastIndexOf([char]10)
    if ($last -ge 0) {
        $complete = $text.Substring(0, $last + 1)
        $match = [regex]::Match($complete, '(?m)^' + [regex]::Escape($Coordination.StartupMarkerPrefix) + '[^\r\n]{0,4096}\r?\n')
        if ($match.Success) {
            $record = [ordered]@{
                CaptureId = $Coordination.CaptureId; Pid = $ProcessId; ObservedUtc = [DateTime]::UtcNow.ToString('o')
                Prefix = $Coordination.StartupMarkerPrefix; Line = $match.Value.TrimEnd([char[]]@([char]13, [char]10))
                StdoutCharacters = $DeliveredCharacters
            }
            $temporary = $Coordination.StartupReceiptPath + '.writing'
            [IO.File]::WriteAllText($temporary, ($record | ConvertTo-Json))
            [IO.File]::Move($temporary, $Coordination.StartupReceiptPath)
            $State.Completed = $true
            $State.Pending = ''
            return
        }
        $text = $text.Substring($last + 1)
    }
    if ($text.Length -gt 8192) { $State.Pending = ''; $State.Discarding = $true }
    else { $State.Pending = $text }
}

function Read-NativeAttachStartup {
    param([string]$Path, [hashtable]$Identity, [string]$Prefix)

    $document = Read-NativeAttachDocument -IdentityPath $Path -Names @('CaptureId', 'Pid', 'ObservedUtc', 'Prefix', 'Line', 'StdoutCharacters')
    try {
        $root = $document.RootElement
        $observed = $root.GetProperty('ObservedUtc').GetString()
        $line = $root.GetProperty('Line').GetString()
        $when = [DateTimeOffset]::ParseExact($observed, "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal)
        if ($root.GetProperty('CaptureId').GetString() -cne $Identity.CaptureId -or $root.GetProperty('Pid').GetInt32() -ne $Identity.Pid -or
            $root.GetProperty('Prefix').GetString() -cne $Prefix -or [string]::IsNullOrEmpty($line) -or
            -not $line.StartsWith($Prefix, [StringComparison]::Ordinal) -or $line.Length -gt 4352 -or $line.IndexOfAny([char[]]@([char]13, [char]10)) -ge 0 -or
            $root.GetProperty('StdoutCharacters').GetInt64() -le 0 -or $when.UtcTicks -lt $Identity.CreatedTicks -or $when -gt [DateTimeOffset]::UtcNow) {
            throw 'Startup observation does not match the owned direct target.'
        }
        return @{ CaptureId = $Identity.CaptureId; Pid = $Identity.Pid; ObservedUtc = $observed; Prefix = $Prefix; Line = $line; StdoutCharacters = $root.GetProperty('StdoutCharacters').GetInt64() }
    } finally { $document.Dispose() }
}

function Invoke-NativeAttachWorker {
    param([hashtable]$Specification, [string]$ArtifactsPath)

    $observation = @{}
    try {
        $null = Invoke-NativeCaptureProcess -FilePath $Specification.ExecutablePath -Arguments $Specification.Arguments `
            -Directory $Specification.Directory -Seconds $Specification.Seconds -Observation $observation `
            -ReceiptPath (Join-Path $ArtifactsPath 'build-target-process.json') -PreserveSymbolPath -Coordination $Specification.Coordination `
            -HeadPath (Join-Path $ArtifactsPath 'build-target-console-head.log') -TailPath (Join-Path $ArtifactsPath 'build-target-console.log')
    } finally {
        [IO.File]::WriteAllText((Join-Path $ArtifactsPath 'build-worker-result.json'), ($observation | ConvertTo-Json))
    }
}

function Invoke-NativeAttachCapture {
    param(
        [string]$CaptureScript, [string]$EditorPath, [string[]]$Arguments, [string]$DebuggerPath,
        [string]$PrivateDirectory, [string]$ArtifactsPath, [int]$Seconds, [System.Collections.IDictionary]$Record,
        [switch]$AfterStartup,
        [switch]$AfterShutdown
    )

    if ($AfterStartup -and $AfterShutdown) { throw 'Attachment boundaries are mutually exclusive.' }
    $captureId = [Guid]::NewGuid().ToString('N')
    $identityPath = Join-Path $PrivateDirectory 'target-identity.json'
    $cancelPath = Join-Path $PrivateDirectory 'cancel-target'
    $startupPath = Join-Path $PrivateDirectory 'target-startup.json'
    $startupPrefix = if ($AfterShutdown) { '##utp:{"type":"MemoryLeaks",' } else { 'Domain Reload Profiling:' }
    $expected = @{
        CaptureId = $captureId; ExecutablePath = [IO.Path]::GetFullPath($EditorPath)
        ExecutableSha256 = (Get-FileHash -LiteralPath $EditorPath -Algorithm SHA256).Hash
        EarliestTicks = [DateTime]::UtcNow.Ticks
    }
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    $specification = @{
        ExecutablePath = $expected.ExecutablePath; Arguments = $Arguments; Directory = $Record.WorkingDirectory; Seconds = $Seconds
        Coordination = @{ CaptureId = $captureId; IdentityPath = $identityPath; CancelPath = $cancelPath; ControllerPid = $PID }
    }
    if ($AfterStartup -or $AfterShutdown) {
        $specification.Coordination.StartupMarkerPrefix = $startupPrefix
        $specification.Coordination.StartupReceiptPath = $startupPath
    }
    $case = $Record.Cases[-1]
    $case.TargetProcess = @{}
    $case.Attach = [ordered]@{
        CaptureId = $captureId; State = 'starting-worker'; Expected = $expected; Identity = $null
        RequestedUtc = $null; VerifiedUtc = $null; CleanupRequested = $false; WorkerState = $null
        Boundary = $(if ($AfterShutdown) { 'reported-memory-leak-telemetry' } elseif ($AfterStartup) { 'reported-domain-reload' } else { 'initial-identity' }); Startup = $null; ObservedCommandLine = $null
        Qualification = 'Direct startup followed by invasive attachment. Heap output describes attachment only; startup heap and causal repair remain unverified.'
    }
    $Record.DebuggerHeap.Requested = 'direct-start-then-attach'
    $Record.DebuggerArguments = @()
    Save-NativeCaptureRecord
    $job = $null
    $opened = $null
    $output = ''
    try {
        $job = Start-Job -ScriptBlock {
            param($Script, $Specification, $Artifacts)
            & $Script -WorkerSpecification $Specification -ArtifactsPath $Artifacts
        } -ArgumentList $CaptureScript, $specification, $ArtifactsPath
        while (-not (Test-Path -LiteralPath $identityPath -PathType Leaf)) {
            if ($job.State -notin @('Running', 'NotStarted') -or [DateTime]::UtcNow -ge $deadline) { throw 'Direct worker exited or timed out before its identity receipt.' }
            Start-Sleep -Milliseconds 50
        }
        $opened = Open-NativeAttachTarget -IdentityPath $identityPath -Expected $expected
        $case.Attach.Identity = $opened.Identity
        $case.Attach.VerifiedUtc = $opened.VerifiedUtc
        $case.Attach.ObservedCommandLine = $opened.ObservedCommandLine
        $case.Attach.State = 'identity-verified'
        if ($AfterStartup -or $AfterShutdown) {
            $case.Attach.State = if ($AfterShutdown) { 'waiting-memory-leak-telemetry' } else { 'waiting-domain-reload' }
            Save-NativeCaptureRecord
            while (-not (Test-Path -LiteralPath $startupPath -PathType Leaf)) {
                if ($opened.Target.HasExited -or $job.State -notin @('Running', 'NotStarted') -or [DateTime]::UtcNow -ge $deadline) {
                    if ($AfterShutdown) { throw 'Direct target exited or timed out before the required final memory-leak telemetry.' }
                    throw 'Direct target exited or timed out before the required domain-reload observation.'
                }
                Start-Sleep -Milliseconds 50
            }
            $case.Attach.Startup = Read-NativeAttachStartup -Path $startupPath -Identity $opened.Identity -Prefix $startupPrefix
        }
        $command = '.printf "WUH_ATTACH_PID=%x\n", @$tpid;!gflag;!heap -s;.echo WUH_ATTACH_HEAP_END;' + (Get-NativeCaptureDebuggerCommand -DumpPath (Join-Path $PrivateDirectory 'fault.dmp'))
        $debuggerArguments = @('-pd', '-p', [string]$opened.Identity.Pid, '-c', $command)
        $Record.DebuggerArguments = $debuggerArguments
        $case.Attach.RequestedUtc = [DateTime]::UtcNow.ToString('o')
        $case.Attach.State = 'attach-requested'
        Save-NativeCaptureRecord
        if ($opened.Target.HasExited -or $opened.Worker.HasExited) { throw 'Owned process exited before debugger launch.' }
        $remaining = [int][Math]::Floor(($deadline - [DateTime]::UtcNow).TotalSeconds)
        if ($remaining -lt 1) { throw 'Attach deadline expired before debugger launch.' }
        $output = Invoke-NativeCaptureProcess -FilePath $DebuggerPath -Arguments $debuggerArguments `
            -Directory $Record.WorkingDirectory -Seconds $remaining -Observation $case.Process `
            -ReceiptPath (Join-Path $ArtifactsPath 'build-debugger-process.json') -PreserveSymbolPath `
            -HeadPath (Join-Path $ArtifactsPath 'build-attach-console-head.log') -TailPath (Join-Path $ArtifactsPath 'build-debugger-console.log')
        $case.TargetExitEvent = Get-NativeCaptureExitEvent -Text $output
        $head = [IO.File]::ReadAllText((Join-Path $ArtifactsPath 'build-attach-console-head.log'))
        $markers = [regex]::Matches($head, '(?m)^WUH_ATTACH_PID=([0-9a-fA-F]+)\r?$')
        if ($markers.Count -ne 1 -or [Convert]::ToInt32($markers[0].Groups[1].Value, 16) -ne $opened.Identity.Pid -or
            [regex]::Matches($head, '(?m)^WUH_ATTACH_HEAP_END\r?$').Count -ne 1) { throw 'Debugger did not observe the exact owned target at the attachment boundary.' }
        $case.Attach.State = 'target-observed'
        $case.Attach.HeapObservation = 'bounded-command-output-retained; inspect commands and errors before interpreting heap state'
        while ($job.State -in @('Running', 'NotStarted') -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 50 }
        if ($job.State -ne 'Completed') { throw 'Direct target worker did not complete within the attach deadline.' }
        $null = Receive-Job -Job $job -ErrorAction Stop
        $case.TargetProcess = Get-Content -LiteralPath (Join-Path $ArtifactsPath 'build-worker-result.json') -Raw | ConvertFrom-Json -AsHashtable
        if (-not $case.TargetProcess.Reaped -or $null -ne $case.TargetProcess.KillReason -or $case.TargetProcess.Pid -ne $opened.Identity.Pid -or
            $null -eq $case.TargetExitEvent -or $case.TargetExitEvent.Pid -ne $opened.Identity.Pid) { throw 'Attach target did not complete naturally with a matching observed debugger exit.' }
        $bits = [Convert]::ToUInt32($case.TargetExitEvent.ExitCodeHex, 16)
        $targetExit = [BitConverter]::ToInt32([BitConverter]::GetBytes($bits), 0)
        if ($case.TargetProcess.RawExitCode -ne $targetExit) { throw 'Direct process exit disagrees with the debugger target exit.' }
        $case.Attach.State = 'completed'
        return $output
    } finally {
        if ($null -ne $job) {
            if ($job.State -in @('Running', 'NotStarted')) {
                $case.Attach.CleanupRequested = $true
                [IO.File]::WriteAllText($cancelPath, 'coordinator-cleanup')
                $null = Wait-Job -Job $job -Timeout 12
            }
            if ($job.State -in @('Running', 'NotStarted')) {
                if ($null -ne $opened -and -not $opened.Target.HasExited) {
                    $case.Attach.TargetKilledByCoordinator = $true
                    $opened.Target.Kill($true)
                    $case.Attach.TargetReapedByCoordinator = $opened.Target.WaitForExit(10000)
                }
                Stop-Job -Job $job
            }
            $case.Attach.WorkerState = [string]$job.State
            $case.Attach.WorkerErrors = @($job.ChildJobs | ForEach-Object { $_.Error | ForEach-Object { ConvertTo-UnitySafeLogText -Text $_.ToString() } })
            if (Test-Path -LiteralPath (Join-Path $ArtifactsPath 'build-worker-result.json') -PathType Leaf) {
                $case.TargetProcess = Get-Content -LiteralPath (Join-Path $ArtifactsPath 'build-worker-result.json') -Raw | ConvertFrom-Json -AsHashtable
            }
            Remove-Job -Job $job
        }
        if ($null -ne $opened) { $opened.Target.Dispose(); $opened.Worker.Dispose() }
        Save-NativeCaptureRecord
    }
}
