#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Behavioral tests for the Unity configure-pass Package Manager retry.

.DESCRIPTION
    Extracts the shipping functions from run-ci-tests.ps1 and drives the configure pass through its
    test invoker. No Unity process is launched. The cases prove that a missing fresh marker plus an
    exact UPM cancellation signature retries once, while marker success and ordinary failures do not.
#>
[CmdletBinding()]
param(
    [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../unity/lib/credential-redaction.ps1')

$scriptRoot = Split-Path -Parent $PSScriptRoot
$target = Join-Path $scriptRoot 'unity/run-ci-tests.ps1'
. (Join-Path $scriptRoot 'unity/lib/catastrophic-patterns.ps1')
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    $target,
    [ref]$tokens,
    [ref]$parseErrors
)
if ($parseErrors -and $parseErrors.Count -gt 0) {
    Write-Host "FATAL: run-ci-tests.ps1 has parse errors:"
    $parseErrors | ForEach-Object { Write-Host "  $($_.Extent.StartLineNumber): $($_.Message)" }
    exit 1
}

foreach ($name in @(
        'ConvertTo-SingleLineDiagnostic',
        'Assert-NoUnityCompilerWarnings',
        'Test-UnityPackageManagerTransientFailure',
        'Test-UnityConfigurePackageManagerRetryableFailure',
        'Write-UnityPackageManagerTransientFailureWarnings',
        'Clear-UnityPackageManagerRetryState',
        'Test-UnityConfigureMarker',
        'Select-UnityProcessFailureEvents',
        'Get-UnityProcessFailureEvents',
        'Read-UnityProcessFailureEvents',
        'Write-UnityConfigureFailureObservation',
        'Get-UnityNativeReportSummary',
        'Write-UnityExecutionObservation',
        'New-UnityNativeEvidenceCapture',
        'Get-UnityNativeEvidence',
        'Invoke-UnityEditor',
        'Get-EditorTestCompletionGraceSeconds',
        'Get-EditorTestRunTimeoutSeconds',
        'Get-EditorTestStallSeconds',
        'Invoke-UnityEditorTestsWithPackageManagerRetry',
        'Invoke-UnityConfigurePass'
    )) {
    $functionAst = $ast.FindAll(
        {
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -eq $name
        },
        $true
    ) | Select-Object -First 1
    if (-not $functionAst) {
        Write-Host "FATAL: function '$name' not found in run-ci-tests.ps1"
        exit 1
    }
    Invoke-Expression $functionAst.Extent.Text
}

$script:passed = 0
$script:failed = 0
$script:notices = New-Object System.Collections.Generic.List[string]
$script:CatastrophicPatterns = @(Get-CatastrophicPatterns)
$script:capturedNativeDirectories = New-Object System.Collections.Generic.List[string]
$script:lastNativeArguments = @()
$RepoRoot = Split-Path -Parent $scriptRoot

function Assert-That {
    param([string]$Description, [bool]$Condition)
    if ($Condition) {
        if ($VerboseOutput) { Write-Host "  PASS: $Description" }
        $script:passed++
    } else {
        Write-Host "  FAIL: $Description"
        $script:failed++
    }
}

function Write-CiWarning { param([string]$Message) $script:notices.Add("warning:$Message") }
function Write-CiNotice { param([string]$Message) $script:notices.Add("notice:$Message") }
function Write-UnityRunFailureDiagnostics { }
function Write-AnalyzerSetupDiagnostics { }
function Write-UnityBenignExitWarning { }
function Get-NativeExitCodeDescription { param([int]$ExitCode) return "exit-$ExitCode" }

$temporaryRoot = Join-Path (
    [System.IO.Path]::GetTempPath()
) ("unity-configure-upm-retry-{0}" -f [System.Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $temporaryRoot | Out-Null

$nativePrivateRoot = Join-Path $temporaryRoot 'private'
$nativeArtifacts = Join-Path $temporaryRoot 'public'
New-Item -ItemType Directory -Force -Path $nativeArtifacts | Out-Null
$captureLog = Join-Path $nativeArtifacts 'editor.log'
$capture = New-UnityNativeEvidenceCapture -LogPath $captureLog -PrivateRoot $nativePrivateRoot
$anotherCapture = New-UnityNativeEvidenceCapture -LogPath $captureLog -PrivateRoot $nativePrivateRoot
Assert-That 'native evidence uses a fresh private folder for each invocation' ($capture.State -eq 'configured' -and $capture.CrashDirectory -ne $anotherCapture.CrashDirectory -and $capture.UpmLogPath -ne $anotherCapture.UpmLogPath)
Assert-That 'crash dumps are staged outside the public artifact directory' (-not $capture.CrashDirectory.StartsWith($nativeArtifacts, [StringComparison]::OrdinalIgnoreCase))
$publicCapture = New-UnityNativeEvidenceCapture -LogPath $captureLog -PrivateRoot $nativeArtifacts
Assert-That 'capture setup refuses to place raw files under public artifacts' ($publicCapture.State -eq 'capture-setup-failed')
$nestedCrash = Join-Path $capture.CrashDirectory 'crash-report'
New-Item -ItemType Directory -Path $nestedCrash | Out-Null
$dumpBytes = [byte[]]@(77, 68, 77, 80, 0, 0, 0, 0)
[IO.File]::WriteAllBytes((Join-Path $nestedCrash 'crash.dmp'), $dumpBytes)
$serialCanary = 'AB-' + ('1234-' * 4) + '1234'
Set-Content -LiteralPath (Join-Path $nestedCrash 'error.log') -Value "native stack $serialCanary user@example.test access token: example-token"
$nativeEvidence = Get-UnityNativeEvidence -Capture $capture -LogPath $captureLog
Assert-That 'native file inventory is explicit and includes the original dump hash' ($nativeEvidence.State -eq 'files-observed' -and $nativeEvidence.Files.Count -eq 2 -and @($nativeEvidence.Files | Where-Object { $_.RelativePath -like '*.dmp' -and $_.HashState -eq 'observed' -and $_.Sha256.Length -eq 64 }).Count -eq 1)
Assert-That 'a missing per-attempt UPM log is explicit' ($nativeEvidence.UpmLogState -eq 'missing-at-capture')
Set-Content -LiteralPath $capture.UpmLogPath -Value 'UPM process control'
$upmEvidence = Get-UnityNativeEvidence -Capture $capture -LogPath $captureLog
Assert-That 'a written per-attempt UPM log is linked to the same capture' ($upmEvidence.UpmLogState -eq 'present-at-capture' -and $upmEvidence.CaptureId -eq $capture.CaptureId -and $upmEvidence.UpmLogPath -eq $capture.UpmLogPath)
Assert-That 'the live UPM writer stays outside public artifacts' ($capture.UpmLogPath.StartsWith($capture.CrashDirectory, [StringComparison]::OrdinalIgnoreCase) -and -not [string]::IsNullOrWhiteSpace($upmEvidence.UpmTextArtifact))
$upmSnapshotPath = Join-Path $nativeArtifacts $upmEvidence.UpmTextArtifact
$upmSnapshot = Get-Content -LiteralPath $upmSnapshotPath -Raw
[IO.File]::AppendAllText($capture.UpmLogPath, "late UPM output $serialCanary")
Assert-That 'late UPM output cannot rewrite the published redacted snapshot' ((Get-Content -LiteralPath $upmSnapshotPath -Raw) -ceq $upmSnapshot)
$liveCapture = New-UnityNativeEvidenceCapture -LogPath $captureLog -PrivateRoot $nativePrivateRoot
$liveBytes = [Text.Encoding]::UTF8.GetBytes("live UPM output $serialCanary user@example.test access token: example-token")
$liveWriter = [IO.File]::Open($liveCapture.UpmLogPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
try {
    $liveWriter.Write($liveBytes, 0, $liveBytes.Length)
    $liveWriter.Flush()
    $liveEvidence = Get-UnityNativeEvidence -Capture $liveCapture -LogPath $captureLog
    $liveFile = $liveEvidence.Files[0]
    Assert-That 'a live UPM writer permits bounded hashing without changing its sharing mode' ($liveFile.HashState -eq 'observed' -and $liveFile.HashBytes -eq $liveBytes.Length -and $liveFile.Sha256 -ceq '6dbb97f92823a761b44603df3d414dbef753443b4217045980b971037041fb37')
    $liveSnapshot = Join-Path $nativeArtifacts $liveEvidence.UpmTextArtifact
    $liveCaptured = if (Test-Path -LiteralPath $liveSnapshot -PathType Leaf) { Get-Content -LiteralPath $liveSnapshot -Raw } else { '' }
    Assert-That 'a live UPM writer exports a redacted text snapshot' ($liveCaptured -match 'live UPM output' -and $liveCaptured -notmatch [regex]::Escape($serialCanary) -and $liveCaptured -notmatch 'user@example.test|example-token')
    $lateBytes = [Text.Encoding]::UTF8.GetBytes(' late unredacted writer output')
    $liveWriter.Write($lateBytes, 0, $lateBytes.Length)
    $liveWriter.Flush()
    Assert-That 'a still-open UPM writer cannot append to its published snapshot' ($liveCaptured.Length -gt 0 -and (Get-Content -LiteralPath $liveSnapshot -Raw) -ceq $liveCaptured)
} finally { $liveWriter.Dispose() }
Assert-That 'no raw dump reaches public artifacts' (@(Get-ChildItem -LiteralPath $nativeArtifacts -Recurse -Filter '*.dmp').Count -eq 0 -and (Test-Path -LiteralPath (Join-Path $nestedCrash 'crash.dmp')))
$textReport = @($nativeEvidence.Files | Where-Object { $_.TextArtifact })[0]
$safeText = Get-Content -LiteralPath (Join-Path $nativeArtifacts $textReport.TextArtifact) -Raw
Assert-That 'crash text is copied with credentials redacted' ($safeText -match 'native stack' -and $safeText -notmatch [regex]::Escape($serialCanary) -and $safeText -notmatch 'user@example.test|example-token')
Set-Content -LiteralPath (Join-Path $nestedCrash 'error.log') -Value ('x' * 300000)
$bounded = Get-UnityNativeEvidence -Capture $capture -LogPath $captureLog
$boundedText = @($bounded.Files | Where-Object { $_.TextArtifact })[0]
Assert-That 'large crash text has a fixed read limit and reports truncation' ($boundedText.TextTruncated -and (Get-Item -LiteralPath (Join-Path $nativeArtifacts $boundedText.TextArtifact)).Length -lt 262160)
[IO.File]::WriteAllBytes((Join-Path $nestedCrash 'binary.log'), $dumpBytes)
$binaryEvidence = Get-UnityNativeEvidence -Capture $capture -LogPath $captureLog
$binaryReport = @($binaryEvidence.Files | Where-Object { $_.RelativePath -like '*binary.log' })[0]
Assert-That 'a log extension cannot publish binary content' ($binaryReport.TextArtifact -eq 'non-text-file' -and -not (Test-Path -LiteralPath (Join-Path $nativeArtifacts 'non-text-file')))
$emptyEvidence = Get-UnityNativeEvidence -Capture $anotherCapture -LogPath $captureLog
Assert-That 'no crash file is an observation rather than a safety claim' ($emptyEvidence.State -eq 'no-files-at-capture' -and $emptyEvidence.Qualification -match 'do not rule out')
Assert-That 'an empty crash folder remains available to a delayed crash writer' (Test-Path -LiteralPath $anotherCapture.CrashDirectory -PathType Container)
$unavailableEvidence = Get-UnityNativeEvidence -Capture @{ State = 'capture-setup-failed' } -LogPath $captureLog
Assert-That 'failed crash setup remains explicit' ($unavailableEvidence.State -eq 'capture-setup-failed' -and $unavailableEvidence.Files.Count -eq 0)
$malformedEvidence = Get-UnityNativeEvidence -Capture @{ State = 'configured' } -LogPath $captureLog
Assert-That 'malformed crash capture metadata cannot throw' ($malformedEvidence.State -eq 'capture-unavailable' -and $malformedEvidence.Files.Count -eq 0)
$badRoot = Join-Path $temporaryRoot 'not-a-directory'
Set-Content -LiteralPath $badRoot -Value 'file'
$failedCapture = New-UnityNativeEvidenceCapture -LogPath $captureLog -PrivateRoot $badRoot
Assert-That 'unwritable crash setup does not change the process result' ($failedCapture.State -eq 'capture-setup-failed')
$limitedCapture = New-UnityNativeEvidenceCapture -LogPath $captureLog -PrivateRoot $nativePrivateRoot
foreach ($index in 0..64) { Set-Content -LiteralPath (Join-Path $limitedCapture.CrashDirectory "$index.json") -Value 'not-published' }
$limitedEvidence = Get-UnityNativeEvidence -Capture $limitedCapture -LogPath $captureLog
Assert-That 'native inventory caps files and reports the limit' ($limitedEvidence.Files.Count -eq 64 -and $limitedEvidence.FileLimitReached -and @($limitedEvidence.Files | Where-Object { $_.TextArtifact }).Count -eq 0)
$largeCapture = New-UnityNativeEvidenceCapture -LogPath $captureLog -PrivateRoot $nativePrivateRoot
$largeDumpPath = Join-Path $largeCapture.CrashDirectory 'large.dmp'
$largeStream = [IO.File]::Create($largeDumpPath)
try { $largeStream.SetLength(268435457) } finally { $largeStream.Dispose() }
$largeEvidence = Get-UnityNativeEvidence -Capture $largeCapture -LogPath $captureLog
Assert-That 'large dumps remain private and explicitly exceed the hashing limit' ($largeEvidence.Files.Count -eq 1 -and $largeEvidence.Files[0].Length -eq 268435457 -and $largeEvidence.Files[0].HashState -eq 'over-hash-limit' -and $null -eq $largeEvidence.Files[0].Sha256 -and $null -eq $largeEvidence.Files[0].TextArtifact)

function Invoke-ConfigureCase {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string[]]$Attempts
    )

    $caseRoot = Join-Path $temporaryRoot $Name
    $project = Join-Path $caseRoot 'project'
    $artifacts = Join-Path $caseRoot 'artifacts'
    New-Item -ItemType Directory -Force -Path $project, $artifacts | Out-Null
    foreach ($relativePath in @('Library/PackageCache', 'Library/PackageManager', 'Temp')) {
        New-Item -ItemType Directory -Force -Path (Join-Path $project $relativePath) | Out-Null
        Set-Content -LiteralPath (Join-Path $project "$relativePath/stale.txt") -Value 'stale'
    }

    $marker = Join-Path $artifacts 'configure.marker'
    $log = Join-Path $artifacts 'configure.log'
    $state = [pscustomobject]@{ Count = 0 }
    $invoker = {
        param($arguments, $logPath, $label)
        $state.Count++
        $token = $Attempts[[Math]::Min($state.Count - 1, $Attempts.Count - 1)]
        switch ($token) {
            'success' {
                Set-Content -LiteralPath $logPath -Value 'configure completed'
                Set-Content -LiteralPath $env:UH_CONFIGURE_MARKER_PATH -Value 'configured'
                return 0
            }
            'success-with-old-upm-log' {
                Set-Content -LiteralPath $logPath -Value 'Failed to resolve packages: operation cancelled'
                Set-Content -LiteralPath $env:UH_CONFIGURE_MARKER_PATH -Value 'configured'
                return 1
            }
            'upm-cancel' {
                Set-Content -LiteralPath $logPath -Value 'IPCStream (Upm-123): IPC stream failed to read'
                return 1
            }
            'upm-and-compile-error' {
                Set-Content -LiteralPath $logPath -Value @(
                    'IPCStream (Upm-123): IPC stream failed to read',
                    'error CS1002: ; expected'
                )
                return 1
            }
            'upm-and-mixed-case-fatal' {
                Set-Content -LiteralPath $logPath -Value @(
                    'IPCStream (Upm-123): IPC stream failed to read',
                    'FATAL ERROR IN THE MONO RUNTIME'
                )
                return 1
            }
            default {
                Set-Content -LiteralPath $logPath -Value 'error CS1002: ; expected'
                return 1
            }
        }
    }.GetNewClosure()

    $threw = $false
    $message = ''
    try {
        Invoke-UnityConfigurePass `
            -EditorPath 'fake-editor' `
            -ProjectPath $project `
            -MarkerPath $marker `
            -LogPath $log `
            -Label $Name `
            -ConfigureInvoker $invoker
    } catch {
        $threw = $true
        $message = $_.Exception.Message
    }

    return [pscustomobject]@{
        Attempts = $state.Count
        Threw = $threw
        Message = $message
        Marker = $marker
        Log = $log
        Project = $project
        FirstAttemptLog = Join-Path $artifacts 'configure.first-attempt.log'
        FirstAttemptObservation = Join-Path $artifacts 'configure.first-attempt.process-diagnostics.json'
        FailureObservation = Join-Path $artifacts 'configure.process-diagnostics.json'
        EnvironmentCleared = -not (Test-Path -LiteralPath Env:\UH_CONFIGURE_MARKER_PATH)
    }
}

try {
    $ordinarySuccess = Invoke-ConfigureCase -Name 'ordinary-success' -Attempts @('success')
    Assert-That 'success runs once' ($ordinarySuccess.Attempts -eq 1)
    Assert-That 'success does not throw' (-not $ordinarySuccess.Threw)
    Assert-That 'success clears the marker-path environment variable' (
        $ordinarySuccess.EnvironmentCleared
    )

    Assert-That 'successful configuration does not query or persist failure diagnostics' (-not (Test-Path $ordinarySuccess.FailureObservation))

    $markerWins = Invoke-ConfigureCase `
        -Name 'marker-wins' `
        -Attempts @('success-with-old-upm-log', 'upm-cancel')
    Assert-That 'a fresh marker prevents retry despite an old UPM signature' ($markerWins.Attempts -eq 1)
    Assert-That 'fresh-marker success does not throw on a non-zero exit' (-not $markerWins.Threw)
    Assert-That 'fresh-marker success clears the marker-path environment variable' (
        $markerWins.EnvironmentCleared
    )

    $retried = Invoke-ConfigureCase -Name 'retry' -Attempts @('upm-cancel', 'success')
    Assert-That 'an exact marker-less UPM signature retries once' ($retried.Attempts -eq 2)
    Assert-That 'successful retry does not throw' (-not $retried.Threw)
    Assert-That 'successful retry writes the configure marker' (
        Test-Path -LiteralPath $retried.Marker -PathType Leaf
    )
    Assert-That 'first failed configure log is preserved' (
        (Test-Path -LiteralPath $retried.FirstAttemptLog -PathType Leaf) -and
        (Get-Content -LiteralPath $retried.FirstAttemptLog -Raw) -match 'IPC stream failed to read'
    )
    Assert-That 'existing retry preserves first-attempt observation even when second succeeds' (Test-Path $retried.FirstAttemptObservation)
    $firstObservation = Get-Content $retried.FirstAttemptObservation -Raw | ConvertFrom-Json
    Assert-That 'custom invoker records explicit unavailable process identity' ($firstObservation.Process.State -eq 'unavailable' -and $firstObservation.WindowsEvents.State -eq 'no-query')
    Assert-That 'retry clears and recreates PackageCache' (
        (Test-Path -LiteralPath (Join-Path $retried.Project 'Library/PackageCache') -PathType Container) -and
        -not (Test-Path -LiteralPath (Join-Path $retried.Project 'Library/PackageCache/stale.txt'))
    )
    Assert-That 'successful retry clears the marker-path environment variable' (
        $retried.EnvironmentCleared
    )

    $bounded = Invoke-ConfigureCase -Name 'bounded' -Attempts @('upm-cancel', 'upm-cancel')
    Assert-That 'two UPM cancellations stop after exactly two attempts' ($bounded.Attempts -eq 2)
    Assert-That 'a failed retry remains fatal' $bounded.Threw
    Assert-That 'a failed retry clears the marker-path environment variable' (
        $bounded.EnvironmentCleared
    )

    $compileFailure = Invoke-ConfigureCase -Name 'compile-failure' -Attempts @('compile-error')
    Assert-That 'an ordinary compile failure is not retried' ($compileFailure.Attempts -eq 1)
    Assert-That 'an ordinary compile failure remains fatal' $compileFailure.Threw
    Assert-That 'an ordinary compile failure clears the marker-path environment variable' (
        $compileFailure.EnvironmentCleared
    )
    Assert-That 'ordinary failure persists diagnostics without changing rejection' (Test-Path $compileFailure.FailureObservation)
    Assert-That 'an ordinary compile failure does not create a retry log' (
        -not (Test-Path -LiteralPath $compileFailure.FirstAttemptLog)
    )

    $mixedFailure = Invoke-ConfigureCase `
        -Name 'mixed-upm-compile-failure' `
        -Attempts @('upm-and-compile-error', 'success')
    Assert-That 'a mixed UPM and compile failure is not retried' ($mixedFailure.Attempts -eq 1)
    Assert-That 'a mixed UPM and compile failure remains fatal' $mixedFailure.Threw
    Assert-That 'a mixed failure clears the marker-path environment variable' (
        $mixedFailure.EnvironmentCleared
    )

    $mixedCaseFatal = Invoke-ConfigureCase `
        -Name 'mixed-upm-case-insensitive-fatal' `
        -Attempts @('upm-and-mixed-case-fatal', 'success')
    Assert-That 'a case-varied simple catastrophic pattern is not retried' (
        $mixedCaseFatal.Attempts -eq 1
    )
    Assert-That 'a mixed UPM and case-varied fatal signal remains fatal' $mixedCaseFatal.Threw
    Assert-That 'a mixed case-varied failure clears the marker-path environment variable' (
        $mixedCaseFatal.EnvironmentCleared
    )
    $observation = @{
        State = 'observed'; ProcessId = 42; ExecutablePath = 'D:\Unity\Unity.exe'
        LaunchUtc = '2026-10-09T20:05:20Z'; EndObservedUtc = '2026-10-09T20:05:22Z'
    }
    function New-EventRecord {
        param([int]$Id, [hashtable]$Fields, [DateTime]$Time = [DateTime]'2026-10-09T20:05:22Z', [string]$XmlOverride = '')
        $data = foreach ($key in $Fields.Keys) {
            '<Data Name="' + [Security.SecurityElement]::Escape($key) + '">' + [Security.SecurityElement]::Escape([string]$Fields[$key]) + '</Data>'
        }
        $record = [pscustomobject]@{
            Id = $Id; ProviderName = if ($Id -eq 1000) { 'Application Error' } else { 'Windows Error Reporting' }
            TimeCreated = $Time; RecordId = 1
            EventXml = if ($XmlOverride) { $XmlOverride } else { '<Event><EventData>' + ($data -join '') + '</EventData></Event>' }
        }
        $record | Add-Member -MemberType ScriptMethod -Name ToXml -Value { return $this.EventXml }
        return $record
    }
    $reportId = '35d131f6-0a49-4207-83da-f9d809c86aa0'
    $fault = New-EventRecord -Id 1000 -Fields @{
        ProcessId = '0x2a'; AppPath = 'd:/Unity/Unity.exe'; IntegratorReportId = $reportId
        ExceptionCode = 'ffffffff'; ModuleName = "access token=secret@example.com`n::error::untrusted"
        CommandLine = 'never export'; Message = 'never export'
    }
    $reportingEvent = New-EventRecord -Id 1001 -Fields @{ ReportId = $reportId; AppName = 'Unity.exe'; Message = 'never export' }
    $events = Select-UnityProcessFailureEvents -ProcessObservation $observation -Candidates @($reportingEvent, $fault)
    Assert-That 'exact hex PID/path and report-only Windows report join match independent of input order' ($events.Events.Count -eq 2 -and $events.Events[0].Match -eq 'joined-report-id' -and $events.MalformedEvents -eq 0)
    $json = ConvertTo-Json $events -Depth 8
    Assert-That 'event export excludes arbitrary fields and credential data' ($json -notmatch 'never export|secret@example.com' -and $events.Events[1].Fields.ModuleName -notmatch '[\r\n]')
    foreach ($conflict in @(@{ ProcessId = '43'; ReportId = $reportId }, @{ ProcessId = 'not-a-pid'; ReportId = $reportId }, @{ AppPath = 'D:\Other\Unity.exe'; ReportId = $reportId })) {
        $contradictory = New-EventRecord -Id 1001 -Fields $conflict
        $events = Select-UnityProcessFailureEvents -ProcessObservation $observation -Candidates @($fault, $contradictory)
        Assert-That 'report-ID join rejects explicitly contradictory target identity' ($events.Events.Count -eq 1 -and $events.Events[0].EventId -eq 1000)
    }
    $decimal = New-EventRecord -Id 1000 -Fields @{ ProcessId = '42'; AppPath = 'D:\Unity\Unity.exe' }
    $events = Select-UnityProcessFailureEvents -ProcessObservation $observation -Candidates @($decimal)
    Assert-That 'decimal target PID matches without optional report fields' ($events.Events.Count -eq 1 -and $events.MalformedEvents -eq 0)
    foreach ($case in @(
        @{ Name = 'wrong-pid'; Fields = @{ ProcessId = '43'; AppPath = 'D:\Unity\Unity.exe' } },
        @{ Name = 'wrong-path'; Fields = @{ ProcessId = '42'; AppPath = 'D:\Other\Unity.exe' } },
        @{ Name = 'basename-only'; Fields = @{ ProcessId = '42'; AppName = 'Unity.exe' } },
        @{ Name = 'path-without-pid'; Fields = @{ AppPath = 'D:\Unity\Unity.exe' } }
    )) {
        $record = New-EventRecord -Id 1000 -Fields $case.Fields
        $events = Select-UnityProcessFailureEvents -ProcessObservation $observation -Candidates @($record)
        Assert-That "unrelated event rejected: $($case.Name)" ($events.State -eq 'no-matching-events' -and $events.MalformedEvents -eq 0)
    }
    $outside = New-EventRecord -Id 1000 -Fields @{ ProcessId = '42'; AppPath = 'D:\Unity\Unity.exe' } -Time ([DateTime]'2026-10-09T20:04:00Z')
    $events = Select-UnityProcessFailureEvents -ProcessObservation $observation -Candidates @($outside, $reportingEvent)
    Assert-That 'outside-window anchor cannot qualify Windows report' ($events.Events.Count -eq 0)
    $wrongProvider = New-EventRecord -Id 1000 -Fields @{ ProcessId = '42'; AppPath = 'D:\Unity\Unity.exe' }
    $wrongProvider.ProviderName = 'Unrelated provider'
    $events = Select-UnityProcessFailureEvents -ProcessObservation $observation -Candidates @($wrongProvider)
    Assert-That 'unrelated provider cannot qualify an event' ($events.Events.Count -eq 0)
    foreach ($payload in @('<broken', '<!DOCTYPE Event [<!ENTITY x "expanded">]><Event><EventData><Data Name="AppName">&x;</Data></EventData></Event>', ('x' * 65537))) {
        $record = New-EventRecord -Id 1000 -Fields @{} -XmlOverride $payload
        $events = Select-UnityProcessFailureEvents -ProcessObservation $observation -Candidates @($record)
        Assert-That 'malformed, DTD and oversized XML are bounded and rejected' ($events.Events.Count -eq 0 -and $events.MalformedEvents -eq 1)
    }
    $events = Select-UnityProcessFailureEvents -ProcessObservation $observation -Candidates (@($decimal) * 65)
    Assert-That 'candidate and match bounds report truncation explicitly' ($events.Events.Count -eq 8 -and $events.CandidateLimitReached -and $events.MatchLimitReached)
    $noIdentity = Get-UnityProcessFailureEvents -ProcessObservation @{ State = 'unavailable' }
    Assert-That 'missing process identity has explicit no-query state' ($noIdentity.State -eq 'no-query' -and $noIdentity.Reason -eq 'process-identity-unavailable')
    if (-not $IsWindows) {
        $unsupported = Get-UnityProcessFailureEvents -ProcessObservation $observation
        Assert-That 'non-Windows host cannot supply runner events' ($unsupported.State -eq 'no-query' -and $unsupported.Reason -eq 'unsupported-platform')
    }
    $script:eventQueryMode = 'empty'
    $script:eventQueryFilter = $null
    $script:eventQueryLimit = 0
    function Get-WinEvent {
        [CmdletBinding()]
        param([hashtable]$FilterHashtable, [int]$MaxEvents)
        $script:eventQueryFilter = $FilterHashtable
        $script:eventQueryLimit = $MaxEvents
        if ($script:eventQueryMode -eq 'empty' -or $script:eventQueryMode -eq 'unavailable') {
            $errorId = if ($script:eventQueryMode -eq 'empty') { 'NoMatchingEventsFound' } else { 'AccessDenied' }
            $record = [Management.Automation.ErrorRecord]::new([InvalidOperationException]::new('not exported'), $errorId, [Management.Automation.ErrorCategory]::NotSpecified, $null)
            Write-Error -ErrorRecord $record
        } elseif ($script:eventQueryMode -eq 'throw') { throw 'not exported' }
        else { return @($decimal) * 65 }
    }
    $query = Read-UnityProcessFailureEvents -ProcessObservation $observation
    Assert-That 'native no-events error maps to no-matching rather than unavailable' ($query.State -eq 'no-matching-events')
    Assert-That 'query bounds request sixty-five and only intended event providers and IDs' ($script:eventQueryLimit -eq 65 -and $script:eventQueryFilter.LogName -eq 'Application' -and $script:eventQueryFilter.ProviderName.Count -eq 2 -and $script:eventQueryFilter.Id.Count -eq 2)
    foreach ($queryMode in @('unavailable', 'throw')) {
        $script:eventQueryMode = $queryMode
        $query = Read-UnityProcessFailureEvents -ProcessObservation $observation
        Assert-That "query failures explicitly unavailable: $queryMode" ($query.State -eq 'query-unavailable' -and ($query | ConvertTo-Json -Depth 8) -notmatch 'not exported')
    }
    $script:eventQueryMode = 'matched'
    $query = Read-UnityProcessFailureEvents -ProcessObservation $observation
    Assert-That 'query passes bounded records to strict matcher with truncation' ($query.State -eq 'matched-events' -and $query.Events.Count -eq 8 -and $query.CandidateLimitReached)
    $future = $observation.Clone()
    $future.EndObservedUtc = [DateTime]::UtcNow.AddHours(1).ToString('o')
    $query = Read-UnityProcessFailureEvents -ProcessObservation $future
    Assert-That 'query upper time is clamped to capture time without waiting' ([DateTime]$query.WindowEndUtc -eq [DateTime]$query.CapturedUtc)
    Remove-Item Function:\Get-WinEvent

    $persistPath = Join-Path $temporaryRoot 'event-failure.log'
    Write-UnityConfigureFailureObservation -LogPath $persistPath -ProcessObservation $observation
    $persisted = Get-Content (Join-Path $temporaryRoot 'event-failure.process-diagnostics.json') -Raw | ConvertFrom-Json
    Assert-That 'persisted failure report preserves process identity' ($persisted.Process.ProcessId -eq 42)
    $savedPreferences = $ErrorActionPreference
    $ErrorActionPreference = 'Stop'
    try {
        Write-UnityConfigureFailureObservation -LogPath (Join-Path $temporaryRoot 'missing/diagnostics.log') -ProcessObservation $observation
        Assert-That 'diagnostic persistence failure never hides original failure' $true
    } finally { $ErrorActionPreference = $savedPreferences }

    $nativeLog = Join-Path $temporaryRoot 'native.log'
    $summary = Get-UnityNativeReportSummary -LogPath $nativeLog
    Assert-That 'missing native log is explicit missing evidence' ($summary.State -eq 'missing-log')
    Set-Content -LiteralPath $nativeLog -Value ''
    $summary = Get-UnityNativeReportSummary -LogPath $nativeLog
    Assert-That 'readable empty native log is observed with zero reports' ($summary.State -eq 'observed' -and $summary.NativeReportHeaderCount -eq 0 -and $summary.UnknownNativeConditionCount -eq 0)
    Set-Content -LiteralPath $nativeLog -Value @('Native Crash Reporting', 'Got a UNKNOWN while executing native code', 'managed stack', 'NATIVE CRASH REPORTING', 'Got a UNKNOWN while executing native code')
    $summary = Get-UnityNativeReportSummary -LogPath $nativeLog
    Assert-That 'headers and unknown conditions are counted independently without summing events' ($summary.NativeReportHeaderCount -eq 2 -and $summary.UnknownNativeConditionCount -eq 2)
    Assert-That 'native summaries retain exact line locations without raw text' (($summary.NativeReportHeaderLines -join ',') -eq '1,4' -and ($summary.UnknownNativeConditionLines -join ',') -eq '2,5' -and ($summary | ConvertTo-Json -Depth 8) -notmatch 'managed stack')
    Set-Content -LiteralPath $nativeLog -Value (@('Native Crash Reporting', 'Got a UNKNOWN while executing native code') * 12)
    $summary = Get-UnityNativeReportSummary -LogPath $nativeLog
    Assert-That 'native report locations are bounded while counts remain complete' ($summary.NativeReportHeaderCount -eq 12 -and $summary.UnknownNativeConditionCount -eq 12 -and $summary.NativeReportHeaderLines.Count -eq 8 -and $summary.UnknownNativeConditionLines.Count -eq 8 -and $summary.LocationsTruncated)
    $locked = [IO.File]::Open($nativeLog, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $summary = Get-UnityNativeReportSummary -LogPath $nativeLog
        Assert-That 'locked native log is explicit unreadable evidence' ($summary.State -eq 'unreadable-log')
    } finally { $locked.Dispose() }

    function Write-UnityEnvironmentWarningAnnotations { }
    function Write-UnityCatastrophicErrorAnnotations { }
    function Write-UnityMissingModuleAnnotations { }
    function Get-UnityProcessFailureEvents {
        param([hashtable]$ProcessObservation)
        return @{ State = 'controlled-event-query'; ProcessId = $ProcessObservation['ProcessId']; Events = @() }
    }
    $script:executionExit = 0
    $script:executionAttempt = 0
    $script:executionRetry = $false
    $script:executionLogWriteFailure = $false
    $script:EditorTestCompletionSentinel = 'Test run completed'
    function Invoke-ProcessWithTreeKillTimeout {
        param($FilePath, $Arguments, $TimeoutSeconds, $LogPath, $Label,
            $CompletionPattern, $CompletionGraceSeconds, $StallSeconds, [hashtable]$ProcessObservation)
        $script:lastNativeArguments = @($Arguments)
        $crashArgument = [array]::IndexOf($Arguments, '-crash-report-folder')
        if ($crashArgument -ge 0) { $script:capturedNativeDirectories.Add($Arguments[$crashArgument + 1]) }
        ++$script:executionAttempt
        $ProcessObservation.Clear()
        $ProcessObservation['ProcessId'] = 100 + $script:executionAttempt
        $ProcessObservation['ExecutablePath'] = $FilePath
        $ProcessObservation['RawExitCode'] = $script:executionExit
        $ProcessObservation['ReturnedExitCode'] = $script:executionExit
        $ProcessObservation['LaunchUtc'] = [DateTime]::UtcNow.ToString('o')
        $ProcessObservation['EndObservedUtc'] = [DateTime]::UtcNow.ToString('o')
        $ProcessObservation['LogPersistence'] = if ($script:executionLogWriteFailure) { 'write-failed' } else { 'written' }
        if ($script:executionLogWriteFailure) {
            return @{ ExitCode = $script:executionExit; TimedOut = $false; Stalled = $false }
        }
        if ($script:executionRetry -and $script:executionAttempt -eq 1) {
            Set-Content -LiteralPath $LogPath -Value 'IPCStream (Upm-123): IPC stream failed to read'
        } else {
            Set-Content -LiteralPath $LogPath -Value @('Native Crash Reporting', 'Got a UNKNOWN while executing native code', 'Test run completed. Exiting with code 0')
        }
        return @{ ExitCode = $script:executionExit; TimedOut = $false; Stalled = $false }
    }
    foreach ($code in @(0, 2, 124, -1073741819)) {
        $script:executionExit = $code
        $exitCode = Invoke-UnityEditor -EditorPath 'owned-editor' -Arguments @('-runTests') -Label 'controlled test' -LogPath $nativeLog -TimeoutSeconds 5
        $receipt = Get-Content -LiteralPath ([IO.Path]::ChangeExtension($nativeLog, 'execution-diagnostics.json')) -Raw | ConvertFrom-Json
        Assert-That "execution diagnostics preserve exit $code" ($exitCode -eq $code -and $receipt.Process.RawExitCode -eq $code -and $receipt.Process.ReturnedExitCode -eq $code)
        Assert-That "guarded execution captures native reports even at exit $code" ($receipt.NativeReports.NativeReportHeaderCount -eq 1 -and $receipt.NativeReports.UnknownNativeConditionCount -eq 1)
        Assert-That "guarded execution binds event query to child identity at exit $code" ($receipt.WindowsEvents.ProcessId -eq $receipt.Process.ProcessId)
        if ($IsWindows) {
            $crashArgument = [array]::IndexOf($script:lastNativeArguments, '-crash-report-folder')
            $upmArgument = [array]::IndexOf($script:lastNativeArguments, '-upmLogFile')
            Assert-That "Windows child arguments match its native receipt at exit $code" ($crashArgument -ge 0 -and $upmArgument -ge 0 -and $script:lastNativeArguments[$crashArgument + 1] -eq $receipt.Process.NativeEvidence.CrashDirectory -and $script:lastNativeArguments[$upmArgument + 1] -eq $receipt.Process.NativeEvidence.UpmLogPath)
        }
    }
    $before = $receipt.Process.ProcessId
    $script:executionExit = 0
    $providedObservation = @{ Stale = $true }
    $null = Invoke-UnityEditor -EditorPath 'owned-editor' -Arguments @('-runTests') -Label 'fresh test' -LogPath $nativeLog -TimeoutSeconds 5 -ProcessObservation $providedObservation
    $fresh = Get-Content -LiteralPath ([IO.Path]::ChangeExtension($nativeLog, 'execution-diagnostics.json')) -Raw | ConvertFrom-Json
    Assert-That 'fresh receipt replaces stale diagnostics and preserves supplied observation ownership' ($fresh.Process.ProcessId -ne $before -and $providedObservation.ProcessId -eq $fresh.Process.ProcessId -and -not $providedObservation.ContainsKey('Stale'))
    $script:executionLogWriteFailure = $true
    $null = Invoke-UnityEditor -EditorPath 'owned-editor' -Arguments @('-runTests') -Label 'failed log persistence' -LogPath $nativeLog -TimeoutSeconds 5
    $unavailable = Get-Content -LiteralPath ([IO.Path]::ChangeExtension($nativeLog, 'execution-diagnostics.json')) -Raw | ConvertFrom-Json
    Assert-That 'a stale log cannot certify a new execution when log persistence fails' ($unavailable.NativeReports.State -eq 'log-write-failed' -and $unavailable.NativeReports.NativeReportHeaderCount -eq 0 -and (Get-Content -LiteralPath $nativeLog -Raw) -match 'Native Crash Reporting')
    $script:executionLogWriteFailure = $false
    Write-UnityExecutionObservation -LogPath (Join-Path $temporaryRoot 'missing/diagnostics.log') -ProcessObservation @{} -Label 'missing destination'
    Assert-That 'execution diagnostic write failure does not throw' $true

    $script:executionAttempt = 0
    $script:executionRetry = $true
    $retryResults = Join-Path $temporaryRoot 'retry-results.xml'
    $null = Invoke-UnityEditorTestsWithPackageManagerRetry -EditorPath 'owned-editor' -Arguments @('-runTests') -Label 'retry test' -LogPath $nativeLog -ResultsPath $retryResults -Project (Join-Path $temporaryRoot 'retry-project')
    $firstReceipt = Get-Content -LiteralPath (Join-Path $temporaryRoot 'native.first-attempt.execution-diagnostics.json') -Raw | ConvertFrom-Json
    $lastReceipt = Get-Content -LiteralPath ([IO.Path]::ChangeExtension($nativeLog, 'execution-diagnostics.json')) -Raw | ConvertFrom-Json
    Assert-That 'UPM retry retains distinct first and final child receipts' ($script:executionAttempt -eq 2 -and $firstReceipt.Process.ProcessId -eq 101 -and $lastReceipt.Process.ProcessId -eq 102)
    Assert-That 'retained first receipt refers to its matching first log' ($firstReceipt.NativeReports.NativeReportHeaderCount -eq 0 -and $lastReceipt.NativeReports.NativeReportHeaderCount -eq 1 -and (Get-Content (Join-Path $temporaryRoot 'native.first-attempt.log') -Raw) -match 'IPC stream failed')

} finally {
    Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
    foreach ($directory in $script:capturedNativeDirectories) {
        if (Test-Path -LiteralPath $directory -PathType Container) { Remove-Item -LiteralPath $directory -Recurse -Force -ErrorAction Stop }
    }
}

Write-Host "Unity configure UPM retry tests: $($script:passed) passed, $($script:failed) failed"
if ($script:failed -gt 0) {
    exit 1
}
