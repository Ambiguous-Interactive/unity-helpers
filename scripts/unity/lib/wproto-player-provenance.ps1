# Shared source census for the WProto synthetic player declaration and verifier.
. (Join-Path $PSScriptRoot 'catastrophic-patterns.ps1')
function Get-WProtoPlayerSourceFiles {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Repository)

    $root = [IO.Path]::GetFullPath($Repository).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $paths = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($directory in @('Runtime', 'Editor', 'Tests', 'Generator~', 'scripts/unity')) {
        $pending = New-Object 'System.Collections.Generic.Stack[string]'
        $pending.Push((Join-Path $root $directory))
        while ($pending.Count -gt 0) {
            $current = Get-Item -LiteralPath $pending.Pop() -Force -ErrorAction Stop
            if (-not $current.PSIsContainer -or ($current.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw 'WProto source roots and directories must be ordinary directories.'
            }
            foreach ($item in @(Get-ChildItem -LiteralPath $current.FullName -Force -ErrorAction Stop)) {
                if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'WProto source census refuses reparse points.' }
                if ($item.PSIsContainer) {
                    if ($directory -eq 'Generator~' -and $item.Name -in @('bin', 'obj', '.git')) { continue }
                    $pending.Push($item.FullName)
                } else {
                    if ($directory -eq 'Generator~' -and $item.Extension -notin @('.cs', '.csproj', '.props', '.targets', '.rsp', '.json', '.xml', '.dll', '.meta')) { continue }
                    $relative = $item.FullName.Substring($root.Length + 1).Replace('\', '/')
                    $null = $paths.Add($relative)
                    if ($paths.Count -gt 16384) { throw 'WProto source census exceeds 16384 files.' }
                }
            }
        }
    }
    foreach ($path in @('package.json', '.github/unity-versions.json', '.github/workflows/unity-tests.yml',
            'scripts/unity/run-ci-tests.ps1', 'scripts/unity/run-acceptance.ps1', 'scripts/unity/verify-acceptance.ps1',
            'scripts/unity/verify-wproto-corpus.js', 'scripts/unity/wproto-corpus.v1.json',
            'scripts/unity/lib/wproto-player-provenance.ps1', 'scripts/unity/lib/wproto-player-provenance.ps1.meta')) {
        $null = $paths.Add($path)
    }
    foreach ($item in @(Get-ChildItem -LiteralPath $root -File -Force)) {
        if ($item.Extension -in @('.rsp', '.props', '.targets', '.csproj')) { $null = $paths.Add($item.Name) }
    }
    [string[]]$ordered = @($paths)
    [Array]::Sort($ordered, [StringComparer]::Ordinal)
    foreach ($relative in $ordered) {
        $item = Get-Item -LiteralPath (Join-Path $root $relative) -Force -ErrorAction Stop
        if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "WProto source input must be an ordinary file: $relative"
        }
        [pscustomobject][ordered]@{ Path = $relative; Sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
}

# Retain complete original byte lines, including unknown or malformed records. The
# full player log is validated before this extraction; support logs may be redacted.
function Copy-WProtoCorpusRecords {
    param([string]$Source, [string]$Destination)
    $bytes = [IO.File]::ReadAllBytes($Source)
    $encoding = New-Object Text.UTF8Encoding($false, $true)
    $output = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $start = 0
        for ($index = 0; $index -le $bytes.Length; $index++) {
            if ($index -lt $bytes.Length -and $bytes[$index] -ne 10) { continue }
            $length = $index - $start
            if ($index -lt $bytes.Length) { $length++ }
            if ($length -gt 0 -and $encoding.GetString($bytes, $start, $length).Contains('WPROTO_CORPUS_')) {
                $output.Write($bytes, $start, $length)
            }
            $start = $index + 1
        }
        if ($output.Length -eq 0) { throw 'WProto raw record retention cannot be empty.' }
    } finally { $output.Dispose() }
}

function Get-WProtoFrozenOperationNames {
    @('read-memory', 'read-stream', 'read-finite', 'read-direct', 'read-public',
        'write-buffer', 'write-stream', 'write-array', 'write-direct', 'write-public')
}

