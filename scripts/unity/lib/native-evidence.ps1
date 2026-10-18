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

function Read-NativeEvidenceFileBytes {
    param([string]$Path, [int]$MaximumBytes)
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Evidence input must be a regular file.' }
    $stream = [IO.File]::Open($item.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        if ($stream.Length -lt 1 -or $stream.Length -gt $MaximumBytes) { throw 'Evidence input exceeds its byte bound or is empty.' }
        $bytes = [byte[]]::new([int]$stream.Length)
        $offset = 0
        while ($offset -lt $bytes.Length) {
            $count = $stream.Read($bytes, $offset, $bytes.Length - $offset)
            if ($count -eq 0) { throw 'Evidence input changed while reading.' }
            $offset += $count
        }
        if ($stream.ReadByte() -ne -1) { throw 'Evidence input changed while reading.' }
        return ,$bytes
    } finally { $stream.Dispose() }
}

function Get-NativeTestFrameworkSources {
    param([string]$LogPath, [hashtable]$ProcessObservation, [string]$ArtifactsPath)
    $bytes = Read-NativeEvidenceFileBytes -Path $LogPath -MaximumBytes 4194304
    $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
    $locations = [regex]::Matches($text, '(?m)^\s*com\.unity\.test-framework@1\.8\.0 \(location: ([^\r\n]+)\)\s*$')
    $projects = [regex]::Matches($text, '(?m)^Successfully changed project path to: ([^\r\n]+)\r?$')
    if ($locations.Count -ne 1 -or $projects.Count -ne 1 -or
        $text -cnotmatch ('"processId"\s*:\s*' + $ProcessObservation.ProcessId + '(?=[,}])') -or
        -not $text.Contains($ProcessObservation.ExecutablePath)) { throw 'Test-framework log does not uniquely bind the observed process and package.' }
    if (-not [IO.Path]::IsPathRooted($projects[0].Groups[1].Value)) { throw 'Test-framework project path must be absolute.' }
    $project = [IO.Path]::GetFullPath($projects[0].Groups[1].Value)
    if ([IO.Path]::GetFileName($project) -cnotin @('6000.6.0f1-standalone', '6000.6.0f1-standalone-proto-only')) {
        throw 'Test-framework source log must describe a persistent latest standalone project.'
    }
    $package = [IO.Path]::GetFullPath($locations[0].Groups[1].Value)
    $expected = Join-Path $project 'Library/PackageCache/com.unity.test-framework@7a3849e09bd0'
    if (-not [IO.Path]::IsPathRooted($locations[0].Groups[1].Value) -or
        -not [string]::Equals($package, $expected, [StringComparison]::OrdinalIgnoreCase)) { throw 'Test-framework package cache identity differs.' }
    $output = Join-Path $ArtifactsPath 'test-framework-sources'
    if (Test-Path -LiteralPath $output) { throw 'Test-framework source output must be fresh.' }
    if ([IO.Path]::GetFullPath($output).StartsWith($project.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Test-framework source output must be outside the observed project.'
    }
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { $logHash = [BitConverter]::ToString($algorithm.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant() }
    finally { $algorithm.Dispose() }
    $record = [ordered]@{
        Scope = 'current-persistent-package-sources'; OriginalOwnedProjectBytesProven = $false
        State = 'unavailable'; ObservedLogPath = [IO.Path]::GetFullPath($LogPath); ObservedLogSha256 = $logHash
        PackagePath = $package; PackageVersion = '1.8.0'; CacheIdentity = '7a3849e09bd0'; Files = @(); ErrorType = $null
    }
    try {
        $packageItem = Get-Item -LiteralPath $package -Force -ErrorAction Stop
        if (-not $packageItem.PSIsContainer -or ($packageItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Test-framework package must be an ordinary directory.' }
        $files = @{}
        foreach ($relative in @('package.json', 'UnityEditor.TestRunner/TestLaunchers/PlayerLauncherContextSettings.cs', 'UnityEditor.TestRunner/TestLaunchers/PlayerLauncher.cs')) {
            $path = $package
            foreach ($component in $relative.Split('/')) {
                $path = Join-Path $path $component
                $item = Get-Item -LiteralPath $path -Force -ErrorAction Stop
                if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Test-framework sources must not traverse reparse points.' }
            }
            $files[$relative] = Read-NativeEvidenceFileBytes -Path $path -MaximumBytes 262144
        }
        $document = [Text.Json.JsonDocument]::Parse([Text.UTF8Encoding]::new($false, $true).GetString($files['package.json']))
        try {
            if ($document.RootElement.GetProperty('name').GetString() -cne 'com.unity.test-framework' -or
                $document.RootElement.GetProperty('version').GetString() -cne '1.8.0') { throw 'Test-framework package manifest identity differs.' }
        } finally { $document.Dispose() }
        $null = New-Item -ItemType Directory -Path $output -ErrorAction Stop
        foreach ($relative in @('package.json', 'UnityEditor.TestRunner/TestLaunchers/PlayerLauncherContextSettings.cs', 'UnityEditor.TestRunner/TestLaunchers/PlayerLauncher.cs')) {
            $file = [IO.Path]::GetFileName($relative) + '.txt'
            $destination = Join-Path $output $file
            [IO.File]::WriteAllBytes($destination, $files[$relative])
            $record.Files += [ordered]@{
                Path = Join-Path $package $relative; SavedPath = 'test-framework-sources/' + $file; Length = $files[$relative].Length
                Sha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }
        $record.State = 'observed'
    } catch { $record.ErrorType = $_.Exception.GetType().Name; $record.Error = $_.Exception.Message }
    return [pscustomobject]$record
}

function Get-NativeReadbackArtifactSelection {
    param([string]$Repository, [string]$SourceRunId, [string]$ArtifactsPath, [string]$Token)
    $output = [IO.Path]::GetFullPath($ArtifactsPath)
    if (Test-Path -LiteralPath $output) { throw 'Readback selection output must be fresh.' }
    $null = New-Item -ItemType Directory -Path $output -ErrorAction Stop
    $record = [ordered]@{
        EvidenceOnly = $true; State = 'failed'; Repository = $Repository; RequestedRunId = $SourceRunId
        RunId = $null; HeadSha = $null; RunAttempt = $null; AcceptanceArtifactId = $null; StandaloneArtifactId = $null
        Artifacts = @(); StartedUtc = [DateTime]::UtcNow.ToString('o'); EndedUtc = $null; ErrorType = $null; Error = $null
    }
    try {
        $runNumber = 0L
        if ($Repository -cnotmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' -or $SourceRunId -cnotmatch '^[1-9][0-9]{0,19}$' -or
            -not [long]::TryParse($SourceRunId, [ref]$runNumber) -or [string]::IsNullOrWhiteSpace($Token)) { throw 'Readback requires a current-repository name, positive run ID, and Actions read token.' }
        $headers = @{ Authorization = 'Bearer ' + $Token; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28' }
        $base = 'https://api.github.com/repos/' + $Repository + '/actions/runs/' + $SourceRunId
        $run = Invoke-RestMethod -Method Get -Uri $base -Headers $headers -TimeoutSec 30
        if (($run.id -isnot [int] -and $run.id -isnot [long]) -or ($run.run_attempt -isnot [int] -and $run.run_attempt -isnot [long]) -or $run.id -ne $runNumber -or $run.repository.full_name -cne $Repository -or $run.status -cne 'completed' -or
            $run.run_attempt -ne 1 -or $run.head_sha -cnotmatch '^[a-f0-9]{40}$') { throw 'Readback requires the exact completed current-repository run with one attempt; ambiguous or repeated attempts are unsupported.' }
        $record.RunId = $run.id; $record.HeadSha = $run.head_sha; $record.RunAttempt = $run.run_attempt
        $response = Invoke-RestMethod -Method Get -Uri ($base + '/artifacts?per_page=100') -Headers $headers -TimeoutSec 30
        if (($response.total_count -isnot [int] -and $response.total_count -isnot [long]) -or $response.total_count -lt 1 -or $response.total_count -gt 100 -or @($response.artifacts).Count -ne $response.total_count) { throw 'Readback artifact inventory is empty, truncated, or exceeds its bound.' }
        foreach ($name in @('unity-6000.6.0f1-acceptance', 'unity-6000.6.0f1-standalone')) {
            $matches = @($response.artifacts | Where-Object { $_.name -ceq $name })
            if ($matches.Count -ne 1) { throw "Readback requires exactly one artifact named $name." }
            $artifact = $matches[0]
            if (($artifact.id -isnot [int] -and $artifact.id -isnot [long]) -or ($artifact.workflow_run.id -isnot [int] -and $artifact.workflow_run.id -isnot [long]) -or
                ($artifact.size_in_bytes -isnot [int] -and $artifact.size_in_bytes -isnot [long]) -or $artifact.size_in_bytes -lt 1 -or $artifact.id -le 0 -or $artifact.expired -isnot [bool] -or $artifact.expired -or
                $artifact.workflow_run.id -ne $runNumber -or $artifact.workflow_run.head_sha -cne $run.head_sha) { throw "Readback artifact identity differs or has expired: $name." }
            $record.Artifacts += [ordered]@{ Id = $artifact.id; Name = $name; HeadSha = $artifact.workflow_run.head_sha; SizeBytes = $artifact.size_in_bytes }
            if ($name -ceq 'unity-6000.6.0f1-acceptance') { $record.AcceptanceArtifactId = $artifact.id }
            else { $record.StandaloneArtifactId = $artifact.id }
        }
        $record.State = 'selected'
    } catch { $record.ErrorType = $_.Exception.GetType().Name; $record.Error = $_.Exception.Message; throw }
    finally {
        $record.EndedUtc = [DateTime]::UtcNow.ToString('o')
        [IO.File]::WriteAllText((Join-Path $output 'selection.json'), (ConvertTo-UnitySafeLogText -Text ($record | ConvertTo-Json -Depth 8)))
    }
    return [pscustomobject]$record
}
