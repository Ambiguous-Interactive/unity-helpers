#!/usr/bin/env pwsh
[CmdletBinding()]
param([switch]$CoreOnly)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$selected = @($env:SELECTED_MODES | ConvertFrom-Json)
$known = if ($CoreOnly) { @('editmode', 'playmode') } else { @('editmode', 'playmode', 'standalone') }
if ($selected.Count -eq 0 -or @($selected | Select-Object -Unique).Count -ne $selected.Count) {
    throw "Selected Unity modes must be a non-empty set: $env:SELECTED_MODES"
}
$unknown = @($selected | Where-Object { $_ -notin $known })
if ($unknown.Count -ne 0) {
    throw "Unknown selected Unity modes: $($unknown -join ', ')"
}

$stages = @('RUN', 'VERIFY', 'REDACT', 'UPLOAD')
$failures = [System.Collections.Generic.List[string]]::new()
foreach ($mode in $known) {
    $expected = if ($mode -in $selected) { 'success' } else { 'skipped' }
    foreach ($stage in $stages) {
        $key = "$($mode.ToUpperInvariant())_$stage"
        $actual = [Environment]::GetEnvironmentVariable($key, 'Process')
        if ($actual -ne $expected) {
            $failures.Add("$mode/$stage=$actual (expected $expected)")
        }
    }
}
if ($failures.Count -ne 0) {
    throw "Unity mode failures: $($failures -join '; ')"
}