function Assert-WProtoSourceCensus {
    param([string]$Repository, $DeclaredFiles)
    $declared = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::Ordinal)
    foreach ($entry in @($DeclaredFiles)) {
        if ($entry.Path -isnot [string] -or $entry.Sha256 -isnot [string] -or $entry.Sha256 -cnotmatch '^[a-f0-9]{64}$' -or
            $declared.ContainsKey($entry.Path)) { throw 'WProto source census has an invalid or duplicate declaration.' }
        $declared.Add($entry.Path, $entry.Sha256)
    }
    $actual = @(Get-WProtoPlayerSourceFiles -Repository $Repository)
    if ($actual.Count -ne $declared.Count) { throw 'WProto source census is incomplete.' }
    foreach ($entry in $actual) {
        if (-not $declared.ContainsKey($entry.Path) -or $declared[$entry.Path] -cne $entry.Sha256) {
            throw "WProto source census is incomplete or changed: $($entry.Path)"
        }
    }
}

function Invoke-WProtoCorpusVerifier {
    param([string]$Repository, [string]$PlayerLogPath, [string]$Commit, [string]$UnityVersion)
    $temporary = Join-Path ([IO.Path]::GetTempPath()) ('wproto-corpus-' + [Guid]::NewGuid().ToString('N') + '.json')
    try {
        & node (Join-Path $Repository 'scripts/unity/verify-wproto-corpus.js') --log $PlayerLogPath --commit $Commit --unity-version $UnityVersion --output $temporary | Out-Null
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $temporary -PathType Leaf)) { throw 'WProto raw corpus verifier rejected the record stream.' }
        return Get-Content -LiteralPath $temporary -Raw | ConvertFrom-Json -ErrorAction Stop
    } finally { Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue }
}

function Get-WProtoFrozenPlayerRuntimeJoin {
    param($Context, [string]$PlayerLogPath, [bool]$PrerequisitesPassed)
    $errors = New-Object 'System.Collections.Generic.List[string]'
    $summary = $null
    $state = 'rejected'
    try {
        if (-not $PrerequisitesPassed) { throw 'WProto runtime join requires current healthy invocation, complete XML, and stable source/build snapshots.' }
        $declaration = $Context.Declaration.Value
        Assert-WProtoSourceCensus -Repository $Context.RepoRoot -DeclaredFiles $declaration.SourceFiles
        $summary = Invoke-WProtoCorpusVerifier -Repository $Context.RepoRoot -PlayerLogPath $PlayerLogPath -Commit $declaration.SourceCommit -UnityVersion $declaration.UnityVersion
        if ($summary.strictRecordValidity -isnot [bool] -or -not $summary.strictRecordValidity -or
            $summary.scope -cne 'synthetic-timing-only' -or $summary.corpusVersion -cne 'wproto-seven-families-v1') {
            throw 'WProto strict corpus validity is missing.'
        }
        $operations = @(Get-WProtoFrozenOperationNames)
        if (@($summary.runtimeRecords.PSObject.Properties).Count -ne $operations.Count) { throw 'Exactly ten WProto runtime records are required.' }
        $built = @($Context.Snapshots | Where-Object { $_.Stage -ceq 'after-build' })
        if ($built.Count -ne 1) { throw 'One immutable build snapshot is required.' }
        foreach ($operation in $operations) {
            $record = $summary.runtimeRecords.$operation
            if (($record.SchemaVersion -isnot [int] -and $record.SchemaVersion -isnot [long]) -or $record.SchemaVersion -ne 1) { throw 'WProto runtime schema must be integer one.' }
            foreach ($name in @('FrozenRunId', 'FrozenDeclarationSha256', 'FrozenCorpusSha256', 'SourceCommit')) {
                if ($record.$name -isnot [string]) { throw "WProto runtime $name must be a scalar string." }
            }
            if ($record.FrozenRunId -cne $declaration.RunId -or $record.FrozenDeclarationSha256 -cne $Context.Declaration.Sha256 -or
                $record.FrozenCorpusSha256 -cne $Context.Declaration.CanonicalCorpus.Sha256 -or $record.SourceCommit -cne $declaration.SourceCommit) {
                throw 'WProto runtime anchor does not match the exact declaration.'
            }
            $identity = $record.RuntimeIdentity
            if ($identity -isnot [pscustomobject] -or $identity.UnityVersion -isnot [string] -or
                $identity.UnityVersion -cne $declaration.UnityVersion -or $identity.Backend -isnot [string] -or $identity.Backend -cne 'Mono' -or
                $identity.Platform -isnot [string] -or $identity.Platform -cne 'WindowsPlayer' -or
                ($identity.ProcessBits -isnot [int] -and $identity.ProcessBits -isnot [long]) -or $identity.ProcessBits -ne 64 -or
                $identity.IsEditor -isnot [bool] -or $identity.IsEditor -or $identity.DevelopmentBuild -isnot [bool] -or $identity.DevelopmentBuild -or
                $identity.WallstopProto -isnot [bool] -or -not $identity.WallstopProto) {
                throw 'WProto runtime must be a WALLSTOP_PROTO non-development Windows x64 Mono player.'
            }
            if ($record.OracleAssemblies -isnot [array] -or $record.OracleAssemblies.Count -ne 2) { throw 'Both actually loaded oracle assemblies are required.' }
            foreach ($name in @('protobuf-net', 'protobuf-net.Core')) {
                $oracle = @($record.OracleAssemblies | Where-Object { $_.Name -ceq $name })
                if ($oracle.Count -ne 1 -or $oracle[0].Name -isnot [string] -or $oracle[0].Version -isnot [string] -or
                    $oracle[0].Version -cnotmatch '^\d+\.\d+\.\d+\.\d+$' -or $oracle[0].Sha256 -isnot [string] -or $oracle[0].Sha256 -cnotmatch '^[a-f0-9]{64}$') {
                    throw 'Loaded oracle assembly identity is invalid.'
                }
                $source = @($declaration.SourceFiles | Where-Object { $_.Path -ceq "Runtime/Protobuf-Net/$name.dll" })
                $payload = @($built[0].PayloadFiles | Where-Object { $_.Kind -ceq 'file' -and $_.Path -cmatch ('^[^/]+_Data/Managed/' + [regex]::Escape($name) + '\.dll$') })
                if ($source.Count -ne 1 -or $payload.Count -ne 1 -or $source[0].Sha256 -cne $oracle[0].Sha256 -or $payload[0].Sha256 -cne $oracle[0].Sha256) {
                    throw "Loaded oracle is not joined to the source and built managed payload: $name"
                }
            }
        }
        $state = 'passed'
    } catch { $errors.Add($_.ToString()) }
    return [pscustomobject]@{
        State = $state; CorrectnessCorpusConsumptionState = $state; CampaignCorpusConsumptionState = $state
        Summary = $summary; Errors = $errors.ToArray()
    }
}

