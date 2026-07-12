Set-StrictMode -Version Latest

function Test-UnityLicenseReturnResourceSafe {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][int]$ExitCode,
        [Parameter(Mandatory = $true)][string]$LogPath
    )

    if ($ExitCode -eq 0) {
        return $true
    }

    # Process termination is never proof of cleanup, even if a partial/stale log
    # happens to contain Unity's normal success markers.
    if ($ExitCode -in @(137, 143, -1073741510, -1073740791)) {
        return $false
    }

    try {
        if (-not (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
            return $false
        }

        $lines = [System.Collections.Generic.HashSet[string]]::new(
            [System.StringComparer]::Ordinal
        )
        foreach ($line in (Get-Content -LiteralPath $LogPath -ErrorAction Stop)) {
            [void]$lines.Add(([string]$line).Trim())
        }

        return (
            $lines.Contains('Successfully returned the entitlement license') -and
            $lines.Contains('Serial number unavailable for ULF return')
        )
    } catch {
        return $false
    }
}
