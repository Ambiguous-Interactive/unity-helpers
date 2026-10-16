#!/usr/bin/env pwsh
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$source = Join-Path $repo 'scripts/unity/capture-native-fault.ps1'
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($source, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw ($errors | Out-String) }
foreach ($name in @('Invoke-NativeCaptureProcess', 'Get-NativeCaptureExitEvent', 'Get-NativeCaptureXmlOutcome', 'Read-NativeCaptureTail')) {
    $function = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true)
    if ($null -eq $function) { throw "Missing shipping function $name" }
    . ([scriptblock]::Create($function.Extent.Text))
}
$root = Join-Path ([IO.Path]::GetTempPath()) ('native-capture-controls-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $root)
$passed = 0
function Assert-Capture([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    ++$script:passed
}
try {
    $normal = Get-NativeCaptureExitEvent 'Last event: abc.123: Exit process 0:abc, code 0'
    Assert-Capture ($normal.Pid -eq 2748 -and $normal.ExitCodeHex -eq '00000000') 'Normal native exit provenance'
    $crash = Get-NativeCaptureExitEvent 'Last event: 6dc84.63204: Exit process 0:6dc84, code c0000005'
    Assert-Capture ($crash.ExitCodeHex -eq 'C0000005') 'Access violation exit provenance'
    Assert-Capture ($null -eq (Get-NativeCaptureExitEvent 'debugger returned zero')) 'Debugger exit cannot stand in for target exit'
    Assert-Capture ($null -eq (Get-NativeCaptureExitEvent "Last event: abc.123: Exit process 0:abc, code 0`nLast event: abd.123: Exit process 0:abd, code 0")) 'Ambiguous target exit rejected'

    $xml = Join-Path $root 'results.xml'
    Assert-Capture ((Get-NativeCaptureXmlOutcome $xml 'Fixture.Case').State -eq 'missing') 'Missing XML explicit'
    [IO.File]::WriteAllText($xml, '<test-run><test-case fullname="Fixture.Case" result="Passed" /></test-run>')
    Assert-Capture ((Get-NativeCaptureXmlOutcome $xml 'Fixture.Case').Result -eq 'Passed') 'Real single case observed'
    foreach ($bad in @('<test-run />', '<test-run><test-case fullname="Other.Case" result="Passed" /></test-run>', '<!DOCTYPE x [<!ENTITY secret SYSTEM "file:///unavailable">]><test-run>&secret;</test-run>')) {
        [IO.File]::WriteAllText($xml, $bad)
        $rejected = $false
        try { Get-NativeCaptureXmlOutcome $xml 'Fixture.Case' | Out-Null } catch { $rejected = $true }
        Assert-Capture $rejected 'Invalid or unrelated XML rejected'
    }

    $snapshot = Join-Path $root 'writer.log'
    [IO.File]::WriteAllText($snapshot, (('x' * 800000) + 'END'))
    $writer = [IO.File]::Open($snapshot, [IO.FileMode]::Open, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite)
    try { $tail = Read-NativeCaptureTail $snapshot } finally { $writer.Dispose() }
    Assert-Capture ($tail.Length -eq 524288 -and $tail.EndsWith('END')) 'Bounded snapshot permits active writer'

    $child = Join-Path $root 'child.ps1'
    [IO.File]::WriteAllText($child, '[Console]::Out.Write((''x'' * 2000000) + ''END''); [Console]::Error.Write((''y'' * 2000000) + ''ERROR_END''); exit 7')
    $observation = @{}
    $receipt = Join-Path $root 'process.json'
    $output = Invoke-NativeCaptureProcess -FilePath (Get-Process -Id $PID).Path -Arguments @('-NoProfile', '-File', $child) -Directory $root -Seconds 20 -Observation $observation -ReceiptPath $receipt
    Assert-Capture ($observation.Reaped -and $observation.RawExitCode -eq 7 -and $null -eq $observation.KillReason) "Actual nonzero process exit retained: $($observation | ConvertTo-Json -Compress); retained characters: $($output.Length)"
    Assert-Capture ($output.Length -le 524288 -and $output.Contains('END')) 'Both streams drain without unbounded retention'
    $saved = Get-Content -LiteralPath $receipt -Raw | ConvertFrom-Json
    Assert-Capture ($saved.Pid -eq $observation.Pid -and $saved.RawExitCode -eq 7) 'Durable PID and terminal receipt agree'
    [IO.File]::WriteAllText($child, 'Start-Sleep -Seconds 30')
    $timeout = @{}
    Invoke-NativeCaptureProcess -FilePath (Get-Process -Id $PID).Path -Arguments @('-NoProfile', '-File', $child) -Directory $root -Seconds 1 -Observation $timeout | Out-Null
    Assert-Capture ($timeout.Reaped -and $timeout.KillReason -eq 'wall-clock-timeout' -and $null -ne $timeout.RawExitCode) 'Watchdog kill and actual OS exit remain distinct'
    $failed = @{}
    $rejected = $false
    try { Invoke-NativeCaptureProcess -FilePath (Join-Path $root 'absent.exe') -Arguments @() -Directory $root -Seconds 1 -Observation $failed | Out-Null } catch { $rejected = $true }
    Assert-Capture ($rejected -and -not $failed.Reaped -and $null -eq $failed.RawExitCode) 'Failed launch cannot fabricate an exit'

    $workflow = [IO.File]::ReadAllText((Join-Path $repo '.github/workflows/unity-tests.yml'))
    Assert-Capture ($workflow -match '(?s)native-diagnostics:.*?default: false.*?type: boolean') 'Diagnostic input defaults off'
    $captureStep = [regex]::Match($workflow, '(?s)- name: Capture native exit diagnostics.*?(?=\n      - name:)').Value
    Assert-Capture ($captureStep -match "inputs.native-diagnostics" -and $captureStep -match "steps.unity_lock.outputs.acquired == 'true'" -and $captureStep -match "steps.run_editmode.outcome == 'success'" -and $captureStep -match '!cancelled\(\)') 'Capture requires opt-in, live lock, and prepared project'
    Assert-Capture ($captureStep -match 'capture-native-fault.ps1' -and $captureStep -match 'native-diagnostics') 'Standalone driver is wired to uploaded EditMode artifacts'
    Write-Host "$passed native capture controls passed"
} finally {
    Remove-Item -LiteralPath $root -Recurse -Force
}
