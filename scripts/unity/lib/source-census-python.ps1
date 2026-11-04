function Stop-WProtoSourceCensusWorker {
    $state = Get-Variable -Scope Script -Name WProtoSourceCensusWorker -ValueOnly -ErrorAction SilentlyContinue
    if ($null -eq $state) { return }
    $script:WProtoSourceCensusWorker = $null
    try {
        if (-not $state.Process.HasExited) {
            $state.Process.StandardInput.Close()
            if (-not $state.Process.WaitForExit(5000)) {
                $state.Process.Kill($true)
                $null = $state.Process.WaitForExit(5000)
            }
        }
    } finally { $state.Process.Dispose() }
}

function Invoke-WProtoSourceCensus {
    param(
        [ValidateSet('sources', 'host-sources')][string]$Operation,
        [string]$Root
    )
    $implementation = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../frozen_files.py'))
    $project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../python~'))
    try {
        $hashes = @($implementation, (Join-Path $project 'pyproject.toml'), (Join-Path $project 'uv.lock') | ForEach-Object {
            (Get-FileHash -LiteralPath $_ -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
        })
    } catch { Stop-WProtoSourceCensusWorker; throw }
    $fingerprint = $hashes -join ':'
    $state = Get-Variable -Scope Script -Name WProtoSourceCensusWorker -ValueOnly -ErrorAction SilentlyContinue
    if ($null -ne $state -and ($state.Process.HasExited -or $state.Fingerprint -cne $fingerprint)) {
        Stop-WProtoSourceCensusWorker
        $state = $null
    }
    if ($null -eq $state) {
        $info = [Diagnostics.ProcessStartInfo]::new()
        $info.FileName = @(Get-Command uv -CommandType Application -ErrorAction Stop)[0].Source
        $info.UseShellExecute = $false
        $info.CreateNoWindow = $true
        $info.RedirectStandardInput = $true
        $info.RedirectStandardOutput = $true
        $info.RedirectStandardError = $true
        $info.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
        $info.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
        foreach ($argument in @('run', '--quiet', '--project', $project, '--locked', '--no-dev', 'python', $implementation, '--serve')) {
            $info.ArgumentList.Add($argument)
        }
        $process = [Diagnostics.Process]::new()
        $process.StartInfo = $info
        try {
            if (-not $process.Start()) { throw 'Source census worker did not start.' }
            $process.StandardInput.AutoFlush = $true
            $state = [pscustomobject]@{ Process = $process; Fingerprint = $fingerprint; WorkerId = $null; SessionId = $null; ErrorOutput = $process.StandardError.ReadToEndAsync() }
            $script:WProtoSourceCensusWorker = $state
        } catch { $process.Dispose(); throw }
    }
    $request = [ordered]@{ protocol = 1; requestId = [Guid]::NewGuid().ToString('N'); operation = $Operation; root = $Root }
    try {
        $state.Process.StandardInput.WriteLine((ConvertTo-Json -InputObject $request -Depth 8 -Compress))
        $responseTask = $state.Process.StandardOutput.ReadLineAsync()
        if (-not $responseTask.Wait(300000)) { throw 'Source census worker timed out.' }
        if ($null -eq $responseTask.Result) {
            $diagnostic = if ($state.ErrorOutput.IsCompleted) { $state.ErrorOutput.Result } else { 'Worker closed its output unexpectedly.' }
            throw $diagnostic
        }
        $response = $responseTask.Result | ConvertFrom-Json -AsHashtable -ErrorAction Stop
        if (($response.protocol -isnot [int] -and $response.protocol -isnot [long]) -or $response.protocol -ne 1 -or
            $response.requestId -isnot [string] -or $response.requestId -cne $request.requestId -or
            $response.toolSha256 -isnot [string] -or $response.toolSha256 -cne $hashes[0] -or $response.ok -isnot [bool] -or
            ($response.processId -isnot [int] -and $response.processId -isnot [long]) -or $response.processId -le 0 -or
            ($null -ne $state.WorkerId -and $state.WorkerId -ne $response.processId) -or
            $response.sessionId -isnot [string] -or $response.sessionId -cnotmatch '^[a-f0-9]{32}$' -or
            ($null -ne $state.SessionId -and $state.SessionId -cne $response.sessionId)) {
            throw 'Source census worker returned a mismatched response.'
        }
        $state.WorkerId = $response.processId
        $state.SessionId = $response.sessionId
    } catch {
        Stop-WProtoSourceCensusWorker
        throw
    }
    try {
        $afterHashes = @($implementation, (Join-Path $project 'pyproject.toml'), (Join-Path $project 'uv.lock') | ForEach-Object {
            (Get-FileHash -LiteralPath $_ -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
        })
        if (($afterHashes -join ':') -cne $fingerprint) {
            Stop-WProtoSourceCensusWorker
            throw 'WProto source census implementation or project changed during the request.'
        }
    } catch { Stop-WProtoSourceCensusWorker; throw }
    if (-not $response.ok) { throw ("Source census failed: " + $response.error) }
    $response.value
}
