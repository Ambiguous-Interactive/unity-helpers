# MIT License - Copyright (c) wallstop studios

. (Join-Path $PSScriptRoot 'credential-redaction.ps1')

# Collapse any run of whitespace (including CR/LF) to a single space and trim, so
# a multi-line NUnit <failure>/<message> renders as ONE line. GitHub `::error::`
# annotations are single-line: an embedded newline silently truncates the
# annotation at the first line break, so the whole message must be flattened
# before it is emitted. Mirrors the `.Trim()` collapse the catastrophic-pattern
# scanner applies to each matched log line.

function ConvertTo-SingleLineDiagnostic {
    param([string]$Text)
    if (-not $Text) {
        return ''
    }
    return (((ConvertTo-UnitySafeLogText -Text $Text) -replace '\s+', ' ').Trim())
}

function Get-UnityNativeEvidence {
    param([hashtable]$Capture, [string]$LogPath)

    $report = [ordered]@{ State = 'capture-unavailable'; Files = @() }
    try {
        if (-not $Capture -or $Capture['State'] -ne 'configured') {
            return @{ State = if ($Capture) { $Capture['State'] } else { 'unsupported-platform' }; Files = @() }
        }
        $report = [ordered]@{
            State = 'capture-unavailable'; CaptureId = $Capture.CaptureId
            CrashDirectory = $Capture.CrashDirectory; UpmLogPath = $Capture.UpmLogPath
            UpmLogState = if (Test-Path -LiteralPath $Capture.UpmLogPath -PathType Leaf) { 'present-at-capture' } else { 'missing-at-capture' }
            UpmTextArtifact = $null
            CapturedUtc = [DateTime]::UtcNow.ToString('o'); Files = @(); FileLimitReached = $false
            MaximumDirectoryDepth = 2
            Qualification = 'An isolated folder associates files with this invocation. Dump identity and timing still need verification. Missing or delayed files do not rule out a native failure. Raw dumps stay on the runner.'
        }
        $root = Get-Item -LiteralPath $Capture.CrashDirectory -Force -ErrorAction Stop
        if ($root.Attributes -band [IO.FileAttributes]::ReparsePoint) { return $report }
        $entries = @(Get-ChildItem -LiteralPath $Capture.CrashDirectory -File -Recurse -Depth 2 -Force -ErrorAction Stop | Select-Object -First 65)
        $report.FileLimitReached = $entries.Count -gt 64
        $number = 0
        foreach ($entry in @($entries | Select-Object -First 64 | Sort-Object FullName)) {
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
            $number++
            $file = [ordered]@{
                RelativePath = [IO.Path]::GetRelativePath($Capture.CrashDirectory, $entry.FullName)
                Length = $entry.Length; LastWriteUtc = $entry.LastWriteTimeUtc.ToString('o')
                Sha256 = $null; HashState = 'over-hash-limit'; HashBytes = 0L; HashScope = 'bounded-observed-bytes'
                TextArtifact = $null; TextTruncated = $false
            }
            if ($entry.Length -le 268435456) {
                $hashStream = $null
                $algorithm = $null
                try {
                    $hashStream = [IO.File]::Open($entry.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
                    $algorithm = [Security.Cryptography.SHA256]::Create()
                    $buffer = [byte[]]::new(65536)
                    $remaining = [long]$entry.Length
                    while ($remaining -gt 0) {
                        $read = $hashStream.Read($buffer, 0, [int][Math]::Min($buffer.Length, $remaining))
                        if ($read -eq 0) { break }
                        [void]$algorithm.TransformBlock($buffer, 0, $read, $buffer, 0)
                        $remaining -= $read
                        $file.HashBytes += $read
                    }
                    [void]$algorithm.TransformFinalBlock([byte[]]::new(0), 0, 0)
                    $file.Sha256 = [BitConverter]::ToString($algorithm.Hash).Replace('-', '').ToLowerInvariant()
                    $file.HashState = if ($file.HashBytes -eq $entry.Length -and $hashStream.Length -eq $entry.Length) { 'observed' } else { 'changed-during-capture' }
                } catch { $file.HashState = 'unavailable' }
                finally {
                    if ($hashStream) { $hashStream.Dispose() }
                    if ($algorithm) { $algorithm.Dispose() }
                }
            }
            if ($entry.Extension -in @('.log', '.txt')) {
                $reader = $null
                $textStream = $null
                try {
                    $textStream = [IO.File]::Open($entry.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
                    $reader = [IO.StreamReader]::new($textStream, [Text.UTF8Encoding]::new($false, $true), $true, 4096)
                    $characters = [char[]]::new(262144)
                    $read = $reader.ReadBlock($characters, 0, $characters.Length)
                    $file.TextTruncated = $reader.Peek() -ne -1
                    $text = [string]::new($characters, 0, $read)
                    if ($text.IndexOf([char]0) -ge 0) {
                        $file.TextArtifact = 'non-text-file'
                    } else {
                        $name = "native-$($Capture.CaptureId)-$number.txt"
                        $destination = Join-Path (Split-Path -Parent ([IO.Path]::GetFullPath($LogPath))) $name
                        Set-Content -LiteralPath $destination -Value (ConvertTo-UnitySafeLogText -Text $text) -Encoding UTF8 -ErrorAction Stop
                        $file.TextArtifact = $name
                    }
                } catch { $file.TextArtifact = 'capture-failed' }
                finally {
                    if ($reader) { $reader.Dispose() }
                    elseif ($textStream) { $textStream.Dispose() }
                }
            }
            if ([string]::Equals($entry.FullName, $Capture.UpmLogPath, [StringComparison]::OrdinalIgnoreCase)) {
                $report.UpmTextArtifact = $file.TextArtifact
            }
            $report.Files += $file
        }
        $report.State = if ($report.Files.Count -gt 0) { 'files-observed' } else { 'no-files-at-capture' }
    } catch { $report.State = 'capture-unavailable' }
    return $report
}

function Select-UnityProcessFailureEvents {
    param([hashtable]$ProcessObservation, [object[]]$Candidates)

    $matched = New-Object System.Collections.Generic.List[object]
    $parsed = New-Object System.Collections.Generic.List[object]
    $reportIds = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $malformed = 0
    $startUtc = [DateTime]::Parse($ProcessObservation['LaunchUtc']).ToUniversalTime().AddSeconds(-2)
    $endUtc = [DateTime]::Parse($ProcessObservation['EndObservedUtc']).ToUniversalTime().AddSeconds(15)
    foreach ($candidate in @($Candidates | Select-Object -First 64)) {
        try {
            if ($candidate.TimeCreated.ToUniversalTime() -lt $startUtc -or $candidate.TimeCreated.ToUniversalTime() -gt $endUtc) { continue }
            if (($candidate.Id -ne 1000 -or $candidate.ProviderName -ne 'Application Error') -and
                ($candidate.Id -ne 1001 -or $candidate.ProviderName -ne 'Windows Error Reporting')) { continue }
            $xml = New-Object System.Xml.XmlDocument
            $xml.XmlResolver = $null
            $eventXml = $candidate.ToXml()
            if ($eventXml.Length -gt 65536) { ++$malformed; continue }
            $settings = New-Object System.Xml.XmlReaderSettings
            $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
            $settings.MaxCharactersInDocument = 65536
            $reader = [System.Xml.XmlReader]::Create([IO.StringReader]::new($eventXml), $settings)
            try { $xml.Load($reader) } finally { $reader.Dispose() }
            $fields = @{}
            foreach ($node in $xml.SelectNodes("//*[local-name()='EventData']/*[local-name()='Data']")) {
                $name = $node.GetAttribute('Name')
                if (-not [string]::IsNullOrWhiteSpace($name)) { $fields[$name] = $node.InnerText }
            }
            $pidValue = if ($fields.ContainsKey('ProcessId')) { $fields.ProcessId } else { $fields['FaultingProcessId'] }
            $targetPid = 0L
            $pidMatches = $false
            if ($pidValue -match '^0x[0-9a-fA-F]+$') {
                $pidMatches = [long]::TryParse($pidValue.Substring(2), [Globalization.NumberStyles]::HexNumber,
                    [Globalization.CultureInfo]::InvariantCulture, [ref]$targetPid)
            } elseif ($pidValue -match '^\d+$') {
                $pidMatches = [long]::TryParse($pidValue, [ref]$targetPid)
            }
            $pidMatches = $pidMatches -and $targetPid -eq $ProcessObservation['ProcessId']
            $pathMatches = $fields.ContainsKey('AppPath') -and [string]::Equals(
                $fields.AppPath.Replace('\', '/'), $ProcessObservation['ExecutablePath'].Replace('\', '/'),
                [StringComparison]::OrdinalIgnoreCase)
            $reportId = if ($fields.ContainsKey('IntegratorReportId')) { $fields.IntegratorReportId } else { $fields['ReportId'] }
            $validReportId = [Guid]::Empty
            if (-not [Guid]::TryParse($reportId, [ref]$validReportId) -or $validReportId -eq [Guid]::Empty) { $reportId = $null }
            $directMatch = $pidMatches -and $pathMatches
            $identityConflict = ($null -ne $pidValue -and -not $pidMatches) -or ($fields.ContainsKey('AppPath') -and -not $pathMatches)
            if ($candidate.Id -eq 1000 -and $directMatch -and $reportId) { [void]$reportIds.Add($reportId) }
            $parsed.Add(@{ Candidate = $candidate; Fields = $fields; DirectMatch = $directMatch; IdentityConflict = $identityConflict; ReportId = $reportId })
        } catch { ++$malformed }
    }
    foreach ($item in $parsed) {
        $joined = $item.Candidate.Id -eq 1001 -and -not $item.IdentityConflict -and $item.ReportId -and $reportIds.Contains($item.ReportId)
        if (-not $item.DirectMatch -and -not $joined) { continue }
        $safeFields = [ordered]@{}
        foreach ($name in @('AppName', 'ModuleName', 'ModulePath', 'ExceptionCode', 'FaultingOffset')) {
            if ($item.Fields.ContainsKey($name)) {
                $value = ConvertTo-SingleLineDiagnostic -Text $item.Fields[$name]
                $safeFields[$name] = $value.Substring(0, [Math]::Min(256, $value.Length))
            }
        }
        $matched.Add([ordered]@{
            EventUtc = $item.Candidate.TimeCreated.ToUniversalTime().ToString('o')
            Provider = $item.Candidate.ProviderName
            EventId = $item.Candidate.Id
            RecordId = $item.Candidate.RecordId
            Match = if ($item.DirectMatch) { 'path-and-pid' } else { 'joined-report-id' }
            ReportId = $item.ReportId
            Fields = $safeFields
        })
    }
    return @{
        State = if ($matched.Count -gt 0) { 'matched-events' } else { 'no-matching-events' }
        Events = @($matched | Select-Object -First 8)
        CandidateLimitReached = @($Candidates).Count -gt 64
        MatchLimitReached = $matched.Count -gt 8
        MalformedEvents = $malformed
    }
}

function Get-UnityProcessFailureEvents {
    param([hashtable]$ProcessObservation)

    $report = @{ State = 'no-query'; Reason = 'process-identity-unavailable'; Events = @() }
    if (-not $ProcessObservation -or -not $ProcessObservation['ProcessId'] -or
        -not $ProcessObservation['ExecutablePath'] -or -not $ProcessObservation['LaunchUtc'] -or
        -not $ProcessObservation['EndObservedUtc']) { return $report }
    if (-not $IsWindows) { $report.Reason = 'unsupported-platform'; return $report }
    return Read-UnityProcessFailureEvents -ProcessObservation $ProcessObservation
}

function Read-UnityProcessFailureEvents {
    param([hashtable]$ProcessObservation)

    $report = @{ State = 'query-unavailable'; Reason = 'event-query-failed'; Events = @() }
    try {
        $startUtc = [DateTime]::Parse($ProcessObservation['LaunchUtc']).ToUniversalTime().AddSeconds(-2)
        $captureUtc = [DateTime]::UtcNow
        $endUtc = [DateTime]::Parse($ProcessObservation['EndObservedUtc']).ToUniversalTime().AddSeconds(15)
        if ($endUtc -gt $captureUtc) { $endUtc = $captureUtc }
        $queryErrors = @()
        $candidates = @(Get-WinEvent -FilterHashtable @{
            LogName = 'Application'; ProviderName = @('Application Error', 'Windows Error Reporting')
            Id = @(1000, 1001); StartTime = $startUtc; EndTime = $endUtc
        } -MaxEvents 65 -ErrorAction SilentlyContinue -ErrorVariable queryErrors)
        $unavailable = @($queryErrors | Where-Object { $_.FullyQualifiedErrorId -notlike 'NoMatchingEventsFound*' })
        if ($unavailable.Count -gt 0) {
            $report.State = 'query-unavailable'
            $report.Reason = 'event-query-failed'
        } else {
            $report = Select-UnityProcessFailureEvents -ProcessObservation $ProcessObservation -Candidates $candidates
        }
        $report.WindowStartUtc = $startUtc.ToString('o')
        $report.WindowEndUtc = $endUtc.ToString('o')
        $report.CapturedUtc = $captureUtc.ToString('o')
        $report.Qualification = 'Only events observed by capture time; missing or delayed events do not rule out a crash.'
    } catch {
        $report.State = 'query-unavailable'
        $report.Reason = 'event-query-failed'
    }
    return $report
}
