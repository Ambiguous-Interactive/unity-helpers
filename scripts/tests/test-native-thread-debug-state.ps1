#!/usr/bin/env pwsh
[CmdletBinding()]
param([switch]$RequireWindows, [string]$NativeProgramsDirectory)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $repo 'scripts/unity/lib/native-thread-debug-state.ps1')
$passed = 0
function Assert-ThreadState([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    ++$script:passed
}
if ($RequireWindows -and -not $IsWindows) { throw 'Actual Windows thread-state controls were required.' }
$empty = [Microsoft.Win32.SafeHandles.SafeWaitHandle]::new([IntPtr]::Zero, $true)
try {
    $row = Read-NativeThreadDebugRow -Handle $empty -ThreadId 1 -ExpectedPid 1
    Assert-ThreadState ($row.State -ceq 'handle-unavailable' -and $null -eq $row.HideFromDebugger -and $null -eq $row.NtStatus) 'An invalid handle cannot fabricate a clear debugging flag.'
} finally { $empty.Dispose() }
$row = Read-NativeThreadDebugRow -Handle $empty -ThreadId 1 -ExpectedPid 1
Assert-ThreadState ($row.State -ceq 'handle-unavailable' -and $null -eq $row.HideFromDebugger) 'A closed handle remains unavailable.'
foreach ($limit in @(0, 257)) {
    $rejected = $false
    try { $null = Start-NativeThreadDebugSnapshot -Target $null -ExpectedPid 1 -MaximumThreads $limit } catch { $rejected = $true }
    Assert-ThreadState $rejected 'Thread-handle growth is bounded independently of the target count.'
}
if (-not $IsWindows) {
    $context = Start-NativeThreadDebugSnapshot -Target $null -ExpectedPid 1
    Complete-NativeThreadDebugSnapshot -Context $context
    Complete-NativeThreadDebugSnapshot -Context $context
    Assert-ThreadState ($context.Observation.State -ceq 'unsupported-platform' -and $context.Entries.Count -eq 0 -and $context.Observation.Before.Count -eq 0 -and $context.Observation.After.Count -eq 0) 'Unsupported platforms retain unknown state without native calls.'
    Assert-ThreadState ($context.Completed -and $context.Observation.OpenedHandles -eq 0 -and $context.Observation.ClosedHandles -eq 0) 'Empty completion is idempotent.'
} elseif ($RequireWindows) {
    if ([string]::IsNullOrWhiteSpace($NativeProgramsDirectory)) { throw 'Compiled visible/early-hidden/late-hidden control programs are required.' }
    Assert-ThreadState (Initialize-NativeThreadDebugInterop) 'The actual native query export is available on this test host.'
    Assert-ThreadState (Initialize-NativeThreadDebugInterop) 'Repeated initialization preserves the native query.'
    foreach ($selection in @(@{ Name = 'visible'; Before = $false; After = $false }, @{ Name = 'early-hidden'; Before = $true; After = $true }, @{ Name = 'late-hidden'; Before = $false; After = $true })) {
        $program = Join-Path $NativeProgramsDirectory ($selection.Name + '/control.exe')
        $hash = (Get-FileHash -LiteralPath $program).Hash
        $info = [Diagnostics.ProcessStartInfo]::new($program)
        $info.UseShellExecute = $false; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true; $info.CreateNoWindow = $true
        $process = [Diagnostics.Process]::Start($info)
        $context = $null; $limited = $null; $expired = $null
        try {
            $mainThreadId = 0
            for ($index = 0; $index -lt 8; ++$index) {
                $pending = $process.StandardOutput.ReadLineAsync()
                if (-not $pending.Wait(5000)) { throw 'Owned control startup output timed out.' }
                $line = $pending.Result
                if ($line -match '^WUH_CONTROL_MAIN_THREAD=(\d+)$') { $mainThreadId = [uint32]$Matches[1] }
                if ($line -like '##utp:*MemoryLeaks*') { break }
            }
            Assert-ThreadState ($mainThreadId -gt 0 -and -not $process.HasExited) 'The owned native control reports its main thread before capture.'
            $wrong = Start-NativeThreadDebugSnapshot -Target $process -ExpectedPid ([uint32]($process.Id + 1))
            Complete-NativeThreadDebugSnapshot -Context $wrong
            Assert-ThreadState ($wrong.Observation.State -ceq 'target-mismatch' -and $wrong.Observation.OpenedHandles -eq 0) 'A different process identity cannot open target threads.'
            $expired = Start-NativeThreadDebugSnapshot -Target $process -ExpectedPid $process.Id -Deadline ([DateTime]::MinValue)
            Complete-NativeThreadDebugSnapshot -Context $expired
            Assert-ThreadState ($expired.Observation.BeforeBudgetExpired -and $expired.Observation.OpenedHandles -eq 0) 'An expired query budget opens no handles.'
            $limited = Start-NativeThreadDebugSnapshot -Target $process -ExpectedPid $process.Id -MaximumThreads 1
            Assert-ThreadState ($limited.Entries.Count -le 1 -and $limited.Observation.OpenedHandles -le 1) 'The requested handle limit holds on the real target.'
            Complete-NativeThreadDebugSnapshot -Context $limited
            $context = Start-NativeThreadDebugSnapshot -Target $process -ExpectedPid $process.Id
            $before = @($context.Observation.Before | Where-Object { $_.ThreadId -eq $mainThreadId })
            Assert-ThreadState ($before.Count -eq 1 -and $before[0].State -ceq 'observed' -and $before[0].HideFromDebugger -eq $selection.Before -and $before[0].OwnerPid -eq $process.Id) 'Actual pre-attach flags match the controlled thread state.'
            $entry = @($context.Entries | Where-Object { $_.ThreadId -eq $mainThreadId })[0]
            $foreign = Read-NativeThreadDebugRow -Handle $entry.Handle -ThreadId $mainThreadId -ExpectedPid ([uint32]($process.Id + 1))
            Assert-ThreadState ($foreign.State -ceq 'owner-mismatch' -and $null -eq $foreign.HideFromDebugger) 'An owner mismatch is unknown, not a clear flag.'
            $mismatch = Read-NativeThreadDebugRow -Handle $entry.Handle -ThreadId $mainThreadId -ExpectedPid $process.Id -ExpectedCreation ($entry.CreationFileTime + 1)
            Assert-ThreadState ($mismatch.State -ceq 'creation-mismatch' -and $null -eq $mismatch.HideFromDebugger) 'A different native thread lifetime is rejected.'
            Assert-ThreadState ($process.WaitForExit(15000) -and $process.ExitCode -eq -1073741819) 'The source-known native AV exits without coordinator termination.'
            Complete-NativeThreadDebugSnapshot -Context $context
            $after = @($context.Observation.After | Where-Object { $_.ThreadId -eq $mainThreadId })
            Assert-ThreadState ($after.Count -eq 1 -and $after[0].State -ceq 'observed' -and $after[0].HideFromDebugger -eq $selection.After -and $after[0].ThreadExitObserved -and $after[0].CreationFileTime -eq $before[0].CreationFileTime) 'Held-object post-exit queries preserve identity and detect a late flag change.'
            $closed = $context.Observation.ClosedHandles
            Complete-NativeThreadDebugSnapshot -Context $context
            Assert-ThreadState ($closed -eq $context.Observation.OpenedHandles -and $context.Observation.ClosedHandles -eq $closed -and @($context.Entries | Where-Object { -not $_.Handle.IsClosed }).Count -eq 0) 'All handles close exactly once and completion is idempotent.'
            Assert-ThreadState ((Get-FileHash -LiteralPath $program).Hash -ceq $hash) 'The controlled program stays byte-identical during the observation.'
        } finally {
            Complete-NativeThreadDebugSnapshot -Context $context
            Complete-NativeThreadDebugSnapshot -Context $limited
            Complete-NativeThreadDebugSnapshot -Context $expired
            if (-not $process.HasExited) { $process.Kill($true); [void]$process.WaitForExit(10000) }
            $process.Dispose()
        }
    }
} else { Write-Host 'Actual Windows thread-state controls were not requested; no Windows qualification claimed.' }
Write-Host "$passed native thread-state controls passed"
