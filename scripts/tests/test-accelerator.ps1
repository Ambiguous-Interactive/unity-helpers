#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Test runner for scripts/unity/lib/accelerator.ps1's Unity Accelerator helpers.

.DESCRIPTION
    Verifies three things:
      1. ConvertTo-NormalizedAcceleratorEndpoint normalization semantics:
           empty/whitespace -> $null; bare host:port preserved; URL form reduced
           to host:port; bracketed IPv6 preserved; missing-port / out-of-range /
           malformed forms THROW.
      2. Test-AcceleratorReachable is the RED-GREEN core: a real loopback
           TcpListener is reachable ($true), and a definitely-closed port is
           unreachable ($false) AND returns FAST (well under the bounded
           timeout, proving it never blocks).
      3. Get-AcceleratorArguments gates on reachability: full -EnableCacheServer
           array for a reachable endpoint, @() for an unreachable endpoint, @()
           for an empty endpoint, and (with -SkipReachabilityCheck) the
           deterministic 6-element pure-args path.

    Self-contained, deterministic, cross-platform (TcpListener/TcpClient on
    loopback work on Linux pwsh). No network dependency beyond loopback.

.PARAMETER VerboseOutput
    Show detailed output during test execution.

.EXAMPLE
    pwsh -NoProfile -File scripts/tests/test-accelerator.ps1
    pwsh -NoProfile -File scripts/tests/test-accelerator.ps1 -VerboseOutput
