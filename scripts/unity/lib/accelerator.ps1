#!/usr/bin/env pwsh
# accelerator.ps1 - Unity Accelerator (cache server) endpoint helpers.
#
# Dot-sourceable library (NO param() block, just function definitions) so the
# normalize/args/reachability logic can be unit-tested with plain pwsh without
# triggering run-ci-tests.ps1's main / mandatory param() prompts.
#
# Functions:
#   ConvertTo-NormalizedAcceleratorEndpoint - pure host:port normalization.
#   Test-AcceleratorReachable               - bounded TCP reachability probe.
#   Get-AcceleratorArguments                - emits the -EnableCacheServer args
#                                             (gated on reachability).

function ConvertTo-NormalizedAcceleratorEndpoint {
    param([string]$Endpoint)

    # Pure: returns $null for empty input or a non-empty 'host:port' string;
    # THROWS with form-only diagnostics (never echoes the input value -- the
    # raw form is sensitive even if it just looks like a URL, and a future
    # secret-masking lapse must not exfiltrate it through our error text).
    if (-not $Endpoint -or $Endpoint.Trim().Length -eq 0) {
        return $null
    }

    $trimmed = $Endpoint.Trim()
    $hostPart = $null
    $portPart = 0

    # URL form: a scheme is present. [System.Uri]::TryCreate handles userinfo
    # stripping, path/query/fragment stripping, bracketed IPv6 hosts, and
    # explicit port extraction in one call. PS 5.1 compatible.
    if ($trimmed -match '^[a-zA-Z][a-zA-Z0-9+.\-]*://') {
        [System.Uri]$uri = $null
        # NOTE (leak-guard): the throw text below is form-only and intentionally
        # interpolates NO part of `$Endpoint`/`$trimmed`. The fourth normalizer
        # throw path (URL TryCreate failure) is therefore statically safe even
        # though it cannot be deterministically triggered from a unit test --
        # [System.Uri]::TryCreate is too permissive about most malformed URLs.
        if (-not [System.Uri]::TryCreate($trimmed, [System.UriKind]::Absolute, [ref]$uri)) {
            throw 'UNITY_ACCELERATOR_ENDPOINT could not be parsed as a URL form (scheme present, but not RFC 3986 well-formed). Expected host:port or scheme://host:port.'
        }
        # IsDefaultPort=TRUE means the URL OMITTED :port and the scheme's
        # default (e.g. 80/443 for http/https) was substituted -- both cases
        # are wrong for a Unity cache server, which needs an EXPLICIT port.
        # The `$uri.Port -lt 0` clause is belt-and-suspenders: on pwsh 7+ a
        # missing port yields Port == -1 AND IsDefaultPort == True, so the
        # -lt 0 check is subsumed -- it stays here as defense against a future
        # .NET runtime change that decouples the two flags.
        if ($uri.Port -lt 0 -or $uri.IsDefaultPort) {
            throw 'UNITY_ACCELERATOR_ENDPOINT URL is missing an explicit :port. Provide host:port or scheme://host:port.'
        }
        # `Uri.Host` returns `[::1]` (with brackets) on pwsh 7+ / .NET Core (the
        # CI runtime), and historically returned `::1` (no brackets) on PS 5.1 /
        # .NET Framework. The `StartsWith('[')` guard makes the assembled
        # 'host:port' string unambiguous on both runtimes; the production target
        # is pwsh 7+, so this is defense-in-depth against a future PS 5.1
        # backport.
        $hostPart = $uri.Host
        if ($uri.HostNameType -eq [System.UriHostNameType]::IPv6 -and -not $hostPart.StartsWith('[')) {
            $hostPart = "[$hostPart]"
        }
        $portPart = $uri.Port
    }
    else {
        # Bare host:port (canonical). Bracketed IPv6 first because the v4 /
        # hostname regex would mis-anchor on the closing bracket.
        #
        # LEAK GUARD: pre-validate the port digit length BEFORE the `[int]` cast.
        # The .NET Int32 overflow exception text echoes the offending value
        # verbatim ("Cannot convert value "99999999999" to type ...") which would
        # contradict the function's "never echoes the input" invariant. 5 digits
        # is the max legal port (65535); anything longer is automatically out of
        # range, so reject with the existing form-only message before the cast.
        if ($trimmed -match '^\[([0-9A-Fa-f:]+)\]:(\d+)$') {
            if ($matches[2].Length -gt 5) {
                throw 'UNITY_ACCELERATOR_ENDPOINT port is out of range (must be 1-65535).'
            }
            $hostPart = "[$($matches[1])]"
            $portPart = [int]$matches[2]
        }
        elseif ($trimmed -match '^([^:\s/?#]+):(\d+)$') {
            if ($matches[2].Length -gt 5) {
                throw 'UNITY_ACCELERATOR_ENDPOINT port is out of range (must be 1-65535).'
            }
            $hostPart = $matches[1]
            $portPart = [int]$matches[2]
        }
        else {
            throw 'UNITY_ACCELERATOR_ENDPOINT could not be parsed: expected host:port (e.g. 127.0.0.1:10080), [ipv6]:port, or scheme://host:port[/path].'
        }
    }

    if ($portPart -le 0 -or $portPart -gt 65535) {
        throw 'UNITY_ACCELERATOR_ENDPOINT port is out of range (must be 1-65535).'
    }

    return ('{0}:{1}' -f $hostPart, $portPart)
}