function Get-WProtoKnownFaultScan {
    param([string[]]$Paths)
    $observations = New-Object 'System.Collections.Generic.List[object]'
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $Paths) {
        $row = [ordered]@{ Path = $path; State = 'unavailable'; Sha256 = $null; Length = $null; NativeReportHeaderCount = 0; UnknownNativeConditionCount = 0; CatastrophicLabels = @(); Error = $null }
        try {
            if (-not $seen.Add([IO.Path]::GetFullPath($path))) { throw 'Known-fault log paths must be distinct.' }
            $item = Get-Item -LiteralPath $path -Force -ErrorAction Stop
            if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $item.Length -gt 67108864) { throw 'Known-fault log input is not a bounded ordinary file.' }
            $native = Get-NativeReportSummary -LogPath $path
            if ($native.State -cne 'observed') { throw 'Known-fault log input was unreadable.' }
            $row.NativeReportHeaderCount = $native.NativeReportHeaderCount
            $row.UnknownNativeConditionCount = $native.UnknownNativeConditionCount
            $row.CatastrophicLabels = @(foreach ($pattern in @(Get-CatastrophicPatterns)) {
                    if (Select-String -LiteralPath $path -Pattern $pattern.Pattern -SimpleMatch:$pattern.UseSimple -Quiet -ErrorAction Stop) { $pattern.Label }
                })
            $row.Length = $item.Length
            $row.Sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
            $row.State = 'observed'
        } catch { $row.Error = $_.ToString() }
        $observations.Add([pscustomobject]$row)
    }
    return [pscustomobject]@{
        Scope = 'Historical scan of original configure, build, and player logs for known fault indicators; absence does not rule out native faults.'
        Observations = $observations.ToArray()
    }
}

function Test-WProtoKnownFaultObservations {
    param($Scan, [int]$ExpectedCount)
    if ($null -eq $Scan -or $Scan.Observations.Count -ne $ExpectedCount) { return $false }
    foreach ($log in $Scan.Observations) {
        if ($log.State -cne 'observed' -or $null -ne $log.Error -or
            ($log.NativeReportHeaderCount -isnot [int] -and $log.NativeReportHeaderCount -isnot [long]) -or $log.NativeReportHeaderCount -ne 0 -or
            ($log.UnknownNativeConditionCount -isnot [int] -and $log.UnknownNativeConditionCount -isnot [long]) -or $log.UnknownNativeConditionCount -ne 0 -or
            $log.CatastrophicLabels.Count -ne 0) { return $false }
    }
    return $true
}

