#!/usr/bin/env pwsh
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$source = Join-Path $repo 'scripts/unity/capture-native-fault.ps1'
. (Join-Path $repo 'scripts/unity/lib/native-attach.ps1')
. (Join-Path $repo 'scripts/unity/lib/credential-redaction.ps1')
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($source, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'Native capture source did not parse.' }
foreach ($name in @('Invoke-NativeCaptureProcess', 'Get-NativeCaptureDebuggerCommand', 'Get-NativeCaptureExitEvent')) {
    $function = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq $name }, $true)
    . ([scriptblock]::Create($function.Extent.Text))
}
$root = Join-Path ([IO.Path]::GetTempPath()) ('native-startup-controls-' + [Guid]::NewGuid().ToString('N'))
$null = [IO.Directory]::CreateDirectory($root)
$passed = 0; $completed = $false
function Assert-Startup([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message }; ++$script:passed }
function Send-Startup([hashtable]$Coordination, [hashtable]$State, [string]$Text) {
    Add-NativeAttachStartupOutput -Coordination $Coordination -State $State -Buffer $Text.ToCharArray() -Count $Text.Length -ProcessId 123 -DeliveredCharacters 10000
}
try {
    $prefix = 'Domain Reload Profiling:'
    $coordination = @{ CaptureId = [Guid]::NewGuid().ToString('N'); StartupMarkerPrefix = $prefix; StartupReceiptPath = (Join-Path $root 'startup.json') }
    $state = @{ Pending = ''; Discarding = $false; Completed = $false }
    Send-Startup $coordination $state 'Domain Reload Pro'
    Send-Startup $coordination $state 'filing: 1500ms'
    Assert-Startup (-not (Test-Path -LiteralPath $coordination.StartupReceiptPath)) 'A split or unterminated line cannot authorize attachment.'
    Send-Startup $coordination $state "`r`n"
    $identity = @{ CaptureId = $coordination.CaptureId; Pid = 123; CreatedTicks = [DateTime]::UtcNow.AddMinutes(-1).Ticks }
    $observed = Read-NativeAttachStartup -Path $coordination.StartupReceiptPath -Identity $identity -Prefix $prefix
    Assert-Startup ($state.Completed -and $observed.Line -ceq 'Domain Reload Profiling: 1500ms' -and $observed.Pid -eq 123) 'Complete split stdout lines produce a bound startup observation.'
    $original = [IO.File]::ReadAllText($coordination.StartupReceiptPath)
    Send-Startup $coordination $state "Domain Reload Profiling: later`n"
    Assert-Startup ([IO.File]::ReadAllText($coordination.StartupReceiptPath) -ceq $original) 'The first startup observation is immutable.'
    foreach ($kind in @('mid-line', 'overlong-pending', 'overlong-complete')) {
        $coord = $coordination.Clone(); $coord.StartupReceiptPath = Join-Path $root ($kind + '.json')
        $stream = @{ Pending = ''; Discarding = $false; Completed = $false }
        if ($kind -eq 'mid-line') { Send-Startup $coord $stream "junkDomain Reload Profiling: false`n" }
        elseif ($kind -eq 'overlong-complete') { Send-Startup $coord $stream ($prefix + ('x' * 5000) + "`n") }
        else { Send-Startup $coord $stream ('x' * 9000); Send-Startup $coord $stream "Domain Reload Profiling: false`n" }
        Assert-Startup (-not $stream.Completed -and $stream.Pending.Length -le 8192) "Reject $kind without inventing a new line boundary."
        Send-Startup $coord $stream "Domain Reload Profiling: real`n"
        Assert-Startup $stream.Completed "A later complete line remains observable after $kind."
    }
    foreach ($mutation in @('nonce', 'pid', 'prefix', 'line', 'old-time', 'future-time', 'zero-delivery', 'pid-string', 'duplicate', 'unknown', 'partial', 'oversize')) {
        $record = $original | ConvertFrom-Json -AsHashtable
        switch ($mutation) {
            'nonce' { $record.CaptureId = [Guid]::NewGuid().ToString('N') }
            'pid' { $record.Pid = 124 }
            'prefix' { $record.Prefix = 'other' }
            'line' { $record.Line = "Domain Reload Profiling: injected`n" }
            'old-time' { $record.ObservedUtc = [DateTime]::UtcNow.AddDays(-1).ToString('o') }
            'future-time' { $record.ObservedUtc = [DateTime]::UtcNow.AddDays(1).ToString('o') }
            'zero-delivery' { $record.StdoutCharacters = 0 }
            'pid-string' { $record.Pid = '123' }
            'unknown' { $record.Extra = 1 }
        }
        $text = $record | ConvertTo-Json
        if ($mutation -eq 'duplicate') { $text = $text.Replace('{', '{"Pid":123,') }
        if ($mutation -eq 'partial') { $text = '{' }
        if ($mutation -eq 'oversize') { $text = ' ' * 32769 }
        $path = Join-Path $root ($mutation + '.json'); [IO.File]::WriteAllText($path, $text)
        $rejected = $false
        try { $null = Read-NativeAttachStartup -Path $path -Identity $identity -Prefix $prefix } catch { $rejected = $true }
        Assert-Startup $rejected "Refuse startup $mutation observation."
    }
    & {
        $shipping = ${function:Invoke-NativeAttachCapture}
        $script:boundaryPid = 0; $script:boundaryEnds = 0; $script:boundaryCalls = 0; $script:boundarySignal = ''
        function Save-NativeCaptureRecord { }
        function Open-NativeAttachTarget {
            param($IdentityPath, $Expected)
            $id = Get-Content -LiteralPath $IdentityPath -Raw | ConvertFrom-Json -AsHashtable
            $script:boundaryPid = $id.Pid
            return @{ Identity = $id; Target = [Diagnostics.Process]::GetProcessById($id.Pid); Worker = [Diagnostics.Process]::GetProcessById($id.WorkerPid); VerifiedUtc = [DateTime]::UtcNow.ToString('o'); ObservedCommandLine = 'protocol identity stand-in' }
        }
        function Invoke-NativeCaptureProcess {
            param($FilePath, $Arguments, $Directory, $Seconds, $Observation, $ReceiptPath, [switch]$PreserveSymbolPath, $HeadPath, $TailPath)
            ++$script:boundaryCalls
            $hex = $script:boundaryPid.ToString('x')
            $head = "cdb: Reading initial command '.echo WUH_ATTACH_HEAP_END;'`nWUH_ATTACH_PID=$hex`n" + ("WUH_ATTACH_HEAP_END`n" * $script:boundaryEnds)
            [IO.File]::WriteAllText($HeadPath, $head)
            $text = "Last event: $hex.1: Exit process 0:$hex, code 0`n"
            [IO.File]::WriteAllText($TailPath, $text)
            [IO.File]::WriteAllText($script:boundarySignal, 'release')
            $Observation.Pid = 999; $Observation.Reaped = $true; $Observation.KillReason = $null; $Observation.RawExitCode = 0
            return $text
        }
        $program = Join-Path $root 'protocol target.ps1'
        [IO.File]::WriteAllText($program, 'param($Signal,$Emit); if($Emit -eq "yes"){[Console]::WriteLine("Domain Reload Profiling: controlled")}else{[Console]::Error.WriteLine("Domain Reload Profiling: stderr only")}; while(-not(Test-Path -LiteralPath $Signal)){Start-Sleep -Milliseconds 20}')
        foreach ($scenario in @('header-only', 'complete', 'duplicate-end', 'missing-startup', 'removed-end-guard')) {
            ${function:Invoke-NativeAttachCapture} = $shipping
            $script:boundaryEnds = if ($scenario -eq 'complete') { 1 } elseif ($scenario -eq 'duplicate-end') { 2 } else { 0 }
            if ($scenario -eq 'removed-end-guard') {
                $mutant = $shipping.ToString().Replace("[regex]::Matches(`$head, '(?m)^WUH_ATTACH_HEAP_END\r?`$').Count -ne 1", '$false')
                Assert-Startup ($mutant -cne $shipping.ToString()) 'Boundary guard removal binds actual shipping code.'
                ${function:Invoke-NativeAttachCapture} = [scriptblock]::Create($mutant)
            }
            $casePath = Join-Path $root $scenario; $private = Join-Path $casePath 'private'; $null = [IO.Directory]::CreateDirectory($private)
            $script:boundarySignal = Join-Path $casePath 'release'
            $script:boundaryCalls = 0
            $record = [ordered]@{ Cases = @(@{ Process = @{}; TargetExitEvent = $null }); WorkingDirectory = $repo; DebuggerHeap = @{} }
            $emit = if ($scenario -eq 'missing-startup') { 'no' } else { 'yes' }
            $errorText = $null
            try { $null = Invoke-NativeAttachCapture -CaptureScript $source -EditorPath (Get-Process -Id $PID).Path -Arguments @('-NoProfile', '-File', $program, $script:boundarySignal, $emit) -DebuggerPath 'protocol output stand-in' -PrivateDirectory $private -ArtifactsPath $casePath -Seconds $(if ($scenario -eq 'missing-startup') { 8 } else { 15 }) -Record $record -AfterStartup }
            catch { $errorText = $_.Exception.Message }
            $case = $record.Cases[0]
            if ($scenario -in @('complete', 'removed-end-guard')) {
                Assert-Startup ($null -eq $errorText -and $case.Attach.State -ceq 'completed' -and $null -ne $case.Attach.Startup -and $case.TargetProcess.RawExitCode -eq 0) "Complete actual worker protocol outcome: $scenario."
            } elseif ($scenario -eq 'missing-startup') {
                Assert-Startup ($errorText -ceq 'Direct target exited or timed out before the required domain-reload observation.' -and $script:boundaryCalls -eq 0 -and $case.TargetProcess.Reaped -and $null -ne $case.TargetProcess.KillReason) 'Stderr or missing startup cannot launch a debugger; timeout reaps the owned target.'
            } else {
                Assert-Startup ($errorText -ceq 'Debugger did not observe the exact owned target at the attachment boundary.' -and $case.Attach.State -cne 'completed') "Command echoes and ambiguous marker lines cannot complete capture: $scenario."
            }
        }
        ${function:Invoke-NativeAttachCapture} = $shipping
    }
    Write-Host "$passed startup attachment controls passed (protocol identity/debugger stand-ins; real isolated workers, no Windows or Unity qualification)."
    $completed = $true
} finally {
    if ($completed) { Remove-Item -LiteralPath $root -Recurse -Force }
    else { Write-Host "Retained startup evidence at $root" }
}
