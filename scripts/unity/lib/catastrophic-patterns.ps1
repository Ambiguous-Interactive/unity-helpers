# Host consumers retain native regex matching, including bootstrap-failure diagnostics.
function Get-CatastrophicPatterns {
    [CmdletBinding()]
    param()
    $catalogue = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../log-patterns.json') -Raw -ErrorAction Stop | ConvertFrom-Json -AsHashtable
    if ($catalogue.Catastrophic -isnot [array] -or $catalogue.Catastrophic.Count -eq 0) { throw 'Missing catastrophic pattern catalogue.' }
    return $catalogue.Catastrophic
}

function Get-EnvironmentWarningPatterns {
    [CmdletBinding()]
    param()
    $catalogue = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../log-patterns.json') -Raw -ErrorAction Stop | ConvertFrom-Json -AsHashtable
    if ($catalogue.Environment -isnot [array] -or $catalogue.Environment.Count -eq 0) { throw 'Missing environment warning catalogue.' }
    return $catalogue.Environment
}

function Get-NativeReportSummary {
    param([string]$LogPath)
    $summary = [ordered]@{
        State = 'missing-log'; NativeReportHeaderCount = 0; UnknownNativeConditionCount = 0
        NativeReportHeaderLines = @(); UnknownNativeConditionLines = @(); LocationsTruncated = $false
    }
    if ([string]::IsNullOrWhiteSpace($LogPath) -or -not (Test-Path -LiteralPath $LogPath -PathType Leaf)) { return $summary }
    $priorExitVariable = Get-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue
    $hadExitVariable = $null -ne $priorExitVariable
    $priorExitCode = if ($hadExitVariable) { $priorExitVariable.Value } else { $null }
    $nativeReader = $null
    try {
        $nativeReader = [IO.File]::OpenRead($LogPath)
        $scriptsRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $output = @(& uv run --project (Join-Path $scriptsRoot 'python~') --locked python (Join-Path $scriptsRoot 'unity/native_report_summary.py') "--log-path=$LogPath")
        if ($global:LASTEXITCODE -ne 0) { throw 'Native report scanner failed.' }
        $value = ($output -join "`n") | ConvertFrom-Json -AsHashtable -ErrorAction Stop
        if ($value -isnot [Collections.IDictionary] -or $value.Count -ne 6 -or
            $value.State -notin @('missing-log', 'observed', 'unreadable-log') -or
            $value.LocationsTruncated -isnot [bool]) { throw 'Invalid native report scanner response.' }
        foreach ($group in @('NativeReportHeader', 'UnknownNativeCondition')) {
            $count = $value[$group + 'Count']
            $lines = $value[$group + 'Lines']
            if (($count -isnot [int] -and $count -isnot [long]) -or $count -lt 0 -or
                $lines -isnot [array] -or $lines.Count -ne [Math]::Min($count, 8)) {
                throw 'Invalid native report scanner counters.'
            }
            foreach ($line in $lines) {
                if (($line -isnot [int] -and $line -isnot [long]) -or $line -le 0) { throw 'Invalid native report scanner location.' }
            }
        }
        if ($value.LocationsTruncated -ne ($value.NativeReportHeaderCount -gt 8 -or $value.UnknownNativeConditionCount -gt 8)) {
            throw 'Invalid native report scanner truncation.'
        }
        return $value
    }
    catch {
        $summary.State = 'unreadable-log'
        Write-Warning "Native report observations unavailable: $($_.Exception.Message)"
        return $summary
    }
    finally {
        try { if ($null -ne $nativeReader) { $nativeReader.Dispose() } }
        finally {
            if ($hadExitVariable) { $global:LASTEXITCODE = $priorExitCode }
            else { Remove-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue }
        }
    }
}
