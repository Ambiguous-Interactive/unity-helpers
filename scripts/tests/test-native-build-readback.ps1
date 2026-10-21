#!/usr/bin/env pwsh
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$scriptPath = Join-Path $repo 'scripts/unity/read-native-build-evidence.ps1'
$root = Join-Path ([IO.Path]::GetTempPath()) ('build-readback-' + [Guid]::NewGuid().ToString('N'))
$base = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'unity-helpers-native-debugger'
$private = Join-Path $base ([Guid]::NewGuid().ToString('N'))
$build = Join-Path $private 'build'
$passed = 0
$links = [Collections.Generic.List[string]]::new()
function Assert-Readback([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    ++$script:passed
}
function New-BuildReceipt {
    $editor = (Get-Process -Id $PID).Path
    $created = [DateTime]::new(2020, 1, 1, 0, 0, 0, [DateTimeKind]::Utc).AddMilliseconds(500).Ticks
    return @{
        Machine = [Environment]::MachineName; State = 'completed'; EvidenceOnly = $true; Mode = 'standalone-build'
        PrivateRoot = $private; EditorPath = $editor; EditorSha256 = ('a' * 64)
        ActualArguments = @('-crash-report-folder', $build, '-upmLogFile', (Join-Path $build 'upm.log'))
        Cases = @(@{
                TargetProcess = @{ Pid = [int]::MaxValue; Reaped = $true; RawExitCode = -1073741819; StartedUtc = '2020-01-01T00:00:00.0000000Z'; EndedUtc = '2020-01-01T00:00:01.0000000Z' }
                Attach = @{ Identity = @{ Pid = [int]::MaxValue; Machine = [Environment]::MachineName; CreatedTicks = $created; ExecutablePath = $editor; ExecutableSha256 = ('a' * 64) } }
            })
    }
}
function Invoke-BuildReadback {
    param([string]$InputPath, [string]$Hash, [string]$OutputPath, [string]$Machine = [Environment]::MachineName)
    $info = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path)
    $info.UseShellExecute = $false; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    foreach ($value in @('-NoProfile', '-File', $scriptPath, '-BuildReceiptPath', $InputPath, '-ExpectedReceiptSha256', $Hash, '-ExpectedMachine', $Machine, '-ArtifactsPath', $OutputPath)) { $info.ArgumentList.Add($value) }
    $process = [Diagnostics.Process]::Start($info)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(20000)) { $process.Kill($true); [void]$process.WaitForExit(10000); throw 'Readback control exceeded its deadline.' }
        return @{ ExitCode = $process.ExitCode; Output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult() }
    } finally { $process.Dispose() }
}
try {
    [void][IO.Directory]::CreateDirectory($root)
    [void][IO.Directory]::CreateDirectory($build)
    [IO.File]::WriteAllText((Join-Path $build 'upm.log'), 'OWNED_BUILD_READBACK_CONTROL')
    [IO.File]::WriteAllBytes((Join-Path $build 'original.dmp'), [byte[]]@(65, 66, 67))
    $originalDump = (Get-FileHash -LiteralPath (Join-Path $build 'original.dmp')).Hash
    foreach ($mutation in @('valid', 'failed-source', 'bad-hash', 'wrong-machine', 'wrong-receipt-machine', 'not-evidence', 'wrong-mode', 'active-source', 'duplicate-case', 'wrong-pid', 'unreaped', 'bad-created', 'future-time', 'local-time', 'wrong-executable', 'outside-private', 'wrong-arguments', 'duplicate-arguments', 'oversize', 'linked-private', 'linked-output')) {
        $receipt = New-BuildReceipt
        $expectedMachine = [Environment]::MachineName
        $output = Join-Path $root ('output-' + $mutation)
        switch ($mutation) {
            'failed-source' { $receipt.State = 'failed' }
            'wrong-machine' { $expectedMachine = 'wrong-machine' }
            'wrong-receipt-machine' { $receipt.Machine = 'wrong-machine' }
            'not-evidence' { $receipt.EvidenceOnly = $false }
            'wrong-mode' { $receipt.Mode = 'historical-readback' }
            'active-source' { $receipt.State = 'running' }
            'duplicate-case' { $receipt.Cases += $receipt.Cases[0] }
            'wrong-pid' { $receipt.Cases[0].TargetProcess.Pid = 1 }
            'unreaped' { $receipt.Cases[0].TargetProcess.Reaped = $false }
            'bad-created' { $receipt.Cases[0].Attach.Identity.CreatedTicks = 0 }
            'future-time' { $receipt.Cases[0].TargetProcess.EndedUtc = [DateTime]::UtcNow.AddDays(1).ToString('o') }
            'local-time' { $receipt.Cases[0].TargetProcess.StartedUtc = '2020-01-01T00:00:00' }
            'wrong-executable' { $receipt.EditorSha256 = ('b' * 64) }
            'outside-private' { $receipt.PrivateRoot = $root }
            'wrong-arguments' { $receipt.ActualArguments[-3] = $root }
            'duplicate-arguments' { $receipt.ActualArguments = @('-crash-report-folder', $root) + $receipt.ActualArguments }
            'linked-private' {
                $link = Join-Path $base ([Guid]::NewGuid().ToString('N'))
                $kind = if ($IsWindows) { 'Junction' } else { 'SymbolicLink' }
                [void](New-Item -ItemType $kind -Path $link -Target $private)
                $links.Add($link); $receipt.PrivateRoot = $link
                $receipt.ActualArguments = @('-crash-report-folder', (Join-Path $link 'build'), '-upmLogFile', (Join-Path $link 'build/upm.log'))
            }
            'linked-output' {
                $link = Join-Path $root 'output-link'
                $kind = if ($IsWindows) { 'Junction' } else { 'SymbolicLink' }
                [void](New-Item -ItemType $kind -Path $link -Target $private)
                $links.Add($link); $output = Join-Path $link 'output'
            }
        }
        $inputPath = Join-Path $root ($mutation + '.json')
        [IO.File]::WriteAllText($inputPath, ($receipt | ConvertTo-Json -Depth 12))
        if ($mutation -eq 'oversize') { [IO.File]::WriteAllBytes($inputPath, [byte[]]::new(4194305)) }
        $hash = (Get-FileHash -LiteralPath $inputPath).Hash
        if ($mutation -eq 'bad-hash') { $hash = ('0' * 64) }
        $before = (Get-FileHash -LiteralPath $inputPath).Hash
        $result = Invoke-BuildReadback -InputPath $inputPath -Hash $hash -OutputPath $output -Machine $expectedMachine
        if ($mutation -in @('valid', 'failed-source')) {
            Assert-Readback ($result.ExitCode -eq 0) $result.Output
            $observed = Get-Content -LiteralPath (Join-Path $output 'receipt.json') -Raw | ConvertFrom-Json -AsHashtable
            Assert-Readback ($observed.EvidenceOnly -and $observed.Mode -ceq 'native-build-readback' -and $observed.OriginalState -ceq $receipt.State -and $observed.OriginalReceiptSha256 -ieq $hash) 'Readback must retain source state and exact receipt binding.'
            $dump = @($observed.NativeEvidence.Files | Where-Object { $_.RelativePath -ceq 'original.dmp' })
            Assert-Readback ($dump.Count -eq 1 -and $dump[0].Length -eq 3 -and $dump[0].Sha256 -ieq $originalDump -and $null -eq $dump[0].TextArtifact) 'Dump metadata must be retained without copying its body.'
            Assert-Readback (@(Get-ChildItem -LiteralPath $output -Filter '*.dmp' -Recurse).Count -eq 0 -and @($observed.NativeEvidence.Files | Where-Object { $_.RelativePath -ceq 'upm.log' -and $_.TextArtifact }).Count -eq 1) 'Readback must export bounded text and no dump bodies.'
        } else {
            Assert-Readback ($result.ExitCode -ne 0 -and -not (Test-Path -LiteralPath $output)) "Rejected $mutation must not create output: $($result.Output)"
        }
        Assert-Readback ((Get-FileHash -LiteralPath $inputPath).Hash -ceq $before -and (Get-FileHash -LiteralPath (Join-Path $build 'original.dmp')).Hash -ceq $originalDump) 'Readback must preserve original source and private dump bytes.'
    }
    $readerCopy = Join-Path $root 'reader'
    [void][IO.Directory]::CreateDirectory((Join-Path $readerCopy 'lib'))
    Copy-Item -LiteralPath $scriptPath -Destination (Join-Path $readerCopy 'read-native-build-evidence.ps1')
    foreach ($name in @('native-evidence.ps1', 'credential-redaction.ps1')) {
        Copy-Item -LiteralPath (Join-Path $repo ('scripts/unity/lib/' + $name)) -Destination (Join-Path $readerCopy ('lib/' + $name))
    }
    Add-Content -LiteralPath (Join-Path $readerCopy 'lib/native-evidence.ps1') -Value @'
function Get-UnityProcessFailureEvents { param($ProcessObservation) return @{ State = 'fixture-event-observed' } }
function Get-UnityNativeEvidence { param($Capture, $LogPath) throw 'controlled interrupted readback' }
'@
    $scriptPath = Join-Path $readerCopy 'read-native-build-evidence.ps1'
    $inputPath = Join-Path $root 'interrupted.json'
    [IO.File]::WriteAllText($inputPath, ((New-BuildReceipt) | ConvertTo-Json -Depth 12))
    $hash = (Get-FileHash -LiteralPath $inputPath).Hash
    $output = Join-Path $root 'interrupted'
    $result = Invoke-BuildReadback -InputPath $inputPath -Hash $hash -OutputPath $output
    Assert-Readback ($result.ExitCode -ne 0) 'Interrupted collector must fail.'
    $partial = Get-Content -LiteralPath (Join-Path $output 'receipt.json') -Raw | ConvertFrom-Json -AsHashtable
    Assert-Readback ($partial.State -ceq 'failed' -and $partial.Error -ceq 'controlled interrupted readback') 'Interrupted collector must retain a failed receipt.'
    Assert-Readback ($partial.WindowsEvents.State -ceq 'fixture-event-observed' -and $null -eq $partial.NativeEvidence) 'Partial observations must survive a later collector failure.'
    Assert-Readback ($partial.OriginalReceiptSha256 -ieq $hash -and [DateTimeOffset]::Parse($partial.StartedUtc) -le [DateTimeOffset]::Parse($partial.EndedUtc)) 'Failed receipt must retain original binding and collection times.'
    Assert-Readback ((Get-FileHash -LiteralPath $inputPath).Hash -ceq $hash -and (Get-FileHash -LiteralPath (Join-Path $build 'original.dmp')).Hash -ceq $originalDump) 'Interrupted readback must preserve original evidence.'
} finally {
    foreach ($link in $links) { [IO.Directory]::Delete($link) }
    if (Test-Path -LiteralPath $private) { Remove-Item -LiteralPath $private -Recurse -Force }
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
Write-Host "$passed build readback controls passed (protocol fixtures; no Unity cause qualification)."
exit 0
