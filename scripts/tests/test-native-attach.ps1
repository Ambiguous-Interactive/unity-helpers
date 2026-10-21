#!/usr/bin/env pwsh
[CmdletBinding()]
param([switch]$RequireWindows, [string]$DebuggerDirectory, [string]$NativeProgramsDirectory)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$source = Join-Path $repo 'scripts/unity/capture-native-fault.ps1'
. (Join-Path $repo 'scripts/unity/lib/native-attach.ps1')
. (Join-Path $repo 'scripts/unity/lib/credential-redaction.ps1')
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($source, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'Shipping capture did not parse.' }
foreach ($name in @('Invoke-NativeCaptureProcess', 'Get-NativeCaptureDebuggerCommandBudget', 'Get-NativeCaptureDebuggerCommand', 'Get-NativeCaptureExitEvent')) {
    $function = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq $name }, $true)
    if ($null -eq $function) { throw "Missing shipping function $name" }
    . ([scriptblock]::Create($function.Extent.Text))
}
if ($RequireWindows -and -not $IsWindows) { throw 'Windows attach controls were required.' }
$root = Join-Path ([IO.Path]::GetTempPath()) ('native-attach-controls-' + [Guid]::NewGuid().ToString('N'))
$null = [IO.Directory]::CreateDirectory($root)
$passed = 0
$completed = $false
function Assert-Attach([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    ++$script:passed
}
function Save-NativeCaptureRecord { }
try {
    $shell = (Get-Process -Id $PID).Path
    $child = Join-Path $root 'child with spaces.ps1'
    $descendantPath = Join-Path $root 'descendant.pid'
    [IO.File]::WriteAllText($child, 'param($DescendantPath); $info = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path); $info.UseShellExecute = $false; $info.ArgumentList.Add("-NoProfile"); $info.ArgumentList.Add("-Command"); $info.ArgumentList.Add("Start-Sleep -Seconds 30"); $descendant = [Diagnostics.Process]::Start($info); [IO.File]::WriteAllText($DescendantPath, [string]$descendant.Id); [Console]::Write("WORKER_READY|" + [Environment]::CurrentDirectory + "|" + $env:_NT_SYMBOL_PATH); Start-Sleep -Seconds 30')
    $identityPath = Join-Path $root 'identity.json'
    $cancelPath = Join-Path $root 'cancel'
    $expected = @{
        CaptureId = [Guid]::NewGuid().ToString('N'); ExecutablePath = $shell
        ExecutableSha256 = (Get-FileHash -LiteralPath $shell -Algorithm SHA256).Hash; EarliestTicks = [DateTime]::UtcNow.Ticks
    }
    $spec = @{
        ExecutablePath = $shell; Arguments = @('-NoProfile', '-File', $child, $descendantPath); Directory = $root; Seconds = 30
        Coordination = @{ CaptureId = $expected.CaptureId; ControllerPid = $PID; IdentityPath = $identityPath; CancelPath = $cancelPath }
    }
    $symbols = [Environment]::GetEnvironmentVariable('_NT_SYMBOL_PATH', 'Process')
    $heap = [Environment]::GetEnvironmentVariable('_NO_DEBUG_HEAP', 'Process')
    $job = Start-Job -ScriptBlock { param($Script, $Spec, $Root) & $Script -WorkerSpecification $Spec -ArtifactsPath $Root } -ArgumentList $source, $spec, $root
    $held = $null
    $descendant = $null
    try {
        $limit = [DateTime]::UtcNow.AddSeconds(15)
        while (-not (Test-Path -LiteralPath $identityPath) -and $job.State -eq 'Running' -and [DateTime]::UtcNow -lt $limit) { Start-Sleep -Milliseconds 50 }
        Assert-Attach (Test-Path -LiteralPath $identityPath) 'Shipping worker must publish an early identity.'
        $identityText = [IO.File]::ReadAllText($identityPath)
        $identity = $identityText | ConvertFrom-Json -AsHashtable
        $held = [Diagnostics.Process]::GetProcessById($identity.Pid)
        $null = $held.Handle
        $creationMatches = if ($IsWindows) { $held.StartTime.ToUniversalTime().Ticks -eq $identity.CreatedTicks }
        else { [Math]::Abs($held.StartTime.ToUniversalTime().Ticks - $identity.CreatedTicks) -lt [TimeSpan]::TicksPerSecond }
        Assert-Attach (-not $held.HasExited -and $job.State -eq 'Running' -and $identity.CaptureId -ceq $expected.CaptureId -and
            $identity.ControllerPid -eq $PID -and $identity.WorkerPid -ne $PID -and $identity.ExecutablePath -ceq $shell -and
            $identity.ExecutableSha256 -ceq $expected.ExecutableSha256 -and $creationMatches) 'Receipt binds a live owned target and a separate worker; exact cross-process creation ticks are required on Windows.'
        while (-not (Test-Path -LiteralPath $descendantPath) -and -not $held.HasExited -and [DateTime]::UtcNow -lt $limit) { Start-Sleep -Milliseconds 50 }
        $descendant = [Diagnostics.Process]::GetProcessById([int][IO.File]::ReadAllText($descendantPath))
        $null = $descendant.Handle
        Assert-Attach (-not $descendant.HasExited) 'Direct target owns a live descendant before cancellation.'
        foreach ($mutation in @('nonce', 'host', 'controller', 'worker', 'pid', 'path', 'hash', 'creation', 'worker-creation', 'duplicate', 'missing', 'unknown', 'wrong-type', 'partial', 'oversize')) {
            $changed = $identityText | ConvertFrom-Json -AsHashtable
            switch ($mutation) {
                'nonce' { $changed.CaptureId = [Guid]::NewGuid().ToString('N') }
                'host' { $changed.Machine = 'foreign-machine' }
                'controller' { $changed.ControllerPid = 0 }
                'worker' { $changed.WorkerPid = $PID }
                'pid' { $changed.Pid = $PID }
                'path' { $changed.ExecutablePath += '.different' }
                'hash' { $changed.ExecutableSha256 = '0' * 64 }
                'creation' { $changed.CreatedTicks = [DateTime]::UtcNow.AddDays(1).Ticks }
                'worker-creation' { $changed.WorkerCreatedTicks = 0L }
                'missing' { $changed.Remove('CreatedTicks') }
                'unknown' { $changed.Foreign = 1 }
                'wrong-type' { $changed.Pid = [string]$changed.Pid }
            }
            $text = $changed | ConvertTo-Json
            if ($mutation -eq 'duplicate') { $text = $text.Replace('{', '{"Pid":1,') }
            if ($mutation -eq 'partial') { $text = '{' }
            if ($mutation -eq 'oversize') { $text = ' ' * 32769 }
            $path = Join-Path $root ($mutation + '.json')
            [IO.File]::WriteAllText($path, $text)
            $rejected = $false
            try { $opened = Open-NativeAttachTarget -IdentityPath $path -Expected $expected; $opened.Target.Dispose(); $opened.Worker.Dispose() }
            catch { $rejected = $true }
            Assert-Attach ($rejected -and -not $held.HasExited) "Refuse $mutation identity without touching the live target."
        }
        if ($IsWindows) {
            $opened = Open-NativeAttachTarget -IdentityPath $identityPath -Expected $expected
            try { Assert-Attach ($opened.Identity.Pid -eq $identity.Pid -and -not $opened.Target.HasExited) 'Actual Windows parent ownership and creation identity pass.' }
            finally { $opened.Target.Dispose(); $opened.Worker.Dispose() }
            $shippingOpen = ${function:Open-NativeAttachTarget}
            $foreignInfo = [Diagnostics.ProcessStartInfo]::new($shell)
            $foreignInfo.UseShellExecute = $false
            foreach ($argument in @('-NoProfile', '-Command', 'Start-Sleep -Seconds 60')) { $foreignInfo.ArgumentList.Add($argument) }
            $foreign = [Diagnostics.Process]::Start($foreignInfo)
            $null = $foreign.Handle
            $policies = @(
                @{ Name = 'nonce'; Clause = '$identity.CaptureId -cne $Expected.CaptureId' },
                @{ Name = 'host'; Clause = '$identity.Machine -cne [Environment]::MachineName' },
                @{ Name = 'controller'; Clause = '$identity.ControllerPid -ne $PID' },
                @{ Name = 'path'; Clause = '$identity.ExecutablePath -cne $Expected.ExecutablePath' },
                @{ Name = 'hash'; Clause = '$identity.ExecutableSha256 -cne $Expected.ExecutableSha256' },
                @{ Name = 'creation'; Clause = '$target.StartTime.ToUniversalTime().Ticks -ne $identity.CreatedTicks' },
                @{ Name = 'worker-creation'; Clause = '$worker.StartTime.ToUniversalTime().Ticks -ne $identity.WorkerCreatedTicks' },
                @{ Name = 'owner'; Clause = '$targetOwner.ParentProcessId -ne $identity.WorkerPid' }
            )
            try {
                foreach ($policy in $policies) {
                    $changed = $identityText | ConvertFrom-Json -AsHashtable
                    $request = $expected.Clone()
                    switch ($policy.Name) {
                        'nonce' { $changed.CaptureId = [Guid]::NewGuid().ToString('N') }
                        'host' { $changed.Machine = 'foreign-machine' }
                        'controller' { $changed.ControllerPid = 0 }
                        'path' { $request.ExecutablePath += '.different' }
                        'hash' { $request.ExecutableSha256 = '0' * 64 }
                        'creation' { $changed.CreatedTicks += 1 }
                        'worker-creation' { $changed.WorkerCreatedTicks += 1 }
                        'owner' { $changed.Pid = $foreign.Id; $changed.CreatedTicks = $foreign.StartTime.ToUniversalTime().Ticks }
                    }
                    $path = Join-Path $root ('removed-' + $policy.Name + '.json')
                    [IO.File]::WriteAllText($path, ($changed | ConvertTo-Json))
                    $rejected = $false
                    try { $opened = Open-NativeAttachTarget -IdentityPath $path -Expected $request; $opened.Target.Dispose(); $opened.Worker.Dispose() }
                    catch { $rejected = $true }
                    Assert-Attach $rejected "Shipping policy refuses the isolated $($policy.Name) mutation."
                    $mutant = $shippingOpen.ToString().Replace($policy.Clause, '$false')
                    Assert-Attach ($mutant -cne $shippingOpen.ToString()) "Guard removal changes the actual $($policy.Name) implementation."
                    ${function:Open-NativeAttachTarget} = [scriptblock]::Create($mutant)
                    try {
                        $opened = Open-NativeAttachTarget -IdentityPath $path -Expected $request
                        try { Assert-Attach (-not $opened.Target.HasExited) "Removed $($policy.Name) guard admits its otherwise rejected live identity."
                        } finally { $opened.Target.Dispose(); $opened.Worker.Dispose() }
                    } finally { ${function:Open-NativeAttachTarget} = $shippingOpen }
                }
            } finally {
                ${function:Open-NativeAttachTarget} = $shippingOpen
                if (-not $foreign.HasExited) { $foreign.Kill($true); $null = $foreign.WaitForExit(10000) }
                $foreign.Dispose()
            }
        }
        [IO.File]::WriteAllText($cancelPath, 'controlled cancellation')
        $null = Wait-Job -Job $job -Timeout 12
        $null = Receive-Job -Job $job -ErrorAction Stop
        $result = Get-Content -LiteralPath (Join-Path $root 'build-worker-result.json') -Raw | ConvertFrom-Json -AsHashtable
        Assert-Attach ($job.State -eq 'Completed' -and $result.Pid -eq $identity.Pid -and $result.Reaped -and $result.KillReason -ceq 'coordinator-cancellation' -and $held.WaitForExit(1000)) 'Cancellation reaps the exact direct target through the shipping worker.'
        Assert-Attach ($descendant.WaitForExit(5000)) 'Cancellation terminates the live target descendant as well as its parent.'
        Assert-Attach ([IO.File]::ReadAllText($identityPath) -ceq $identityText) 'Terminal worker cannot rewrite the immutable early identity.'
        Assert-Attach ([IO.File]::ReadAllText((Join-Path $root 'build-target-console.log')) -ceq ('WORKER_READY|' + $root + '|' + $symbols)) 'The direct child observes the requested working directory and inherited symbol environment.'
        Assert-Attach ([Environment]::GetEnvironmentVariable('_NT_SYMBOL_PATH', 'Process') -ceq $symbols -and
            [Environment]::GetEnvironmentVariable('_NO_DEBUG_HEAP', 'Process') -ceq $heap) 'Worker leaves inherited symbol and heap environment unchanged.'
        $rejected = $false
        try { $opened = Open-NativeAttachTarget -IdentityPath $identityPath -Expected $expected; $opened.Target.Dispose(); $opened.Worker.Dispose() }
        catch { $rejected = $true }
        Assert-Attach $rejected 'Exited target identity cannot authorize a later attach.'
    } finally {
        [IO.File]::WriteAllText($cancelPath, 'fixture cleanup')
        $null = Wait-Job -Job $job -Timeout 12
        if ($job.State -eq 'Running') { Stop-Job -Job $job }
        Remove-Job -Job $job
        if ($null -ne $held) { if (-not $held.HasExited) { $held.Kill($true); $null = $held.WaitForExit(10000) }; $held.Dispose() }
        if ($null -ne $descendant) { if (-not $descendant.HasExited) { $descendant.Kill($true); $null = $descendant.WaitForExit(10000) }; $descendant.Dispose() }
    }
    $emit = Join-Path $root 'emit.ps1'
    [IO.File]::WriteAllText($emit, '[Console]::Write("HEAD" + ("x" * 2000000) + "TAIL")')
    $observation = @{}
    $headPath = Join-Path $root 'head.log'; $tailPath = Join-Path $root 'tail.log'
    $text = Invoke-NativeCaptureProcess -FilePath $shell -Arguments @('-NoProfile', '-File', $emit) -Directory $root -Seconds 15 -Observation $observation -HeadPath $headPath -TailPath $tailPath -PreserveSymbolPath
    Assert-Attach ([IO.File]::ReadAllText($headPath).StartsWith('HEAD') -and [IO.File]::ReadAllText($headPath).Length -eq 524288 -and
        $text.EndsWith('TAIL') -and [IO.File]::ReadAllText($tailPath) -ceq $text -and $observation.ConsoleTailTruncated -and $observation.RawExitCode -eq 0) 'Retain bounded first and last console windows while draining the full stream.'
    if ($RequireWindows) {
        if ([string]::IsNullOrWhiteSpace($DebuggerDirectory) -or [string]::IsNullOrWhiteSpace($NativeProgramsDirectory)) { throw 'Real Windows capture requires the pinned debugger and normal/AV control programs.' }
        $project = Join-Path $root 'project'; $null = [IO.Directory]::CreateDirectory((Join-Path $project 'ProjectSettings'))
        [IO.File]::WriteAllText((Join-Path $project 'ProjectSettings/ProjectVersion.txt'), "m_EditorVersion: 6000.6.0f1`n")
        foreach ($kind in @('normal', 'av', 'startup-normal', 'startup-av', 'shutdown-normal', 'shutdown-av', 'shutdown-status', 'shutdown-handled', 'shutdown-thread')) {
            $shutdown = $kind.StartsWith('shutdown-', [StringComparison]::Ordinal)
            $startup = $kind.StartsWith('startup-', [StringComparison]::Ordinal)
            $delayed = $startup -or $shutdown
            $isAv = $kind.EndsWith('av', [StringComparison]::Ordinal)
            $isThreadExit = $kind -ceq 'shutdown-thread'
            $program = Join-Path $NativeProgramsDirectory ($kind + '/control.exe')
            $casePath = Join-Path $root $kind; $null = [IO.Directory]::CreateDirectory($casePath)
            $arguments = @('-batchmode', '-nographics', '-projectPath', $project, '-runTests', '-testPlatform', 'StandaloneWindows64', '-logFile', '-')
            $direct = @{}
            $null = Invoke-NativeCaptureProcess -FilePath $program -Arguments $arguments -Directory ([Environment]::CurrentDirectory) -Seconds 30 -Observation $direct -PreserveSymbolPath
            & $source -EditorPath $program -ProjectPath $project -BuildArguments $arguments -BuildLogPath (Join-Path $casePath 'build.log') `
                -ArtifactsPath (Join-Path $casePath 'capture') -DebuggerDirectory $DebuggerDirectory -TimeoutSeconds 45 -AttachToDirect -AttachAfterStartup:$startup -AttachAfterShutdown:$shutdown
            $record = Get-Content -LiteralPath (Join-Path $casePath 'capture/receipt.json') -Raw | ConvertFrom-Json -AsHashtable
            $case = $record.Cases[0]
            $raw = if ($isThreadExit) { $case.TargetProcess.RawExitCode } elseif ($isAv -or $kind -ceq 'shutdown-status') { -1073741819 } else { 0 }
            Assert-Attach ($record.EvidenceOnly -and $record.State -ceq 'completed' -and $case.Attach.State -ceq 'completed' -and
                -not $case.Attach.CleanupRequested -and $case.Attach.WorkerState -ceq 'Completed' -and $case.TargetProcess.RawExitCode -eq $raw -and
                ($isThreadExit -or $direct.RawExitCode -eq $raw) -and $case.TargetExitEvent.Pid -eq $case.TargetProcess.Pid -and $case.Process.Reaped -and
                $null -eq $case.Process.KillReason -and $case.Process.Pid -ne $case.TargetProcess.Pid) "Actual $kind program preserves separate natural target and controller exits."
            Assert-Attach ($case.DumpPresent -eq $isAv -and $record.DebuggerArguments[0] -ceq '-pd' -and $record.DebuggerArguments[1] -ceq '-p' -and
                $record.DebuggerHeap.Requested -ceq 'direct-start-then-attach') "Actual $kind Build entry binds attach mode and private fault dump."
            Assert-Attach ($case.Attach.ObservedCommandLine.Contains($program) -and $case.Attach.ObservedCommandLine.Contains('-projectPath') -and
                $case.Attach.ObservedCommandLine.Contains($project) -and (Test-Path -LiteralPath (Join-Path $casePath 'capture/build-target-console-head.log'))) "Actual $kind retains the observed OS command line and bounded startup output."
            if ($delayed) {
                Assert-Attach ($case.Attach.Boundary -ceq $(if ($shutdown) { 'reported-memory-leak-telemetry' } else { 'reported-domain-reload' }) -and $case.Attach.Startup.Line -ceq $(if ($shutdown) { '##utp:{"type":"MemoryLeaks","controlled":true}' } else { 'Domain Reload Profiling: controlled' }) -and
                    $case.Attach.Startup.Pid -eq $case.TargetProcess.Pid -and $case.Attach.Startup.CaptureId -ceq $case.Attach.CaptureId -and
                    [DateTimeOffset]::Parse($case.Attach.Startup.ObservedUtc) -le [DateTimeOffset]::Parse($case.Attach.RequestedUtc)) "Actual $kind observes startup on the exact target before requesting attachment."
            }
            Assert-Attach ([Environment]::GetEnvironmentVariable('_NT_SYMBOL_PATH', 'Process') -ceq $symbols -and [Environment]::GetEnvironmentVariable('_NO_DEBUG_HEAP', 'Process') -ceq $heap) "Actual $kind capture preserves inherited environment."
            Assert-Attach ($record.DebuggerCommandBudget.Admitted -and $record.DebuggerCommandBudget.Maximum -eq 4095 -and $record.DebuggerCommandBudget.Actual -eq $record.DebuggerArguments[4].Length) "Actual $kind binds the complete debugger command budget."
            $threadState = $case.Attach.ThreadDebugState
            Assert-Attach ($case.Attach.ThreadDebugApi.Available -and $threadState.OpenedHandles -gt 0 -and $threadState.OpenedHandles -eq $threadState.ClosedHandles -and $null -ne $threadState.CompletedUtc) "Actual $kind records and closes its owned thread observations."
            $console = [IO.File]::ReadAllText((Join-Path $casePath 'capture/build-attach-console-head.log'))
            $breakpointSections = [regex]::Matches($console, '(?ms)^WUH_BREAKPOINTS_BEGIN\r?\n(?<Rows>.*?)^WUH_BREAKPOINTS_END\r?$')
            Assert-Attach ($breakpointSections.Count -eq 1) "Actual $kind retains one executed breakpoint-state section."
            foreach ($name in @('NtTerminateProcess', 'RtlExitUserProcess', 'RtlExitUserThread', 'NtTerminateThread')) {
                $pattern = '(?m)^\s*\d+ e [0-9a-f`]+\s+[^\r\n]*ntdll!' + $name + '(?:\s|$)'
                Assert-Attach ([regex]::Matches($breakpointSections[0].Groups['Rows'].Value, $pattern).Count -eq 1) "Actual $kind resolves and enables $name before continuation."
            }
            $firstChanceCount = [regex]::Matches($console, '(?m)^WUH_FIRST_CHANCE_AV\r?$').Count
            Assert-Attach ($console -match '(?m)^\s*av - Access violation - second-chance break - not handled\r?$' -and
                [regex]::Matches($console, '(?m)^WUH_EXCEPTION_FILTERS_END\r?$').Count -eq 1) "Actual $kind retains the AV filter policy and executed filter output marker."
            Assert-Attach ($firstChanceCount -eq $(if ($kind -ceq 'shutdown-handled') { 8 } elseif ($isAv) { 1 } else { 0 })) "Actual $kind distinguishes bounded handled AV, fault and status-only termination."
            if ($kind -in @('shutdown-normal', 'shutdown-status', 'shutdown-handled')) {
                Assert-Attach ([regex]::Matches($console, '(?m)^WUH_TERMINATE_REQUEST\r?$').Count -ge 1) "Actual $kind records the native termination request stack."
            }
            if ($isThreadExit) {
                Assert-Attach ($raw -in @(0, -1073741819) -and $direct.RawExitCode -in @(0, -1073741819) -and
                    $case.TargetProcess.Reaped -and $null -eq $case.TargetProcess.KillReason -and $direct.Reaped -and $null -eq $direct.KillReason) 'Thread-status controls retain separate natural outcomes without assuming the caller is the final thread.'
                $targetHead = [IO.File]::ReadAllText((Join-Path $casePath 'capture/build-target-console-head.log'))
                $mainThread = [regex]::Matches($targetHead, '(?m)^WUH_CONTROL_MAIN_THREAD=(\d+)\r?$')
                Assert-Attach ($mainThread.Count -eq 1) 'Native thread control reports one actual main OS thread ID.'
                $threadId = [uint32]$mainThread[0].Groups[1].Value
                $waiterThread = [regex]::Matches($targetHead, '(?m)^WUH_CONTROL_WAITER_THREAD=(\d+)\r?$')
                Assert-Attach ($waiterThread.Count -eq 1 -and [uint32]$waiterThread[0].Groups[1].Value -ne $threadId) 'Native control owns a distinct waiter thread until the main thread terminates.'
                $waiterId = [uint32]$waiterThread[0].Groups[1].Value
                foreach ($observation in @(@{ Marker = 'WUH_THREAD_EXIT'; Register = 'rcx' }, @{ Marker = 'WUH_THREAD_TERMINATE'; Register = 'rdx' })) {
                    $threadSections = [regex]::Matches($console, '(?ms)^' + $observation.Marker + '\r?\n(?<Body>.*?)^' + $observation.Marker + '_END\r?$')
                    $observedCalls = 0
                    foreach ($section in $threadSections) {
                        $body = $section.Groups['Body'].Value
                        $identity = [regex]::Match($body, '(?m)^Evaluate expression: (\d+) = ([0-9a-f`]+)\r?$', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
                        $status = [regex]::Match($body, '\b' + $observation.Register + '=([0-9a-f]{16})\b', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
                        if ($identity.Success -and $status.Success -and [uint32]$identity.Groups[1].Value -eq $threadId -and
                            [Convert]::ToUInt32($identity.Groups[2].Value.Replace('`', ''), 16) -eq $threadId -and
                            ([Convert]::ToUInt64($status.Groups[1].Value, 16) -band 4294967295L) -eq 3221225477L) {
                            foreach ($listedThread in [regex]::Matches($body, '(?im)^\s*[.#]?\s*\d+\s+Id:\s+[0-9a-f]+\.([0-9a-f]+)\s')) {
                                if ([Convert]::ToUInt32($listedThread.Groups[1].Value, 16) -eq $waiterId) { ++$observedCalls }
                            }
                        }
                    }
                    Assert-Attach ($observedCalls -ge 1) "Observe the actual control thread's C0000005 status with its owned waiter still listed at $($observation.Marker)."
                }
                Assert-Attach (-not $case.Process.ConsoleTailTruncated -and $console -ceq [IO.File]::ReadAllText((Join-Path $casePath 'capture/build-debugger-console.log'))) 'Native thread-status assertions use a complete retained debugger console.'
            }
            Write-Host "Actual $kind Build entry evidence: $casePath"
        }
        foreach ($kind in @('early', 'timeout')) {
            $casePath = Join-Path $root ('startup-' + $kind); $null = [IO.Directory]::CreateDirectory($casePath)
            $failed = $false
            try {
                & $source -EditorPath (Join-Path $NativeProgramsDirectory ($kind + '/control.exe')) -ProjectPath $project -BuildArguments $arguments -BuildLogPath (Join-Path $casePath 'build.log') `
                    -ArtifactsPath (Join-Path $casePath 'capture') -DebuggerDirectory $DebuggerDirectory -TimeoutSeconds 8 -AttachToDirect -AttachAfterStartup
            } catch { $failed = $true }
            $record = Get-Content -LiteralPath (Join-Path $casePath 'capture/receipt.json') -Raw | ConvertFrom-Json -AsHashtable
            $case = $record.Cases[0]
            Assert-Attach ($failed -and $record.State -ceq 'failed' -and $case.Process.Count -eq 0 -and $null -eq $case.Attach.Startup -and
                $case.TargetProcess.Reaped -and $case.Attach.WorkerState -cne 'Running') "Actual startup $kind cannot attach without a complete startup observation and reaps the target."
        }
        foreach ($kind in @('early', 'timeout')) {
            $program = Join-Path $NativeProgramsDirectory ($kind + '/control.exe')
            $casePath = Join-Path $root $kind; $null = [IO.Directory]::CreateDirectory($casePath)
            $arguments = @('-batchmode', '-nographics', '-projectPath', $project, '-runTests', '-testPlatform', 'StandaloneWindows64', '-logFile', '-')
            $failed = $false
            try {
                & $source -EditorPath $program -ProjectPath $project -BuildArguments $arguments -BuildLogPath (Join-Path $casePath 'build.log') `
                    -ArtifactsPath (Join-Path $casePath 'capture') -DebuggerDirectory $DebuggerDirectory -TimeoutSeconds 4 -AttachToDirect
            } catch { $failed = $true }
            $record = Get-Content -LiteralPath (Join-Path $casePath 'capture/receipt.json') -Raw | ConvertFrom-Json -AsHashtable
            $case = $record.Cases[0]
            Assert-Attach ($failed -and $record.State -ceq 'failed' -and $case.Attach.State -cne 'completed' -and $case.TargetProcess.Reaped -and $case.Attach.WorkerState -cne 'Running') "Actual $kind target is reaped and cannot complete attach evidence."
            if ($kind -eq 'early') {
                Assert-Attach ($case.TargetProcess.RawExitCode -eq 0 -and $null -eq $case.TargetProcess.KillReason -and $case.Process.Count -eq 0) 'Early natural exit is retained without a debugger launch or fabricated controller outcome.'
            } else {
                Assert-Attach ($null -ne $case.TargetProcess.KillReason -and $case.TargetProcess.KillReason -in @('coordinator-cancellation', 'wall-clock-timeout')) 'Timed-out direct target records its explicit termination origin.'
            }
        }
        $originalBudgetCommand = ${function:Get-NativeCaptureDebuggerCommand}
        try {
            function Get-NativeCaptureDebuggerCommand { param($DumpPath) return (';' * 4095) }
            $casePath = Join-Path $root 'overlength-attach'; $null = [IO.Directory]::CreateDirectory($casePath)
            $private = Join-Path $casePath 'private'; $null = [IO.Directory]::CreateDirectory($private)
            $record = [ordered]@{ Cases = @(@{ Process = @{}; TargetExitEvent = $null }); WorkingDirectory = [Environment]::CurrentDirectory; DebuggerHeap = @{} }
            $failed = $false
            try {
                $null = Invoke-NativeAttachCapture -CaptureScript $source -EditorPath (Join-Path $NativeProgramsDirectory 'timeout/control.exe') -Arguments @() -DebuggerPath (Join-Path $root 'must-not-launch-cdb.exe') -PrivateDirectory $private -ArtifactsPath $casePath -Seconds 15 -Record $record
            } catch {
                if ($_.Exception.Message -notlike '*4095-character*') { throw }
                $failed = $true
            }
            $case = $record.Cases[0]
            Assert-Attach ($failed -and -not $record.DebuggerCommandBudget.Admitted -and $record.DebuggerCommandBudget.Actual -eq $record.DebuggerArguments[4].Length -and $record.DebuggerCommandBudget.Actual -gt 4095) 'The attachment prefix counts toward the complete command limit.'
            Assert-Attach ($case.Process.Count -eq 0 -and $case.TargetProcess.Reaped -and $case.TargetProcess.KillReason -ceq 'coordinator-cancellation' -and $case.Attach.WorkerState -ceq 'Completed') 'Overlength attachment launches no debugger and cancels/reaps its owned direct target.'
        } finally { ${function:Get-NativeCaptureDebuggerCommand} = $originalBudgetCommand }
        foreach ($failure in @('missing-debugger', 'no-attach-controller')) {
            $casePath = Join-Path $root $failure; $null = [IO.Directory]::CreateDirectory($casePath)
            $private = Join-Path $casePath 'private'; $null = [IO.Directory]::CreateDirectory($private)
            $record = [ordered]@{ Cases = @(@{ Process = @{}; TargetExitEvent = $null }); WorkingDirectory = [Environment]::CurrentDirectory; DebuggerHeap = @{} }
            $controller = if ($failure -eq 'missing-debugger') { Join-Path $root 'missing-cdb.exe' } else { Join-Path $NativeProgramsDirectory 'early/control.exe' }
            $failed = $false
            try {
                $null = Invoke-NativeAttachCapture -CaptureScript $source -EditorPath (Join-Path $NativeProgramsDirectory 'timeout/control.exe') -Arguments @() `
                    -DebuggerPath $controller -PrivateDirectory $private -ArtifactsPath $casePath -Seconds 15 -Record $record
            } catch { $failed = $true }
            $case = $record.Cases[0]
            Assert-Attach ($failed -and $case.Attach.CleanupRequested -and $case.Attach.State -cne 'completed' -and $case.TargetProcess.Reaped -and
                $case.TargetProcess.KillReason -ceq 'coordinator-cancellation' -and $case.Attach.WorkerState -ceq 'Completed') "Actual $failure cancels and reaps the owned direct target."
        }
        $workerScript = Join-Path $root 'wrong-identity-worker.ps1'
        [IO.File]::WriteAllText($workerScript, @'
param($WorkerSpecification, $ArtifactsPath)
$ErrorActionPreference = 'Stop'
$repo = $env:NATIVE_ATTACH_CONTROL_REPOSITORY
. (Join-Path $repo 'scripts/unity/lib/native-attach.ps1')
. (Join-Path $repo 'scripts/unity/lib/credential-redaction.ps1')
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'scripts/unity/capture-native-fault.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'Controlled worker source did not parse.' }
$function = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq 'Invoke-NativeCaptureProcess' }, $true)
. ([scriptblock]::Create($function.Extent.Text))
$shippingIdentity = ${function:Write-NativeAttachIdentity}
function Write-NativeAttachIdentity {
    param($Process, $Coordination)
    $changed = $Coordination.Clone()
    $changed.CaptureId = [Guid]::NewGuid().ToString('N')
    & $shippingIdentity -Process $Process -Coordination $changed
}
Invoke-NativeAttachWorker -Specification $WorkerSpecification -ArtifactsPath $ArtifactsPath
'@)
        $originalRepository = [Environment]::GetEnvironmentVariable('NATIVE_ATTACH_CONTROL_REPOSITORY', 'Process')
        try {
            $env:NATIVE_ATTACH_CONTROL_REPOSITORY = $repo
            $casePath = Join-Path $root 'wrong-identity'; $null = [IO.Directory]::CreateDirectory($casePath)
            $private = Join-Path $casePath 'private'; $null = [IO.Directory]::CreateDirectory($private)
            $record = [ordered]@{ Cases = @(@{ Process = @{}; TargetExitEvent = $null }); WorkingDirectory = [Environment]::CurrentDirectory; DebuggerHeap = @{} }
            $failed = $false
            try {
                $null = Invoke-NativeAttachCapture -CaptureScript $workerScript -EditorPath (Join-Path $NativeProgramsDirectory 'timeout/control.exe') -Arguments @() `
                    -DebuggerPath (Join-Path $DebuggerDirectory 'cdb.exe') -PrivateDirectory $private -ArtifactsPath $casePath -Seconds 15 -Record $record
            } catch { $failed = $_.Exception.Message -ceq 'Attach identity does not match the owned launch.' }
            $case = $record.Cases[0]
            Assert-Attach ($failed -and $null -eq $case.Attach.Identity -and $case.Process.Count -eq 0 -and $case.Attach.CleanupRequested -and
                $case.TargetProcess.Reaped -and $case.TargetProcess.KillReason -ceq 'coordinator-cancellation' -and $case.Attach.WorkerState -ceq 'Completed') 'A copied worker with the wrong nonce is refused before debugger launch while its real owned target is cancelled and reaped.'
        } finally { [Environment]::SetEnvironmentVariable('NATIVE_ATTACH_CONTROL_REPOSITORY', $originalRepository, 'Process') }
        Write-Host "Retained Windows controls at $root"
    } else { Write-Host 'Actual Windows CDB attachment controls were not requested; no Windows qualification claimed.' }
    Write-Host "$passed native attach controls passed"
    & (Join-Path $PSScriptRoot 'test-native-attach-startup.ps1')
    & (Join-Path $PSScriptRoot 'test-native-thread-debug-state.ps1')
    $completed = $true
} finally {
    if ($completed -and -not $RequireWindows) { Remove-Item -LiteralPath $root -Recurse -Force }
    else { Write-Host "Retained attach control evidence at $root" }
}