function Get-WProtoNativeSupportScans {
    param($Context)
    $configure = if ($Context.ConfigureOutcomes.Count -eq 1) { $Context.ConfigureOutcomes[0] } else { $null }
    foreach ($observation in @($configure, $Context.BuildObservation)) {
        $scan = $null
        if ($null -ne $observation -and $null -ne $observation.NativeEvidence -and
            $observation.NativeEvidence.State -ceq 'files-observed') {
            $scan = Get-WProtoKnownFaultScan -Paths @($observation.NativeEvidence.UpmLogPath)
        }
        [pscustomobject]@{ Scan = $scan }
    }
}

function Test-WProtoNativeCapture {
    param($Native, $SupportScan)
    if ($null -eq $Native -or $Native.State -cnotin @('no-files-at-capture', 'files-observed') -or
        $Native.FileLimitReached -isnot [bool] -or $Native.FileLimitReached) { return $false }
    if ($Native.State -ceq 'no-files-at-capture') { return $Native.Files.Count -eq 0 -and $null -eq $SupportScan.Scan }
    if ($Native.Files.Count -ne 1) { return $false }
    $file = $Native.Files[0]
    if ($file.RelativePath -cne 'upm.log' -or $file.HashState -cne 'observed' -or
        $file.Sha256 -isnot [string] -or $file.Sha256 -cnotmatch '^[a-f0-9]{64}$' -or
        $file.TextTruncated -isnot [bool] -or $file.TextTruncated -or
        $file.TextArtifact -isnot [string] -or $file.TextArtifact -cnotmatch '^native-[a-f0-9]{32}-[0-9]+\.txt$' -or
        $Native.UpmTextArtifact -cne $file.TextArtifact -or
        -not (Test-WProtoKnownFaultObservations -Scan $SupportScan.Scan -ExpectedCount 1)) { return $false }
    $log = $SupportScan.Scan.Observations[0]
    return $log.Path -ceq $Native.UpmLogPath -and $log.Sha256 -ceq $file.Sha256 -and $log.Length -eq $file.Length
}

function Test-WProtoFrozenProcessHealth {
    param($Context)
    foreach ($name in @('LaunchAttempted', 'InvocationObserved', 'ResultsValidated')) {
        if ($Context.$name -isnot [bool] -or -not $Context.$name) { return $false }
    }
    if ($null -ne $Context.ExecutionError -or $Context.ConfigureOutcomes.Count -ne 1) { return $false }
    if ($Context.NativeSupportScans.Count -ne 2) { return $false }
    $observationIndex = 0
    foreach ($observation in @($Context.ConfigureOutcomes[0], $Context.BuildObservation)) {
        if ($null -eq $observation -or $observation.State -cne 'observed' -or
            ($observation.ReturnedExitCode -isnot [int] -and $observation.ReturnedExitCode -isnot [long]) -or $observation.ReturnedExitCode -ne 0 -or
            ($observation.RawExitCode -isnot [int] -and $observation.RawExitCode -isnot [long]) -or $observation.RawExitCode -ne 0 -or
            $observation.ExitOrigin -cne 'process-exit' -or -not [string]::IsNullOrEmpty($observation.KillReason) -or
            -not [string]::IsNullOrEmpty($observation.ExceptionType)) { return $false }
        if (-not (Test-WProtoNativeCapture -Native $observation.NativeEvidence -SupportScan $Context.NativeSupportScans[$observationIndex])) { return $false }
        $observationIndex++
    }
    foreach ($outcome in @($Context.BuildOutcome, $Context.PlayerOutcome)) {
        if ($null -eq $outcome -or $outcome.TimedOut -isnot [bool] -or $outcome.TimedOut -or
            ($outcome.ExitCode -isnot [int] -and $outcome.ExitCode -isnot [long]) -or $outcome.ExitCode -ne 0) { return $false }
    }
    return Test-WProtoKnownFaultObservations -Scan $Context.KnownFaultScan -ExpectedCount 3
}

