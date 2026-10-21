#!/usr/bin/env pwsh
[CmdletBinding()]
param([switch]$RequireWindowsRegistry)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $repo 'scripts/unity/lib/native-global-flags.ps1')
. (Join-Path $repo 'scripts/unity/lib/native-evidence.ps1')
. (Join-Path $repo 'scripts/unity/lib/native-loader-flag-lease.ps1')
if ($RequireWindowsRegistry -and -not $IsWindows) { throw 'Windows registry controls were required' }
$source = Join-Path $repo 'scripts/unity/capture-native-fault.ps1'
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($source, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw ($errors | Out-String) }
foreach ($name in @('Invoke-NativeCaptureProcess', 'Get-NativeCaptureDebuggerCommandBudget', 'Get-NativeCaptureDebuggerCommand', 'Invoke-NativeStandaloneBuildCapture', 'Get-NativeCaptureLayout', 'Get-NativeCaptureExitEvent', 'Get-NativeCaptureXmlOutcome', 'Read-NativeCaptureTail', 'Test-NativeCaptureInputsUnchanged', 'Invoke-NativeEvidenceReadback')) {
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
    $readbackInput = Join-Path $root 'execution-diagnostics.json'
    $captureId = '0123456789abcdef0123456789abcdef'
    $captureDirectory = Join-Path $root $captureId
    $null = [IO.Directory]::CreateDirectory($captureDirectory)
    [IO.File]::WriteAllText((Join-Path $captureDirectory 'upm.log'), 'retained package log')
    $savedProcess = [ordered]@{
        State = 'observed'; MachineName = [Environment]::MachineName; ProcessId = 123
        ExecutablePath = (Join-Path $root 'missing-editor/Unity.exe')
        LaunchUtc = '2020-01-01T00:00:00.0000000Z'; EndObservedUtc = '2020-01-01T00:00:01.0000000Z'
        RawExitCode = -1073741819; ReturnedExitCode = -1073741819; ExitOrigin = 'process-exit'; KillReason = $null
        NativeEvidence = [ordered]@{ State = 'files-observed'; CaptureId = $captureId; CrashDirectory = $captureDirectory; UpmLogPath = (Join-Path $captureDirectory 'upm.log') }
    }
    $readbackJson = @{ Process = $savedProcess; CapturedUtc = '2020-01-01T00:00:01.0100000Z' } | ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText($readbackInput, $readbackJson)
    $originalHash = (Get-FileHash -LiteralPath $readbackInput -Algorithm SHA256).Hash.ToLowerInvariant()
    $originalEventCollector = ${function:Get-UnityProcessFailureEvents}
    $originalFileCollector = ${function:Get-UnityNativeEvidence}
    $originalCaptureProcess = ${function:Invoke-NativeCaptureProcess}
    $originalGlobalFlags = ${function:Get-NativeGlobalFlagSnapshot}
    $script:readbackEventCalls = 0
    $script:readbackFileCalls = 0
    $script:readbackEventIdentity = $null
    function Get-UnityProcessFailureEvents {
        param($ProcessObservation)
        $script:readbackEventCalls++
        $script:readbackEventIdentity = $ProcessObservation
        return @{ State = 'no-matching-events'; Events = @(); Qualification = 'No events observed by this controlled collector.' }
    }
    function Get-UnityNativeEvidence {
        param($Capture, $LogPath)
        $script:readbackFileCalls++
        return & $originalFileCollector -Capture $Capture -LogPath $LogPath
    }
    function Invoke-NativeCaptureProcess { throw 'Readback must not launch a capture process.' }
    function Get-NativeGlobalFlagSnapshot { throw 'Readback must not inspect registry settings.' }
    try {
        $output = Join-Path $root 'readback-success'
        $result = Invoke-NativeEvidenceReadback -ExecutionDiagnosticsPath $readbackInput -ExpectedMachine ([Environment]::MachineName) -ArtifactsPath $output
        Assert-Capture ($script:readbackEventCalls -eq 1 -and $script:readbackFileCalls -eq 1 -and $script:readbackEventIdentity.ProcessId -eq 123 -and
            $script:readbackEventIdentity.ExecutablePath -ceq $savedProcess.ExecutablePath -and $script:readbackEventIdentity.LaunchUtc -ceq $savedProcess.LaunchUtc -and
            $script:readbackEventIdentity.EndObservedUtc -ceq $savedProcess.EndObservedUtc) 'Readback collectors receive the saved process and capture identity'
        Assert-Capture ($result.EvidenceOnly -and $result.Mode -ceq 'historical-readback' -and $result.State -ceq 'completed' -and
            $result.WindowsEvents.State -ceq 'no-matching-events' -and $result.NativeEvidence.State -ceq 'files-observed' -and
            $result.NativeEvidence.CaptureId -ceq $captureId -and $result.NativeEvidence.Files.Count -eq 1) 'Matching-host readback retains observation states without acceptance claims'
        Assert-Capture ($result.OriginalSha256 -ceq $originalHash -and
            (Get-FileHash -LiteralPath $readbackInput -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $originalHash -and
            (Get-FileHash -LiteralPath (Join-Path $output 'execution-diagnostics.original.json') -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $originalHash) 'Readback preserves original bytes and binds its retained copy by hash'
        Assert-Capture ([DateTimeOffset]::Parse($result.StartedUtc) -le [DateTimeOffset]::Parse($result.NativeEvidence.CapturedUtc) -and
            [DateTimeOffset]::Parse($result.NativeEvidence.CapturedUtc) -le [DateTimeOffset]::Parse($result.EndedUtc) -and
            (Test-Path -LiteralPath (Join-Path $output 'receipt.json'))) 'Readback receipt records enclosing collection times'
        $controlledEventCollector = ${function:Get-UnityProcessFailureEvents}
        function Get-UnityProcessFailureEvents { param($ProcessObservation) throw 'controlled collection interruption' }
        $interruptedOutput = Join-Path $root 'readback-interrupted'
        $interrupted = $false
        try { $null = Invoke-NativeEvidenceReadback -ExecutionDiagnosticsPath $readbackInput -ExpectedMachine ([Environment]::MachineName) -ArtifactsPath $interruptedOutput }
        catch { $interrupted = $_.Exception.Message -ceq 'controlled collection interruption' }
        finally { ${function:Get-UnityProcessFailureEvents} = $controlledEventCollector }
        $partial = [IO.File]::ReadAllText((Join-Path $interruptedOutput 'receipt.json')) | ConvertFrom-Json -AsHashtable
        Assert-Capture ($interrupted -and $partial.EvidenceOnly -and $partial.State -ceq 'collecting' -and $null -eq $partial.EndedUtc -and
            $partial.OriginalProcess.ProcessId -eq 123 -and $partial.OriginalSha256 -ceq $originalHash -and
            (Get-FileHash -LiteralPath (Join-Path $interruptedOutput 'execution-diagnostics.original.json') -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $originalHash) 'Interrupted event collection retains the original process receipt and unfinished collection state'
        $eventsBefore = $script:readbackEventCalls
        $filesBefore = $script:readbackFileCalls
        $rejected = $false
        try { $null = Invoke-NativeEvidenceReadback -ExecutionDiagnosticsPath $readbackInput -ExpectedMachine ([Environment]::MachineName) -ArtifactsPath $output }
        catch { $rejected = $_.Exception.Message -like '*must be fresh*' }
        Assert-Capture ($rejected -and $script:readbackEventCalls -eq $eventsBefore -and $script:readbackFileCalls -eq $filesBefore -and
            (Get-FileHash -LiteralPath (Join-Path $output 'execution-diagnostics.original.json') -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $originalHash) 'Existing readback output is refused before collection and preserved'

        $nestedOutput = Join-Path $captureDirectory 'readback-output'
        $captureFilesBefore = @(Get-ChildItem -LiteralPath $captureDirectory -File -Recurse | ForEach-Object {
                $_.FullName + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            })
        $rejected = $false
        try { $null = Invoke-NativeEvidenceReadback -ExecutionDiagnosticsPath $readbackInput -ExpectedMachine ([Environment]::MachineName) -ArtifactsPath $nestedOutput }
        catch { $rejected = $_.Exception.Message -like '*outside the original native capture directory*' }
        $captureFilesAfter = @(Get-ChildItem -LiteralPath $captureDirectory -File -Recurse | ForEach-Object {
                $_.FullName + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            })
        Assert-Capture ($rejected -and $script:readbackEventCalls -eq $eventsBefore -and $script:readbackFileCalls -eq $filesBefore -and
            -not (Test-Path -LiteralPath $nestedOutput) -and $captureFilesBefore.Count -eq 1 -and
            ($captureFilesBefore -join '|') -ceq ($captureFilesAfter -join '|')) 'Readback output inside original capture is refused before collection without contaminating original evidence'

        foreach ($mutation in @('expected-host', 'saved-host', 'both-foreign-hosts', 'process-state', 'pid-zero', 'pid-string', 'pid-array', 'executable-relative',
                'invalid-time', 'reversed-time', 'future-time', 'numeric-time', 'exit-string', 'native-partial', 'native-id', 'native-path', 'native-upm', 'missing-native-paths', 'empty', 'malformed', 'oversize')) {
            $changed = $readbackJson | ConvertFrom-Json -AsHashtable
            $changed.Process.LaunchUtc = $savedProcess.LaunchUtc
            $changed.Process.EndObservedUtc = $savedProcess.EndObservedUtc
            $expectedMachine = [Environment]::MachineName
            switch ($mutation) {
                'expected-host' { $expectedMachine = 'different-readback-machine' }
                'saved-host' { $changed.Process.MachineName = 'different-readback-machine' }
                'both-foreign-hosts' { $expectedMachine = 'different-readback-machine'; $changed.Process.MachineName = $expectedMachine }
                'process-state' { $changed.Process.State = 'unavailable' }
                'pid-zero' { $changed.Process.ProcessId = 0 }
                'pid-string' { $changed.Process.ProcessId = '123' }
                'pid-array' { $changed.Process.ProcessId = @(123) }
                'executable-relative' { $changed.Process.ExecutablePath = 'Unity.exe' }
                'invalid-time' { $changed.Process.LaunchUtc = 'yesterday' }
                'reversed-time' { $changed.Process.LaunchUtc = '2020-01-02T00:00:00Z' }
                'future-time' { $changed.Process.EndObservedUtc = '9999-01-01T00:00:00Z' }
                'numeric-time' { $changed.Process.LaunchUtc = 1 }
                'exit-string' { $changed.Process.RawExitCode = '-1073741819' }
                'native-partial' { [void]$changed.Process.NativeEvidence.Remove('UpmLogPath') }
                'native-id' { $changed.Process.NativeEvidence.CaptureId = 'wrong' }
                'native-path' { $changed.Process.NativeEvidence.CrashDirectory = Join-Path $root 'another-directory' }
                'native-upm' { $changed.Process.NativeEvidence.UpmLogPath = Join-Path $root 'another.log' }
                'missing-native-paths' { $changed.Process.NativeEvidence = @{ State = 'files-observed' } }
            }
            $changedJson = $changed | ConvertTo-Json -Depth 8
            if ($mutation -eq 'empty') { $changedJson = '' }
            if ($mutation -eq 'malformed') { $changedJson = '{' }
            if ($mutation -eq 'oversize') { $changedJson = ' ' * 4194305 }
            [IO.File]::WriteAllText($readbackInput, $changedJson)
            $inputHash = (Get-FileHash -LiteralPath $readbackInput -Algorithm SHA256).Hash
            $eventsBefore = $script:readbackEventCalls
            $filesBefore = $script:readbackFileCalls
            $rejected = $false
            $negativeOutput = Join-Path $root ('readback-' + $mutation)
            try { $null = Invoke-NativeEvidenceReadback -ExecutionDiagnosticsPath $readbackInput -ExpectedMachine $expectedMachine -ArtifactsPath $negativeOutput }
            catch { $rejected = $true }
            Assert-Capture ($rejected -and $script:readbackEventCalls -eq $eventsBefore -and $script:readbackFileCalls -eq $filesBefore -and
                -not (Test-Path -LiteralPath $negativeOutput) -and (Get-FileHash -LiteralPath $readbackInput -Algorithm SHA256).Hash -ceq $inputHash) "Readback rejects $mutation before collection without changing input or creating output"
        }
        foreach ($unavailable in @('original-unavailable', 'missing-directory')) {
            $changed = $readbackJson | ConvertFrom-Json -AsHashtable
            $changed.Process.LaunchUtc = $savedProcess.LaunchUtc
            $changed.Process.EndObservedUtc = $savedProcess.EndObservedUtc
            if ($unavailable -eq 'original-unavailable') { $changed.Process.NativeEvidence = @{ State = 'capture-unavailable' } }
            else { Remove-Item -LiteralPath $captureDirectory -Recurse -Force }
            [IO.File]::WriteAllText($readbackInput, ($changed | ConvertTo-Json -Depth 8))
            $result = Invoke-NativeEvidenceReadback -ExecutionDiagnosticsPath $readbackInput -ExpectedMachine ([Environment]::MachineName) -ArtifactsPath (Join-Path $root $unavailable)
            Assert-Capture ($result.NativeEvidence.State -ceq 'capture-unavailable' -and $result.WindowsEvents.State -ceq 'no-matching-events' -and $result.EvidenceOnly) "Readback retains $unavailable without claiming the original process was healthy"
        }
    } finally {
        ${function:Get-UnityProcessFailureEvents} = $originalEventCollector
        ${function:Get-UnityNativeEvidence} = $originalFileCollector
        ${function:Invoke-NativeCaptureProcess} = $originalCaptureProcess
        ${function:Get-NativeGlobalFlagSnapshot} = $originalGlobalFlags
    }
    $cliOutput = Join-Path $root 'readback-cli'
    $cliRecord = & $source -ExecutionDiagnosticsPath $readbackInput -ExpectedMachine ([Environment]::MachineName) -ArtifactsPath $cliOutput
    Assert-Capture ($cliRecord.Mode -ceq 'historical-readback' -and $cliRecord.EvidenceOnly -and $cliRecord.State -ceq 'completed') 'Readback command completes without editor, project, assemblies, debugger download or process launch'
    $parameterSets = (Get-Command $source).ParameterSets
    $captureSet = @($parameterSets | Where-Object { $_.Name -ceq 'Capture' })
    Assert-Capture ($captureSet.Count -eq 1 -and $captureSet[0].IsDefault -and
        @($captureSet[0].Parameters | Where-Object { $_.IsMandatory -and $_.Name -in @('EditorPath', 'ProjectPath', 'ArtifactsPath', 'AssemblyNames') }).Count -eq 4) 'Capture remains the default parameter set with its original required inputs'

    $selectionRun = @{ id = 39132725629L; repository = @{ full_name = 'wallstop/unity-helpers' }; status = 'completed'; run_attempt = 1; head_sha = 'a' * 40 }
    $selectionArtifacts = @{ total_count = 2; artifacts = @(
            @{ id = 12139481462L; name = 'unity-6000.6.0f1-acceptance'; expired = $false; size_in_bytes = 100; workflow_run = @{ id = 39132725629L; head_sha = 'a' * 40 } },
            @{ id = 12138863641L; name = 'unity-6000.6.0f1-standalone'; expired = $false; size_in_bytes = 200; workflow_run = @{ id = 39132725629L; head_sha = 'a' * 40 } }
        ) }
    $oldRestFunction = Get-Item Function:Invoke-RestMethod -ErrorAction SilentlyContinue
    $script:selectionCalls = [Collections.Generic.List[object]]::new()
    function Invoke-RestMethod {
        param($Method, $Uri, $Headers, $TimeoutSec)
        $script:selectionCalls.Add(@{ Method = $Method; Uri = $Uri; Timeout = $TimeoutSec; Token = $Headers.Authorization })
        if ($script:selectionNetworkFailure) { throw 'controlled metadata unavailable' }
        if ($Uri.EndsWith('/artifacts?per_page=100')) { return $script:selectionArtifactsResponse }
        return $script:selectionRunResponse
    }
    try {
        foreach ($mutation in @('valid', 'wrong-run', 'string-run', 'wrong-repository', 'active-run', 'repeated-attempt', 'string-attempt', 'invalid-head',
                'missing-artifact', 'duplicate-artifact', 'expired-artifact', 'untyped-expired', 'artifact-run', 'artifact-head', 'artifact-id-string', 'artifact-size',
                'truncated-inventory', 'over-bound-inventory', 'string-count', 'network-failure', 'invalid-request')) {
            $script:selectionRunResponse = $selectionRun | ConvertTo-Json -Depth 8 | ConvertFrom-Json -AsHashtable
            $script:selectionArtifactsResponse = $selectionArtifacts | ConvertTo-Json -Depth 8 | ConvertFrom-Json -AsHashtable
            $script:selectionNetworkFailure = $false
            $script:selectionCalls.Clear()
            $request = '39132725629'
            switch ($mutation) {
                'wrong-run' { $script:selectionRunResponse.id++ }
                'string-run' { $script:selectionRunResponse.id = '39132725629' }
                'wrong-repository' { $script:selectionRunResponse.repository.full_name = 'other/repository' }
                'active-run' { $script:selectionRunResponse.status = 'in_progress' }
                'repeated-attempt' { $script:selectionRunResponse.run_attempt = 2 }
                'string-attempt' { $script:selectionRunResponse.run_attempt = '1' }
                'invalid-head' { $script:selectionRunResponse.head_sha = 'a' * 39 }
                'missing-artifact' { $script:selectionArtifactsResponse.artifacts[0].name = 'different' }
                'duplicate-artifact' { $script:selectionArtifactsResponse.artifacts += $script:selectionArtifactsResponse.artifacts[0]; $script:selectionArtifactsResponse.total_count = 3 }
                'expired-artifact' { $script:selectionArtifactsResponse.artifacts[0].expired = $true }
                'untyped-expired' { $script:selectionArtifactsResponse.artifacts[0].expired = 'false' }
                'artifact-run' { $script:selectionArtifactsResponse.artifacts[0].workflow_run.id++ }
                'artifact-head' { $script:selectionArtifactsResponse.artifacts[0].workflow_run.head_sha = 'b' * 40 }
                'artifact-id-string' { $script:selectionArtifactsResponse.artifacts[0].id = '12139481462' }
                'artifact-size' { $script:selectionArtifactsResponse.artifacts[0].size_in_bytes = -1 }
                'truncated-inventory' { $script:selectionArtifactsResponse.total_count = 3 }
                'over-bound-inventory' { $script:selectionArtifactsResponse.total_count = 101 }
                'string-count' { $script:selectionArtifactsResponse.total_count = '2' }
                'network-failure' { $script:selectionNetworkFailure = $true }
                'invalid-request' { $request = '../latest' }
            }
            $selectionOutput = Join-Path $root ('selection-' + $mutation)
            $rejected = $false
            try { $selected = Get-NativeReadbackArtifactSelection -Repository 'wallstop/unity-helpers' -SourceRunId $request -ArtifactsPath $selectionOutput -Token 'controlled-actions-token' }
            catch { $rejected = $true }
            $selectionText = [IO.File]::ReadAllText((Join-Path $selectionOutput 'selection.json'))
            $selectionReceipt = $selectionText | ConvertFrom-Json -AsHashtable
            Assert-Capture ($selectionReceipt.EvidenceOnly -and -not $selectionText.Contains('controlled-actions-token') -and
                [DateTimeOffset]::Parse($selectionReceipt.StartedUtc) -le [DateTimeOffset]::Parse($selectionReceipt.EndedUtc)) "Selection retains diagnostic identity and times without credentials: $mutation"
            if ($mutation -ceq 'valid') {
                Assert-Capture (-not $rejected -and $selected.State -ceq 'selected' -and $selected.RunId -eq 39132725629L -and $selected.RunAttempt -eq 1 -and
                    $selected.HeadSha -ceq ('a' * 40) -and $selected.AcceptanceArtifactId -eq 12139481462L -and $selected.StandaloneArtifactId -eq 12138863641L -and
                    $selected.Artifacts.Count -eq 2) 'Selection binds both exact artifact IDs to the requested completed run and head'
                Assert-Capture ($script:selectionCalls.Count -eq 2 -and $script:selectionCalls[0].Uri -ceq 'https://api.github.com/repos/wallstop/unity-helpers/actions/runs/39132725629' -and
                    $script:selectionCalls[1].Uri -ceq 'https://api.github.com/repos/wallstop/unity-helpers/actions/runs/39132725629/artifacts?per_page=100' -and
                    @($script:selectionCalls | Where-Object { $_.Method -cne 'Get' -or $_.Timeout -ne 30 -or $_.Token -cne 'Bearer controlled-actions-token' }).Count -eq 0) 'Selection performs only two bounded current-repository metadata GETs'
                $selectionHash = (Get-FileHash -LiteralPath (Join-Path $selectionOutput 'selection.json')).Hash
                $beforeCalls = $script:selectionCalls.Count
                $refusedExisting = $false
                try { $null = Get-NativeReadbackArtifactSelection -Repository 'wallstop/unity-helpers' -SourceRunId $request -ArtifactsPath $selectionOutput -Token 'controlled-actions-token' }
                catch { $refusedExisting = $_.Exception.Message -like '*must be fresh*' }
                Assert-Capture ($refusedExisting -and $script:selectionCalls.Count -eq $beforeCalls -and
                    (Get-FileHash -LiteralPath (Join-Path $selectionOutput 'selection.json')).Hash -ceq $selectionHash) 'Selection preserves an existing attempt without network access'
            } else {
                Assert-Capture ($rejected -and $selectionReceipt.State -ceq 'failed' -and -not [string]::IsNullOrWhiteSpace($selectionReceipt.Error)) "Selection refuses and retains $mutation"
                if ($mutation -ceq 'invalid-request') { Assert-Capture ($script:selectionCalls.Count -eq 0) 'Invalid requested run never invokes the API' }
            }
        }
    } finally {
        if ($null -eq $oldRestFunction) { Remove-Item Function:Invoke-RestMethod }
        else { Set-Item Function:Invoke-RestMethod -Value $oldRestFunction.ScriptBlock }
    }

    $sourceProject = Join-Path $root '6000.6.0f1-standalone'
    $sourcePackage = Join-Path $sourceProject 'Library/PackageCache/com.unity.test-framework@7a3849e09bd0'
    $sourceLaunchers = Join-Path $sourcePackage 'UnityEditor.TestRunner/TestLaunchers'
    $null = [IO.Directory]::CreateDirectory($sourceLaunchers)
    $sourceLog = Join-Path $root 'ordinary-unity.log'
    $sourceManifest = Join-Path $sourcePackage 'package.json'
    $sourceContext = Join-Path $sourceLaunchers 'PlayerLauncherContextSettings.cs'
    $sourceLauncher = Join-Path $sourceLaunchers 'PlayerLauncher.cs'
    $sourceLogText = "COMMAND LINE: $($savedProcess.ExecutablePath)`n{`"processId`":123,`"label`":`"controlled`"}`nSuccessfully changed project path to: $sourceProject`n  com.unity.test-framework@1.8.0 (location: $sourcePackage)`n"
    foreach ($mutation in @('valid', 'missing-source', 'oversize-source', 'wrong-manifest-version', 'wrong-manifest-name', 'malformed-manifest',
            'wrong-cache', 'wrong-log-version', 'duplicate-package', 'wrong-pid', 'wrong-editor', 'relative-project', 'output-in-project', 'existing-output')) {
        [IO.File]::WriteAllText($sourceManifest, '{"name":"com.unity.test-framework","version":"1.8.0"}')
        [IO.File]::WriteAllText($sourceContext, 'controlled context source')
        [IO.File]::WriteAllText($sourceLauncher, 'controlled launcher source')
        $logText = $sourceLogText
        $sourceOutput = Join-Path $root ('source-' + $mutation)
        switch ($mutation) {
            'missing-source' { Remove-Item -LiteralPath $sourceLauncher }
            'oversize-source' { [IO.File]::WriteAllText($sourceLauncher, ('x' * 262145)) }
            'wrong-manifest-version' { [IO.File]::WriteAllText($sourceManifest, '{"name":"com.unity.test-framework","version":"1.8.1"}') }
            'wrong-manifest-name' { [IO.File]::WriteAllText($sourceManifest, '{"name":"another-package","version":"1.8.0"}') }
            'malformed-manifest' { [IO.File]::WriteAllText($sourceManifest, '{') }
            'wrong-cache' { $logText = $logText.Replace('7a3849e09bd0', '000000000000') }
            'wrong-log-version' { $logText = $logText.Replace('@1.8.0 ', '@1.8.1 ') }
            'duplicate-package' { $logText += "  com.unity.test-framework@1.8.0 (location: $sourcePackage)`n" }
            'wrong-pid' { $logText = $logText.Replace('123,', '124,') }
            'wrong-editor' { $logText = $logText.Replace($savedProcess.ExecutablePath, 'another-editor') }
            'relative-project' { $logText = $logText.Replace("path to: $sourceProject", 'path to: 6000.6.0f1-standalone') }
            'output-in-project' { $sourceOutput = Join-Path $sourceProject 'output' }
            'existing-output' { $null = [IO.Directory]::CreateDirectory((Join-Path $sourceOutput 'test-framework-sources')) }
        }
        [IO.File]::WriteAllText($sourceLog, $logText)
        $beforeSourceFiles = @(Get-ChildItem -LiteralPath $sourcePackage -File -Recurse | Sort-Object FullName | ForEach-Object { $_.FullName + ':' + (Get-FileHash -LiteralPath $_.FullName).Hash })
        $sourceResult = $null
        $rejected = $false
        try { $sourceResult = Get-NativeTestFrameworkSources -LogPath $sourceLog -ProcessObservation ([hashtable]$savedProcess) -ArtifactsPath $sourceOutput }
        catch { $rejected = $true }
        $afterSourceFiles = @(Get-ChildItem -LiteralPath $sourcePackage -File -Recurse | Sort-Object FullName | ForEach-Object { $_.FullName + ':' + (Get-FileHash -LiteralPath $_.FullName).Hash })
        Assert-Capture (($beforeSourceFiles -join '|') -ceq ($afterSourceFiles -join '|')) "Package source collection preserves the original package: $mutation"
        if ($mutation -ceq 'valid') {
            Assert-Capture (-not $rejected -and $sourceResult.State -ceq 'observed' -and $sourceResult.Files.Count -eq 3 -and
                $sourceResult.Scope -ceq 'current-persistent-package-sources' -and -not $sourceResult.OriginalOwnedProjectBytesProven -and
                $sourceResult.PackageVersion -ceq '1.8.0' -and $sourceResult.CacheIdentity -ceq '7a3849e09bd0' -and
                $sourceResult.ObservedLogSha256 -ceq (Get-FileHash -LiteralPath $sourceLog -Algorithm SHA256).Hash.ToLowerInvariant()) 'Package copies bind the observed log and current persistent cache without claiming original owned bytes'
            foreach ($file in $sourceResult.Files) {
                $retainedSource = Join-Path $sourceOutput $file.SavedPath
                Assert-Capture ((Get-FileHash -LiteralPath $retainedSource -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $file.Sha256 -and
                    (Get-FileHash -LiteralPath $file.Path -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $file.Sha256 -and
                    (Get-Item -LiteralPath $retainedSource).Length -eq $file.Length) 'Each of the three bounded source copies preserves and hashes exact bytes'
            }
        } else {
            Assert-Capture ($rejected -or ($null -ne $sourceResult -and $sourceResult.State -ceq 'unavailable' -and $sourceResult.Files.Count -eq 0)) "Package source collection distinguishes unavailable or rejected $mutation"
            if ($mutation -cne 'existing-output') { Assert-Capture (-not (Test-Path -LiteralPath (Join-Path $sourceOutput 'test-framework-sources'))) "Invalid package creates no source output: $mutation" }
        }
    }

    $editorInput = Join-Path $root 'editor-input.txt'
    $lockInput = Join-Path $root 'package-lock-input.json'
    [IO.File]::WriteAllText($editorInput, 'editor')
    [IO.File]::WriteAllText($lockInput, '{}')
    $inputRecord = @{ EditorSha256 = (Get-FileHash $editorInput -Algorithm SHA256).Hash; PackagesLockSha256 = (Get-FileHash $lockInput -Algorithm SHA256).Hash }
    Assert-Capture (Test-NativeCaptureInputsUnchanged -EditorPath $editorInput -PackageLockPath $lockInput -Record $inputRecord) 'Comparison recognizes unchanged inputs'
    [IO.File]::WriteAllText($lockInput, '{"changed":true}')
    Assert-Capture (-not (Test-NativeCaptureInputsUnchanged -EditorPath $editorInput -PackageLockPath $lockInput -Record $inputRecord)) 'Comparison refuses package-lock drift'
    [IO.File]::WriteAllText($lockInput, '{}')
    [IO.File]::WriteAllText($editorInput, 'changed editor')
    Assert-Capture (-not (Test-NativeCaptureInputsUnchanged -EditorPath $editorInput -PackageLockPath $lockInput -Record $inputRecord)) 'Comparison refuses editor drift'
    Remove-Item -LiteralPath $lockInput
    Assert-Capture (-not (Test-NativeCaptureInputsUnchanged -EditorPath $editorInput -PackageLockPath $lockInput -Record $inputRecord)) 'Comparison refuses missing inputs'
    $flagCases = @(
        @{ Value = '0x00000002'; Kind = 'String'; DisabledHex = '00000000' }
        @{ Value = '2'; Kind = 'String'; DisabledHex = '00000000' }
        @{ Value = '00000012'; Kind = 'String'; DisabledHex = '00000010' }
        @{ Value = 'FFFFFFFF'; Kind = 'String'; DisabledHex = 'FFFFFFFD' }
        @{ Value = 2; Kind = 'DWord'; DisabledHex = '00000000' }
        @{ Value = 18; Kind = 'DWord'; DisabledHex = '00000010' }
        @{ Value = -1; Kind = 'DWord'; DisabledHex = 'FFFFFFFD' }
        @{ Value = ([int]::MinValue + 2); Kind = 'DWord'; DisabledHex = '80000000' }
    )
    foreach ($case in $flagCases) {
        $disabledValue = Get-NativeLoaderDisabledValue -Value $case.Value -Kind $case.Kind
        $disabled = ConvertTo-NativeGlobalFlagObservation -Value $disabledValue -Kind $case.Kind
        Assert-Capture ($disabled.State -eq 'observed' -and $disabled.Hex -ceq $case.DisabledHex) 'Flag computation matches independent exact-bit expectations'
    }
    if ($IsWindows) {
        $registryRoot = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryView]::Registry64)
        $registryPath = 'Software\UnityHelpersNativeFlagControls-' + [Guid]::NewGuid().ToString('N')
        $registryKey = $null
        try {
            $registryKey = $registryRoot.CreateSubKey($registryPath)
            foreach ($case in $flagCases) {
                $kind = [Microsoft.Win32.RegistryValueKind]::$($case.Kind)
                $registryKey.SetValue('GlobalFlag', $case.Value, $kind)
                $lease = New-NativeLoaderFlagLease -Key $registryKey
                Assert-Capture (Test-NativeLoaderFlagValue -Key $registryKey -Kind $kind -Value $case.Value) 'Preparation does not mutate a value'
                $leaseFile = Join-Path $root ([Guid]::NewGuid().ToString('N') + '.json')
                Disable-NativeLoaderFlag -Lease $lease -Key $registryKey -LeasePath $leaseFile
                $savedLease = Read-NativeLoaderFlagLease -Path $leaseFile
                $disabled = ConvertTo-NativeGlobalFlagObservation -Value $registryKey.GetValue('GlobalFlag') -Kind $case.Kind
                $original = ConvertTo-NativeGlobalFlagObservation -Value $case.Value -Kind $case.Kind
                Assert-Capture ($disabled.State -eq 'observed' -and -not $disabled.LoaderSnapsEnabled -and [Convert]::ToUInt32($disabled.Hex, 16) -eq ([Convert]::ToUInt32($original.Hex, 16) -band 4294967293u)) 'Disabling changes only the loader-snaps bit, including signed DWORD values'
                Restore-NativeLoaderFlag -Lease $savedLease -Key $registryKey
                Restore-NativeLoaderFlag -Lease $savedLease -Key $registryKey
                Assert-Capture ($savedLease.State -eq 'restored' -and (Test-NativeLoaderFlagValue -Key $registryKey -Kind $kind -Value $case.Value)) 'Restoration preserves exact string spelling, numeric bits and value kind; repeated restoration is safe'
                $registryKey.SetValue('GlobalFlag', (Get-NativeLoaderDisabledValue -Value $case.Value -Kind $case.Kind), $kind)
                $refused = $false
                try { Complete-NativeLoaderFlagLease -Lease $savedLease -Key $registryKey } catch { $refused = $true }
                Assert-Capture ($refused -and (Test-NativeLoaderFlagValue -Key $registryKey -Kind $kind -Value (Get-NativeLoaderDisabledValue -Value $case.Value -Kind $case.Kind))) 'Automatic cleanup preserves an external edit after restoration finished'
                $registryKey.SetValue('GlobalFlag', $case.Value, $kind)
                $beforeWrite = [IO.File]::ReadAllText($leaseFile)
                $refused = $false
                try { Save-NativeLoaderFlagLease -Lease $savedLease -Path $leaseFile } catch { $refused = $true }
                Assert-Capture ($refused -and [IO.File]::ReadAllText($leaseFile) -ceq $beforeWrite) 'Original lease receipt cannot be overwritten'
                $refused = $false
                try { Disable-NativeLoaderFlag -Lease $savedLease -Key $registryKey -LeasePath $leaseFile } catch { $refused = $true }
                Assert-Capture ($refused -and $savedLease.State -eq 'prepared' -and (Test-NativeLoaderFlagValue -Key $registryKey -Kind $kind -Value $case.Value)) 'A failed durable receipt write prevents the registry change'
            }
            $registryKey.SetValue('GlobalFlag', 2, [Microsoft.Win32.RegistryValueKind]::DWord)
            $lease = New-NativeLoaderFlagLease -Key $registryKey
            $registryKey.SetValue('GlobalFlag', 0, [Microsoft.Win32.RegistryValueKind]::DWord)
            $refused = $false
            try { Complete-NativeLoaderFlagLease -Lease $lease -Key $registryKey } catch { $refused = $true }
            Assert-Capture ($refused -and (Test-NativeLoaderFlagValue -Key $registryKey -Kind DWord -Value 0)) 'Automatic cleanup must preserve an external edit made before this lease writes anything'
            $registryKey.SetValue('GlobalFlag', 2, [Microsoft.Win32.RegistryValueKind]::DWord)
            $lease = New-NativeLoaderFlagLease -Key $registryKey
            foreach ($change in @(
                @{ Name = 'Machine'; Value = 'DifferentMachine' }
                @{ Name = 'RegistryKey'; Value = 'DifferentKey' }
                @{ Name = 'RegistryView'; Value = 'Registry32' }
                @{ Name = 'SchemaVersion'; Value = 2 }
                @{ Name = 'SchemaVersion'; Value = '1' }
                @{ Name = 'OriginalKind'; Value = 'QWord' }
                @{ Name = 'OriginalValue'; Value = 'invalid' }
                @{ Name = 'DisabledValue'; Value = 8 }
            )) {
                $damaged = $lease | ConvertTo-Json | ConvertFrom-Json
                $damaged.($change.Name) = $change.Value
                foreach ($operation in @('Disable-NativeLoaderFlag', 'Restore-NativeLoaderFlag')) {
                    $operationArguments = @{ Lease = $damaged; Key = $registryKey }
                    if ($operation -eq 'Disable-NativeLoaderFlag') { $operationArguments.LeasePath = Join-Path $root ([Guid]::NewGuid().ToString('N') + '.json') }
                    $refused = $false
                    try { & $operation @operationArguments } catch { $refused = $true }
                    Assert-Capture ($refused -and (Test-NativeLoaderFlagValue -Key $registryKey -Kind DWord -Value 2)) 'Invalid receipts cannot change registry values'
                }
            }
            $registryKey.SetValue('GlobalFlag', 6, [Microsoft.Win32.RegistryValueKind]::DWord)
            $refused = $false
            try { Disable-NativeLoaderFlag -Lease $lease -Key $registryKey -LeasePath (Join-Path $root 'changed-baseline.json') } catch { $refused = $true }
            Assert-Capture ($refused -and $registryKey.GetValue('GlobalFlag') -eq 6) 'A changed baseline is not overwritten'
            $registryKey.SetValue('GlobalFlag', 2, [Microsoft.Win32.RegistryValueKind]::DWord)
            Disable-NativeLoaderFlag -Lease $lease -Key $registryKey -LeasePath (Join-Path $root 'external-edit.json')
            $registryKey.SetValue('GlobalFlag', 8, [Microsoft.Win32.RegistryValueKind]::DWord)
            $refused = $false
            try { Restore-NativeLoaderFlag -Lease $lease -Key $registryKey } catch { $refused = $true }
            Assert-Capture ($refused -and $lease.State -eq 'conflict' -and $registryKey.GetValue('GlobalFlag') -eq 8) 'Concurrent edits are reported and retained'
            $registryKey.SetValue('GlobalFlag', '0x00000000', [Microsoft.Win32.RegistryValueKind]::String)
            $refused = $false
            try { Restore-NativeLoaderFlag -Lease $lease -Key $registryKey } catch { $refused = $true }
            Assert-Capture ($refused -and $registryKey.GetValueKind('GlobalFlag') -eq [Microsoft.Win32.RegistryValueKind]::String) 'A concurrent value-kind change is not overwritten'
            $registryKey.SetValue('GlobalFlag', 0, [Microsoft.Win32.RegistryValueKind]::DWord)
            Restore-NativeLoaderFlag -Lease $lease -Key $registryKey
            $readOnlyKey = $registryRoot.OpenSubKey($registryPath, $false)
            try {
                $refused = $false
                try { Disable-NativeLoaderFlag -Lease $lease -Key $readOnlyKey -LeasePath (Join-Path $root 'permission.json') } catch { $refused = $true }
                Assert-Capture ($refused -and (Test-NativeLoaderFlagValue -Key $registryKey -Kind DWord -Value 2)) 'Permission failure leaves the original value intact'
            } finally { $readOnlyKey.Dispose() }
            $permissionLease = Join-Path $root 'denied-open.json'
            $readOnlyKey = $registryRoot.OpenSubKey($registryPath, $false)
            try {
                $prepared = New-NativeLoaderFlagLease -Key $readOnlyKey
                Save-NativeLoaderFlagLease -Lease $prepared -Path ($permissionLease + '.prepared')
            } finally { $readOnlyKey.Dispose() }
            $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
            $restrictedSecurity = [Microsoft.Win32.RegistryAclExtensions]::GetAccessControl($registryKey)
            $denyWrite = [Security.AccessControl.RegistryAccessRule]::new($identity.User, [Security.AccessControl.RegistryRights]::SetValue, [Security.AccessControl.AccessControlType]::Deny)
            $restrictedSecurity.AddAccessRule($denyWrite)
            try {
                [Microsoft.Win32.RegistryAclExtensions]::SetAccessControl($registryKey, $restrictedSecurity)
                $deniedKey = $null
                $refused = $false
                try { $deniedKey = $registryRoot.OpenSubKey($registryPath, $true) } catch { $refused = $true }
                finally { if ($null -ne $deniedKey) { $deniedKey.Dispose() } }
                Assert-Capture ($refused) 'A real registry ACL denies opening a writable handle'
                foreach ($state in @('prepared', 'clear-requested', 'restored')) {
                    $recovered = Read-NativeLoaderFlagRecoveryLease -Path $permissionLease
                    $recovered.State = $state
                    Invoke-NativeLoaderFlagRecovery -BaseKey $registryRoot -KeyPath $registryPath -Lease $recovered -Apply
                    Assert-Capture (Test-NativeLoaderFlagValue -Key $registryKey -Kind DWord -Value 2) 'Exact unchanged recovery succeeds without registry write permission in every valid lease phase'
                }
                $registryKey.SetValue('GlobalFlag', 8, [Microsoft.Win32.RegistryValueKind]::DWord)
                $recovered = Read-NativeLoaderFlagRecoveryLease -Path $permissionLease
                $refused = $false
                try { Invoke-NativeLoaderFlagRecovery -BaseKey $registryRoot -KeyPath $registryPath -Lease $recovered -Apply } catch { $refused = $true }
                Assert-Capture ($refused -and (Test-NativeLoaderFlagValue -Key $registryKey -Kind DWord -Value 8)) 'Read-only recovery refuses a conflicting value without requesting a write'
                $registryKey.SetValue('GlobalFlag', 0, [Microsoft.Win32.RegistryValueKind]::DWord)
                $recovered.State = 'clear-requested'
                $refused = $false
                try { Invoke-NativeLoaderFlagRecovery -BaseKey $registryRoot -KeyPath $registryPath -Lease $recovered -Apply } catch { $refused = $true }
                Assert-Capture ($refused -and (Test-NativeLoaderFlagValue -Key $registryKey -Kind DWord -Value 0)) 'A real restoration still requires write permission and cannot claim success when denied'
            } finally {
                $restrictedSecurity.RemoveAccessRuleSpecific($denyWrite)
                [Microsoft.Win32.RegistryAclExtensions]::SetAccessControl($registryKey, $restrictedSecurity)
                $identity.Dispose()
            }
            Invoke-NativeLoaderFlagRecovery -BaseKey $registryRoot -KeyPath $registryPath -Lease $recovered -Apply
            Assert-Capture (Test-NativeLoaderFlagValue -Key $registryKey -Kind DWord -Value 2) 'Active recovery restores the exact original value once write permission is available'
            $interrupted = Join-Path $root 'interrupted.ps1'
            $interruptedLease = Join-Path $root 'interrupted-lease.json'
            [IO.File]::WriteAllText($interrupted, @'
param($LibraryRoot, $KeyPath, $LeasePath)
$ErrorActionPreference = 'Stop'
. (Join-Path $LibraryRoot 'native-global-flags.ps1')
. (Join-Path $LibraryRoot 'native-loader-flag-lease.ps1')
$base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryView]::Registry64)
$key = $base.OpenSubKey($KeyPath, $true)
$lease = New-NativeLoaderFlagLease -Key $key
Save-NativeLoaderFlagLease -Lease $lease -Path ($LeasePath + '.prepared')
Disable-NativeLoaderFlag -Lease $lease -Key $key -LeasePath $LeasePath
$key.Dispose()
$base.Dispose()
exit 7
'@)
            $interruptedProcess = @{}
            Invoke-NativeCaptureProcess -FilePath (Get-Process -Id $PID).Path -Arguments @('-NoProfile', '-File', $interrupted, (Join-Path $repo 'scripts/unity/lib'), $registryPath, $interruptedLease) -Directory $root -Seconds 20 -Observation $interruptedProcess | Out-Null
            Assert-Capture ($interruptedProcess.Reaped -and $interruptedProcess.RawExitCode -eq 7 -and (Test-NativeLoaderFlagValue -Key $registryKey -Kind DWord -Value 0)) 'An exited process leaves a real disabled value and a durable original lease'
            $activeReceipt = [IO.File]::ReadAllBytes($interruptedLease)
            [IO.File]::WriteAllText($interruptedLease, '{}')
            $refused = $false
            try { Read-NativeLoaderFlagRecoveryLease -Path $interruptedLease | Out-Null } catch { $refused = $true }
            Assert-Capture ($refused -and (Test-NativeLoaderFlagValue -Key $registryKey -Kind DWord -Value 0)) 'Damaged post-write evidence fails without a fallback or registry change'
            Remove-Item -LiteralPath $interruptedLease
            $unprovenLease = Read-NativeLoaderFlagRecoveryLease -Path $interruptedLease
            $refused = $false
            try { Complete-NativeLoaderFlagLease -Lease $unprovenLease -Key $registryKey } catch { $refused = $true }
            Assert-Capture ($refused -and (Test-NativeLoaderFlagValue -Key $registryKey -Kind DWord -Value 0)) 'Missing post-write evidence cannot use a prepared receipt to restore a changed value'
            [IO.File]::WriteAllBytes($interruptedLease, $activeReceipt)
            $recoveredLease = Read-NativeLoaderFlagRecoveryLease -Path $interruptedLease
            Complete-NativeLoaderFlagLease -Lease $recoveredLease -Key $registryKey
            Assert-Capture (Test-NativeLoaderFlagValue -Key $registryKey -Kind DWord -Value 2) 'A later process can restore the exact original value from the durable lease'
            $beforeWriteScript = Join-Path $root 'before-write.ps1'
            [IO.File]::WriteAllText($beforeWriteScript, @'
param($LibraryRoot, $KeyPath, $LeasePath, $StopMode)
$ErrorActionPreference = 'Stop'
. (Join-Path $LibraryRoot 'native-global-flags.ps1')
. (Join-Path $LibraryRoot 'native-loader-flag-lease.ps1')
$base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryView]::Registry64)
$key = $base.OpenSubKey($KeyPath, $true)
$lease = New-NativeLoaderFlagLease -Key $key
Save-NativeLoaderFlagLease -Lease $lease -Path ($LeasePath + '.prepared')
$key.Dispose()
$base.Dispose()
if ($StopMode -eq 'failure') { throw 'Deliberate failure before a flag write' }
Start-Sleep -Seconds 300
'@)
            foreach ($stopMode in @('failure', 'cancellation')) {
                $beforeWriteLease = Join-Path $root ($stopMode + '-lease.json')
                $beforeWriteProcess = @{}
                Invoke-NativeCaptureProcess -FilePath (Get-Process -Id $PID).Path -Arguments @('-NoProfile', '-File', $beforeWriteScript, (Join-Path $repo 'scripts/unity/lib'), $registryPath, $beforeWriteLease, $stopMode) -Directory $root -Seconds 5 -Observation $beforeWriteProcess | Out-Null
                Assert-Capture ($beforeWriteProcess.Reaped -and -not (Test-Path -LiteralPath $beforeWriteLease) -and (Test-Path -LiteralPath ($beforeWriteLease + '.prepared'))) 'A stopped pre-write process retains original evidence without an active write lease'
                if ($stopMode -eq 'failure') {
                    Assert-Capture ($beforeWriteProcess.RawExitCode -ne 0 -and $null -eq $beforeWriteProcess.KillReason) 'A deliberate pre-write failure exits naturally'
                } else {
                    Assert-Capture ($beforeWriteProcess.KillReason -ceq 'wall-clock-timeout') 'Cancellation kills and reaps the waiting pre-write process'
                }
                $preparedLease = Read-NativeLoaderFlagRecoveryLease -Path $beforeWriteLease
                Complete-NativeLoaderFlagLease -Lease $preparedLease -Key $registryKey
                Assert-Capture (Test-NativeLoaderFlagValue -Key $registryKey -Kind DWord -Value 2) 'Recovery after a pre-write stop verifies the unchanged original value'
                foreach ($conflict in @(0, 8)) {
                    $registryKey.SetValue('GlobalFlag', $conflict, [Microsoft.Win32.RegistryValueKind]::DWord)
                    $preparedLease = Read-NativeLoaderFlagRecoveryLease -Path $beforeWriteLease
                    $refused = $false
                    try { Complete-NativeLoaderFlagLease -Lease $preparedLease -Key $registryKey } catch { $refused = $true }
                    Assert-Capture ($refused -and (Test-NativeLoaderFlagValue -Key $registryKey -Kind DWord -Value $conflict)) 'A prepared receipt cannot restore an externally changed or unproven disabled value'
                }
                $registryKey.SetValue('GlobalFlag', 2, [Microsoft.Win32.RegistryValueKind]::DWord)
            }
            $registryKey.SetValue('GlobalFlag', 0, [Microsoft.Win32.RegistryValueKind]::DWord)
            $refused = $false
            try { New-NativeLoaderFlagLease -Key $registryKey | Out-Null } catch { $refused = $true }
            Assert-Capture ($refused) 'An already-disabled flag is refused before comparison'
            $registryKey.SetValue('GlobalFlag', 2L, [Microsoft.Win32.RegistryValueKind]::QWord)
            $refused = $false
            try { New-NativeLoaderFlagLease -Key $registryKey | Out-Null } catch { $refused = $true }
            Assert-Capture ($refused) 'Unsupported registry kinds cannot prepare a comparison'
            $registryKey.DeleteValue('GlobalFlag')
            $refused = $false
            try { New-NativeLoaderFlagLease -Key $registryKey | Out-Null } catch { $refused = $true }
            Assert-Capture ($refused) 'Missing registry values cannot prepare a comparison'
        } finally {
            if ($null -ne $registryKey) { $registryKey.Dispose() }
            try { $registryRoot.DeleteSubKeyTree($registryPath, $false) } finally { $registryRoot.Dispose() }
        }
    } else { Write-Host 'Windows registry transaction controls are not run on this platform; require them separately on Windows.' }
    $envelopeFile = Join-Path $root 'envelope.json'
    $sampleLease = [ordered]@{ SchemaVersion = 1; Machine = 'Sample'; RegistryKey = 'Sample'; OriginalKind = 'String'; OriginalValue = '0x00000002'; DisabledValue = '0x00000000'; State = 'prepared' }
    Save-NativeLoaderFlagLease -Lease $sampleLease -Path $envelopeFile
    $recoveryPath = Join-Path $root 'recovery.json'
    Save-NativeLoaderFlagLease -Lease $sampleLease -Path ($recoveryPath + '.prepared')
    Assert-Capture ((Read-NativeLoaderFlagRecoveryLease -Path $recoveryPath).State -ceq 'prepared') 'Recovery reads the prepared receipt when no write lease exists'
    foreach ($badPath in @($recoveryPath, ($recoveryPath + '.prepared'))) {
        [IO.File]::WriteAllText($badPath, '{}')
        $refused = $false
        try { Read-NativeLoaderFlagRecoveryLease -Path $recoveryPath | Out-Null } catch { $refused = $true }
        Assert-Capture ($refused) 'A damaged active or prepared receipt cannot fall back to a successful recovery'
        Remove-Item -LiteralPath $badPath
    }
    $refused = $false
    try { Read-NativeLoaderFlagRecoveryLease -Path $recoveryPath | Out-Null } catch { $refused = $true }
    Assert-Capture ($refused) 'Absent recovery evidence cannot prove unchanged state'
    $sampleLease.State = 'clear-requested'
    Save-NativeLoaderFlagLease -Lease $sampleLease -Path ($recoveryPath + '.prepared')
    $refused = $false
    try { Read-NativeLoaderFlagRecoveryLease -Path $recoveryPath | Out-Null } catch { $refused = $true }
    Assert-Capture ($refused) 'The fallback receipt must prove the pre-write phase'
    Save-NativeLoaderFlagLease -Lease $sampleLease -Path $recoveryPath
    Assert-Capture ((Read-NativeLoaderFlagRecoveryLease -Path $recoveryPath).State -ceq 'clear-requested') 'An active write lease takes precedence over the prepared receipt'
    $envelope = Get-Content -LiteralPath $envelopeFile -Raw | ConvertFrom-Json
    Assert-Capture ((Read-NativeLoaderFlagLease -Path $envelopeFile).OriginalValue -ceq '0x00000002') 'Checksummed receipt round-trips exact original data'
    foreach ($bad in @(
        ($envelope | ConvertTo-Json).Replace($envelope.Sha256, ('0' * 64))
        ($envelope | ConvertTo-Json).Replace($envelope.Data, '%%%')
        ($envelope | ConvertTo-Json).Replace('"Version": 1', '"Version": 2')
        ('x' * 65537)
        '{}'
    )) {
        [IO.File]::WriteAllText($envelopeFile, $bad)
        $refused = $false
        try { Read-NativeLoaderFlagLease -Path $envelopeFile | Out-Null } catch { $refused = $true }
        Assert-Capture ($refused) 'Damaged, unsupported and excessive receipts are rejected before restoration'
    }
    foreach ($case in @(
        @{ Value = 0; Kind = 'DWord'; Hex = '00000000'; Enabled = $false }
        @{ Value = 2; Kind = 'DWord'; Hex = '00000002'; Enabled = $true }
        @{ Value = 16; Kind = 'DWord'; Hex = '00000010'; Enabled = $false }
        @{ Value = -1; Kind = 'DWord'; Hex = 'FFFFFFFF'; Enabled = $true }
        @{ Value = [int]::MinValue; Kind = 'DWord'; Hex = '80000000'; Enabled = $false }
        @{ Value = '0x00000002'; Kind = 'String'; Hex = '00000002'; Enabled = $true }
        @{ Value = 'aBcDeF00'; Kind = 'String'; Hex = 'ABCDEF00'; Enabled = $false }
        @{ Value = '00000002'; Kind = 'String'; Hex = '00000002'; Enabled = $true }
    )) {
        $flags = ConvertTo-NativeGlobalFlagObservation -Value $case.Value -Kind $case.Kind
        Assert-Capture ($flags.State -eq 'observed' -and $flags.Hex -eq $case.Hex -and $flags.LoaderSnapsEnabled -eq $case.Enabled) 'Registry flag types preserve all bits and distinguish loader snaps'
    }
    foreach ($case in @(
        @{ Value = '100000000'; Kind = 'String' }
        @{ Value = '0x'; Kind = 'String' }
        @{ Value = 'garbage'; Kind = 'String' }
        @{ Value = ' 2'; Kind = 'String' }
        @{ Value = ''; Kind = 'String' }
        @{ Value = $null; Kind = 'DWord' }
        @{ Value = 2L; Kind = 'QWord' }
        @{ Value = @('2'); Kind = 'MultiString' }
        @{ Value = '%TEMP%'; Kind = 'ExpandString' }
    )) {
        $flags = ConvertTo-NativeGlobalFlagObservation -Value $case.Value -Kind $case.Kind
        Assert-Capture ($flags.State -eq 'invalid' -and $null -eq $flags.Hex -and $null -eq $flags.LoaderSnapsEnabled) 'Invalid and unsupported registry values cannot claim disabled tracing'
    }
    $flags = Get-NativeGlobalFlagSnapshot
    Assert-Capture ($flags.Settings.Count -eq 2 -and $flags.Scope -eq 'registry-settings-only') 'Registry observations cover system and Unity settings without claiming process flags'
    foreach ($setting in $flags.Settings) {
        Assert-Capture ($setting.State -in @('observed', 'absent', 'unavailable', 'invalid')) 'Every registry read has an explicit state'
        if ($setting.State -ne 'observed') {
            Assert-Capture ($null -eq $setting.LoaderSnapsEnabled) 'Missing or inaccessible settings cannot claim disabled tracing'
        }
    }
    $missing = Get-NativeGlobalFlagRegistryObservation -Subject 'MissingControl' -KeyPath ('SOFTWARE\unity-helpers-missing-' + [Guid]::NewGuid().ToString('N'))
    Assert-Capture ($missing.State -eq $(if ($IsWindows) { 'absent' } else { 'unavailable' }) -and $null -eq $missing.LoaderSnapsEnabled) 'Real missing key and unsupported platform remain distinct'
    $captureSource = [IO.File]::ReadAllText($source)
    Assert-Capture ($captureSource.Contains('GlobalFlagRegistry = Get-NativeGlobalFlagSnapshot') -and $captureSource.Contains('WUH_EXIT_REQUEST;!gflag;r rcx')) 'Receipt includes registry settings and exit breakpoint observes effective target flags'
    $runnerAction = [IO.File]::ReadAllText((Join-Path $repo '.github/actions/print-self-hosted-runner-diagnostics/action.yml'))
    Assert-Capture ($runnerAction.Contains('. ./scripts/unity/lib/native-global-flags.ps1') -and $runnerAction.Contains('Get-NativeGlobalFlagSnapshot | ConvertTo-Json')) 'Regular runner diagnostics use the same read-only snapshot'
    $layout = Get-NativeCaptureLayout -Root (Join-Path $root 'layout')
    foreach ($directory in $layout.Values) { [void](New-Item -ItemType Directory -Path $directory) }
    Assert-Capture (@(Get-ChildItem -LiteralPath (Join-Path $root 'layout') -Directory).Count -eq 3) 'Default tool and capture directories coexist without collisions'
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
    foreach ($bad in @('<test-run />', '<test-run><test-case', '<test-run><test-case fullname="Other.Case" result="Passed" /></test-run>', '<!DOCTYPE x [<!ENTITY secret SYSTEM "file:///unavailable">]><test-run>&secret;</test-run>')) {
        [IO.File]::WriteAllText($xml, $bad)
        $outcome = Get-NativeCaptureXmlOutcome $xml 'Fixture.Case'
        Assert-Capture ($outcome.State -eq 'invalid') 'Invalid or unrelated XML is recorded without interrupting evidence capture'
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

    $originalSymbolPath = [Environment]::GetEnvironmentVariable('_NT_SYMBOL_PATH', 'Process')
    try {
        $env:_NT_SYMBOL_PATH = 'native-build-symbol-canary'
        [IO.File]::WriteAllText($child, '[Console]::Write($env:_NT_SYMBOL_PATH + "|" + [Environment]::CurrentDirectory)')
        $preserved = @{}
        $output = Invoke-NativeCaptureProcess -FilePath (Get-Process -Id $PID).Path -Arguments @('-NoProfile', '-File', $child) -Directory $root -Seconds 20 -Observation $preserved -PreserveSymbolPath
        Assert-Capture ($output -ceq ('native-build-symbol-canary|' + $root) -and $preserved.RawExitCode -eq 0) 'Build capture preserves inherited symbol environment and requested working directory'
    } finally {
        [Environment]::SetEnvironmentVariable('_NT_SYMBOL_PATH', $originalSymbolPath, 'Process')
    }

    Assert-Capture ((Get-NativeCaptureDebuggerCommand -DumpPath 'C:\capture with spaces\fault.dmp').Contains('\"C:\\capture with spaces\\fault.dmp\"')) 'CDB command-string parsing preserves quoted Windows dump paths and separators'
    $debuggerCommand = Get-NativeCaptureDebuggerCommand
    Assert-Capture ($debuggerCommand.Contains('sx- -c ".if (@$t0 < 8)') -and $debuggerCommand.Contains('r @$t0=0;') -and $debuggerCommand.Contains('WUH_FIRST_CHANCE_AV') -and $debuggerCommand.Contains('r @$t0=@$t0+1')) 'First-chance AV records have an eight-event bound without changing break or handling policy'
    Assert-Capture ($debuggerCommand.Contains('sxd -c2 ".exr -1;r;kv;lm;.dump /m fault.dmp;gn" av') -and $debuggerCommand.Contains('sx;.echo WUH_EXCEPTION_FILTERS_END')) 'Retain the second-chance dump policy and print actual exception filters before continuation'
    Assert-Capture ($debuggerCommand.Contains('bu ntdll!NtTerminateProcess ".echo WUH_TERMINATE_REQUEST;r rcx,rdx;kv20;g"')) 'Capture the local native termination request status and caller stack'
    Assert-Capture ($debuggerCommand.Contains('bu ntdll!RtlExitUserThread ".echo WUH_THREAD_EXIT;?@$tid;r rcx;kv20;~;.echo WUH_THREAD_EXIT_END;g"')) 'Record actual thread-exit status, OS thread identity, caller stack and stopped thread list'
    Assert-Capture ($debuggerCommand.Contains('bu ntdll!NtTerminateThread ".echo WUH_THREAD_TERMINATE;?@$tid;r rcx,rdx;kv20;~;.echo WUH_THREAD_TERMINATE_END;g"')) 'Record native thread-termination handle and status without treating a process exit code as an exception'
    Assert-Capture ($debuggerCommand.EndsWith(';.echo WUH_BREAKPOINTS_BEGIN;bl /L;.echo WUH_BREAKPOINTS_END;g')) 'Print process and thread breakpoint state before resuming'
    $originalBuildCaptureProcess = ${function:Invoke-NativeCaptureProcess}
    $script:buildCaptureCalls = @()
    $script:buildSavedRecords = @()
    $script:buildControllerExit = 0
    $script:buildKillReason = $null
    $script:buildThrowOnLaunch = $false
    $script:buildTargetText = 'Last event: 2a.1: Exit process 0:2a, code c0000005'
    function Save-NativeCaptureRecord { $script:buildSavedRecords += ($Record | ConvertTo-Json -Depth 12 -Compress) }
    function Invoke-NativeCaptureProcess {
        param($FilePath, $Arguments, $Directory, $Seconds, $Observation, $ReceiptPath, [switch]$PreserveSymbolPath)
        $script:buildCaptureCalls += @{ FilePath = $FilePath; Arguments = @($Arguments); Directory = $Directory; Seconds = $Seconds; PreserveSymbolPath = [bool]$PreserveSymbolPath }
        if ($script:buildThrowOnLaunch) { throw 'Controlled debugger launch failure.' }
        $Observation.RawExitCode = $script:buildControllerExit
        $Observation.Reaped = $true
        $Observation.KillReason = $script:buildKillReason
        $Observation.Pid = 888
        return $script:buildTargetText
    }
    try {
        $project = Join-Path $root 'project with spaces'
        $buildArguments = @('-batchmode', '-nographics', '-projectPath', $project, '-runTests', '-testPlatform', 'StandaloneWindows64', '-assemblyNames', 'Exact.Assembly', '-testFilter', 'Exact.Case', '-logFile', '-')
        $buildPrivate = Join-Path $root 'build capture private'
        $buildRecord = [ordered]@{ State = 'preparing'; Cases = @(); TestFilter = 'old-editmode-case' }
        Invoke-NativeStandaloneBuildCapture -EditorPath 'exact-editor.exe' -ProjectPath $project -Arguments $buildArguments -DebuggerPath 'pinned-cdb.exe' -PrivateDirectory $buildPrivate -ArtifactsPath $root -LogPath (Join-Path $root 'build.log') -Seconds 123 -Record $buildRecord
        Assert-Capture ($script:buildCaptureCalls.Count -eq 1 -and $buildRecord.Cases.Count -eq 1 -and $buildRecord.State -ceq 'completed') 'Build capture launches exactly one debugger build'
        $call = $script:buildCaptureCalls[0]
        $forwarded = @($call.Arguments | Select-Object -Skip 3)
        Assert-Capture (($forwarded[0..($buildArguments.Count - 1)] -join [char]0) -ceq ($buildArguments -join [char]0) -and $forwarded.Count -eq $buildArguments.Count + 4) 'Every existing build argument is forwarded in order without replacement'
        Assert-Capture ($call.Arguments[0] -ceq '-c' -and $call.Arguments[2] -ceq 'exact-editor.exe' -and $call.FilePath -ceq 'pinned-cdb.exe') 'Debugger wraps the selected editor once'
        Assert-Capture ($call.Directory -ceq [Environment]::CurrentDirectory -and $call.PreserveSymbolPath -and $call.Seconds -eq 123) 'Build debugger retains caller working directory and symbol environment with the existing timeout'
        Assert-Capture ($call.Arguments[1].Contains('\"' + (Join-Path $buildPrivate 'fault.dmp').Replace('\', '\\') + '\"')) 'Debugger dump stays in the private folder when the working directory is preserved'
        Assert-Capture ($buildRecord.Cases[0].Process.Pid -eq 888 -and $buildRecord.Cases[0].Process.RawExitCode -eq 0 -and $buildRecord.Cases[0].TargetExitEvent.Pid -eq 42 -and $buildRecord.Cases[0].TargetExitEvent.ExitCodeHex -ceq 'C0000005') 'Debugger controller success cannot replace the target access violation'
        Assert-Capture ($null -eq $buildRecord.TestFilter -and $buildRecord.Mode -ceq 'standalone-build' -and $buildRecord.ConsoleScope -ceq 'bounded-debugger-and-Unity-console-tail') 'Build record does not claim the unrelated default EditMode selection or a full console log'
        Assert-Capture ($script:buildSavedRecords.Count -ge 3 -and (Test-Path -LiteralPath (Join-Path $root 'build.log'))) 'Build capture persists preparation and completed evidence'
        $script:buildControllerExit = -1073741819
        $matchingRecord = [ordered]@{ State = 'preparing'; Cases = @(); TestFilter = $null }
        Invoke-NativeStandaloneBuildCapture -EditorPath 'exact-editor.exe' -ProjectPath $project -Arguments $buildArguments -DebuggerPath 'pinned-cdb.exe' -PrivateDirectory (Join-Path $root 'matching-target-code') -ArtifactsPath $root -LogPath (Join-Path $root 'matching-target-code.log') -Seconds 123 -Record $matchingRecord
        Assert-Capture ($matchingRecord.State -ceq 'completed' -and $matchingRecord.Cases[0].TargetExitCode -eq -1073741819 -and $matchingRecord.Cases[0].ControllerExitRelation -ceq 'matches-observed-target') 'A matching nonzero controller code preserves the observed target fault independently'
        $standardRecord = [ordered]@{ State = 'preparing'; Cases = @(); TestFilter = $null }
        $standardPrivate = Join-Path $root 'standard heap private'
        $originalNoDebugHeap = [Environment]::GetEnvironmentVariable('_NO_DEBUG_HEAP', 'Process')
        Invoke-NativeStandaloneBuildCapture -EditorPath 'exact-editor.exe' -ProjectPath $project -Arguments $buildArguments -DebuggerPath 'pinned-cdb.exe' -PrivateDirectory $standardPrivate -ArtifactsPath $root -LogPath (Join-Path $root 'standard-heap.log') -Seconds 123 -Record $standardRecord -UseStandardHeap
        $standardCall = $script:buildCaptureCalls[-1]
        Assert-Capture ($standardCall.Arguments[0] -ceq '-hd' -and $standardCall.Arguments[1] -ceq '-c' -and $standardCall.Arguments[3] -ceq 'exact-editor.exe') 'Standard heap selection changes only CDB launch options'
        $standardForwarded = @($standardCall.Arguments | Select-Object -Skip 4)
        Assert-Capture (($standardForwarded[0..($buildArguments.Count - 1)] -join [char]0) -ceq ($buildArguments -join [char]0) -and $standardForwarded.Count -eq $buildArguments.Count + 4) 'Standard heap selection preserves every Unity argument and its order'
        Assert-Capture ($standardRecord.DebuggerHeap.Requested -ceq 'standard' -and $buildRecord.DebuggerHeap.Requested -ceq 'debugger-default') 'Receipts distinguish requested standard heap from the unchanged debugger default'
        Assert-Capture (($standardRecord.DebuggerArguments -join [char]0) -ceq ($standardCall.Arguments -join [char]0) -and ($buildRecord.DebuggerArguments -join [char]0) -ceq ($call.Arguments -join [char]0)) 'Receipts bind both requested policies to the actual CDB argument arrays'
        Assert-Capture ($standardRecord.DebuggerHeap.TargetState -ceq 'unverified' -and [Environment]::GetEnvironmentVariable('_NO_DEBUG_HEAP', 'Process') -ceq $originalNoDebugHeap) 'Heap request does not claim target state or mutate inherited heap environment'
        Assert-Capture ($standardRecord.Cases[0].TargetExitCode -eq -1073741819 -and $standardRecord.Cases[0].Process.RawExitCode -eq -1073741819 -and $standardCall.PreserveSymbolPath -and $standardCall.Directory -ceq [Environment]::CurrentDirectory) 'Standard heap capture preserves target/controller faults and environment boundaries'
        $failedRecord = [ordered]@{ State = 'preparing'; Cases = @(); TestFilter = $null }
        $savedBeforeLaunch = $script:buildSavedRecords.Count
        $script:buildThrowOnLaunch = $true
        $launchFailed = $false
        try { Invoke-NativeStandaloneBuildCapture -EditorPath 'exact-editor.exe' -ProjectPath $project -Arguments $buildArguments -DebuggerPath 'pinned-cdb.exe' -PrivateDirectory (Join-Path $root 'failed-standard-launch') -ArtifactsPath $root -LogPath (Join-Path $root 'failed-standard.log') -Seconds 123 -Record $failedRecord -UseStandardHeap }
        catch { $launchFailed = $_.Exception.Message -ceq 'Controlled debugger launch failure.' }
        finally { $script:buildThrowOnLaunch = $false }
        Assert-Capture ($launchFailed -and $script:buildSavedRecords.Count -eq $savedBeforeLaunch + 1 -and $failedRecord.State -ceq 'preparing' -and $failedRecord.DebuggerArguments[0] -ceq '-hd' -and -not $failedRecord.Cases[0].Process.ContainsKey('RawExitCode')) 'Failed launch retains requested heap arguments before starting and cannot fabricate an exit'
        foreach ($scenario in @('controller-failed', 'target-missing', 'target-ambiguous', 'timeout')) {
            $script:buildControllerExit = if ($scenario -eq 'controller-failed') { 1 } else { 0 }
            $script:buildKillReason = if ($scenario -eq 'timeout') { 'wall-clock-timeout' } else { $null }
            $script:buildTargetText = if ($scenario -eq 'target-missing') { 'no target event' } elseif ($scenario -eq 'target-ambiguous') { "Last event: 2a.1: Exit process 0:2a, code 0`nLast event: 2a.1: Exit process 0:2a, code 0" } else { 'Last event: 2a.1: Exit process 0:2a, code 0' }
            $record = [ordered]@{ State = 'preparing'; Cases = @(); TestFilter = $null }
            $rejected = $false
            try { Invoke-NativeStandaloneBuildCapture -EditorPath 'exact-editor.exe' -ProjectPath $project -Arguments $buildArguments -DebuggerPath 'pinned-cdb.exe' -PrivateDirectory (Join-Path $root $scenario) -ArtifactsPath $root -LogPath (Join-Path $root ($scenario + '.log')) -Seconds 123 -Record $record } catch { $rejected = $true }
            Assert-Capture ($rejected -and $record.State -cne 'completed' -and $record.Cases.Count -eq 1) "Unqualified build controller outcome refuses completion: $scenario"
            Assert-Capture (Test-Path -LiteralPath (Join-Path $root ($scenario + '.log'))) "Failed build retains console evidence: $scenario"
        }
        foreach ($scenario in @('wrong-platform', 'wrong-project', 'changed-log', 'early-quit', 'duplicate-run', 'unbounded')) {
            $arguments = @($buildArguments)
            if ($scenario -eq 'wrong-platform') { $arguments[6] = 'editmode' }
            if ($scenario -eq 'wrong-project') { $arguments[3] = 'another-project' }
            if ($scenario -eq 'changed-log') { $arguments[-1] = 'another.log' }
            if ($scenario -eq 'early-quit') { $arguments += '-quit' }
            if ($scenario -eq 'duplicate-run') { $arguments += '-runTests' }
            $before = $script:buildCaptureCalls.Count
            $record = [ordered]@{ State = 'preparing'; Cases = @() }
            $rejected = $false
            try { Invoke-NativeStandaloneBuildCapture -EditorPath 'exact-editor.exe' -ProjectPath $project -Arguments $arguments -DebuggerPath 'pinned-cdb.exe' -PrivateDirectory (Join-Path $root ('invalid-' + $scenario)) -ArtifactsPath $root -LogPath (Join-Path $root 'invalid.log') -Seconds $(if ($scenario -eq 'unbounded') { 0 } else { 123 }) -Record $record } catch { $rejected = $true }
            Assert-Capture ($rejected -and $script:buildCaptureCalls.Count -eq $before -and $record.Cases.Count -eq 0) "Invalid build selection starts no process: $scenario"
        }
        $originalBudgetCommand = ${function:Get-NativeCaptureDebuggerCommand}
        $script:buildKillReason = $null
        $script:buildControllerExit = 0
        $script:buildThrowOnLaunch = $false
        $script:buildTargetText = 'Last event: 2a.1: Exit process 0:2a, code c0000005'
        try {
            foreach ($standardHeap in @($false, $true)) {
                foreach ($length in @(4096, 4095, 4094)) {
                    $script:controlledCommandLength = $length
                    function Get-NativeCaptureDebuggerCommand { param($DumpPath) return (';' * $script:controlledCommandLength) }
                    $record = [ordered]@{ State = 'preparing'; Cases = @() }
                    $callsBefore = $script:buildCaptureCalls.Count
                    $rejected = $false
                    try {
                        Invoke-NativeStandaloneBuildCapture -EditorPath 'exact-editor.exe' -ProjectPath $project -Arguments $buildArguments -DebuggerPath 'pinned-cdb.exe' -PrivateDirectory (Join-Path $root ('command-budget-' + $standardHeap + '-' + $length)) -ArtifactsPath $root -LogPath (Join-Path $root 'command-budget.log') -Seconds 123 -Record $record -UseStandardHeap:$standardHeap
                    } catch {
                        if ($_.Exception.Message -notlike '*4095-character*') { throw }
                        $rejected = $true
                    }
                    $admitted = $length -le 4095
                    Assert-Capture ($rejected -eq (-not $admitted) -and $script:buildCaptureCalls.Count -eq $callsBefore + [int]$admitted) "Complete debugger command length controls launch admission: $length"
                    Assert-Capture ($record.DebuggerCommandBudget.Maximum -eq 4095 -and $record.DebuggerCommandBudget.Actual -eq $length -and $record.DebuggerCommandBudget.Admitted -eq $admitted) 'Persist the actual complete command budget before debugger launch'
                    if (-not $admitted) {
                        Assert-Capture ($record.Cases[0].Process.Count -eq 0 -and $record.State -ceq 'preparing' -and $record.DebuggerArguments[(1 + [int]$standardHeap)].Length -eq $length) 'Overlength rejection retains planned arguments without fabricating a debugger process'
                    }
                }
            }
            $legacyLoop = $ast.Find({ param($node) $node -is [Management.Automation.Language.ForEachStatementAst] -and $node.Variable.Extent.Text -ceq '$mode' }, $true)
            $firstLegacy = @($legacyLoop.Body.Statements | Where-Object { $_.Extent.Text -ceq '$executable = $EditorPath' })[0]
            $lastLegacy = @($legacyLoop.Body.Statements | Where-Object { $_.Extent.Text.StartsWith('$output = Invoke-NativeCaptureProcess -FilePath $executable') })[0]
            $legacyLaunch = [scriptblock]::Create($ast.Extent.Text.Substring($firstLegacy.Extent.StartOffset, $lastLegacy.Extent.EndOffset - $firstLegacy.Extent.StartOffset))
            foreach ($length in @(4096, 4095, 4094)) {
                $script:controlledCommandLength = $length
                $mode = 'debugger'; $EditorPath = 'exact-editor.exe'; $debugger = 'pinned-cdb.exe'
                $unityArguments = $buildArguments; $privateCase = $root; $artifactRoot = $root; $TimeoutSeconds = 123; $CompareLoaderSnaps = $false
                $record = [ordered]@{ Cases = @() }; $callsBefore = $script:buildCaptureCalls.Count; $rejected = $false
                try { . $legacyLaunch } catch {
                    if ($_.Exception.Message -notlike '*4095-character*') { throw }
                    $rejected = $true
                }
                $admitted = $length -le 4095
                Assert-Capture ($rejected -eq (-not $admitted) -and $script:buildCaptureCalls.Count -eq $callsBefore + [int]$admitted -and $record.Cases[0].DebuggerCommandBudget.Actual -eq $length) 'Legacy debugger launch applies the same complete command budget'
            }
            $mode = 'direct'; $callsBefore = $script:buildCaptureCalls.Count; $record = [ordered]@{ Cases = @() }
            . $legacyLaunch
            Assert-Capture ($script:buildCaptureCalls.Count -eq $callsBefore + 1 -and -not $record.Cases[0].ContainsKey('DebuggerCommandBudget')) 'Legacy Direct does not acquire a debugger command budget'
        } finally { ${function:Get-NativeCaptureDebuggerCommand} = $originalBudgetCommand }
    } finally { ${function:Invoke-NativeCaptureProcess} = $originalBuildCaptureProcess }

    $runnerTokens = $null
    $runnerErrors = $null
    $runnerAst = [Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'scripts/unity/run-ci-tests.ps1'), [ref]$runnerTokens, [ref]$runnerErrors)
    Assert-Capture ($runnerErrors.Count -eq 0) 'Build integration parses'
    $buildBranch = $runnerAst.Find({ param($node) $node -is [Management.Automation.Language.IfStatementAst] -and $node.Clauses[0].Item1.Extent.Text -ceq '$StandaloneBuildCapture -ne ''None''' -and $node.Extent.Text.Contains('Invoke-UnityStandaloneBuildDiagnostic -Mode') }, $true)
    Assert-Capture ($null -ne $buildBranch) 'Existing Standalone build has a diagnostic branch'
    $script:diagnosticBuildCalls = 0
    $script:diagnosticSnapshots = 0
    $script:diagnosticPlayerCalls = 0
    $script:diagnosticHeapRequests = @()
    function Invoke-UnityStandaloneBuildDiagnostic { param($Mode, $EditorPath, $ProjectPath, $Arguments, $ArtifactsPath, $LogPath, $TimeoutSeconds, [switch]$UseStandardHeap, [switch]$AttachToDirect, [switch]$AttachAfterStartup, [switch]$AttachAfterShutdown) ++$script:diagnosticBuildCalls; $script:diagnosticHeapRequests += [bool]$UseStandardHeap; $script:diagnosticAttachRequests += [bool]$AttachToDirect; $script:diagnosticStartupRequests += [bool]$AttachAfterStartup; $script:diagnosticShutdownRequests += [bool]$AttachAfterShutdown }
    $StandaloneBuildAttachToDirect = $false
    $StandaloneBuildAttachAfterStartup = $false
    $StandaloneBuildAttachAfterShutdown = $false
    $script:diagnosticAttachRequests = @()
    $script:diagnosticStartupRequests = @()
    $script:diagnosticShutdownRequests = @()
    function Add-FrozenPlayerProvenanceSnapshot { param($Context, $Stage, [switch]$IncludePayload) Assert-Capture ($Stage -ceq 'after-build' -and $IncludePayload) 'Diagnostic records the actual post-build inventory'; ++$script:diagnosticSnapshots }
    function Get-StandaloneBuildTimeoutSeconds { return 123 }
    function Invoke-StandaloneTestPlayer { ++$script:diagnosticPlayerCalls }
    foreach ($selection in @(@{ Mode = 'Direct'; StandardHeap = $false; Attach = $false; Startup = $false; Shutdown = $false }, @{ Mode = 'Debugger'; StandardHeap = $false; Attach = $false; Startup = $false; Shutdown = $false }, @{ Mode = 'Debugger'; StandardHeap = $true; Attach = $false; Startup = $false; Shutdown = $false }, @{ Mode = 'Debugger'; StandardHeap = $false; Attach = $true; Startup = $false; Shutdown = $false }, @{ Mode = 'Debugger'; StandardHeap = $false; Attach = $true; Startup = $true; Shutdown = $false }, @{ Mode = 'Debugger'; StandardHeap = $false; Attach = $true; Startup = $false; Shutdown = $true })) {
        $StandaloneBuildCapture = $selection.Mode
        $StandaloneBuildDebuggerStandardHeap = $selection.StandardHeap
        $StandaloneBuildAttachToDirect = $selection.Attach
        $StandaloneBuildAttachAfterStartup = $selection.Startup
        $StandaloneBuildAttachAfterShutdown = $selection.Shutdown
        $UnityEditorPath = 'exact-editor.exe'; $ProjectPath = $project; $buildArgs = $buildArguments; $ArtifactsPath = $root; $logPath = Join-Path $root 'integration.log'; $frozenPlayerProvenance = @{}
        $rejected = $false
        try { & ([scriptblock]::Create($buildBranch.Extent.Text + '; Invoke-StandaloneTestPlayer')) } catch { $rejected = $_.Exception.Message -like 'Evidence-only Standalone build capture stops before player launch*' }
        Assert-Capture ($rejected -and $script:diagnosticPlayerCalls -eq 0 -and $script:diagnosticHeapRequests[-1] -eq $selection.StandardHeap -and $script:diagnosticAttachRequests[-1] -eq $selection.Attach -and $script:diagnosticStartupRequests[-1] -eq $selection.Startup -and $script:diagnosticShutdownRequests[-1] -eq $selection.Shutdown) "Build-only route retains diagnostic selection and never launches a player: $($selection.Mode)/$($selection.StandardHeap)/$($selection.Attach)/$($selection.Startup)"
    }
    Assert-Capture ($script:diagnosticBuildCalls -eq 6 -and $script:diagnosticSnapshots -eq 6) 'Each diagnostic arm performs one existing build and one post-build inventory'
    $StandaloneBuildCapture = 'None'
    $StandaloneBuildDebuggerStandardHeap = $false
    $StandaloneBuildAttachToDirect = $false
    $StandaloneBuildAttachAfterStartup = $false
    $StandaloneBuildAttachAfterShutdown = $false
    & ([scriptblock]::Create($buildBranch.Extent.Text + '; Invoke-StandaloneTestPlayer'))
    Assert-Capture ($script:diagnosticBuildCalls -eq 6 -and $script:diagnosticPlayerCalls -eq 1) 'Disabled diagnostic leaves the ordinary player path reachable'

    $acceptanceTokens = $null
    $acceptanceErrors = $null
    $acceptanceAst = [Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'scripts/unity/run-acceptance.ps1'), [ref]$acceptanceTokens, [ref]$acceptanceErrors)
    Assert-Capture ($acceptanceErrors.Count -eq 0) 'Heap acceptance driver parses'
    $shutdownTransfer = $acceptanceAst.Find({ param($node) $node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -ceq '$parameters.StandaloneBuildAttachAfterShutdown' }, $true)
    Assert-Capture ($null -ne $shutdownTransfer) 'Acceptance forwards the final telemetry attachment request'
    foreach ($requested in @($false, $true)) {
        $StandaloneBuildAttachAfterShutdown = $requested; $parameters = @{}
        Invoke-Expression $shutdownTransfer.Extent.Text
        Assert-Capture ($parameters.StandaloneBuildAttachAfterShutdown -is [bool] -and $parameters.StandaloneBuildAttachAfterShutdown -eq $requested) 'Final telemetry selection forwards as Boolean'
    }
    foreach ($driverAst in @($runnerAst, $acceptanceAst)) {
        $shutdownGuard = $driverAst.Find({ param($node) $node -is [Management.Automation.Language.IfStatementAst] -and $node.Clauses[0].Item1.Extent.Text.StartsWith('$StandaloneBuildAttachAfterShutdown -and') }, $true)
        Assert-Capture ($null -ne $shutdownGuard) 'Actual driver admits only one attachment boundary'
        foreach ($requested in @($false, $true)) {
            foreach ($attach in @($false, $true)) {
                foreach ($startup in @($false, $true)) {
                    $StandaloneBuildAttachAfterShutdown = $requested; $StandaloneBuildAttachToDirect = $attach; $StandaloneBuildAttachAfterStartup = $startup
                    $rejected = $false
                    try { Invoke-Expression $shutdownGuard.Extent.Text } catch { $rejected = $_.Exception.Message -ceq 'Shutdown-delayed attachment requires Direct attachment without startup selection.' }
                    Assert-Capture ($rejected -eq ($requested -and (-not $attach -or $startup))) 'Final telemetry selection refuses startup or unattached targets'
                }
            }
        }
    }
    $StandaloneBuildAttachAfterShutdown = $false
    $startupTransfer = $acceptanceAst.Find({ param($node) $node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -ceq '$parameters.StandaloneBuildAttachAfterStartup' }, $true)
    Assert-Capture ($null -ne $startupTransfer) 'Acceptance forwards the startup attachment request'
    foreach ($requested in @($false, $true)) {
        $StandaloneBuildAttachAfterStartup = $requested; $parameters = @{}
        Invoke-Expression $startupTransfer.Extent.Text
        Assert-Capture ($parameters.StandaloneBuildAttachAfterStartup -is [bool] -and $parameters.StandaloneBuildAttachAfterStartup -eq $requested) 'Startup selection is forwarded as an actual Boolean'
    }
    foreach ($driverAst in @($runnerAst, $acceptanceAst)) {
        $startupGuard = $driverAst.Find({ param($node) $node -is [Management.Automation.Language.IfStatementAst] -and $node.Clauses[0].Item1.Extent.Text.StartsWith('$StandaloneBuildAttachAfterStartup -and') }, $true)
        Assert-Capture ($null -ne $startupGuard) 'Actual driver requires attachment for startup selection'
        foreach ($requested in @($false, $true)) {
            foreach ($attach in @($false, $true)) {
                $StandaloneBuildAttachAfterStartup = $requested; $StandaloneBuildAttachToDirect = $attach
                $rejected = $false
                try { Invoke-Expression $startupGuard.Extent.Text } catch { $rejected = $_.Exception.Message -ceq 'Startup-delayed attachment requires Direct attachment.' }
                Assert-Capture ($rejected -eq ($requested -and -not $attach)) 'Actual startup admission rejects an unattached target'
            }
        }
    }
    $attachTransfer = $acceptanceAst.Find({ param($node) $node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -ceq '$parameters.StandaloneBuildAttachToDirect' }, $true)
    Assert-Capture ($null -ne $attachTransfer) 'Acceptance forwards the attachment request to the runner'
    foreach ($requested in @($false, $true)) {
        $StandaloneBuildAttachToDirect = $requested
        $parameters = @{}
        Invoke-Expression $attachTransfer.Extent.Text
        Assert-Capture ($parameters.StandaloneBuildAttachToDirect -is [bool] -and $parameters.StandaloneBuildAttachToDirect -eq $requested) 'Acceptance forwards the actual attachment request as a Boolean'
    }
    foreach ($driverAst in @($runnerAst, $acceptanceAst)) {
        $attachGuard = $driverAst.Find({ param($node) $node -is [Management.Automation.Language.IfStatementAst] -and $node.Clauses[0].Item1.Extent.Text.StartsWith('$StandaloneBuildAttachToDirect -and') }, $true)
        Assert-Capture ($null -ne $attachGuard) 'Actual driver has a direct attachment admission guard'
        foreach ($requested in @($false, $true)) {
            foreach ($mode in @('None', 'Direct', 'Debugger')) {
                foreach ($heapRequest in @($false, $true)) {
                    foreach ($selection in @('wproto', 'all', 'intmap')) {
                        $StandaloneBuildAttachToDirect = $requested; $StandaloneBuildCapture = $mode
                        $StandaloneBuildDebuggerStandardHeap = $heapRequest; $Acceptance = $selection
                        $rejected = $false
                        try { Invoke-Expression $attachGuard.Extent.Text } catch { $rejected = $_.Exception.Message -like 'Direct attachment requires*' }
                        $expected = $requested -and ($mode -cne 'Debugger' -or $heapRequest -or ($driverAst -eq $acceptanceAst -and $selection -cne 'wproto'))
                        Assert-Capture ($rejected -eq $expected) "Actual driver attachment admission: $requested/$mode/$heapRequest/$selection"
                    }
                }
            }
        }
    }
    $heapTransfer = $acceptanceAst.Find({ param($node) $node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -ceq '$parameters.StandaloneBuildDebuggerStandardHeap' }, $true)
    Assert-Capture ($null -ne $heapTransfer) 'Acceptance forwards the heap request to the runner'
    foreach ($requested in @($false, $true)) {
        $StandaloneBuildDebuggerStandardHeap = $requested
        $parameters = @{}
        Invoke-Expression $heapTransfer.Extent.Text
        Assert-Capture ($parameters.StandaloneBuildDebuggerStandardHeap -is [bool] -and $parameters.StandaloneBuildDebuggerStandardHeap -eq $requested) 'Acceptance forwards the actual heap request as a Boolean'
    }
    foreach ($driverAst in @($runnerAst, $acceptanceAst)) {
        $heapGuard = $driverAst.Find({ param($node) $node -is [Management.Automation.Language.IfStatementAst] -and $node.Clauses[0].Item1.Extent.Text.StartsWith('$StandaloneBuildDebuggerStandardHeap -and') }, $true)
        Assert-Capture ($null -ne $heapGuard) 'Each driver refuses incompatible heap requests'
        foreach ($requested in @($false, $true)) {
            foreach ($mode in @('None', 'Direct', 'Debugger')) {
                foreach ($selection in @('wproto', 'all', 'borrowed')) {
                    $StandaloneBuildDebuggerStandardHeap = $requested
                    $StandaloneBuildCapture = $mode
                    $Acceptance = $selection
                    $rejected = $false
                    try { Invoke-Expression $heapGuard.Extent.Text } catch { $rejected = $_.Exception.Message -like 'Standard heap selection requires*' }
                    $expected = $requested -and ($mode -cne 'Debugger' -or ($driverAst -eq $acceptanceAst -and $selection -cne 'wproto'))
                    Assert-Capture ($rejected -eq $expected) "Actual driver heap admission: $requested/$mode/$selection"
                }
            }
        }
    }

    $workflow = [IO.File]::ReadAllText((Join-Path $repo '.github/workflows/unity-tests.yml'))
    Assert-Capture ($workflow -match '(?s)native-diagnostics:.*?default: false.*?type: boolean') 'Diagnostic input defaults off'
    $captureStep = [regex]::Match($workflow, '(?s)- name: Capture native exit diagnostics.*?(?=\n      - name:)').Value
    Assert-Capture ($captureStep -match "inputs.native-diagnostics" -and $captureStep -match "steps.unity_lock.outputs.acquired == 'true'" -and $captureStep -match "steps.run_editmode.outcome == 'success'" -and $captureStep -match '!cancelled\(\)') 'Capture requires opt-in, live lock, and prepared project'
    Assert-Capture ($captureStep -match 'capture-native-fault.ps1' -and $captureStep -match 'native-diagnostics') 'Standalone driver is wired to uploaded EditMode artifacts'
    Assert-Capture ($captureStep -match 'UH_STREAM_TEST_RESULTS:\s*"1"') 'Capture retains the normal CI streaming callback setting'
    Assert-Capture ($workflow -match '(?s)native-loader-snaps-comparison:.*?default: false.*?type: boolean') 'Registry comparison is a separate explicit opt-in'
    $restoreStep = [regex]::Match($workflow, '(?s)- name: Restore native loader-snaps comparison.*?(?=\n      - name:)').Value
    Assert-Capture ($restoreStep.Contains('always()') -and $restoreStep.Contains("steps.unity_lock.outputs.acquired == 'true'") -and $restoreStep.Contains('restore-native-loader-snaps.ps1 -Apply')) 'Independent restoration runs under the existing lock, including failures and cancellation'
    Assert-Capture ($captureSource.Contains("@('direct-before', 'direct-disabled', 'direct-restored')") -and $captureSource.Contains('finally') -and $captureSource.Contains('-LeasePath') -and $captureSource.Contains('Complete-NativeLoaderFlagLease')) 'Comparison records three direct phases and retains durable and finally recovery paths'
    $prepareOffset = $captureSource.IndexOf("Save-NativeLoaderFlagLease -Lease `$record.LoaderFlagLease")
    Assert-Capture ($prepareOffset -gt 0 -and $prepareOffset -lt $captureSource.IndexOf('if ([string]::IsNullOrWhiteSpace($DebuggerDirectory))')) 'The original receipt is durable before debugger setup and direct-before execution'
    Assert-Capture ($captureSource.IndexOf('$readOnlyFlagKey = $flagBase.OpenSubKey($flagPath, $false)') -lt $prepareOffset -and $prepareOffset -lt $captureSource.IndexOf('$flagKey = $flagBase.OpenSubKey($flagPath, $true)')) 'The prepared receipt precedes the first request for registry write access'
    $restoreSource = [IO.File]::ReadAllText((Join-Path $repo 'scripts/unity/restore-native-loader-snaps.ps1'))
    Assert-Capture ($restoreSource.Contains('Read-NativeLoaderFlagRecoveryLease') -and $restoreSource.Contains('Invoke-NativeLoaderFlagRecovery')) 'Independent recovery uses the phase-aware receipt and cleanup policy'
    Write-Host "$passed native capture controls passed"
    & (Join-Path $PSScriptRoot 'test-native-attach.ps1')
} finally {
    Remove-Item -LiteralPath $root -Recurse -Force
}