#>
param(
    [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:TestsPassed = 0
$script:TestsFailed = 0
$script:FailedTests = @()

function Write-Info($msg) {
    if ($VerboseOutput) { Write-Host "[test-accelerator] $msg" -ForegroundColor Cyan }
}

function Write-TestResult {
    param(
        [string]$TestName,
        [bool]$Passed,
        [string]$Message = ""
    )
    if ($Passed) {
        Write-Host "  [PASS] $TestName" -ForegroundColor Green
        $script:TestsPassed++
    } else {
        Write-Host "  [FAIL] $TestName" -ForegroundColor Red
        if ($Message) {
            Write-Host "         $Message" -ForegroundColor Yellow
        }
        $script:TestsFailed++
        $script:FailedTests += $TestName
    }
}

# Assert that a script block throws (used for the normalizer error paths).
function Test-Throws {
    param([scriptblock]$Action)
    try {
        & $Action | Out-Null
        return $false
    } catch {
        return $true
    }
}

# Dot-source the library. ConvertTo-NormalizedAcceleratorEndpoint,
# Test-AcceleratorReachable, and Get-AcceleratorArguments are defined there.
$libPath = (Resolve-Path (Join-Path $PSScriptRoot '..' 'unity' 'lib' 'accelerator.ps1')).Path
. $libPath

$listener = $null
try {
    Write-Host "Testing ConvertTo-NormalizedAcceleratorEndpoint..." -ForegroundColor White

    Write-Host "`n  Section: Normalizer happy paths" -ForegroundColor White

    # --- Pass_EmptyReturnsNull ---
    $got = ConvertTo-NormalizedAcceleratorEndpoint -Endpoint ''
    Write-TestResult "Pass_EmptyReturnsNull" ($null -eq $got) "Expected `$null, got '$got'"

    # --- Pass_WhitespaceReturnsNull ---
    $got = ConvertTo-NormalizedAcceleratorEndpoint -Endpoint '   '
    Write-TestResult "Pass_WhitespaceReturnsNull" ($null -eq $got) "Expected `$null, got '$got'"

    # --- Pass_BareHostPortPreserved ---
    $got = ConvertTo-NormalizedAcceleratorEndpoint -Endpoint '127.0.0.1:10080'
    Write-TestResult "Pass_BareHostPortPreserved" ($got -eq '127.0.0.1:10080') "Expected '127.0.0.1:10080', got '$got'"

    # --- Pass_UrlFormReducedToHostPort ---
    $got = ConvertTo-NormalizedAcceleratorEndpoint -Endpoint 'http://host:1234/path'
    Write-TestResult "Pass_UrlFormReducedToHostPort" ($got -eq 'host:1234') "Expected 'host:1234', got '$got'"

    # --- Pass_BracketedIPv6Preserved ---
    $got = ConvertTo-NormalizedAcceleratorEndpoint -Endpoint '[::1]:9999'
    Write-TestResult "Pass_BracketedIPv6Preserved" ($got -eq '[::1]:9999') "Expected '[::1]:9999', got '$got'"

    Write-Host "`n  Section: Normalizer error paths (must throw, value-free)" -ForegroundColor White

    # --- Pass_MissingPortUrlThrows ---
    $threw = Test-Throws { ConvertTo-NormalizedAcceleratorEndpoint -Endpoint 'http://host/path' }
    Write-TestResult "Pass_MissingPortUrlThrows" $threw "Expected a throw for a URL missing an explicit :port."

    # --- Pass_OutOfRangePortThrows ---
    $threw = Test-Throws { ConvertTo-NormalizedAcceleratorEndpoint -Endpoint 'host:70000' }
    Write-TestResult "Pass_OutOfRangePortThrows" $threw "Expected a throw for an out-of-range port (>65535)."

    # --- Pass_OverlongPortDigitsThrowBeforeCast ---
    # Leak-guard: a >5-digit port must be rejected BEFORE the [int] cast (whose
    # overflow text echoes the value).
    $threw = Test-Throws { ConvertTo-NormalizedAcceleratorEndpoint -Endpoint 'host:99999999999' }
    Write-TestResult "Pass_OverlongPortDigitsThrowBeforeCast" $threw "Expected a throw for an overlong port digit run."

    # --- Pass_MalformedThrows ---
    $threw = Test-Throws { ConvertTo-NormalizedAcceleratorEndpoint -Endpoint 'not-a-valid-endpoint' }
    Write-TestResult "Pass_MalformedThrows" $threw "Expected a throw for a malformed (no :port) bare endpoint."

    Write-Host "`nTesting Test-AcceleratorReachable (RED-GREEN core)..." -ForegroundColor White

    Write-Host "`n  Section: Reachability gate" -ForegroundColor White

    # Start a real loopback listener on an ephemeral port (port 0 => OS picks).
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $livePort = ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    Write-Info "Live loopback listener on port $livePort"

    # --- Pass_ReachableLoopbackReturnsTrue ---
    $got = Test-AcceleratorReachable -NormalizedEndpoint "127.0.0.1:$livePort"
    Write-TestResult "Pass_ReachableLoopbackReturnsTrue" ($got -eq $true) "Expected `$true for a live loopback listener; got '$got'"

    # Stop the listener so the port is now closed/refused.
    $listener.Stop()
    $listener = $null

    # --- Pass_ClosedPortReturnsFalseFast ---
    # Assert BOTH the result ($false) AND that the probe returns FAST (well
    # under the bounded timeout) -- proving it does not block on a dead endpoint.
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $got = Test-AcceleratorReachable -NormalizedEndpoint "127.0.0.1:$livePort"
    $sw.Stop()
    $elapsedMs = $sw.ElapsedMilliseconds
    Write-Info "Closed-port probe returned in ${elapsedMs}ms"
    Write-TestResult "Pass_ClosedPortReturnsFalse" ($got -eq $false) "Expected `$false for a closed loopback port; got '$got'"
    Write-TestResult "Pass_ClosedPortReturnsFast" ($elapsedMs -lt 6000) "Expected the probe to return in well under 6000ms; took ${elapsedMs}ms (must not block)."

    # --- Pass_UnparseableEndpointReturnsFalse ---
    $got = Test-AcceleratorReachable -NormalizedEndpoint 'garbage-no-port'
    Write-TestResult "Pass_UnparseableEndpointReturnsFalse" ($got -eq $false) "Expected `$false for an unparseable endpoint; got '$got'"

    Write-Host "`nTesting Get-AcceleratorArguments..." -ForegroundColor White

    Write-Host "`n  Section: Arguments gated on reachability" -ForegroundColor White

    # Fresh live listener for the reachable-args assertion.
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $livePort2 = ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port

    # --- Pass_ReachableEndpointReturnsFullArray ---
    $gotArgs = @(Get-AcceleratorArguments -Endpoint "127.0.0.1:$livePort2" -Version '6000.0.50f1' -Mode 'editmode')
    $expected = @(
        '-EnableCacheServer',
        '-cacheServerEndpoint', "127.0.0.1:$livePort2",
        '-cacheServerNamespacePrefix', 'unity-helpers-6000.0.50f1-editmode',
        '-cacheServerEnableDownload', 'true',
        '-cacheServerEnableUpload', 'true'
    )
    $gotArgsMatch = ($gotArgs.Count -eq $expected.Count) -and (-not (Compare-Object $gotArgs $expected -SyncWindow 0))
    Write-TestResult "Pass_ReachableEndpointReturnsFullArray" $gotArgsMatch "Expected the full 6-flag -EnableCacheServer array; got ($($gotArgs -join ' '))"

    $listener.Stop()
    $closedPort = $livePort2
    $listener = $null

    # --- Pass_UnreachableEndpointReturnsEmpty ---
    $gotArgs = @(Get-AcceleratorArguments -Endpoint "127.0.0.1:$closedPort" -Version '6000.0.50f1' -Mode 'editmode')
    Write-TestResult "Pass_UnreachableEndpointReturnsEmpty" ($gotArgs.Count -eq 0) "Expected @() (Count 0) for an unreachable endpoint; got Count $($gotArgs.Count)"

    # --- Pass_EmptyEndpointReturnsEmpty ---
    $gotArgs = @(Get-AcceleratorArguments -Endpoint '' -Version '6000.0.50f1' -Mode 'editmode')
    Write-TestResult "Pass_EmptyEndpointReturnsEmpty" ($gotArgs.Count -eq 0) "Expected @() (Count 0) for an empty endpoint; got Count $($gotArgs.Count)"

    # --- Pass_SkipReachabilityReturnsPureArgs ---
    # Deterministic pure normalize+args path: a definitely-closed port still
    # yields the full 6-flag array when reachability is skipped (no network).
    $gotArgs = @(Get-AcceleratorArguments -Endpoint "127.0.0.1:$closedPort" -Version '6000.0.50f1' -Mode 'playmode' -SkipReachabilityCheck)
    $expectedSkip = @(
        '-EnableCacheServer',
        '-cacheServerEndpoint', "127.0.0.1:$closedPort",
        '-cacheServerNamespacePrefix', 'unity-helpers-6000.0.50f1-playmode',
        '-cacheServerEnableDownload', 'true',
        '-cacheServerEnableUpload', 'true'
    )
    $skipMatch = ($gotArgs.Count -eq $expectedSkip.Count) -and (-not (Compare-Object $gotArgs $expectedSkip -SyncWindow 0))
    Write-TestResult "Pass_SkipReachabilityReturnsPureArgs" $skipMatch "Expected the deterministic 6-element pure-args array (skip path); got ($($gotArgs -join ' '))"

} finally {
    if ($null -ne $listener) {
        try { $listener.Stop() } catch { }
    }
}

Write-Host ""
Write-Host ("Tests passed: {0}" -f $script:TestsPassed) -ForegroundColor Green
Write-Host ("Tests failed: {0}" -f $script:TestsFailed) -ForegroundColor $(if ($script:TestsFailed -gt 0) { "Red" } else { "Green" })
if ($script:FailedTests.Count -gt 0) {
    Write-Host "Failed tests:" -ForegroundColor Red
    foreach ($t in $script:FailedTests) {
        Write-Host "  - $t" -ForegroundColor Red
    }
}

exit $script:TestsFailed
