#!/usr/bin/env pwsh

function Initialize-NativeThreadDebugInterop {
    param([ref]$ErrorType)

    if (-not $IsWindows) { return $false }
    try {
    Add-Type -TypeDefinition @'
namespace WallstopStudios.UnityHelpers.NativeCapture
{
    using System;
    using System.Runtime.InteropServices;

    public static class ThreadDebugInterop
    {
        private const uint QueryAccess = 0x0040;
        private const uint HideInformationClass = 17;
        private const int UnavailableStatus = unchecked((int)0xc0000002);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int QueryInformation(IntPtr thread, uint informationClass, ref byte value, uint length, out uint returned);

        private static QueryInformation query;

        public static bool Initialize()
        {
            if (query != null) { return true; }
            IntPtr module = GetModuleHandle("ntdll.dll");
            if (module == IntPtr.Zero) { return false; }
            IntPtr address = GetProcAddress(module, "NtQueryInformationThread");
            if (address == IntPtr.Zero) { return false; }
            query = Marshal.GetDelegateForFunctionPointer<QueryInformation>(address);
            return true;
        }

        public static IntPtr Open(uint threadId, out int error)
        {
            IntPtr handle = OpenThread(QueryAccess, false, threadId);
            error = handle == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
            return handle;
        }

        public static uint Owner(IntPtr handle, out int error)
        {
            uint owner = GetProcessIdOfThread(handle);
            error = owner == 0 ? Marshal.GetLastWin32Error() : 0;
            return owner;
        }

        public static bool Times(IntPtr handle, out long created, out long exited, out int error)
        {
            long kernel;
            long user;
            bool observed = GetThreadTimes(handle, out created, out exited, out kernel, out user);
            error = observed ? 0 : Marshal.GetLastWin32Error();
            return observed;
        }

        public static int ReadFlag(IntPtr handle, out byte value, out uint returned)
        {
            value = 204;
            returned = 0;
            if (query == null) { return UnavailableStatus; }
            return query(handle, HideInformationClass, ref value, 1, out returned);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenThread(uint access, bool inherit, uint threadId);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetProcessIdOfThread(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetThreadTimes(IntPtr handle, out long created, out long exited, out long kernel, out long user);
    }
}
'@
    return [WallstopStudios.UnityHelpers.NativeCapture.ThreadDebugInterop]::Initialize()
    } catch {
        if ($null -ne $ErrorType) { $ErrorType.Value = $_.Exception.GetType().FullName }
        return $false
    }
}

function Read-NativeThreadDebugRow {
    param(
        [Microsoft.Win32.SafeHandles.SafeWaitHandle]$Handle,
        [uint32]$ThreadId, [uint32]$ExpectedPid, [long]$ExpectedCreation = 0
    )

    $row = [ordered]@{
        ThreadId = $ThreadId; ExpectedPid = $ExpectedPid; OwnerPid = $null
        CreationFileTime = $null; ExitFileTime = $null; ThreadTimesObserved = $false
        NtStatus = $null; ReturnedLength = $null; RawByte = $null; HideFromDebugger = $null
        State = 'unavailable'; Win32Error = $null; ErrorType = $null
        ObservedUtc = [DateTime]::UtcNow.ToString('o')
    }
    if ($null -eq $Handle -or $Handle.IsClosed -or $Handle.IsInvalid) { $row.State = 'handle-unavailable'; return $row }
    try {
        $native = $Handle.DangerousGetHandle()
        [int]$errorCode = 0
        $owner = [WallstopStudios.UnityHelpers.NativeCapture.ThreadDebugInterop]::Owner($native, [ref]$errorCode)
        $row.OwnerPid = $owner
        if ($owner -ne $ExpectedPid) { $row.State = 'owner-mismatch'; $row.Win32Error = $errorCode; return $row }
        [long]$created = 0; [long]$exited = 0
        $row.ThreadTimesObserved = [WallstopStudios.UnityHelpers.NativeCapture.ThreadDebugInterop]::Times($native, [ref]$created, [ref]$exited, [ref]$errorCode)
        $row.CreationFileTime = $created; $row.ExitFileTime = $exited
        if (-not $row.ThreadTimesObserved -or $created -le 0) { $row.State = 'times-unavailable'; $row.Win32Error = $errorCode; return $row }
        if ($ExpectedCreation -gt 0 -and $created -ne $ExpectedCreation) { $row.State = 'creation-mismatch'; return $row }
        [byte]$flag = 204; [uint32]$returned = 0
        $row.NtStatus = [WallstopStudios.UnityHelpers.NativeCapture.ThreadDebugInterop]::ReadFlag($native, [ref]$flag, [ref]$returned)
        $row.ReturnedLength = $returned; $row.RawByte = $flag
        if ($row.NtStatus -eq 0 -and $returned -eq 1 -and $flag -in @(0, 1)) {
            $row.State = 'observed'; $row.HideFromDebugger = ($flag -eq 1)
        } else { $row.State = 'query-unavailable' }
    } catch { $row.State = 'query-failed'; $row.ErrorType = $_.Exception.GetType().FullName }
    return $row
}

function Start-NativeThreadDebugSnapshot {
    param(
        [Diagnostics.Process]$Target, [uint32]$ExpectedPid,
        [DateTime]$Deadline = [DateTime]::MaxValue,
        [ValidateRange(1, 256)][int]$MaximumThreads = 256
    )

    $observation = [ordered]@{
        State = 'initializing'; ExpectedPid = $ExpectedPid; MaximumThreads = $MaximumThreads
        MaximumQueryMilliseconds = 2000; Before = @(); After = @(); OmittedBefore = 0
        OpenedHandles = 0; ClosedHandles = 0; BeforeBudgetExpired = $false; AfterBudgetExpired = $false
        StartedUtc = [DateTime]::UtcNow.ToString('o'); CompletedUtc = $null; ErrorType = $null
        Qualification = 'Read-only initial-thread snapshot and held-object follow-up. New threads are not covered. Unknown or failed queries do not mean a clear flag. OS thread IDs alone do not establish a faulting role.'
    }
    $context = @{ Observation = $observation; Entries = [Collections.Generic.List[hashtable]]::new(); Completed = $false }
    if (-not $IsWindows) { $observation.State = 'unsupported-platform'; return $context }
    $timer = [Diagnostics.Stopwatch]::StartNew()
    try {
        if ($null -eq $Target -or $Target.Id -ne $ExpectedPid) { $observation.State = 'target-mismatch'; return $context }
        [string]$apiError = $null
        if (-not (Initialize-NativeThreadDebugInterop -ErrorType ([ref]$apiError))) { $observation.State = 'api-unavailable'; $observation.ErrorType = $apiError; return $context }
        $Target.Refresh()
        $threads = $Target.Threads
        $count = [Math]::Min($threads.Count, $MaximumThreads)
        $observation.OmittedBefore = $threads.Count - $count
        for ($index = 0; $index -lt $count; ++$index) {
            if ($timer.ElapsedMilliseconds -ge 2000 -or [DateTime]::UtcNow -ge $Deadline) {
                $observation.BeforeBudgetExpired = $true; $observation.OmittedBefore += $count - $index; break
            }
            [uint32]$threadId = $threads[$index].Id
            [int]$errorCode = 0
            $native = [WallstopStudios.UnityHelpers.NativeCapture.ThreadDebugInterop]::Open($threadId, [ref]$errorCode)
            if ($native -eq [IntPtr]::Zero) {
                $observation.Before += @{ ThreadId = $threadId; State = 'open-failed'; Win32Error = $errorCode; HideFromDebugger = $null }
                continue
            }
            $handle = [Microsoft.Win32.SafeHandles.SafeWaitHandle]::new($native, $true)
            ++$observation.OpenedHandles
            $retained = $false
            try {
                $row = Read-NativeThreadDebugRow -Handle $handle -ThreadId $threadId -ExpectedPid $ExpectedPid
                $observation.Before += $row
                if ($row.OwnerPid -eq $ExpectedPid -and $row.ThreadTimesObserved -and $row.CreationFileTime -gt 0) {
                    $context.Entries.Add(@{ Handle = $handle; ThreadId = $threadId; CreationFileTime = $row.CreationFileTime })
                    $retained = $true
                }
            } finally {
                if (-not $retained) { $handle.Dispose(); ++$observation.ClosedHandles }
            }
        }
        $observation.State = 'initial-snapshot-recorded'
    } catch { $observation.State = 'snapshot-failed'; $observation.ErrorType = $_.Exception.GetType().FullName }
    return $context
}

function Complete-NativeThreadDebugSnapshot {
    param([hashtable]$Context)

    if ($null -eq $Context -or $Context.Completed) { return }
    $Context.Completed = $true
    $observation = $Context.Observation
    $timer = [Diagnostics.Stopwatch]::StartNew()
    try {
        foreach ($entry in $Context.Entries) {
            if ($timer.ElapsedMilliseconds -ge 2000) { $observation.AfterBudgetExpired = $true; break }
            $row = Read-NativeThreadDebugRow -Handle $entry.Handle -ThreadId $entry.ThreadId -ExpectedPid $observation.ExpectedPid -ExpectedCreation $entry.CreationFileTime
            $row.ThreadExitObserved = ($row.ThreadTimesObserved -and $row.ExitFileTime -gt 0)
            $observation.After += $row
        }
    } catch { $observation.State = 'follow-up-failed'; $observation.ErrorType = $_.Exception.GetType().FullName }
    finally {
        foreach ($entry in $Context.Entries) {
            if (-not $entry.Handle.IsClosed) { $entry.Handle.Dispose(); ++$observation.ClosedHandles }
        }
        $observation.CompletedUtc = [DateTime]::UtcNow.ToString('o')
    }
}