function Test-AcceleratorReachable {
    param(
        [Parameter(Mandatory)][string]$NormalizedEndpoint,
        [int]$TimeoutMilliseconds = 3000
    )

    # Bounded TCP reachability probe. Returns $true ONLY on a confirmed
    # successful connect; $false on timeout / refused / parse error / any
    # exception. The async connect + bounded Wait pattern guarantees we NEVER
    # block longer than $TimeoutMilliseconds -- this is the whole point of the
    # gate (an unreachable accelerator in batch mode otherwise stalls every
    # editor tick on a multi-minute TCP connect timeout).
    #
    # LEAK GUARD: this function returns a bool ONLY and NEVER echoes the
    # endpoint host/port in any output. The caller logs (with masking already
    # registered) -- we do not.

    # Split the already-normalized 'host:port' form. Bracketed IPv6 ('[::1]:port')
    # must be handled separately because the host itself contains colons.
    $hostPart = $null
    $portPart = 0
    if ($NormalizedEndpoint -match '^\[([0-9A-Fa-f:]+)\]:(\d+)$') {
        $hostPart = $matches[1]
        $portPart = [int]$matches[2]
    }
    elseif ($NormalizedEndpoint -match '^([^:]+):(\d+)$') {
        $hostPart = $matches[1]
        $portPart = [int]$matches[2]
    }
    else {
        # Unparseable (should not happen for a normalized value) -- treat as
        # unreachable rather than throwing; the caller falls back to no-cache.
        return $false
    }

    $client = $null
    try {
        $client = [System.Net.Sockets.TcpClient]::new()
        $connectTask = $client.ConnectAsync($hostPart, $portPart)
        # Bounded wait. Task.Wait(ms) returns $true if the task completed within
        # the window, $false on timeout. A faulted task (refused/unresolved)
        # completes and re-throws from .Wait(), which the catch swallows.
        if (-not $connectTask.Wait($TimeoutMilliseconds)) {
            return $false
        }
        return $client.Connected
    }
    catch {
        return $false
    }
    finally {
        if ($null -ne $client) {
            $client.Dispose()
        }
    }
}

function Get-AcceleratorArguments {
    param(
        [string]$Endpoint,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Mode,
        [switch]$SkipReachabilityCheck
    )

    $normalized = ConvertTo-NormalizedAcceleratorEndpoint -Endpoint $Endpoint
    if (-not $normalized) {
        return @()
    }

    # SECURITY: defense-in-depth masking. GitHub Actions masks the original
    # secret value, but here we extract a NEW substring (the normalized
    # host:port form) -- masking a parent string does NOT propagate to derived
    # substrings. Register BOTH the raw trimmed input (defense-in-depth, in
    # case the secret was passed via non-secret env in some other call path)
    # AND the normalized form BEFORE any downstream log line could echo them:
    # Invoke-UnityEditor prints "$EditorPath $($Arguments -join ' ')" later in
    # this same script (search for `Write-Host "`"$EditorPath`"`) which WOULD
    # leak the host:port unmasked without these directives.
    #
    # `::add-mask::` is a no-op outside GitHub Actions, so local runs are
    # unaffected. Done at the top of the success path so all callers benefit.
    # Masking is registered BEFORE the reachability gate so even the gate's
    # caller-side logging path cannot leak a value.
    Write-Host "::add-mask::$($Endpoint.Trim())"
    Write-Host "::add-mask::$normalized"

    # REACHABILITY GATE: Unity has NO "fail-open" flag for a dead cache server
    # in batch mode -- an unreachable accelerator stalls every editor tick on a
    # multi-minute TCP connect timeout (CI errorcode 10060), exhausting the
    # whole step budget. So we pre-flight a bounded TCP probe and, if the
    # endpoint is configured but UNREACHABLE, run WITHOUT the cache server.
    # The probe is value-free in its output (returns a bool); the warning below
    # is generic and echoes NOTHING about the endpoint (leak-guard).
    # `-SkipReachabilityCheck` is an escape hatch (and lets the unit test
    # exercise the pure normalize+args path deterministically, no network).
    if (-not $SkipReachabilityCheck) {
        if (-not (Test-AcceleratorReachable -NormalizedEndpoint $normalized)) {
            Write-Host '::warning::Unity Accelerator endpoint is configured but UNREACHABLE; running WITHOUT the cache server to avoid the batch-mode connect-timeout stall.'
            return @()
        }
    }

    return @(
        '-EnableCacheServer',
        '-cacheServerEndpoint', $normalized,
        '-cacheServerNamespacePrefix', "unity-helpers-$Version-$Mode",
        '-cacheServerEnableDownload', 'true',
        '-cacheServerEnableUpload', 'true'
    )
}
