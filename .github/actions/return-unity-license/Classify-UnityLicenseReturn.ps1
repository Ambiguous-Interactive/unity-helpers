Set-StrictMode -Version Latest

function Test-UnityLicenseReturnResourceSafe {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][int]$ExitCode,
        [Parameter(Mandatory = $true)][string]$LogPath
    )

    # Process termination is never proof of cleanup, even if a partial/stale log
    # happens to contain Unity's normal success markers.
    if ($ExitCode -in @(137, 143, -1073741510, -1073740791)) {
        return $false
    }

    try {
        if (-not (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
            return $false
        }

        $entitlementReturned = $false
        $ulfReturned = $false
        foreach ($line in (Get-Content -LiteralPath $LogPath -ErrorAction Stop)) {
            $normalized = ([string]$line).Trim()
            if (
                $normalized -ceq 'Successfully returned the entitlement license' -or
                $normalized -ceq '[Licensing::Module] Successfully returned the entitlement license'
            ) {
                $entitlementReturned = $true
            }
            if (
                $normalized -ceq 'Serial number unavailable for ULF return' -or
                $normalized -ceq '[Licensing::Module] Error: Serial number unavailable for ULF return; skipping operation' -or
                $normalized -cmatch '^\[Licensing::Client\] Successfully returned ULF license with serial number\s*:\s*\S+$'
            ) {
                $ulfReturned = $true
            }
        }

        return $entitlementReturned -and $ulfReturned
    } catch {
        return $false
    }
}
