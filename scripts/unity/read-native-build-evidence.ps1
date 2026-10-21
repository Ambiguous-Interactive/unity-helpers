#!/usr/bin/env pwsh
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Mandatory = $true)][string]$BuildReceiptPath,
    [Parameter(Mandatory = $true)][string]$ExpectedReceiptSha256,
    [Parameter(Mandatory = $true)][string]$ExpectedMachine,
    [Parameter(Mandatory = $true)][string]$ArtifactsPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/native-evidence.ps1')

function Read-NativeBuildEvidenceIdentity {
    param([byte[]]$Bytes, [string]$ExpectedMachine)

    $document = [Text.Json.JsonDocument]::Parse([Text.UTF8Encoding]::new($false, $true).GetString($Bytes).TrimStart([char]0xfeff))
    try {
        $root = $document.RootElement
        $machine = $root.GetProperty('Machine').GetString()
        if ($machine -cne $ExpectedMachine -or $machine -cne [Environment]::MachineName) { throw 'Build readback requires the original machine.' }
        $state = $root.GetProperty('State').GetString()
        if (-not $root.GetProperty('EvidenceOnly').GetBoolean() -or $root.GetProperty('Mode').GetString() -cne 'standalone-build' -or $state -cnotin @('completed', 'failed')) { throw 'Build receipt has no terminal diagnostic capture.' }
        $cases = $root.GetProperty('Cases')
        if ($cases.GetArrayLength() -ne 1) { throw 'Build readback requires exactly one original case.' }
        $case = $cases[0]
        $target = $case.GetProperty('TargetProcess')
        $identity = $case.GetProperty('Attach').GetProperty('Identity')
        $targetId = $target.GetProperty('Pid').GetInt32()
        $createdTicks = $identity.GetProperty('CreatedTicks').GetInt64()
        if ($targetId -le 0 -or $identity.GetProperty('Pid').GetInt32() -ne $targetId -or $identity.GetProperty('Machine').GetString() -cne $machine -or -not $target.GetProperty('Reaped').GetBoolean()) { throw 'Build receipt has no matching observed target identity.' }
        $editor = $root.GetProperty('EditorPath').GetString()
        $editorHash = $root.GetProperty('EditorSha256').GetString()
        if ([string]::IsNullOrWhiteSpace($editor) -or -not [IO.Path]::IsPathRooted($editor) -or $editorHash -cnotmatch '^[a-fA-F0-9]{64}$' -or $identity.GetProperty('ExecutablePath').GetString() -cne $editor -or $identity.GetProperty('ExecutableSha256').GetString() -ine $editorHash) { throw 'Build executable identities disagree.' }
        $started = $target.GetProperty('StartedUtc').GetString()
        $ended = $target.GetProperty('EndedUtc').GetString()
        $times = @{}
        foreach ($entry in @(@('started', $started), @('ended', $ended))) {
            $parsed = [DateTimeOffset]::MinValue
            if ($entry[1] -cnotmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?Z$' -or -not [DateTimeOffset]::TryParse($entry[1], [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$parsed)) { throw 'Build process time is not a UTC timestamp.' }
            $times[$entry[0]] = $parsed
        }
        $created = [DateTimeOffset]::new([DateTime]::new($createdTicks, [DateTimeKind]::Utc))
        if ($times.started -gt $created -or $created -gt $times.ended -or $times.ended -gt [DateTimeOffset]::UtcNow) { throw 'Build process time or lifetime ordering is invalid.' }
        $private = $root.GetProperty('PrivateRoot').GetString()
        if ([string]::IsNullOrWhiteSpace($private) -or -not [IO.Path]::IsPathRooted($private)) { throw 'Build private root is not absolute.' }
        $private = [IO.Path]::GetFullPath($private)
        $captureId = [IO.Path]::GetFileName($private)
        $base = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'unity-helpers-native-debugger'))
        $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
        if ($captureId -cnotmatch '^[a-f0-9]{32}$' -or -not [string]::Equals([IO.Path]::GetDirectoryName($private), $base, $comparison)) { throw 'Build private root is outside the original account capture directory.' }
        $build = Join-Path $private 'build'
        foreach ($path in @($base, $private, $build)) {
            if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Build private directory must not be a link.' }
        }
        $arguments = $root.GetProperty('ActualArguments')
        if ($arguments.GetArrayLength() -lt 4 -or $arguments.GetArrayLength() -gt 256) { throw 'Build argument count is invalid.' }
        $values = @($arguments.EnumerateArray() | ForEach-Object { $_.GetString() })
        if (@($values | Where-Object { $_ -ceq '-crash-report-folder' }).Count -ne 1 -or @($values | Where-Object { $_ -ceq '-upmLogFile' }).Count -ne 1 -or $values[-4] -cne '-crash-report-folder' -or $values[-3] -cne $build -or $values[-2] -cne '-upmLogFile' -or $values[-1] -cne (Join-Path $build 'upm.log')) { throw 'Build arguments do not bind the private crash directory.' }
        return @{
            OriginalState = $state; Machine = $machine; PrivateRoot = $private; CaptureId = $captureId; CreatedTicks = $createdTicks
            Process = @{ State = 'observed'; MachineName = $machine; ProcessId = $targetId; ExecutablePath = $editor; LaunchUtc = $started; EndObservedUtc = $ended; RawExitCode = $target.GetProperty('RawExitCode').GetInt32() }
            Capture = @{ State = 'configured'; CaptureId = $captureId; CrashDirectory = $build; UpmLogPath = (Join-Path $build 'upm.log') }
        }
    } finally { $document.Dispose() }
}