function Get-WProtoProjectInputs {
    param([string]$ProjectPath)
    foreach ($directory in @('Assets', 'Packages', 'ProjectSettings')) {
        $root = Join-Path $ProjectPath $directory
        Assert-FrozenPlayerRegularPath -Path $root -Directory
        $inventory = @(Get-FrozenPlayerFileInventory -Root $root)
        foreach ($entry in $inventory) {
            [pscustomobject][ordered]@{ Path = "$directory/$($entry.Path)"; Kind = $entry.Kind; Length = $entry.Length; Sha256 = $entry.Sha256 }
        }
    }
}

function Assert-WProtoRecordedInventory {
    param([string]$Root, $Entries, [switch]$Complete)
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $observed = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($entry in @($Entries)) {
        if ($entry.Path -isnot [string] -or $entry.Path -match '(^/|\\|:|(^|/)\.\.?(/|$))' -or -not $observed.Add($entry.Path)) {
            throw 'WProto recorded inventory contains an invalid or repeated relative path.'
        }
        $path = [IO.Path]::GetFullPath((Join-Path $rootPath $entry.Path))
        if (-not $path.StartsWith($rootPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'WProto recorded path leaves its root.' }
        $item = Get-Item -LiteralPath $path -Force -ErrorAction Stop
        $parent = $item
        while ($null -ne $parent) {
            if ($parent.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'WProto recorded path has a reparse ancestor.' }
            $parentPath = Split-Path -Parent $parent.FullName
            $parent = if ($parentPath) { Get-Item -LiteralPath $parentPath -Force } else { $null }
        }
        if ($entry.Kind -ceq 'directory') {
            if (-not $item.PSIsContainer) { throw 'WProto recorded directory changed kind.' }
        } elseif ($entry.Kind -ceq 'file') {
            if ($item.PSIsContainer -or $entry.Sha256 -isnot [string] -or $entry.Sha256 -cnotmatch '^[a-f0-9]{64}$' -or
                $item.Length -ne $entry.Length -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $entry.Sha256) {
                throw 'WProto recorded file changed.'
            }
        } else { throw 'WProto recorded inventory kind is invalid.' }
    }
    if ($observed.Count -eq 0) { throw 'WProto recorded inventory is empty.' }
    if ($Complete) {
        $actual = @(Get-ChildItem -LiteralPath $rootPath -Recurse -Force)
        if ($actual.Count -ne $observed.Count) { throw 'WProto complete inventory membership changed.' }
        foreach ($item in $actual) {
            if (-not $observed.Contains($item.FullName.Substring($rootPath.Length + 1).Replace('\', '/'))) { throw 'WProto complete inventory membership changed.' }
        }
    }
}

function Assert-WProtoSnapshotInventory {
    param($Observed, $Expected, [switch]$DeclaredHashes)
    $entries = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($entry in @($Expected)) {
        if ($entry.Path -isnot [string] -or $entries.ContainsKey($entry.Path)) { throw 'WProto snapshot baseline paths are invalid or duplicated.' }
        $entries.Add($entry.Path, $entry)
    }
    if (@($Observed).Count -ne $entries.Count) { throw 'WProto snapshot inventory membership differs from its baseline.' }
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($entry in @($Observed)) {
        if ($entry.Path -isnot [string] -or -not $seen.Add($entry.Path) -or -not $entries.ContainsKey($entry.Path)) {
            throw 'WProto snapshot inventory has an unexpected or repeated path.'
        }
        $expectedEntry = $entries[$entry.Path]
        if ($entry.Sha256 -cne $expectedEntry.Sha256 -or
            ($DeclaredHashes -and $entry.Kind -cne 'file') -or
            (-not $DeclaredHashes -and ($entry.Kind -cne $expectedEntry.Kind -or $entry.Length -ne $expectedEntry.Length))) {
            throw 'WProto snapshot inventory differs from its baseline.'
        }
    }
}

function Assert-WProtoBuildPreparation {
    param($Preparation, $ConfiguredInputs, $BuiltInputs, $Declaration, [string]$DeclarationSha256)
    if ($null -eq $Preparation -or $Preparation.SchemaVersion -isnot [long] -and $Preparation.SchemaVersion -isnot [int]) {
        throw 'WProto build preparation receipt is missing or has an invalid schema.'
    }
    if ($Preparation.SchemaVersion -ne 1 -or $Preparation.RunId -isnot [string] -or
        $Preparation.RunId -cne $Declaration.RunId -or $Preparation.DeclarationSha256 -isnot [string] -or
        $Preparation.DeclarationSha256 -cne $DeclarationSha256 -or $Preparation.SourceCommit -isnot [string] -or
        $Preparation.SourceCommit -cne $Declaration.SourceCommit) { throw 'WProto build preparation identity differs.' }
    if ($Preparation.Scenes -isnot [array] -or $Preparation.Scenes.Count -lt 1 -or $Preparation.Scenes.Count -gt 256 -or
        $Preparation.ProjectInputs -isnot [array] -or $Preparation.ProjectInputs.Count -lt 1 -or $Preparation.ProjectInputs.Count -gt 100000 -or
        @($ConfiguredInputs).Count -lt 1 -or @($BuiltInputs).Count -lt 1) { throw 'WProto build preparation vectors must be nonempty bounded arrays.' }
    $scenes = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($scene in $Preparation.Scenes) {
        if ($scene -isnot [string] -or $scene -cnotmatch '^(Assets|Packages)/[^\\:<>"|?*\x00-\x1f]+\.unity$' -or
            @($scene.Split('/') | Where-Object { $_ -eq '' -or $_ -eq '.' -or $_ -eq '..' -or $_ -match '[. ]$' }).Count -ne 0 -or
            -not $scenes.Add($scene)) { throw 'WProto build preparation scene paths are invalid or repeated.' }
    }
    $inventories = @{}
    foreach ($name in @('configured', 'prepared', 'built')) {
        $values = switch ($name) { 'configured' { $ConfiguredInputs } 'prepared' { $Preparation.ProjectInputs } 'built' { $BuiltInputs } }
        $entries = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in @($values)) {
            if ($entry.Path -isnot [string] -or $entry.Path.Length -gt 1024 -or
                $entry.Path -cnotmatch '^(Assets|Packages|ProjectSettings)/[^\\:<>"|?*\x00-\x1f]+$' -or
                @($entry.Path.Split('/') | Where-Object { $_ -eq '' -or $_ -eq '.' -or $_ -eq '..' -or $_ -match '[. ]$' }).Count -ne 0 -or
                $entries.ContainsKey($entry.Path)) { throw 'WProto build preparation input paths are invalid or repeated.' }
            if ($entry.Kind -ceq 'file') {
                if (($entry.Length -isnot [int] -and $entry.Length -isnot [long]) -or $entry.Length -lt 0 -or
                    $entry.Sha256 -isnot [string] -or $entry.Sha256 -cnotmatch '^[a-f0-9]{64}$') { throw 'WProto build preparation file metadata is invalid.' }
            } elseif ($entry.Kind -ceq 'directory') {
                if ($null -ne $entry.Length -or $null -ne $entry.Sha256) { throw 'WProto build preparation directory metadata is invalid.' }
            } else { throw 'WProto build preparation input kind is invalid.' }
            $entries.Add($entry.Path, $entry)
        }
        $inventories[$name] = $entries
    }
    $configured = $inventories.configured
    $prepared = $inventories.prepared
    foreach ($entry in $configured.Values) {
        if (-not $prepared.ContainsKey($entry.Path)) { throw 'WProto build preparation removed a configured input.' }
        Assert-WProtoSnapshotInventory -Observed @($prepared[$entry.Path]) -Expected @($entry)
    }
    $newScenes = @($prepared.Values | Where-Object {
            -not $configured.ContainsKey($_.Path) -and $_.Path -cmatch '^Assets/InitTestScene[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\.unity$'
        })
    if ($newScenes.Count -ne 1) { throw 'WProto build preparation requires exactly one newly generated test scene.' }
    $scenePath = $newScenes[0].Path
    if (-not ($Preparation.Scenes -ccontains $scenePath) -or -not $prepared.ContainsKey($scenePath + '.meta') -or $configured.ContainsKey($scenePath + '.meta')) {
        throw 'WProto build preparation test scene or matching metadata is missing from the observed build.'
    }
    $allowed = @($scenePath, ($scenePath + '.meta'), 'ProjectSettings/SceneTemplateSettings.json', 'ProjectSettings/UnityHelpersSettings.asset')
    foreach ($entry in $prepared.Values) {
        if (-not $configured.ContainsKey($entry.Path) -and ($entry.Kind -cne 'file' -or $allowed -cnotcontains $entry.Path)) {
            throw 'WProto build preparation contains an unexpected added input.'
        }
    }
    # The receipt is observed before BuildPipeline. Later stages may not silently adopt changes.
    Assert-WProtoSnapshotInventory -Observed $BuiltInputs -Expected $Preparation.ProjectInputs
}

function Assert-WProtoPlayerProvenance {
    [CmdletBinding()]
    param([string]$Repository, [string]$Directory, [string]$Commit, [string]$UnityVersion)
    $reportFiles = @(Get-ChildItem -LiteralPath $Directory -Filter 'provenance.json' -File -Recurse)
    if ($reportFiles.Count -ne 1) { throw 'Exactly one WProto provenance report is required.' }
    $reportPath = $reportFiles[0].FullName
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json -ErrorAction Stop
    $declarationPath = Join-Path $reportFiles[0].DirectoryName 'declaration.json'
    $declaration = Get-Content -LiteralPath $declarationPath -Raw | ConvertFrom-Json -ErrorAction Stop
    $hash = (Get-FileHash -LiteralPath $declarationPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($report.CampaignKind -cne 'wproto-seven-families-v1' -or $declaration.CampaignKind -cne $report.CampaignKind -or
        $report.DeclarationSha256 -cne $hash -or $report.RunId -cne $declaration.RunId -or
        $declaration.SourceCommit -cne $Commit -or $report.SourceCommit -cne $Commit -or
        $declaration.UnityVersion -cne $UnityVersion -or $report.RequestedUnityVersion -cne $UnityVersion -or
        $declaration.Backend -cne 'Mono2x' -or $report.RequestedBackend -cne 'Mono2x' -or
        $report.AdoptionEligible -isnot [bool] -or $report.AdoptionEligible -or
        $report.FullQualification -isnot [bool] -or $report.FullQualification) {
        throw 'WProto report/declaration identity or qualification boundary is invalid.'
    }
    Assert-WProtoSourceCensus -Repository $Repository -DeclaredFiles $declaration.SourceFiles
    $stages = @($report.Snapshots | ForEach-Object { $_.Stage })
    if ($stages.Count -ne 5 -or ($stages -join '|') -cne 'before-configure|after-configure|after-build|before-launch|final' -or
        @($report.Snapshots | Where-Object { $_.State -cne 'passed' }).Count -ne 0 -or -not (Test-WProtoFrozenProcessHealth -Context $report)) {
        throw 'WProto source/build stages or observed process health are invalid.'
    }
    $reportDirectory = $reportFiles[0].DirectoryName
    if ($report.GeneratedRoot -cne 'retained/project-inputs' -or $report.PayloadRoot -cne 'retained/player' -or
        $report.PlayerLogPath -cne 'retained/corpus-records.txt' -or $report.ResultsPath -cne 'retained/results.xml' -or
        $report.BuildPreparationPath -cne 'retained/build-preparation.json' -or $report.RetainedEvidenceState -cne 'passed') { throw 'WProto retained evidence paths or state are invalid.' }
    $generatedRoot = Join-Path $reportDirectory $report.GeneratedRoot
    $payloadRoot = Join-Path $reportDirectory $report.PayloadRoot
    $playerLogPath = Join-Path $reportDirectory $report.PlayerLogPath
    $resultsPath = Join-Path $reportDirectory $report.ResultsPath
    $built = $report.Snapshots[2]
    if ($report.SourceFilesCount -ne @($declaration.SourceFiles).Count -or @($built.GeneratedAnchorFiles).Count -ne 7) {
        throw 'WProto snapshot source or generated anchor count is invalid.'
    }
    for ($index = 0; $index -lt $report.Snapshots.Count; $index++) {
        $snapshot = $report.Snapshots[$index]
        if ($snapshot.DeclarationSha256 -cne $hash) { throw 'WProto snapshot declaration identity differs.' }
        Assert-WProtoSnapshotInventory -Observed $snapshot.SourceFiles -Expected $declaration.SourceFiles -DeclaredHashes
        Assert-WProtoSnapshotInventory -Observed $snapshot.CorpusFiles -Expected $declaration.CorpusFiles -DeclaredHashes
        Assert-WProtoSnapshotInventory -Observed $snapshot.GeneratedAnchorFiles -Expected $built.GeneratedAnchorFiles
        if ($index -ge 2) { Assert-WProtoSnapshotInventory -Observed $snapshot.ProjectInputs -Expected $built.ProjectInputs }
        $expectedPayload = @(if ($index -ge 2) { $built.PayloadFiles })
        Assert-WProtoSnapshotInventory -Observed $snapshot.PayloadFiles -Expected $expectedPayload
    }
    Assert-WProtoRecordedInventory -Root $payloadRoot -Entries $built.PayloadFiles -Complete
    Assert-WProtoRecordedInventory -Root $generatedRoot -Entries $built.GeneratedAnchorFiles
    foreach ($directoryName in @('Assets', 'Packages', 'ProjectSettings')) {
        $entries = @($built.ProjectInputs | Where-Object { $_.Path.StartsWith($directoryName + '/', [StringComparison]::Ordinal) } | ForEach-Object {
                [pscustomobject]@{ Path = $_.Path.Substring($directoryName.Length + 1); Kind = $_.Kind; Length = $_.Length; Sha256 = $_.Sha256 }
            })
        Assert-WProtoRecordedInventory -Root (Join-Path $generatedRoot $directoryName) -Entries $entries -Complete
    }
    # EvidenceFiles are historical observations of support logs before redaction.
    # These sealed artifacts bind preparation, XML, and raw records independently of redacted logs.
    $retainedPaths = @($report.RetainedFiles | ForEach-Object { $_.Path })
    if ($retainedPaths.Count -ne 3 -or @($retainedPaths | Where-Object { $_ -ceq 'retained/build-preparation.json' }).Count -ne 1 -or @($retainedPaths | Where-Object { $_ -ceq 'retained/corpus-records.txt' }).Count -ne 1 -or
        @($retainedPaths | Where-Object { $_ -ceq 'retained/results.xml' }).Count -ne 1) { throw 'WProto retained metric/XML membership is invalid.' }
    Assert-WProtoRecordedInventory -Root $reportDirectory -Entries $report.RetainedFiles
    $preparationPath = Join-Path $reportDirectory 'retained/build-preparation.json'
    $preparationHash = (Get-FileHash -LiteralPath $preparationPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $preparation = Get-Content -LiteralPath $preparationPath -Raw | ConvertFrom-Json -ErrorAction Stop
    if ($report.BuildPreparationSha256 -isnot [string] -or $report.BuildPreparationSha256 -cne $preparationHash -or
        (ConvertTo-Json -InputObject $report.BuildPreparation -Depth 12 -Compress) -cne (ConvertTo-Json -InputObject $preparation -Depth 12 -Compress)) {
        throw 'WProto retained build preparation differs from its reported receipt.'
    }
    Assert-WProtoBuildPreparation -Preparation $preparation -ConfiguredInputs $report.Snapshots[1].ProjectInputs -BuiltInputs $built.ProjectInputs `
        -Declaration $declaration -DeclarationSha256 $hash
    $evidence = Read-TestResultEvidence -Path $resultsPath -Format NUnit
    $expected = @(Get-WProtoFrozenOperationNames | ForEach-Object {
            'WallstopStudios.UnityHelpers.Tests.Runtime.Performance.ProtoSerializationPerformanceTests.CompareFrozenSevenFamilyCorpus("' + $_ + '")'
        })
    if (@($evidence.Cases).Count -ne $expected.Count) { throw 'WProto requires exactly ten ordinary NUnit cases.' }
    foreach ($name in $expected) {
        $matches = @($evidence.Cases | Where-Object { $_.FullName -ceq $name -and $_.Result -ceq 'Passed' })
        if ($matches.Count -ne 1) { throw 'WProto XML is missing, duplicating, or skipping an operation.' }
    }
    $canonical = @($declaration.CorpusFiles | Where-Object { $_.Path -ceq 'scripts/unity/wproto-corpus.v1.json' })
    if ($canonical.Count -ne 1) { throw 'WProto canonical corpus declaration is missing.' }
    $context = [pscustomobject]@{
        Declaration = [pscustomobject]@{ Value = $declaration; Sha256 = $hash; CanonicalCorpus = $canonical[0] }
        RepoRoot = $Repository; Snapshots = $report.Snapshots
    }
    $join = Get-WProtoFrozenPlayerRuntimeJoin -Context $context -PlayerLogPath $playerLogPath -PrerequisitesPassed $true
    if ($join.State -cne 'passed') { throw "WProto runtime/corpus join rejected: $($join.Errors -join '; ')" }
    if ($report.CapturedEvidenceValid -isnot [bool] -or -not $report.CapturedEvidenceValid -or
        $report.SyntheticTimingQualified -isnot [bool] -or $report.SyntheticTimingQualified -ne $join.Summary.timingQualified -or
        $report.TimingTwofoldTarget -isnot [bool] -or $report.TimingTwofoldTarget -ne $join.Summary.timingTwofoldTarget) {
        throw 'WProto reported capture/timing disposition does not match recomputation.'
    }
    return $join.Summary
}
