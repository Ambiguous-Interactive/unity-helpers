#!/usr/bin/env pwsh
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../run-wproto-evidence.ps1') -Oracle v3 -RunId ([guid]::NewGuid())
$checks = 0
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('wproto process controls ' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($temporary)

function Assert-Control {
    param([bool]$Passed, [string]$Name)
    if (-not $Passed) { throw "Control failed: $Name" }
    ++$script:checks
}

function Assert-ProcessRejected {
    param($Record, [string]$Name)
    $rejected = $false
    try { Assert-WProtoProcess $Record $Name } catch { $rejected = $true }
    Assert-Control $rejected $Name
}

try {
    $tokens = $null
    $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot '../run-wproto-evidence.ps1'), [ref]$tokens, [ref]$errors)
    Assert-Control ($errors.Count -eq 0) 'wrapper syntax parses'
    $pwsh = (Get-Command pwsh -CommandType Application | Select-Object -First 1).Source
    $child = Join-Path $temporary 'child command.ps1'
    [IO.File]::WriteAllText($child, 'param([string]$Value, [string]$Other, [int]$Code = 0, [int]$Delay = 0); $report = @{ values = @($Value, $Other); utc = [DateTimeOffset]::UtcNow.ToString("o") }; [Console]::Out.WriteLine((ConvertTo-Json -InputObject $report -Compress)); [Console]::Error.WriteLine("stderr witness"); if ($Delay -gt 0) { Start-Sleep -Seconds $Delay }; exit $Code')
    $value = 'argument with spaces'
    $other = 'literal $dollar `backtick "quote" ; punctuation'
    $arguments = @('-NoProfile', '-File', $child, '-Value', $value, '-Other', $other)
    $beforeInvocation = [DateTimeOffset]::UtcNow
    $success = Invoke-WProtoObservedProcess $pwsh $arguments $temporary (Join-Path $temporary 'success') 10
    $afterInvocation = [DateTimeOffset]::UtcNow
    try { Assert-WProtoProcess $success 'success' } catch { throw "Natural success collector failed: $(ConvertTo-Json -InputObject $success -Depth 5 -Compress)" }
    Assert-Control ($success.completed -and $success.exitCode -eq 0 -and $success.pid -gt 0) 'natural success has observed process identity'
    Assert-Control ($success.executableSha256 -ceq (Get-WProtoFileHash $pwsh)) 'executable identity is hashed before launch'
    Assert-Control ([DateTimeOffset]::Parse($success.startedUtc) -lt [DateTimeOffset]::Parse($success.endedUtc)) 'natural success has observed ordered interval'
    Assert-Control ($success.timestampSource -ceq 'parent-utc-launch-and-wait-bounds' -and [DateTimeOffset]::Parse($success.startedUtc) -ge $beforeInvocation -and [DateTimeOffset]::Parse($success.endedUtc) -le $afterInvocation) 'qualification dates stay inside actual parent observation bounds'
    $actual = ConvertFrom-Json ([IO.File]::ReadAllText($success.stdout.path))
    $childUtc = [DateTimeOffset]$actual.utc
    Assert-Control ($actual.values.Count -eq 2 -and $actual.values[0] -ceq $value -and $actual.values[1] -ceq $other) 'ArgumentList preserves spaces and literal shell characters'
    Assert-Control ($childUtc -ge [DateTimeOffset]::Parse($success.startedUtc) -and $childUtc -le [DateTimeOffset]::Parse($success.endedUtc)) "parent qualification bounds enclose independently reported child UTC: $($success.startedUtc) <= $($childUtc.ToString('o')) <= $($success.endedUtc)"
    Assert-Control ([IO.File]::ReadAllText($success.stderr.path).Trim() -ceq 'stderr witness') 'stderr captured separately'
    foreach ($artifact in @($success.stdout, $success.stderr, $success.log)) {
        Assert-Control ((Get-WProtoFileHash $artifact.path) -ceq $artifact.sha256) 'captured artifact has matching hash'
    }
    $failure = Invoke-WProtoObservedProcess $pwsh ($arguments + @('-Code', '17')) $temporary (Join-Path $temporary 'failure') 10
    Assert-Control ([DateTimeOffset]::Parse($success.endedUtc) -le [DateTimeOffset]::Parse($failure.startedUtc)) 'sequential processes have nonoverlapping observed bounds'
    Assert-Control ($failure.completed -and $failure.exitCode -eq 17 -and -not $failure.timedOut -and $null -eq $failure.exception) 'nonzero process preserves raw exit without fabricated exception'
    Assert-ProcessRejected $failure 'shared process contract rejects nonzero exit'
    Assert-Control ([IO.File]::ReadAllText($failure.log.path).Contains('stderr witness') -and [IO.File]::ReadAllText($failure.log.path).Contains($value)) 'failed process retains both streams'
    $timeout = Invoke-WProtoObservedProcess $pwsh ($arguments + @('-Delay', '30')) $temporary (Join-Path $temporary 'timeout') 1
    Assert-Control ($timeout.timedOut -and $timeout.terminationReason -ceq 'timeout-kill-process-tree' -and $timeout.completed -and $timeout.exitCode -is [int]) 'timeout records termination and observed raw exit'
    Assert-ProcessRejected $timeout 'shared process contract rejects timeout'
    Assert-Control (Test-Path -LiteralPath $timeout.log.path -PathType Leaf) 'timeout logs remain present'
    $missing = Invoke-WProtoObservedProcess (Join-Path $temporary 'missing executable') @() $temporary (Join-Path $temporary 'missing') 1
    Assert-Control (-not $missing.completed -and $null -eq $missing.pid -and $null -eq $missing.exitCode -and $null -eq $missing.executableSha256 -and $null -ne $missing.exception) 'start failure does not invent process evidence'
    Assert-ProcessRejected $missing 'shared process contract rejects start failure'
    Assert-Control (Test-Path -LiteralPath $missing.log.path -PathType Leaf) 'start failure logs remain present'
    $executable = Join-Path $temporary ('executable with spaces' + $(if ($IsWindows) { '.exe' } else { '' }))
    if ($IsWindows) {
        [IO.File]::Copy((Join-Path $env:SystemRoot 'System32/whoami.exe'), $executable)
        $spaceArguments = @()
    } else {
        [IO.File]::Copy('/bin/sh', $executable)
        [IO.File]::SetUnixFileMode($executable, [IO.File]::GetUnixFileMode('/bin/sh'))
        $spaceArguments = @('-c', 'printf executable-path-witness')
    }
    $spacePath = Invoke-WProtoObservedProcess $executable $spaceArguments $temporary (Join-Path $temporary 'spacepath') 10
    Assert-WProtoProcess $spacePath 'executable path'
    Assert-Control ($spacePath.exitCode -eq 0 -and [IO.File]::ReadAllText($spacePath.stdout.path).Length -gt 0) 'executable path containing spaces starts correctly'
    $duplicateId = [guid]::NewGuid()
    $duplicateDirectory = Join-Path $temporary $duplicateId.ToString('D')
    [void][IO.Directory]::CreateDirectory($duplicateDirectory)
    $sentinel = Join-Path $duplicateDirectory 'preserved.txt'
    [IO.File]::WriteAllText($sentinel, 'original artifact')
    $rejected = $false
    try { Invoke-WProtoEvidenceRun $temporary $temporary v3 $duplicateId 1 1 | Out-Null } catch { $rejected = $_.Exception.Message -like '*already exists*' }
    Assert-Control $rejected 'duplicate invocation rejected before catalog or build'
    Assert-Control ([IO.File]::ReadAllText($sentinel) -ceq 'original artifact' -and @(Get-ChildItem -LiteralPath $duplicateDirectory -File).Count -eq 1) 'duplicate invocation preserves old artifacts'
    $reservedId = [guid]::NewGuid()
    Write-WProtoNewFile (Join-Path $temporary ($reservedId.ToString('D') + '.reservation')) 'earlier attempt'
    $rejected = $false
    try { Invoke-WProtoEvidenceRun $temporary $temporary v3 $reservedId 1 1 | Out-Null } catch { $rejected = $true }
    Assert-Control ($rejected -and -not (Test-Path -LiteralPath (Join-Path $temporary $reservedId.ToString('D')))) 'reservation prevents reuse even without an output directory'
    Write-Host "WProto process collector controls passed: $checks"
} finally { Remove-Item -LiteralPath $temporary -Recurse -Force }