if ($ExpectedReceiptSha256 -cnotmatch '^[a-fA-F0-9]{64}$' -or [string]::IsNullOrWhiteSpace($ExpectedMachine)) { throw 'Build readback requires the expected receipt hash and machine.' }
$bytes = Read-NativeEvidenceFileBytes -Path $BuildReceiptPath -MaximumBytes 4194304
$algorithm = [Security.Cryptography.SHA256]::Create()
try { $actualHash = [BitConverter]::ToString($algorithm.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant() } finally { $algorithm.Dispose() }
if ($actualHash -ine $ExpectedReceiptSha256) { throw 'Original build receipt hash differs.' }
$identity = Read-NativeBuildEvidenceIdentity -Bytes $bytes -ExpectedMachine $ExpectedMachine
$target = $null
try {
    try { $target = [Diagnostics.Process]::GetProcessById($identity.Process.ProcessId) } catch [ArgumentException] { }
    if ($null -ne $target -and -not $target.HasExited -and $target.StartTime.ToUniversalTime().Ticks -eq $identity.CreatedTicks) { throw 'Original build target is still live.' }
} finally { if ($null -ne $target) { $target.Dispose() } }
$output = [IO.Path]::GetFullPath($ArtifactsPath)
$prefix = $identity.PrivateRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if ($output.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or [string]::Equals($output, $identity.PrivateRoot, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $output)) { throw 'Build readback output must be fresh and outside the original capture.' }
$parent = [IO.DirectoryInfo]::new([IO.Path]::GetDirectoryName($output))
while ($null -ne $parent) {
    if ($parent.Exists -and ($parent.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Build readback output parent must not be a link.' }
    $parent = $parent.Parent
}
[void][IO.Directory]::CreateDirectory($output)
$record = [ordered]@{
    EvidenceOnly = $true; Mode = 'native-build-readback'; State = 'collecting'; Machine = [Environment]::MachineName
    OriginalReceiptSha256 = $actualHash; OriginalReceiptBytes = $bytes.Length; OriginalState = $identity.OriginalState
    OriginalProcess = $identity.Process; OriginalPrivateRoot = $identity.PrivateRoot; OriginalTargetCreatedTicks = $identity.CreatedTicks
    HelperSha256 = (Get-FileHash -LiteralPath $PSCommandPath).Hash; StartedUtc = [DateTime]::UtcNow.ToString('o'); EndedUtc = $null
    WindowsEvents = $null; NativeEvidence = $null; Error = $null
    Qualification = 'Same-host historical metadata and bounded text readback only. Dump bodies stay private. Missing files/events, a completed readback, or no collector kill do not establish the original cause or healthy completion.'
}
try {
    [IO.File]::WriteAllText((Join-Path $output 'receipt.json'), (ConvertTo-UnitySafeLogText -Text ($record | ConvertTo-Json -Depth 12)))
    $record.WindowsEvents = Get-UnityProcessFailureEvents -ProcessObservation $identity.Process
    $record.NativeEvidence = Get-UnityNativeEvidence -Capture $identity.Capture -LogPath (Join-Path $output 'readback.log')
    $record.State = 'completed'
} catch {
    $record.State = 'failed'
    $record.Error = $_.Exception.Message
    throw
} finally {
    $record.EndedUtc = [DateTime]::UtcNow.ToString('o')
    [IO.File]::WriteAllText((Join-Path $output 'receipt.json'), (ConvertTo-UnitySafeLogText -Text ($record | ConvertTo-Json -Depth 12)))
}
Write-Host 'Historical build metadata readback completed; original acceptance remains unqualified.'
exit 0
