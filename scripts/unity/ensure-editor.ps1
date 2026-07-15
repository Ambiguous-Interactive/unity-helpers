#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+f\d+$')]
    [string]$UnityVersion,

    [string]$InstallRoot = $(if ($env:UNITY_EDITOR_INSTALL_ROOT) { $env:UNITY_EDITOR_INSTALL_ROOT } else { 'C:\Unity\Editors' }),

    [string]$DiagnosticsPath = $(if ($env:UH_UNITY_DIAGNOSTICS_PATH) { $env:UH_UNITY_DIAGNOSTICS_PATH } else { '' }),

    [switch]$CiManagedOnly = $($env:GITHUB_ACTIONS -eq 'true'),

    [ValidateSet('EditorOnly', 'StandaloneWindowsIl2Cpp', 'Android', 'Full')]
    [string]$ProvisioningProfile = 'Full',

    [switch]$WithWindowsIl2Cpp,

    [string[]]$RequiredEditorPayloadRelativePath = @(),

    [switch]$RequireHealthyExisting
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($WithWindowsIl2Cpp) {
    if ($PSBoundParameters.ContainsKey('ProvisioningProfile') -and $ProvisioningProfile -ne 'StandaloneWindowsIl2Cpp') {
        throw "-WithWindowsIl2Cpp is an alias for -ProvisioningProfile StandaloneWindowsIl2Cpp and cannot be combined with -ProvisioningProfile $ProvisioningProfile."
    }
    $ProvisioningProfile = 'StandaloneWindowsIl2Cpp'
}

$script:UnityCliPath = 'unity'
$script:UnityProvisioningProfile = $ProvisioningProfile
$script:UnityInstallLockDepth = 0
$script:ProvisioningDeadlineUtc = [DateTime]::MaxValue
$script:ProvisioningBudgetSeconds = 0
$script:ProvisioningEditorPath = ''
$script:ProvisioningFinalClassification = 'not-finished'
$script:ProvisioningTimeoutEvents = New-Object System.Collections.Generic.List[object]
$script:ProvisioningProcessCleanupEvents = New-Object System.Collections.Generic.List[object]
$script:ProvisioningCommandClasses = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
$script:UnityCliVersionText = ''

# PowerShell 7.4 introduced $PSNativeCommandUseErrorActionPreference (stabilizing
# the native-error experimental feature). Its default is $false on current builds,
# so `& <native>` does NOT throw on a non-zero exit and our explicit exit checks
# run as written. However, a host profile or a future/different build could enable
# it, which would make `& <native>` THROW on a non-zero exit BEFORE our
# `if ($LASTEXITCODE -ne 0)` check runs -- making the best-effort invokers rely on
# their catch block instead of the explicit exit check. Pinning it $false makes
# LASTEXITCODE-based handling authoritative and identical across hosts/versions.
# (PS 5.1 lacks this variable; assigning it there is harmless, and the assignment
# is StrictMode-safe.)
$PSNativeCommandUseErrorActionPreference = $false

function Write-CiNotice {
    param([Parameter(Mandatory = $true)][string]$Message)
    Write-Host "::notice::$Message"
}

function Register-UnityCliCommandAttempt {
    param([string[]]$Arguments)

    $args = @($Arguments)
    if ($args.Count -eq 0) {
        [void]$script:ProvisioningCommandClasses.Add('unknown')
        return
    }

    $verb = [string]$args[0]
    $class = $verb
    if ($verb -in @('install', 'install-modules')) {
        if ($args -contains '-m') {
            $class = "$verb/modules"
        }
    }
    [void]$script:ProvisioningCommandClasses.Add($class)
}

function Add-ProvisioningTimeoutEvent {
    param(
        [string[]]$Arguments,
        [int]$TimeoutSeconds,
        [string]$Reason = 'wall-clock',
        [int]$StallSeconds = 0
    )

    $script:ProvisioningTimeoutEvents.Add([pscustomobject]@{
            utc            = [DateTime]::UtcNow.ToString('o')
            command        = (@($Arguments) -join ' ')
            reason         = $Reason
            timeoutSeconds = $TimeoutSeconds
            stallSeconds   = $StallSeconds
        }) | Out-Null
}

function Add-ProvisioningProcessCleanupEvent {
    param(
        [Parameter(Mandatory = $true)][string]$Reason,
        [int]$Matched = 0,
        [int]$Stopped = 0,
        [string[]]$Details
    )

    $script:ProvisioningProcessCleanupEvents.Add([pscustomobject]@{
            utc     = [DateTime]::UtcNow.ToString('o')
            reason  = $Reason
            matched = $Matched
            stopped = $Stopped
            details = @($Details)
        }) | Out-Null
}

function ConvertTo-ProcessArgumentLine {
    param([string[]]$Arguments)

    $quoted = foreach ($arg in @($Arguments)) {
        if ($null -eq $arg) {
            '""'
            continue
        }

        $value = [string]$arg
        if ($value.Length -gt 0 -and $value -notmatch '[\s"]') {
            $value
            continue
        }

        $builder = New-Object System.Text.StringBuilder
        [void]$builder.Append('"')
        $backslashes = 0
        foreach ($ch in $value.ToCharArray()) {
            if ($ch -eq '\') {
                $backslashes++
                continue
            }

            if ($ch -eq '"') {
                if ($backslashes -gt 0) {
                    [void]$builder.Append('\' * ($backslashes * 2))
                }
                [void]$builder.Append('\"')
                $backslashes = 0
                continue
            }

            if ($backslashes -gt 0) {
                [void]$builder.Append('\' * $backslashes)
                $backslashes = 0
            }
            [void]$builder.Append($ch)
        }

        if ($backslashes -gt 0) {
            [void]$builder.Append('\' * ($backslashes * 2))
        }
        [void]$builder.Append('"')
        $builder.ToString()
    }

    return ($quoted -join ' ')
}

function Get-EnsureEditorProvisioningBudgetSeconds {
    param([int]$Default = 9000)

    if ($env:UH_ENSURE_EDITOR_PROVISIONING_BUDGET_SECONDS) {
        $parsed = 0
        if (
            [int]::TryParse($env:UH_ENSURE_EDITOR_PROVISIONING_BUDGET_SECONDS, [ref]$parsed) -and
            $parsed -ge 0
        ) {
            return $parsed
        }
        Write-Host "::warning::Ignoring invalid UH_ENSURE_EDITOR_PROVISIONING_BUDGET_SECONDS='$env:UH_ENSURE_EDITOR_PROVISIONING_BUDGET_SECONDS'; using $Default second(s)."
    }
    return $Default
}

function Get-EnsureEditorProbeTimeoutSeconds {
    param([int]$Default = 120)

    $raw = $env:UH_ENSURE_EDITOR_PROBE_TIMEOUT_SECONDS
    if ($raw) {
        $parsed = 0
        if ([int]::TryParse($raw, [ref]$parsed) -and $parsed -ge 0) {
            return $parsed
        }
        Write-Host "::warning::Ignoring invalid UH_ENSURE_EDITOR_PROBE_TIMEOUT_SECONDS='$raw'; using $Default."
    }
    return $Default
}

function Initialize-UnityProvisioningBudget {
    $script:ProvisioningBudgetSeconds = Get-EnsureEditorProvisioningBudgetSeconds
    if ($script:ProvisioningBudgetSeconds -le 0) {
        $script:ProvisioningDeadlineUtc = [DateTime]::MaxValue
        Write-CiNotice "Unity provisioning whole-run budget is disabled."
        return
    }

    $script:ProvisioningDeadlineUtc = [DateTime]::UtcNow.AddSeconds($script:ProvisioningBudgetSeconds)
    Write-CiNotice "Unity provisioning whole-run budget: $script:ProvisioningBudgetSeconds second(s)."
}

function Get-RemainingUnityProvisioningBudgetSeconds {
    if ($script:ProvisioningDeadlineUtc -eq [DateTime]::MaxValue) {
        return 0
    }

    $remaining = [int][Math]::Floor(($script:ProvisioningDeadlineUtc - [DateTime]::UtcNow).TotalSeconds)
    if ($remaining -lt 0) {
        return 0
    }
    return $remaining
}

function Get-EffectiveUnityCliTimeoutSeconds {
    param([int]$RequestedSeconds)

    $remaining = Get-RemainingUnityProvisioningBudgetSeconds
    if ($script:ProvisioningDeadlineUtc -ne [DateTime]::MaxValue) {
        if ($remaining -le 0) {
            Write-Host "::error::Unity provisioning budget of $script:ProvisioningBudgetSeconds second(s) is exhausted before the next Unity CLI command can start."
            throw "Unity provisioning budget of $script:ProvisioningBudgetSeconds second(s) is exhausted before the next Unity CLI command can start."
        }
        if ($RequestedSeconds -le 0) {
            return $remaining
        }
        return [Math]::Min($RequestedSeconds, $remaining)
    }

    return $RequestedSeconds
}

function Assert-UnityProvisioningBudgetCanFit {
    param(
        [Parameter(Mandatory = $true)][string]$Operation,
        [int]$MinimumSeconds = 60
    )

    if ($script:ProvisioningDeadlineUtc -eq [DateTime]::MaxValue) {
        return
    }

    $remaining = Get-RemainingUnityProvisioningBudgetSeconds
    if ($remaining -lt $MinimumSeconds) {
        Write-Host "::error::Unity provisioning budget cannot fit '$Operation': $remaining second(s) remain, but at least $MinimumSeconds second(s) are required."
        throw "Unity provisioning budget cannot fit '$Operation': $remaining second(s) remain, but at least $MinimumSeconds second(s) are required. Increase UH_ENSURE_EDITOR_PROVISIONING_BUDGET_SECONDS or avoid this recovery path."
    }
}

function Invoke-WithRetry {
    # Generic retry wrapper for a TERMINATING-error-prone operation (the base
    # editor install, which has been observed to fail flakily after a long run
    # with exit code 6 and almost no diagnostic output). Runs $Action; on a
    # thrown terminating error it logs a ::warning:: with the attempt number and
    # message, sleeps with linear backoff (DelaySeconds * attempt), then retries.
    # After exhausting $MaxAttempts it RETHROWS the LAST error so a persistent
    # failure still aborts the bootstrap loudly (never silently swallowed).
    # StrictMode-safe: no collection captures, no property reads on $null.
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Action,
        [int]$MaxAttempts = 2,
        [int]$DelaySeconds = 15
    )

    if ($MaxAttempts -lt 1) { $MaxAttempts = 1 }

    $lastError = $null
    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        try {
            return & $Action
        } catch {
            $lastError = $_
            $message = $_.Exception.Message
            if ($attempt -lt $MaxAttempts) {
                $sleep = $DelaySeconds * $attempt
                Write-Host "::warning::Attempt $attempt of $MaxAttempts failed: $message. Retrying in $sleep second(s)."
                Start-Sleep -Seconds $sleep
            } else {
                Write-Host "::warning::Attempt $attempt of $MaxAttempts failed: $message. No attempts remaining."
            }
        }
    }

    # Exhausted every attempt: rethrow the last terminating error verbatim so the
    # original message (which now includes captured CLI output + exit code) reaches
    # CI logs unchanged.
    if ($lastError) {
        throw $lastError
    }

    # Defensive: only reachable if $MaxAttempts somehow yielded no iteration.
    throw "Invoke-WithRetry exhausted all attempts without capturing an error."
}

function Get-EnsureEditorRetryDelaySeconds {
    # Single source of truth for the Invoke-WithRetry backoff delay. Honors the
    # UH_ENSURE_EDITOR_RETRY_DELAY_SECONDS override (tests set it to 0 to avoid
    # real sleeps; CI leaves it unset for the production 15s backoff). A
    # non-integer or negative override is ignored with a ::warning:: and the
    # default is used. StrictMode-safe: no collection reads.
    param([int]$Default = 15)

    if ($env:UH_ENSURE_EDITOR_RETRY_DELAY_SECONDS) {
        $parsed = 0
        if (
            [int]::TryParse($env:UH_ENSURE_EDITOR_RETRY_DELAY_SECONDS, [ref]$parsed) -and
            $parsed -ge 0
        ) {
            return $parsed
        }
        Write-Host "::warning::Ignoring invalid UH_ENSURE_EDITOR_RETRY_DELAY_SECONDS='$env:UH_ENSURE_EDITOR_RETRY_DELAY_SECONDS'; using $Default second(s)."
    }
    return $Default
}

function Get-EnsureEditorInstallTimeoutSeconds {
    # Single source of truth for the TOTAL wall-clock timeout applied to a
    # module-install CLI invocation (see Invoke-UnityCliCaptureWithTimeout). The
    # Android NDK module install has been observed to HANG so long the GitHub job
    # is cancelled ("The operation was canceled") -- which means the retry never
    # triggers and NO diagnostics are produced. A bounded timeout kills the hung
    # install and lets the existing retry + classification flow run on a hang.
    #
    # Honors the UH_ENSURE_EDITOR_INSTALL_TIMEOUT_SECONDS override following the
    # EXACT convention of Get-EnsureEditorRetryDelaySeconds: tests set it small
    # (e.g. 2) to force the timeout path; CI leaves it unset for the production
    # default. A non-integer or NEGATIVE override is ignored with a ::warning::
    # and the default is used. A value of 0 is the explicit OPT-OUT (no timeout):
    # it returns 0 and the runner waits indefinitely, matching the prior
    # behavior, for the rare case an operator must allow an unbounded install.
    #
    # Default rationale (PROFILE-AWARE): EditorOnly keeps the historical 2700s
    # (45 min) bound because it installs only the base editor. Heavy module
    # profiles get 7200s (2h): Unity 6000.5.2f1 Windows IL2CPP alternate-root
    # repair has emitted steady live progress for the full old 2700s window before
    # completion, and killing that slow-but-live install cascades into a doomed
    # quarantine of the locked canonical editor. 7200s still stays under the
    # 9000s whole-provisioning budget and the 180-minute workflow provisioning
    # step, but gives cold 6000.5 module installs enough room to complete.
    # StrictMode-safe: no collection reads.
    param([int]$Default = -1)

    if ($env:UH_ENSURE_EDITOR_INSTALL_TIMEOUT_SECONDS) {
        $parsed = 0
        if (
            [int]::TryParse($env:UH_ENSURE_EDITOR_INSTALL_TIMEOUT_SECONDS, [ref]$parsed) -and
            $parsed -ge 0
        ) {
            return $parsed
        }
        $defaultDescription = if ($Default -ge 0) { "$Default second(s)" } else { 'the profile-aware default' }
        Write-Host "::warning::Ignoring invalid UH_ENSURE_EDITOR_INSTALL_TIMEOUT_SECONDS='$env:UH_ENSURE_EDITOR_INSTALL_TIMEOUT_SECONDS'; using $defaultDescription."
    }
    if ($Default -ge 0) {
        return $Default
    }

    $profile = 'Full'
    if (Get-Command Get-UnityProvisioningProfile -ErrorAction SilentlyContinue) {
        $profile = Get-UnityProvisioningProfile
    }
    if ($profile -eq 'EditorOnly') {
        return 2700
    }
    return 7200
}

function Get-EnsureEditorProgressStallSeconds {
    # Single source of truth for the HEARTBEAT-STALL threshold applied to a captured
    # CLI invocation (see Invoke-UnityCliCaptureWithTimeout's poll loop). This is
    # COMPLEMENTARY to Get-EnsureEditorInstallTimeoutSeconds (the total wall-clock
    # fallback): the heartbeat detector fires when no stdout/stderr line has arrived
    # for >= this many seconds. Unity module installs can legitimately emit repeated
    # byte-identical progress triples for a long on-disk install phase, so triple
    # advance is diagnostic only; output-line silence is the stall signal. Killing
    # on stall classifies as retryable (sentinel exit 125, distinct from the
    # wall-clock 124 so callers and tests can tell the two apart) and lets the
    # existing retry + classification flow run on a quiet hang.
    #
    # Honors UH_ENSURE_EDITOR_PROGRESS_STALL_SECONDS following the EXACT
    # convention of Get-EnsureEditorInstallTimeoutSeconds: tests set it small
    # (e.g. 2) to force the stall path; CI leaves it unset for the production
    # default. A non-integer or NEGATIVE override is ignored with a ::warning::
    # and the default is used. A value of 0 is the explicit OPT-OUT (no heartbeat
    # detection): the wall-clock fallback alone gates the run.
    #
    # Default rationale (PROFILE-AWARE; inherited from the triple-stall detector):
    # Unity installs have emitted a SINGLE monolithic
    # `{"...,"pct":50,"phase":"install","msg":"Installing Unity (6000.3.16f1)..."}`
    # triple that does NOT advance for the WHOLE on-disk unpack, including healthy
    # installs that keep emitting live output. The heartbeat clock therefore tracks
    # output silence, not triple changes, while the wall-clock timeout bounds a
    # chatty no-advance install. The profile-aware values remain deliberately
    # conservative while this detector changes semantics; lowering them should be
    # driven by measured max quiet gaps on the self-hosted runners. The
    # il2cpp/standalone (and Android/Full) profiles unpack MORE payload than
    # EditorOnly and can spend longer in sparse-output phases, so:
    #   * EditorOnly                       -> 900s  (15 min; base-editor unpack only)
    #   * StandaloneWindowsIl2Cpp/Android/Full -> 1800s (30 min; heavier payload, and
    #                                          a false kill here cascades into the
    #                                          module step, the real-world failure)
    # Both stay well under the install wall-clock fallback for their profile, so
    # a GENUINE quiet hang is still surfaced before the job is cancelled, while a
    # slow-but-real unpack with live output is no longer killed mid-flight. The
    # env override remains authoritative and is honored verbatim (tests set it
    # small to force the stall path; 0 opts out).
    # StrictMode-safe: no collection reads.
    param([int]$Default = -1)

    if ($env:UH_ENSURE_EDITOR_PROGRESS_STALL_SECONDS) {
        $parsed = 0
        if (
            [int]::TryParse($env:UH_ENSURE_EDITOR_PROGRESS_STALL_SECONDS, [ref]$parsed) -and
            $parsed -ge 0
        ) {
            return $parsed
        }
        Write-Host "::warning::Ignoring invalid UH_ENSURE_EDITOR_PROGRESS_STALL_SECONDS='$env:UH_ENSURE_EDITOR_PROGRESS_STALL_SECONDS'; using the profile-aware default."
    }

    if ($Default -ge 0) {
        return $Default
    }

    # Profile-aware default (only when the caller did not pin an explicit $Default).
    # Guard the Get-UnityProvisioningProfile lookup with Get-Command so this helper
    # still works when AST-extracted in ISOLATION (some unit tests extract only the
    # timeout/heartbeat helpers, not the profile getter); fall back to the heavier
    # default in that case, which is the safe (less-likely-to-false-kill) choice.
    $profile = 'Full'
    if (Get-Command Get-UnityProvisioningProfile -ErrorAction SilentlyContinue) {
        $profile = Get-UnityProvisioningProfile
    }
    if ($profile -eq 'EditorOnly') {
        return 900
    }
    return 1800
}

function Get-EnsureEditorProgressNoticeIntervalSeconds {
    # Single source of truth for the PERIODIC ::notice:: cadence in
    # Invoke-UnityCliCaptureWithTimeout's poll loop. The notice is wall-clock
    # gated (NOT per-line) so a long advancing install yields a human-readable
    # mid-flight summary in the live CI log instead of the raw dupe-progress
    # wall the Unity beta CLI emits. Extracted to a helper so tests can drop
    # the cadence to a few seconds without forcing the suite to wait the
    # production minute.
    #
    # Honors UH_ENSURE_EDITOR_PROGRESS_NOTICE_INTERVAL_SECONDS following the
    # EXACT convention of Get-EnsureEditorProgressStallSeconds: a non-integer
    # or NEGATIVE override is ignored with a ::warning:: and the default is
    # used. A value of 0 is the explicit OPT-OUT (no periodic notice ever
    # fires); the live progress stream is unaffected. StrictMode-safe: no
    # collection reads.
    #
    # Default 60s balances "human can see progress mid-flight" against "do not
    # bury the live progress stream"; lower would spam, higher would lose
    # actionability on the long-install path.
    param([int]$Default = 60)

    if ($env:UH_ENSURE_EDITOR_PROGRESS_NOTICE_INTERVAL_SECONDS) {
        $parsed = 0
        if (
            [int]::TryParse($env:UH_ENSURE_EDITOR_PROGRESS_NOTICE_INTERVAL_SECONDS, [ref]$parsed) -and
            $parsed -ge 0
        ) {
            return $parsed
        }
        Write-Host "::warning::Ignoring invalid UH_ENSURE_EDITOR_PROGRESS_NOTICE_INTERVAL_SECONDS='$env:UH_ENSURE_EDITOR_PROGRESS_NOTICE_INTERVAL_SECONDS'; using $Default second(s)."
    }
    return $Default
}

function Get-EnsureEditorInstallRetryAttempts {
    # Single source of truth for the base-install Invoke-WithRetry attempt count.
    # Honors UH_ENSURE_EDITOR_INSTALL_RETRY_ATTEMPTS following the EXACT
    # convention of Get-EnsureEditorRetryDelaySeconds. The DEFAULT is UNCHANGED at
    # 2 (the documented "two attempts fit inside the 180-minute step budget even
    # for a slow install"): now that a HANG is bounded by the install timeout and
    # classified as a retryable failure, the existing 2-attempt retry already
    # covers a transient hang, so the default is deliberately not bumped. This knob
    # only gives an operator a low-risk lever (e.g. set to 3 for a flaky window)
    # without destabilizing the default retry contract. A value below 1 is clamped
    # to 1 by Invoke-WithRetry; a non-integer/negative override is ignored with a
    # ::warning::. StrictMode-safe: no collection reads.
    param([int]$Default = 2)

    if ($env:UH_ENSURE_EDITOR_INSTALL_RETRY_ATTEMPTS) {
        $parsed = 0
        if (
            [int]::TryParse($env:UH_ENSURE_EDITOR_INSTALL_RETRY_ATTEMPTS, [ref]$parsed) -and
            $parsed -ge 1
        ) {
            return $parsed
        }
        Write-Host "::warning::Ignoring invalid UH_ENSURE_EDITOR_INSTALL_RETRY_ATTEMPTS='$env:UH_ENSURE_EDITOR_INSTALL_RETRY_ATTEMPTS'; using $Default attempt(s)."
    }
    return $Default
}

function Get-EnsureEditorQuarantineMoveRetryAttempts {
    # Single source of truth for Move-Item attempts when quarantining a partial
    # Unity editor tree before reinstall. Unity's Windows uninstaller can return
    # before the install directory is fully released/removed, and the runner has
    # observed the editor directory remain locked beyond the old 3-attempt window.
    # Keep this separate from install retry attempts: a quarantine retry is cheap
    # compared with a fresh multi-GB editor install and it protects the recovery path
    # from transient uninstaller/indexer/antivirus handles. Honors
    # UH_ENSURE_EDITOR_QUARANTINE_MOVE_RETRY_ATTEMPTS for tests/operators; invalid
    # values are ignored with a warning. A value below 1 is invalid.
    param([int]$Default = 8)

    if ($env:UH_ENSURE_EDITOR_QUARANTINE_MOVE_RETRY_ATTEMPTS) {
        $parsed = 0
        if (
            [int]::TryParse($env:UH_ENSURE_EDITOR_QUARANTINE_MOVE_RETRY_ATTEMPTS, [ref]$parsed) -and
            $parsed -ge 1
        ) {
            return $parsed
        }
        Write-Host "::warning::Ignoring invalid UH_ENSURE_EDITOR_QUARANTINE_MOVE_RETRY_ATTEMPTS='$env:UH_ENSURE_EDITOR_QUARANTINE_MOVE_RETRY_ATTEMPTS'; using $Default attempt(s)."
    }
    return $Default
}

function Get-EnsureEditorAndroidInstallRetryAttempts {
    # Single source of truth for the DEDICATED Android module-install retry count
    # used by Install-UnityAndroidModules. The Android SDK/NDK is a multi-GB Google
    # download whose NDK UNPACK phase (~93%) fails flakily on Windows (suspected
    # MAX_PATH during extraction, or Defender file-locking), so existing editors
    # get a bounded Android-only repair before the script escalates to a
    # profile-scoped managed quarantine/reinstall with the selected
    # Android-capable provisioning profile. Honors UH_ENSURE_EDITOR_ANDROID_INSTALL_RETRY_ATTEMPTS
    # following the EXACT convention of Get-EnsureEditorInstallRetryAttempts. The
    # DEFAULT is 3 (one more than the base-install default of 2) because the Android
    # unpack flake is the specific failure this loop targets and an extra bounded,
    # editor-preserving attempt is cheaper than managed quarantine/reinstall. A value below 1 is
    # invalid; a non-integer/negative override is ignored with a ::warning::.
    # StrictMode-safe: no collection reads.
    param([int]$Default = 3)

    if ($env:UH_ENSURE_EDITOR_ANDROID_INSTALL_RETRY_ATTEMPTS) {
        $parsed = 0
        if (
            [int]::TryParse($env:UH_ENSURE_EDITOR_ANDROID_INSTALL_RETRY_ATTEMPTS, [ref]$parsed) -and
            $parsed -ge 1
        ) {
            return $parsed
        }
        Write-Host "::warning::Ignoring invalid UH_ENSURE_EDITOR_ANDROID_INSTALL_RETRY_ATTEMPTS='$env:UH_ENSURE_EDITOR_ANDROID_INSTALL_RETRY_ATTEMPTS'; using $Default attempt(s)."
    }
    return $Default
}

function Get-CollapsedCliOutputTail {
    # PURE, StrictMode-safe diagnostic formatter. Takes the captured CLI output
    # lines and COLLAPSES consecutive identical lines into a single line annotated
    # with a repeat count, then returns the LAST $MaxLines of the collapsed result
    # joined with newlines. This is what makes a failed install READABLE: the
    # Android NDK install can spam thousands of IDENTICAL progress lines
    # (`{"type":"progress","pct":96,"msg":"Installing Android NDK..."}`), and the
    # previous "last 20-40 raw lines" tail was therefore thousands of copies of
    # the same line -- useless. Collapsing first means the tail shows DISTINCT
    # recent activity, e.g. `{"...Installing Android NDK..."}  (x3847)`.
    #
    # Contract:
    #   * A run of N (N >= 2) consecutive identical lines becomes ONE line with a
    #     "  (xN)" suffix; a non-repeated line passes through UNCHANGED (no suffix).
    #   * Only the LAST $MaxLines COLLAPSED entries are returned (cap respected
    #     AFTER collapsing, so the cap counts distinct runs, not raw duplicates).
    #   * Empty/whitespace-only input returns the literal '(no output captured)'.
    # StrictMode-safe: @()-wraps the input so a 0/1/many capture never unwraps to
    # AutomationNull, and never indexes a possibly-$null value.
    param(
        [string[]]$Output,
        [int]$MaxLines = 40
    )

    # @()-wrap defends against the 0/1/many AutomationNull hazard, BUT note @($null)
    # is a ONE-element array whose single element is $null -- so .Count is 1, not 0.
    # Treat input that is empty OR carries no non-whitespace content (all $null /
    # all-blank lines) as "nothing to report" and return the placeholder, exactly as
    # the contract above promises. Casting each element via [string] makes $null ->
    # '' so the Trim() probe is StrictMode-safe and never indexes a $null value.
    $capturedLines = @($Output)
    $hasContent = $false
    foreach ($probe in $capturedLines) {
        if (([string]$probe).Trim().Length -gt 0) {
            $hasContent = $true
            break
        }
    }
    if (-not $hasContent) {
        return '(no output captured)'
    }

    if ($MaxLines -lt 1) {
        $MaxLines = 1
    }

    # Collapse consecutive identical lines into "<line>  (xN)" (N >= 2) or the
    # bare line (N == 1). Build the collapsed list in order.
    $collapsed = New-Object System.Collections.Generic.List[string]
    $previous = $null
    $havePrevious = $false
    $runLength = 0
    foreach ($rawLine in $capturedLines) {
        $line = [string]$rawLine
        if ($havePrevious -and $line -eq $previous) {
            $runLength++
            continue
        }
        if ($havePrevious) {
            if ($runLength -gt 1) {
                $collapsed.Add("$previous  (x$runLength)")
            } else {
                $collapsed.Add($previous)
            }
        }
        $previous = $line
        $havePrevious = $true
        $runLength = 1
    }
    if ($havePrevious) {
        if ($runLength -gt 1) {
            $collapsed.Add("$previous  (x$runLength)")
        } else {
            $collapsed.Add($previous)
        }
    }

    $collapsedArray = @($collapsed.ToArray())
    if ($collapsedArray.Count -eq 0) {
        return '(no output captured)'
    }

    $tailCount = [Math]::Min($MaxLines, $collapsedArray.Count)
    $tailLines = @($collapsedArray[($collapsedArray.Count - $tailCount)..($collapsedArray.Count - 1)])
    return ($tailLines -join "`n")
}

function Get-CliProgressTriple {
    # PURE, StrictMode-safe extractor for the (pct, phase, msg) progress TRIPLE
    # from a single captured CLI line. Returns a hashtable with three string
    # fields (any missing field is the empty string), or $null if the line is
    # NOT a JSON progress line. Used by Invoke-UnityCliCaptureWithTimeout to keep
    # human-readable notices and failure diagnostics anchored to the latest Unity
    # progress state without treating repeated identical progress as a liveness
    # failure by itself.
    #
    # Deliberately regex-based (no ConvertFrom-Json): the lines are interleaved
    # progress spam, not a single JSON document, and a malformed/non-JSON beta
    # line must never throw here. Mirrors Get-LastCliProgressMessage's parsing
    # idiom so the two scanners stay in lockstep on field shape.
    param([string]$Line)

    $text = [string]$Line
    if ($text.Length -eq 0) {
        return $null
    }
    # Must carry the progress shape; otherwise it is plainly not a progress line.
    if ($text -notmatch '"type"\s*:\s*"progress"') {
        return $null
    }
    $pctMatch = [regex]::Match($text, '"pct"\s*:\s*(\d+)')
    $phaseMatch = [regex]::Match($text, '"phase"\s*:\s*"((?:\\.|[^"\\])*)"')
    $msgMatch = [regex]::Match($text, '"msg"\s*:\s*"((?:\\.|[^"\\])*)"')
    $pct = if ($pctMatch.Success) { $pctMatch.Groups[1].Value } else { '' }
    $phase = if ($phaseMatch.Success) { $phaseMatch.Groups[1].Value } else { '' }
    $msg = if ($msgMatch.Success) { $msgMatch.Groups[1].Value } else { '' }
    if ($pct -eq '' -and $phase -eq '' -and $msg -eq '') {
        return $null
    }
    return @{
        Pct = $pct
        Phase = $phase
        Msg = $msg
    }
}

function Get-LastCliProgressMessage {
    # PURE, StrictMode-safe extractor for the MOST DIAGNOSTIC progress message in
    # the captured CLI output, for a wrap-immune one-line failure summary. The
    # standalone Unity CLI emits JSON progress lines shaped like
    # `{"type":"progress","phase":"install","pct":93,"msg":"Installing Android NDK"}`.
    #
    # WHY THE INSTALL-PHASE/MAX-PCT PREFERENCE (the fix): the CLI interleaves
    # DOWNLOAD-phase and INSTALL-phase progress, and on a failure the LAST line
    # carrying a `"msg"` is frequently an OUT-OF-ORDER download line (e.g. a late
    # `"Starting install..."` for some other module) -- so naively reporting the
    # last `"msg"` MASKED the true failing module (the Android NDK unpack at 93%).
    # Instead we scan ALL lines and, among INSTALL-phase lines (`"phase":"install"`),
    # track the `"msg"` seen at the MAXIMUM `"pct"`; that is the deepest the
    # installer got before dying (e.g. `Installing Android NDK (93%)`), which is
    # the actionable datum. We return it as `"<msg> (<maxpct>%)"`.
    #
    # Fallbacks (unchanged order): if no install-phase msg is found, the LAST line
    # carrying ANY JSON `"msg"`; else the LAST non-empty captured line; else the
    # literal '(no output captured)'.
    #
    # Deliberately regex-based (no ConvertFrom-Json): the lines are interleaved
    # progress spam, not a single JSON document, and a malformed/non-JSON beta
    # line must never throw here. StrictMode-safe: @()-wraps the input.
    param([string[]]$Output)

    $capturedLines = @($Output)
    if ($capturedLines.Count -eq 0) {
        return '(no output captured)'
    }

    # PREFERRED: the install-phase message at the highest pct seen. Scan ALL lines;
    # for any line that is in the install phase AND carries both a pct and a msg,
    # remember the msg at the maximum pct. This is immune to out-of-order trailing
    # download lines that would otherwise mask the real failing module.
    $bestInstallMsg = $null
    $bestInstallPct = -1
    foreach ($raw in $capturedLines) {
        $line = [string]$raw
        # This phase test could in THEORY match the literal `"phase":"install"`
        # appearing INSIDE a quoted "msg" value, but a real Unity progress message
        # never embeds that token (the msg is human text like "Installing Android
        # NDK"), so there is no realistic trigger; deliberately left as a simple
        # substring match rather than a brittle full-JSON parse of interleaved spam.
        if ($line -notmatch '"phase"\s*:\s*"install"') {
            continue
        }
        $pctMatch = [regex]::Match($line, '"pct"\s*:\s*(\d+)')
        $msgMatch = [regex]::Match($line, '"msg"\s*:\s*"((?:\\.|[^"\\])*)"')
        if (-not ($pctMatch.Success -and $msgMatch.Success)) {
            continue
        }
        $pct = [int]$pctMatch.Groups[1].Value
        if ($pct -ge $bestInstallPct) {
            $bestInstallPct = $pct
            $bestInstallMsg = $msgMatch.Groups[1].Value
        }
    }
    if ($null -ne $bestInstallMsg) {
        return "$bestInstallMsg ($bestInstallPct%)"
    }

    # FALLBACK 1: scan from the END for the last line carrying a JSON "msg":"..." field.
    for ($i = $capturedLines.Count - 1; $i -ge 0; $i--) {
        $line = [string]$capturedLines[$i]
        $match = [regex]::Match($line, '"msg"\s*:\s*"((?:\\.|[^"\\])*)"')
        if ($match.Success) {
            return $match.Groups[1].Value
        }
    }

    # FALLBACK 2: no JSON progress message: fall back to the last non-empty captured line.
    for ($i = $capturedLines.Count - 1; $i -ge 0; $i--) {
        $line = ([string]$capturedLines[$i]).Trim()
        if ($line.Length -gt 0) {
            return $line
        }
    }

    return '(no output captured)'
}

function Invoke-WithUnityInstallLock {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$InstallRoot,
        [Parameter(Mandatory = $true)][scriptblock]$Action,
        [int]$TimeoutMinutes = 180
    )

    if ($script:UnityInstallLockDepth -gt 0) {
        return & $Action
    }

    $lockRoot = Join-Path $InstallRoot '_locks'
    New-Item -ItemType Directory -Force -Path $lockRoot | Out-Null
    $lockPath = Join-Path $lockRoot "$Version-ci-modules.lock"
    $deadline = [DateTime]::UtcNow.AddMinutes($TimeoutMinutes)
    $stream = $null

    while ($null -eq $stream) {
        try {
            $stream = [System.IO.File]::Open(
                $lockPath,
                [System.IO.FileMode]::OpenOrCreate,
                [System.IO.FileAccess]::ReadWrite,
                [System.IO.FileShare]::None
            )
        } catch {
            if ([DateTime]::UtcNow -ge $deadline) {
                throw "Timed out waiting for Unity install lock '$lockPath' after $TimeoutMinutes minute(s)."
            }
            Write-Host "::notice::Waiting for Unity install lock: $lockPath"
            Start-Sleep -Seconds 10
        }
    }

    try {
        $script:UnityInstallLockDepth++
        return & $Action
    } finally {
        $script:UnityInstallLockDepth--
        if ($stream) {
            $stream.Dispose()
        }
    }
}

function Get-UnityEditorCandidates {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Root,
        [switch]$IncludeHostInstalls
    )

    $candidates = New-Object System.Collections.Generic.List[string]
    $versionRoot = Join-Path $Root $Version
    $candidates.Add((Join-Path (Join-Path $versionRoot 'Editor') 'Unity.exe'))
    $candidates.Add((Join-Path $versionRoot 'Unity.exe'))

    $alternateRoot = Get-UnityCiAlternateInstallRoot -InstallRoot $Root
    $alternateVersionRoot = Join-Path $alternateRoot $Version
    $candidates.Add((Join-Path (Join-Path $alternateVersionRoot 'Editor') 'Unity.exe'))
    $candidates.Add((Join-Path $alternateVersionRoot 'Unity.exe'))

    if ($IncludeHostInstalls -and ${env:ProgramFiles} -and ${env:ProgramFiles}.Trim().Length -gt 0) {
        $candidates.Add((Join-Path ${env:ProgramFiles} "Unity\Hub\Editor\$Version\Editor\Unity.exe"))
        $candidates.Add((Join-Path ${env:ProgramFiles} "Unity\$Version\Editor\Unity.exe"))
    }
    if ($IncludeHostInstalls -and ${env:ProgramFiles(x86)} -and ${env:ProgramFiles(x86)}.Trim().Length -gt 0) {
        $candidates.Add(
            (Join-Path ${env:ProgramFiles(x86)} "Unity\Hub\Editor\$Version\Editor\Unity.exe")
        )
    }

    return @($candidates.ToArray() | Where-Object { $_ -and $_.Trim().Length -gt 0 })
}

function Get-UnityCiAlternateInstallRoot {
    param([Parameter(Mandatory = $true)][string]$InstallRoot)

    $fullRoot = [System.IO.Path]::GetFullPath($InstallRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    if ((Split-Path -Leaf $fullRoot) -eq '_ci-managed-editors') {
        return $fullRoot
    }

    return (Join-Path $fullRoot '_ci-managed-editors')
}

function Find-UnityEditor {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Root,
        [switch]$IncludeHostInstalls
    )

    foreach ($candidate in Get-UnityEditorCandidates -Version $Version -Root $Root -IncludeHostInstalls:$IncludeHostInstalls) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    return $null
}

function Find-UnityCiAlternateEditorWithCiModules {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$InstallRoot,
        [Parameter(Mandatory = $true)][string]$Profile
    )

    $alternateRoot = Get-UnityCiAlternateInstallRoot -InstallRoot $InstallRoot
    $alternateEditor = Find-UnityEditor -Version $Version -Root $alternateRoot
    if (-not $alternateEditor) {
        return $null
    }

    $missing = @(Get-MissingUnityCiModuleGroups -EditorPath $alternateEditor -Profile $Profile)
    if ($missing.Count -eq 0) {
        Write-CiNotice "Using reusable alternate-root CI editor for Unity $Version with provisioning profile '$Profile': $alternateEditor"
        return $alternateEditor
    }

    Write-CiNotice "Alternate-root CI editor for Unity $Version exists at '$alternateEditor' but is missing required module groups for provisioning profile '$Profile': $($missing -join ', ')."
    return $null
}

function Update-SessionPathFromRegistry {
    # The standalone Unity CLI installer writes %LOCALAPPDATA%\Unity\bin\unity.exe
    # and updates only the User-scope registry PATH; it never refreshes the
    # current session's $env:PATH. Rebuild the session PATH from the persisted
    # Machine + User registry values so the freshly installed CLI resolves in
    # this process, and prepend the installer's known target in case the
    # registry write lags. CRUCIAL: this .ps1 shares the caller's process
    # environment, so the existing $env:PATH carries process-only entries (e.g.
    # node added by setup-node via $GITHUB_PATH). Append it as the FINAL segment
    # so those entries survive instead of being clobbered.
    $segments = New-Object System.Collections.Generic.List[string]

    if ($env:LOCALAPPDATA) {
        $segments.Add((Join-Path $env:LOCALAPPDATA 'Unity\bin'))
    }

    $machinePath = [System.Environment]::GetEnvironmentVariable('Path', 'Machine')
    if ($machinePath) {
        $segments.Add($machinePath)
    }

    $userPath = [System.Environment]::GetEnvironmentVariable('Path', 'User')
    if ($userPath) {
        $segments.Add($userPath)
    }

    if ($env:PATH) {
        $segments.Add($env:PATH)
    }

    $env:PATH = (($segments | Where-Object { $_ -and $_.Trim().Length -gt 0 }) -join ';')
}

function Ensure-UnityCli {
    $command = Get-Command unity -ErrorAction SilentlyContinue
    if ($command) {
        $script:UnityCliPath = $command.Source
        return $command.Source
    }

    Write-CiNotice "Unity CLI was not found on PATH; installing the standalone Unity CLI for this runner."
    $env:UNITY_CLI_CHANNEL = if ($env:UNITY_CLI_CHANNEL) { $env:UNITY_CLI_CHANNEL } else { 'beta' }
    Invoke-Expression (Invoke-RestMethod 'https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.ps1')

    $maxTries = 3
    for ($try = 1; $try -le $maxTries; $try++) {
        Update-SessionPathFromRegistry
        $command = Get-Command unity -ErrorAction SilentlyContinue
        if ($command) {
            $script:UnityCliPath = $command.Source
            return $command.Source
        }
        if ($try -lt $maxTries) {
            Start-Sleep -Seconds 2
        }
    }

    if ($env:LOCALAPPDATA) {
        $fallback = Join-Path $env:LOCALAPPDATA 'Unity\bin\unity.exe'
        if (Test-Path -LiteralPath $fallback -PathType Leaf) {
            $script:UnityCliPath = (Resolve-Path -LiteralPath $fallback).Path
            return $script:UnityCliPath
        }
    }

    throw "Unity CLI installation completed but 'unity' is still not on PATH. Reopen the runner shell or add the Unity CLI install directory to PATH."
}

function Invoke-UnityCliSafe {
    # NON-THROWING best-effort invoker. The standalone Unity CLI is a moving
    # beta surface (v0.1.0-beta.x); some flags are undocumented and may differ
    # between releases. For optional operations (setting the install path,
    # probing module ids) a non-zero exit must NOT abort the bootstrap, so this
    # variant returns $true/$false and never throws on a non-zero exit code.
    # It echoes the command and surfaces any output via Write-Host so failures
    # remain diagnosable in CI logs. Captured-output callers should use
    # Get-UnityCliOutput instead; this one is for fire-and-forget effects.
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [string]$AcceptCapturedOutputPattern = ''
    )

    $requestedTimeout = Get-EnsureEditorProbeTimeoutSeconds
    $effectiveTimeout = Get-EffectiveUnityCliTimeoutSeconds -RequestedSeconds $requestedTimeout
    $acceptTimedOutOutput = -not [string]::IsNullOrWhiteSpace($AcceptCapturedOutputPattern)
    $result = Invoke-UnityCliCaptureWithTimeout -Arguments $Arguments -TimeoutSeconds $effectiveTimeout -TimeoutKnob 'UH_ENSURE_EDITOR_PROBE_TIMEOUT_SECONDS' -TimeoutAsWarning:$acceptTimedOutOutput
    $exit = [int]$result.ExitCode
    if ($exit -eq 0) {
        return $true
    }

    $wrapperKilled = [bool]$result.StallKilled -or [bool]$result.TimedOutWallClock
    if ($wrapperKilled -and $acceptTimedOutOutput) {
        foreach ($line in @($result.Output)) {
            if ([string]$line -match $AcceptCapturedOutputPattern) {
                Write-CiNotice "Unity CLI emitted the expected completion output before its lingering process tree was stopped."
                return $true
            }
        }
    }

    return $false
}

function Get-UnityCliOutput {
    # CAPTURING, NON-THROWING invoker for getter-style commands (install-path,
    # editors -i --format json). Returns an array of output lines (strings) on
    # success, or $null on any failure. Does NOT echo to the success pipeline
    # of this script: the caller (run-ci-tests.ps1) reads our LAST stdout line
    # as the resolved editor path, so getter output must never leak there.
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [switch]$AcceptCapturedOutputOnTimeout
    )

    $requestedTimeout = Get-EnsureEditorProbeTimeoutSeconds
    $effectiveTimeout = Get-EffectiveUnityCliTimeoutSeconds -RequestedSeconds $requestedTimeout
    $result = Invoke-UnityCliCaptureWithTimeout -Arguments $Arguments -TimeoutSeconds $effectiveTimeout -TimeoutKnob 'UH_ENSURE_EDITOR_PROBE_TIMEOUT_SECONDS' -TimeoutAsWarning:$AcceptCapturedOutputOnTimeout
    if ($result.ExitCode -ne 0) {
        $wrapperKilled = [bool]$result.StallKilled -or [bool]$result.TimedOutWallClock
        if (-not $AcceptCapturedOutputOnTimeout -or -not $wrapperKilled) {
            return $null
        }
        Write-CiNotice "Unity CLI output was captured before its lingering process tree was stopped."
    }

    # Normalize to an array of strings regardless of whether 0/1/many lines came
    # back (a single line returns a scalar under the call operator).
    return @($result.Output | ForEach-Object { [string]$_ })
}

function Get-UnityCliVersionText {
    if ($script:UnityCliVersionText) {
        return $script:UnityCliVersionText
    }

    try {
        $versionLines = @(Get-UnityCliOutput -Arguments @('--version'))
        if ($versionLines.Count -gt 0) {
            $script:UnityCliVersionText = ($versionLines -join ' ')
        } else {
            $script:UnityCliVersionText = '(unavailable)'
        }
    } catch {
        $script:UnityCliVersionText = "(query failed: $($_.Exception.Message))"
    }

    return $script:UnityCliVersionText
}

function Invoke-UnityCliCapture {
    # CAPTURING, NON-THROWING invoker that returns BOTH the exit status AND the
    # full output text, while STILL streaming output live to the console. The
    # other live invokers each give only part of this: Invoke-UnityCliSafe returns
    # a bool (no output, no exit code), and Get-UnityCliOutput captures lines but
    # returns $null on a non-zero exit (discarding the very output you need to
    # diagnose a failure). For classification logic that must inspect WHY a
    # command failed (e.g. the IL2CPP "No modules found to install" no-op) we need
    # the exit code + output together, so this helper provides both.
    #
    # Returns a StrictMode-safe hashtable with:
    #   Success           [bool]     - $true when exit code is 0
    #   ExitCode          [int]      - the native exit code (-1 if the call threw/spawn failed;
    #                                  124 for wall-clock timeout, 125 for heartbeat-stall kill)
    #   Output            [string[]] - @()-wrapped stdout+stderr lines (never $null)
    #   StallKilled       [bool]     - $true when killed by the heartbeat-stall detector
    #                                  (no stdout/stderr line for the stall window); see
    #                                  Invoke-UnityCliCaptureWithTimeout
    #   TimedOutWallClock [bool]     - $true when killed by the absolute wall-clock timeout;
    #                                  mutually exclusive with StallKilled
    # Every field is always populated, so callers can read .Output.Count and
    # index .Output without the 0/1/many AutomationNull hazard.
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    # DELEGATE to the timeout-capable runner so EVERY captured CLI invocation
    # (install/repair/module-add/uninstall) is bounded by a total wall-clock
    # timeout and cannot hang until the GitHub job is cancelled. The contract of
    # this function is UNCHANGED: same per-line LIVE streaming (the timeout runner
    # echoes each line the instant it arrives, exactly like this function's
    # original `& $cli | ForEach-Object { Write-Host }` did), same 2>&1 merge
    # semantics, return shape `@{ Success; ExitCode; Output; StallKilled; TimedOutWallClock }`
    # (the last two added with the heartbeat-stall detector; see the header for field
    # semantics), same exit code on normal completion, same catch-on-spawn-failure behavior
    # (the timeout runner maps a
    # spawn failure to ExitCode -1 with the message in Output, exactly as before).
    # The timeout is sourced from the single override-aware helper so tests can
    # force the timeout path (small value) or opt out (0) without changing callers.
    $requestedTimeout = if (Get-Command Get-EnsureEditorInstallTimeoutSeconds -ErrorAction SilentlyContinue) {
        Get-EnsureEditorInstallTimeoutSeconds
    } else {
        2700
    }
    $effectiveTimeout = if (Get-Command Get-EffectiveUnityCliTimeoutSeconds -ErrorAction SilentlyContinue) {
        Get-EffectiveUnityCliTimeoutSeconds -RequestedSeconds $requestedTimeout
    } else {
        $requestedTimeout
    }
    return Invoke-UnityCliCaptureWithTimeout -Arguments $Arguments -TimeoutSeconds $effectiveTimeout
}

function Invoke-UnityCliCaptureWithTimeout {
    # TIMEOUT-CAPABLE, CAPTURING, NON-THROWING invoker -- the resilience core. It
    # is the implementation Invoke-UnityCliCapture delegates to, and it preserves
    # that function's EXACT contract on the normal-completion path while adding a
    # total wall-clock timeout that a hung install (the Android NDK hang that gets
    # the GitHub job cancelled) cannot exceed.
    #
    # WHY System.Diagnostics.Process and NOT `& <cli>`: the call operator cannot
    # be interrupted -- a hung child runs until the whole job is killed, so the
    # retry never fires and no diagnostics are produced. A Process lets us enforce
    # a wall-clock deadline in the poll loop below and Kill($true) (tree-kill) the
    # whole tree, so a hang is bounded, killed, classified as a (retryable)
    # failure, and annotated.
    #
    # WHY A MAIN-THREAD POLL LOOP OVER TWO ASYNC LINE READS: two invariants must
    # hold AT ONCE -- (1) every line is echoed LIVE the instant it arrives, so a
    # long (45-minute) install is never a silent, blank console where an observer
    # cannot tell a slow install from a hang; and (2) the run is bounded by a total
    # wall-clock deadline. A single ReadToEndAsync per stream satisfies (2) but
    # VIOLATES (1): it yields nothing until the process EXITS, so the whole install
    # streams as one burst at the end (empirically line 1 appeared at process exit,
    # not within ~60ms of being printed). We instead keep ONE outstanding
    # ReadLineAsync per stream and poll BOTH from the main thread: when a line is
    # ready we Write-Host it immediately (live), buffer it, and issue the next
    # ReadLineAsync; every iteration also checks the deadline. Both pipes are always
    # being drained, so neither can fill and back-pressure the child (the classic
    # full-pipe-buffer deadlock is impossible). The reads run on the MAIN thread on
    # purpose: a PowerShell scriptblock has no runspace on an arbitrary threadpool
    # thread, so Write-Host from a Task/Register-ObjectEvent -Action either has no
    # console or (for eventing) delivers lines OUT OF ORDER -- the poll loop avoids
    # both by doing all the I/O and echoing inline. On a deadline hit we tree-kill,
    # which closes the pipes so the outstanding ReadLineAsync tasks complete; we then
    # drain any already-finished line tasks so no pre-kill output is lost. The two
    # streams are merged into a single arrival-order buffer: this reproduces a
    # captured `2>&1` closely enough (the old code also did not interleave the two
    # streams once captured) and ALL downstream consumers of .Output are order-
    # independent (tail de-dup, last-progress parse, substring matches), so
    # arrival-order is acceptable and, for live echo, strictly more faithful.
    #
    # HEARTBEAT-STALL DETECTOR: when no stdout/stderr line has arrived for >=
    # $StallSeconds, the command is classified as hung and tree-killed with sentinel
    # exit 125 (distinct from the wall-clock 124 so callers and tests can tell
    # hang-detected from wall-timeout-elapsed). Repeated identical progress lines
    # are still live output: Unity module installs can legitimately report the same
    # pct/phase/msg throughout a long on-disk install phase. The total wall-clock
    # timeout remains the bound for a chatty no-advance install.
    #
    # The periodic ::notice:: emitted every PROGRESS_NOTICE_INTERVAL_SECONDS
    # makes the live CI log human-readable mid-flight (the alternative is a
    # 4,672-line dupe wall the reader has to scroll past); it is GATED ON
    # WALL-CLOCK TIME, not on every output line, so it cannot dilute the live
    # progress stream.
    #
    # Returns a SUPERSET of Invoke-UnityCliCapture's StrictMode-safe shape, with
    # two additional fields so downstream classifiers can attribute a 125 exit to
    # WHO actually killed the process (NOT to the raw exit code alone -- a
    # native exit 125 from the Unity CLI must NOT be misread as "heartbeat
    # stalled"). All callers that ONLY consume Success / ExitCode / Output
    # continue to work unchanged; the diagnostic sites read the new fields when
    # they need to phrase the failure correctly.
    #   Success            [bool]     - $true when exit code is 0
    #   ExitCode           [int]      - native exit (-1 on spawn failure; the
    #                                   wall-clock timeout sentinel 124 on a
    #                                   wall-clock kill; the heartbeat-stall
    #                                   sentinel 125 on a stall kill OR on a
    #                                   native non-killed 125 from Unity CLI)
    #   Output             [string[]] - @()-wrapped merged stdout+stderr lines
    #                                   (never $null)
    #   StallKilled        [bool]     - $true ONLY when the wrapper's heartbeat
    #                                   detector killed the process (sentinel
    #                                   exit 125 from THIS wrapper). $false on
    #                                   a NATIVE 125 from the CLI.
    #   TimedOutWallClock  [bool]     - $true ONLY when the wrapper's wall-clock
    #                                   deadline killed the process (sentinel
    #                                   exit 124 from THIS wrapper). $false on
    #                                   a NATIVE 124 from the CLI.
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [int]$TimeoutSeconds = 2700,
        [string]$TimeoutKnob = 'UH_ENSURE_EDITOR_INSTALL_TIMEOUT_SECONDS',
        [int]$StallSeconds = -1,
        [string]$StallKnob = 'UH_ENSURE_EDITOR_PROGRESS_STALL_SECONDS',
        [switch]$TimeoutAsWarning
    )

    # Default the stall threshold from the env-aware helper when the caller did
    # not pass one. -1 means "unset" so 0 (explicit opt-out) remains distinguishable
    # from "not specified" -- 0 disables the heartbeat detector entirely.
    if ($StallSeconds -lt 0) {
        $StallSeconds = Get-EnsureEditorProgressStallSeconds
    }

    if (Get-Command Register-UnityCliCommandAttempt -ErrorAction SilentlyContinue) {
        Register-UnityCliCommandAttempt -Arguments $Arguments
    }
    Write-Host "$script:UnityCliPath $($Arguments -join ' ')"

    # Sentinel exit code for a WALL-CLOCK TIMEOUT kill. 124 mirrors GNU coreutils
    # `timeout` (it exits 124 when the command times out), so the code is
    # recognizable in logs; it is non-zero, so the standard non-zero-exit
    # classification (a retryable failure) applies without any special-casing.
    $timeoutExitCode = 124
    # Sentinel exit code for a HEARTBEAT-STALL kill. 125 is one above the
    # wall-clock sentinel so callers (and tests) can tell the two failure modes
    # apart at a glance; both are RETRYABLE and treated identically by
    # Write-ModuleInstallFailureDiagnostics + the install retry classifier.
    $stallExitCode = 125
    # Live-log periodic ::notice:: cadence. 60s default balances "human can see
    # progress mid-flight" against "do not bury the live progress stream"; lower
    # would spam, higher would lose actionability on the stall path. Sourced via
    # the env-aware helper so tests can drop the cadence to a few seconds
    # without forcing the suite to wait the production minute; 0 opts out.
    $progressNoticeIntervalSeconds = Get-EnsureEditorProgressNoticeIntervalSeconds
    $progressNoticeEnabled = ($progressNoticeIntervalSeconds -gt 0)

    # Ordered capture buffer. Appended ONLY from the main-thread poll loop (and the
    # spawn-failure catch), so no synchronization is needed.
    $buffer = New-Object System.Collections.Generic.List[string]

    # A timeout of 0 (or negative) is the explicit OPT-OUT: wait indefinitely,
    # matching the prior unbounded behavior. Otherwise convert seconds to the ms the
    # deadline math uses, guarding against Int64 overflow on a very large value.
    if ($TimeoutSeconds -le 0) {
        $hasDeadline = $false
        $timeoutMs = -1
    } else {
        $hasDeadline = $true
        $timeoutMsLong = [int64]$TimeoutSeconds * 1000
        if ($timeoutMsLong -gt [int64]::MaxValue - 1) {
            $timeoutMs = [int64]::MaxValue - 1
        } else {
            $timeoutMs = $timeoutMsLong
        }
    }

    $proc = $null
    $exit = -1
    $timedOut = $false
    $reaped = $false
    # Declared at the OUTER scope (not just inside the try) so a spawn failure
    # that lands in the catch still leaves both kill-state booleans defined
    # for the StrictMode-safe return-shape construction below.
    $stalled = $false
    try {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $script:UnityCliPath
        $psi.Arguments = ConvertTo-ProcessArgumentLine -Arguments $Arguments
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true

        $proc = New-Object System.Diagnostics.Process
        $proc.StartInfo = $psi

        [void]$proc.Start()

        # Keep ONE outstanding async line read per stream and poll both from the
        # main thread. A completed read whose Result is $null means that stream
        # reached EOF (the pipe closed); any other Result is a line we echo LIVE,
        # buffer, and immediately re-arm with the next ReadLineAsync. Because both
        # streams are continuously drained, neither pipe can fill and block the
        # child (no full-pipe-buffer deadlock).
        $outReader = $proc.StandardOutput
        $errReader = $proc.StandardError
        $oTask = $outReader.ReadLineAsync()
        $eTask = $errReader.ReadLineAsync()

        # Absolute deadline (UtcNow is monotonic-enough for a wall-clock budget and
        # immune to the local-clock skew a relative subtraction would risk). When
        # the timeout is opted out the deadline is DateTime.MaxValue (never fires).
        if ($hasDeadline) {
            $deadline = [DateTime]::UtcNow.AddMilliseconds([double]$timeoutMs)
        } else {
            $deadline = [DateTime]::MaxValue
        }

        # Heartbeat-stall + periodic-notice state. The "last triple" is the most
        # recently observed (pct, phase, msg); it feeds diagnostics only. The stall
        # clock restarts on ANY output line because repeated identical progress can
        # be a healthy Unity install. The notice clock is independent (time-gated,
        # not output-gated) so a long advancing install still gets a human-readable
        # cadence in the live log instead of the raw dupe wall. Opt-out semantics:
        # $StallSeconds == 0 disables heartbeat-kill entirely; $startedAt is the
        # wall-clock anchor for the elapsed fields in the notice.
        $startedAt = [DateTime]::UtcNow
        $lastOutputAt = $startedAt
        $lastTripleAdvanceAt = $startedAt
        $lastNoticeAt = $startedAt
        $lastTripleKey = $null
        $lastTriple = $null
        $stallEnabled = ($StallSeconds -gt 0)
        $stalled = $false

        $oDone = $false
        $eDone = $false
        while (-not ($oDone -and $eDone)) {
            $progressed = $false

            if (-not $oDone -and $oTask.Wait(0)) {
                $line = $oTask.Result
                if ($null -eq $line) {
                    $oDone = $true
                } else {
                    Write-Host $line
                    $buffer.Add([string]$line)
                    $lineReceivedAt = [DateTime]::UtcNow
                    $lastOutputAt = $lineReceivedAt
                    # Triple advance: if this line is a JSON progress line whose
                    # (pct, phase, msg) differs from the last observed triple, the
                    # install reached a new diagnostic state; keep that timestamp
                    # for notices without using it as the liveness clock.
                    $triple = Get-CliProgressTriple -Line $line
                    if ($null -ne $triple) {
                        $key = "$($triple.Pct)|$($triple.Phase)|$($triple.Msg)"
                        if ($key -ne $lastTripleKey) {
                            $lastTripleKey = $key
                            $lastTriple = $triple
                            $lastTripleAdvanceAt = $lineReceivedAt
                        }
                    }
                    $oTask = $outReader.ReadLineAsync()
                }
                $progressed = $true
            }

            if (-not $eDone -and $eTask.Wait(0)) {
                $line = $eTask.Result
                if ($null -eq $line) {
                    $eDone = $true
                } else {
                    Write-Host $line
                    $buffer.Add([string]$line)
                    $lineReceivedAt = [DateTime]::UtcNow
                    $lastOutputAt = $lineReceivedAt
                    $triple = Get-CliProgressTriple -Line $line
                    if ($null -ne $triple) {
                        $key = "$($triple.Pct)|$($triple.Phase)|$($triple.Msg)"
                        if ($key -ne $lastTripleKey) {
                            $lastTripleKey = $key
                            $lastTriple = $triple
                            $lastTripleAdvanceAt = $lineReceivedAt
                        }
                    }
                    $eTask = $errReader.ReadLineAsync()
                }
                $progressed = $true
            }

            $nowUtc = [DateTime]::UtcNow

            # Periodic human-readable progress notice (time-gated, NOT per-line).
            # Reports the last triple + elapsed totals so an observer can see at a
            # glance how far the install has come, how long the output heartbeat has
            # been quiet, and how long the current triple has stayed the same.
            $sinceNotice = ($nowUtc - $lastNoticeAt).TotalSeconds
            if ($progressNoticeEnabled -and $sinceNotice -ge $progressNoticeIntervalSeconds) {
                $lastNoticeAt = $nowUtc
                $elapsedSec = [int][Math]::Floor(($nowUtc - $startedAt).TotalSeconds)
                $quietElapsedSec = [int][Math]::Floor(($nowUtc - $lastOutputAt).TotalSeconds)
                $sameTripleElapsedSec = [int][Math]::Floor(($nowUtc - $lastTripleAdvanceAt).TotalSeconds)
                if ($null -ne $lastTriple) {
                    $pctText = if ($lastTriple.Pct) { $lastTriple.Pct } else { '?' }
                    $phaseText = if ($lastTriple.Phase) { $lastTriple.Phase } else { '?' }
                    $msgText = if ($lastTriple.Msg) { $lastTriple.Msg } else { '?' }
                    Write-Host "::notice::Unity CLI install heartbeat: pct=$pctText phase=$phaseText msg=`"$msgText`" elapsed=${elapsedSec}s quietElapsed=${quietElapsedSec}s sameTripleElapsed=${sameTripleElapsedSec}s"
                } else {
                    Write-Host "::notice::Unity CLI install heartbeat: no progress line observed yet elapsed=${elapsedSec}s quietElapsed=${quietElapsedSec}s"
                }
            }

            # HEARTBEAT-STALL DETECTOR. Fires only when the operator has not opted
            # out AND no output line has arrived for >= the configured window.
            # Tree-kills with the distinct stall sentinel so the failure-diagnostic
            # path can name "heartbeat stall" specifically.
            if ($stallEnabled -and ($nowUtc - $lastOutputAt).TotalSeconds -ge $StallSeconds) {
                $stalled = $true
                $timedOut = $true
                try {
                    $proc.Kill($true)
                } catch {
                    try { $proc.Kill() } catch { }
                }
                break
            }

            if ($nowUtc -ge $deadline) {
                # HUNG (or a quick-exit child whose grandchild still holds the pipe
                # open, so EOF never arrives): kill the WHOLE process tree. The bool
                # overload Kill($true) terminates descendants on .NET Core / PS7 (the
                # Android NDK installer spawns child processes, so a bare Kill() would
                # orphan them); a descendant already reparented away from a
                # quick-exiting child is OS-unreachable by any tree walk -- the
                # CRITICAL fix here is that we no longer mistake that case for success.
                $timedOut = $true
                try {
                    $proc.Kill($true)
                } catch {
                    # Best-effort: the process may have exited between the check and
                    # the kill, or the platform may reject the descendant kill; fall
                    # back to a plain kill so at least the direct child dies.
                    try { $proc.Kill() } catch { }
                }
                break
            }

            # Only sleep when NEITHER stream produced a line this iteration, so a
            # busy stream is drained at full speed while an idle wait does not
            # burn a core spinning.
            if (-not $progressed) {
                Start-Sleep -Milliseconds 50
            }
        }

        # The loop ended either at EOF on both streams (normal/early exit) or at a
        # kill. Reap the process so ExitCode is valid, bounded so a stuck reap
        # cannot hang the harness.
        $reaped = $proc.WaitForExit(5000)

        # Drain any line reads that completed during/after the kill so no pre-kill
        # output is dropped. A non-$null Result is a buffered line; $null is EOF.
        foreach ($pending in @($oTask, $eTask)) {
            try {
                if ($pending.Wait(2000) -and $null -ne $pending.Result) {
                    $line = $pending.Result
                    Write-Host $line
                    $buffer.Add([string]$line)
                }
            } catch {
                # A faulted/cancelled read on a killed pipe carries nothing to add.
            }
        }

        if ($timedOut) {
            # Distinguish heartbeat-stall (125) from wall-clock timeout (124) so
            # callers + tests can attribute the failure mode precisely. Both are
            # treated as retryable by the install retry classifier.
            if ($stalled) {
                $exit = $stallExitCode
            } else {
                $exit = $timeoutExitCode
            }
        } else {
            # ExitCode is only valid after a CONFIRMED exit; HasExited guards the
            # rare case the bounded reap above did not catch a (non-killed) exit.
            if ($reaped -and $proc.HasExited) {
                $exit = $proc.ExitCode
            } else {
                $exit = $timeoutExitCode
                $timedOut = $true
            }
        }
    } catch {
        # Spawn/resolution failure (e.g. the CLI vanished or the path is bad).
        # Mirror Invoke-UnityCliCapture's original catch: surface the message in the
        # captured output AND emit it as a GitHub ::notice:: annotation (the prior
        # implementation did both), and report exit -1.
        $message = "Unity CLI capture invoker threw: $($_.Exception.Message)"
        Write-Host "::notice::$message"
        $buffer.Add($message)
        $exit = -1
    } finally {
        if ($proc) { $proc.Dispose() }
    }

    # Snapshot the captured lines (already streamed LIVE, in arrival order, by the
    # poll loop above) to a plain string[] for classification and the return value.
    $captured = @($buffer.ToArray())

    if ($timedOut) {
        if (Get-Command Add-ProvisioningTimeoutEvent -ErrorAction SilentlyContinue) {
            $timeoutReason = if ($stalled) { 'no-output-stall' } else { 'wall-clock' }
            $eventStallSeconds = if ($stalled) { $StallSeconds } else { 0 }
            Add-ProvisioningTimeoutEvent -Arguments $Arguments -TimeoutSeconds $TimeoutSeconds -Reason $timeoutReason -StallSeconds $eventStallSeconds
        }
        # Wrap-immune timeout annotation (Write-Host "::error::" is NOT subject to
        # ConciseView word-wrap): name the timeout, the configured limit, the env
        # knob to raise it, and the LAST progress message seen so CI has a stable,
        # greppable summary of WHAT hung. The normal throw/classification flow
        # still runs on the returned (retryable) failure. Reuse the de-duplicating
        # tail formatter so the surfaced lines are READABLE even when the CLI spammed
        # thousands of identical progress lines before the kill.
        $lastProgress = Get-LastCliProgressMessage -Output $captured
        $collapsedTail = Get-CollapsedCliOutputTail -Output $captured -MaxLines 10
        if ($stalled) {
            # HEARTBEAT-STALL kill: distinct sentinel (125) AND distinct annotation
            # wording so an observer can tell at a glance whether the install was
            # killed for "no stdout/stderr line in N seconds" (this branch) versus
            # "exceeded the total wall-clock budget" (the else branch below).
            $stallKnobName = if ($StallKnob) { $StallKnob } else { 'UH_ENSURE_EDITOR_PROGRESS_STALL_SECONDS' }
            $annotation = if ($TimeoutAsWarning) { 'warning' } else { 'error' }
            Write-Host "::$annotation::Unity CLI command '$($Arguments -join ' ')' HEARTBEAT STALLED after $StallSeconds second(s) with no Unity CLI stdout/stderr line; the process tree was killed (sentinel exit $stallExitCode). Raise the threshold via $stallKnobName (0 disables the heartbeat detector). Last progress message: $lastProgress. Collapsed tail:`n$collapsedTail"
        } else {
            $knob = if ($TimeoutKnob) { $TimeoutKnob } else { 'UH_ENSURE_EDITOR_INSTALL_TIMEOUT_SECONDS' }
            $annotation = if ($TimeoutAsWarning) { 'warning' } else { 'error' }
            Write-Host "::$annotation::Unity CLI command '$($Arguments -join ' ')' TIMED OUT after $TimeoutSeconds second(s) and the process tree was killed (sentinel exit $timeoutExitCode). Raise the limit via $knob (0 disables the timeout). Last progress message: $lastProgress. Collapsed tail:`n$collapsedTail"
        }
    }

    # StallKilled / TimedOutWallClock distinguish a WRAPPER-DRIVEN kill (the
    # heartbeat detector or the wall-clock deadline) from a NATIVE non-zero exit
    # that happens to share the same sentinel code. A native exit 125 from the
    # Unity CLI must NOT be misread as "heartbeat stalled" by downstream
    # classifiers, and a native exit 124 likewise must not be misread as
    # "wall-clock timeout." Both are derived ONLY from the in-process kill
    # state, never from $exit, so a coincidental native exit code cannot
    # impersonate a kill.
    return @{
        Success           = ($exit -eq 0)
        ExitCode          = $exit
        Output            = @($captured)
        StallKilled       = [bool]$stalled
        TimedOutWallClock = [bool]($timedOut -and -not $stalled)
    }
}

function Write-UnityCliInstallFailureAnnotation {
    # HIGH-SIGNAL, additive CI annotation for a FAILED module install. Scans the
    # captured CLI output for the two failure SIGNATURES this script has been bitten
    # by and emits a targeted ::error::/::warning:: that NAMES the remediation, so a
    # future regression of this exact class is obvious at a glance in the CI log
    # instead of buried in a generic exit-code dump. Additive only: callers still
    # throw/log their full message + arg vector + exit code separately. Best-effort
    # and StrictMode-safe: never throws, @()-wraps the output capture.
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [string[]]$Output,
        [int]$ExitCode = -1,
        [string[]]$Arguments
    )

    $text = (@($Output) -join "`n")
    $argLine = if ($Arguments) { ($Arguments -join ' ') } else { '(unavailable)' }

    # Match only the ACTUAL EULA-rejection phrasing, never a bare `--accept-eula`.
    # The real failing log line is "Error: One or more modules require license
    # acceptance. Pass --accept-eula to accept all module license terms and
    # proceed." -- matching the remediation phrase `Pass --accept-eula` (or the
    # "require[s] license acceptance" cause) avoids a self-false-positive if the CLI
    # ever echoes our own invoked args (which contain `--accept-eula`) back to stdout.
    if ($text -match '(?i)require[s]? license acceptance|Pass\s+--accept-eula') {
        # The fix is structural (Get-UnityCliModuleInstallArguments injects
        # --accept-eula for every module install); if this still fires, the flag is
        # no longer being honored by this CLI build for this verb.
        Write-Host "::error::Unity $Version module install was rejected for missing EULA acceptance (exit $ExitCode). Every module install in this script must pass --accept-eula via Get-UnityCliModuleInstallArguments. Args: $argLine"
    }
    if ($text -match "(?i)couldn't find module|could not find module|missing module|did you mean") {
        # A requested -m id is unknown to this CLI build (e.g. a version-pinned id
        # drifted). OpenJDK is intentionally NOT requested (it arrives as an
        # android-sdk-ndk-tools dependency); any other id here needs correcting in
        # Get-UnityCiModuleIds.
        Write-Host "::warning::Unity $Version module install reported an unknown module id (exit $ExitCode). Check the 'Did you mean:' hint in the CLI output above and reconcile Get-UnityCiModuleIds. Args: $argLine"
    }
}

function Test-VcRedistGeneration {
    # CLASSIFIES a list of missing-DLL names into the Microsoft Visual C++
    # redistributable generation that ships them. Returns one of four string
    # tags so the annotation's cause-line can be precise:
    #   'vc2010'   -- missing includes MSVCP100/MSVCR100 (2010 SP1 generation)
    #   'vcmodern' -- missing includes MSVCP140/VCRUNTIME140/VCRUNTIME140_1
    #                 (2015-2022 generation)
    #   'both'     -- missing contains BOTH 2010 and modern markers
    #   'neither'  -- missing contains neither generation's marker DLLs (the
    #                 missing DLL is something else: KERNEL32/ucrtbase/Unity-
    #                 shipped/etc.)
    # Match is CASE-INSENSITIVE; `.dll` suffix is OPTIONAL. NEVER throws.
    # Empty / null input returns 'neither' (no evidence either way).
    #
    # WHY this matters: production run 70874414898 identified MSVCP100.dll as
    # the load-bearing missing DLL on both self-hosted Windows runners. The
    # previous annotation hard-coded "missing VC++ 2015-2022 Redistributable"
    # which was wrong for MSVCP100 (a 2010 file). The 2010 and 2015-2022
    # redistributables are SEPARATE Microsoft packages -- installing one does
    # NOT install the other. The annotation needs to tell the operator which
    # generation to install, NOT both / neither / the wrong one.
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [AllowNull()]
        [string[]]$MissingDlls
    )

    if ($null -eq $MissingDlls -or $MissingDlls.Count -eq 0) {
        return 'neither'
    }

    # Case-insensitive regex anchors. The .dll suffix is optional so a name
    # like 'MSVCP100' (without extension) still classifies correctly. We
    # match the prefix only -- 'MSVCP100' matches 'MSVCP100.dll' and bare
    # 'MSVCP100', but NOT 'MSVCP100_v2.dll' (that would be a future-version
    # variant the heuristic should not silently absorb).
    $vc2010Pattern = '(?i)^(MSVCP100|MSVCR100)(\.dll)?$'
    $vcmodernPattern = '(?i)^(MSVCP140|MSVCR140|VCRUNTIME140|VCRUNTIME140_1)(\.dll)?$'

    $has2010 = $false
    $hasModern = $false
    foreach ($name in $MissingDlls) {
        if ([string]::IsNullOrWhiteSpace($name)) { continue }
        $trimmed = $name.Trim()
        # Strip any leading directory component (defensive: callers should
        # pass bare filenames, but the resolver may surface a full path on
        # some edge cases).
        try { $trimmed = [System.IO.Path]::GetFileName($trimmed) } catch { }
        if ($trimmed -match $vc2010Pattern) {
            $has2010 = $true
        }
        if ($trimmed -match $vcmodernPattern) {
            $hasModern = $true
        }
    }

    if ($has2010 -and $hasModern) { return 'both' }
    if ($has2010) { return 'vc2010' }
    if ($hasModern) { return 'vcmodern' }
    return 'neither'
}

function Write-UnityHostPrereqAnnotation {
    # WRAP-IMMUNE, single-line CI annotation for the 0xC0000135 short-circuit in
    # Ensure-UnityNativeStartupHealthy. Emits a `::error::` line that NAMES the
    # operator-actionable remediation (run scripts/unity/bootstrap-windows-runner.ps1,
    # or trigger the runner-bootstrap workflow) AND the runbook, so the failure
    # surfaces as a host-OS prerequisite problem instead of a generic Unity install
    # error. The annotation also lists the DLLs Unity.exe IMPORTS (best-effort, via
    # Get-UnityNativeImports) -- listing imports does not tell us WHICH one is
    # missing, but seeing `VCRUNTIME140.dll` / `MSVCP140.dll` / `VCRUNTIME140_1.dll`
    # in the list pins the missing Microsoft Visual C++ Redistributable as the
    # overwhelming-most-likely culprit at a glance.
    #
    # WHY a separate single-line annotation when the throw text right after it also
    # carries this info? PowerShell's ConciseView error formatter word-wraps thrown
    # text at the runner console width and prepends frame markers; that makes the
    # throw message an unreliable grep target. A `Write-Host "::error::..."` line is
    # never wrapped or reformatted by the runner, so it survives as a stable single
    # line in the CI log AND as a stable assertion target for the regression tests
    # that pin this branch. See reference_pwsh_error_wrap_test_fragility for the
    # full pattern.
    #
    # NEVER THROWS: a failure inside the diagnostic must not mask the underlying
    # 0xC0000135 throw the caller is about to raise. Get-UnityNativeImports is itself
    # best-effort; we still emit the rest of the line even with no imports.
    #
    # CONTEXT-AWARE CAUSE PHRASING: when the composite preflight action
    # (`assert-unity-host-prereqs`) has already installed VC++ at job start, it
    # exports UH_RUNNER_PREREQ_INSTALLED=1 to the rest of the job. In that case the
    # 0xC0000135 we are now diagnosing CANNOT be a missing VC++ Redistributable
    # (preflight just installed it successfully); it is a DIFFERENT missing DLL --
    # Unity-version-specific, a corrupt install, or a runtime DLL deleted mid-job.
    # The annotation branches on that env var so we never tell the operator "install
    # VC++" after preflight already did. When the env var is unset (or any value
    # other than '1') we keep the original VC++-most-likely phrasing.
    #
    # REPAIR-PATH AWARENESS: callers that fire this annotation AFTER a managed
    # reinstall has already run pass -RepairAttempted; the annotation then says
    # "managed reinstall already ran and did not help" so the operator does not waste
    # cycles asking us to retry the auto-repair we already attempted.
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][int]$ExitCode,
        [Parameter(Mandatory = $true)][string]$Description,
        [string]$EditorPath,
        [string]$ProbeLog,
        [switch]$RepairAttempted
    )

    try {
        # OUTER @()-wrap is REQUIRED under StrictMode: when the `if` branches both
        # evaluate to an empty array, the right-hand-side captures $null without the
        # wrap (PowerShell "implicit unrolling"), and the subsequent .Count access
        # throws. See reference_powershell_strictmode_collection_safety.
        $imports = @(if ($EditorPath) {
            Get-UnityNativeImports -EditorPath $EditorPath
        } else {
            @()
        })

        # RESOLUTION PROBE: take the full import list and resolve each entry
        # against the Windows loader search path. The result hashtable lets us
        # NAME the specific missing DLL(s) instead of listing the first 12
        # imports and saying "(+24 more)". Test-UnityImportResolution NEVER
        # THROWS: on any failure (non-Windows, unreadable registry, no editor
        # path) it falls through to "everything missing" or partial data, both
        # of which still produce a useful annotation.
        $resolution = $null
        if ($EditorPath -and $imports.Count -gt 0) {
            $resolution = Test-UnityImportResolution -EditorPath $EditorPath -Imports $imports
        }

        # Resolved-count tallies (always computed, even when $resolution is
        # $null, so the annotation's "Resolved: ..." segment is uniform).
        $missingList = @()
        $systemCount = 0
        $windowsCount = 0
        $unityCount = 0
        $pathCount = 0
        $knownDllsCount = 0
        if ($resolution) {
            $missingList = @($resolution.missing)
            $systemCount = $resolution.systemResolved.Count
            $windowsCount = $resolution.windowsResolved.Count
            $unityCount = $resolution.unityResolved.Count
            $pathCount = $resolution.pathResolved.Count
            $knownDllsCount = $resolution.knownDllsResolved.Count
        }

        # MISSING-DLL phrasing: when the resolver identifies at least one DLL
        # the OS loader could not find, NAME it. Otherwise (everything
        # resolves), point at transitive dependencies / loader-init policy.
        if ($missingList.Count -gt 0) {
            # Sub-classify: if ANY missing DLL looks Unity-shipped (libfbxsdk,
            # optix*, OpenImageDenoise, *compress*, FreeImage, etc.), point at
            # "install corruption" because reinstalling Unity WILL fix that
            # case -- and surface UH_UNITY_FORCE_REINSTALL=1 as the
            # operator-actionable override for the 0xC0000135 short-circuit.
            $unityShippedMissing = @($missingList | Where-Object { Test-UnityImportLooksUnityShipped -Name $_ })
            $missingNames = ($missingList -join ', ')
            # R1 (round-3 review nit): segments do NOT end with `.` -- the
            # outer Write-Host template inserts the period between segments so
            # double-period regressions cannot creep in. Each $...Segment
            # value is a CLAUSE not a SENTENCE.
            $missingSegment = "MISSING DLL(s): $missingNames -- these are imported by Unity.exe but were not found on the Windows loader search path (KnownDLLs / Unity install dir / System32 / Windows / PATH); install these DLLs on the host"
            if ($unityShippedMissing.Count -gt 0) {
                $editorDirForHint = if ($EditorPath) {
                    try { Split-Path -Parent $EditorPath } catch { '(unknown)' }
                } else { '(unknown)' }
                $missingSegment += "; some missing DLLs ($($unityShippedMissing -join ', ')) appear to be Unity-shipped third-party libraries, suggesting the Unity install at $editorDirForHint is partial or corrupt -- quarantine and reinstall via ``unity install $Version`` (or set UH_UNITY_FORCE_REINSTALL=1 to override the 0xC0000135 short-circuit and let ensure-editor.ps1 perform a managed reinstall)"
            }
            $diagnosticSegment = $missingSegment
        } else {
            # Either the resolver found everything OR we had no imports to
            # resolve (best-effort fallthrough). Both phrasings carry the
            # transitive-dependency hint.
            if ($imports.Count -gt 0) {
                $diagnosticSegment = "All Unity.exe imports resolve on the loader search path, yet the OS loader still failed -- this is unusual; possible causes: (a) a transitive dependency (one of the imported DLLs has its own unresolved dependency); (b) loader-init-time security policy block (EDR/AppLocker/CIG); (c) a malformed Unity.exe -- try running ``gflags.exe -i Unity.exe +sls`` on the host to enable loader snaps and re-run for more detail"
            } else {
                $diagnosticSegment = "Unity.exe imports: (could not enumerate) -- the OS loader failed but the diagnostic could not list the imports; inspect the probe log"
            }
        }

        # Resolved-count diagnostic appears IN ADDITION to the named-missing
        # block, so the operator sees both "what is missing" and "how much
        # successfully resolved" (useful when only one DLL is missing out of
        # ~36). R4 (round-3 review nit): omit the segment entirely when there
        # are no imports to resolve -- the diagnostic segment already says
        # "could not enumerate" and "Resolved: 0+0+0+0+0 out of 0 total" is
        # operator-confusing noise that adds nothing actionable.
        $resolvedSegment = if ($imports.Count -gt 0) {
            "Resolved: $systemCount system + $unityCount editor + $windowsCount Windows + $pathCount PATH + $knownDllsCount KnownDLLs out of $($imports.Count) total imports"
        } else {
            $null
        }

        $probeLine = if ($ProbeLog) { "Probe log: $ProbeLog. " } else { '' }
        # CONTEXT-AWARE cause phrasing: if preflight already installed both VC++
        # generations this job, the missing DLL is something else; otherwise
        # SUB-CLASSIFY by the generation Microsoft ships the missing DLL in
        # (2010 vs 2015-2022 vs both). The 2010 generation is a SEPARATE
        # Microsoft package -- the bootstrap's `vcredist-2015-2022` step alone
        # does NOT install MSVCP100. R2 (round-3 review minor):
        # SUPPRESS the VC++ cause line when every import resolves (the
        # "all resolve" diagnostic segment directly contradicts a "missing
        # VC++" claim). Same when no imports could be enumerated (no evidence
        # for the VC++ hypothesis either way).
        $preflightRan = ($env:UH_RUNNER_PREREQ_INSTALLED -eq '1')
        $generation = Test-VcRedistGeneration -MissingDlls $missingList
        $causeLine = if ($preflightRan) {
            "Preflight ran successfully at job start (VC++ 2010/VC++ 2015-2022/long-paths/Defender/pwsh OK), so this is a DIFFERENT missing DLL (Unity-version-specific or corrupt install). Re-running the bootstrap script will NOT help. If the missing DLL is MSVCP100.dll, the host needs Microsoft Visual C++ 2010 Redistributable; the bootstrap script's 'vcredist-2010' step installs this."
        } elseif ($missingList.Count -eq 0 -and $imports.Count -gt 0) {
            # The resolver found every import on the loader search path -- the
            # VC++ cause line would directly contradict the "All Unity.exe
            # imports resolve" diagnostic segment. Suppress it. (When we
            # could not enumerate imports AT ALL -- $imports.Count == 0 --
            # we keep the default VC++ cause hypothesis: lack of evidence
            # is not evidence of lack.)
            $null
        } elseif ($generation -eq 'both') {
            # Both 2010 AND 2015-2022 markers in the missing-DLL list: the host
            # is missing BOTH redistributable packages. Direct the operator at
            # the bootstrap which installs both generations.
            "Most likely cause: missing Microsoft Visual C++ Redistributables (BOTH 2010 AND 2015-2022 x64 are missing). Run the bootstrap script as Administrator to install both."
        } elseif ($generation -eq 'vc2010') {
            # MSVCP100 / MSVCR100 in the missing-DLL list -- the host needs the
            # 2010 SP1 redistributable specifically. The modern installer does
            # NOT install these (they are a separate Microsoft package).
            "Most likely cause: missing Microsoft Visual C++ 2010 Redistributable (x64). Run the bootstrap script as Administrator to install it."
        } elseif ($generation -eq 'vcmodern') {
            # MSVCP140 / VCRUNTIME140 / VCRUNTIME140_1 in the missing-DLL list
            # -- the modern 2015-2022 generation. Preserve the original
            # wording so existing test assertions stay green.
            "Most likely cause: missing Microsoft Visual C++ 2015-2022 Redistributable (x64). Run the bootstrap script as Administrator to install it."
        } else {
            # 'neither' (no VC++ marker in the missing-DLL list). The default
            # hypothesis still surfaces VC++ 2015-2022 -- it is the single
            # most common cause empirically -- but mentions VC++ 2010 as a
            # near-second so operators with MSVCP100 missing still get a hint.
            "Most likely cause: missing Microsoft Visual C++ 2015-2022 Redistributable (x64); if the missing DLL is MSVCP100.dll, install Microsoft Visual C++ 2010 Redistributable (x64) instead. Run the bootstrap script as Administrator to install both."
        }
        # REPAIR-PATH awareness: phrase the remediation line based on whether the
        # caller had already tried the managed reinstall before firing this
        # annotation (post-repair short-circuit) vs. firing it on the first probe
        # failure (no reinstall yet).
        $repairLine = if ($RepairAttempted) {
            "The managed reinstall already ran and did not help (as expected for 0xC0000135 -- the missing DLL is on the OS, not in the Unity install)."
        } else {
            $null
        }
        # Build remediation line. When preflight already ran, bootstrap will not
        # help; otherwise direct the operator to run it.
        $fixLine = if ($preflightRan) {
            "Fix: identify the missing DLL from above and install it on the host (or reimage the runner). Runbook: docs/runbooks/unity-runners-after-transfer.md (Windows host prerequisites)."
        } else {
            "Fix: run scripts/unity/bootstrap-windows-runner.ps1 on this runner, or trigger .github/workflows/runner-bootstrap.yml from the Actions UI. Runbook: docs/runbooks/unity-runners-after-transfer.md (Windows host prerequisites)."
        }
        # Single-line, wrap-immune ::error:: annotation. Do NOT split across lines:
        # the runner emits each Write-Host as one CI log line, and a multi-line
        # annotation degrades to a generic ::error::. The standalone "(0xC0000135
        # / STATUS_DLL_NOT_FOUND)" parenthetical is intentionally absent here: it
        # is already carried by $Description (e.g. "0xC0000135 / STATUS_DLL_NOT_FOUND")
        # rendered in "exit $ExitCode ($Description)" so repeating it is redundant.
        $repairSegment = if ($repairLine) { "$repairLine " } else { '' }
        # R1 (round-3 review nit) + R2/R4 conditionality: only emit
        # segments that have content, separated by `. ` exactly once.
        # Building via a builder avoids "$x. $y. $z." producing double
        # periods when an intermediate segment is null/empty.
        $causeFragment = if ($causeLine) { "$causeLine " } else { '' }
        $diagnosticFragment = if ($diagnosticSegment) { "$diagnosticSegment. " } else { '' }
        $resolvedFragment = if ($resolvedSegment) { "$resolvedSegment. " } else { '' }
        Write-Host "::error title=Unity $Version host prerequisite missing::Unity $Version native startup failed with exit $ExitCode ($Description). The Windows loader could not resolve a DLL Unity.exe imports. ${causeFragment}${diagnosticFragment}${resolvedFragment}${probeLine}${repairSegment}$fixLine"
    } catch {
        # A failure here must not mask the caller's throw. Emit a minimal fallback
        # so the operator still sees the host-prereq verdict, then swallow.
        try {
            $fallbackPreflight = ($env:UH_RUNNER_PREREQ_INSTALLED -eq '1')
            if ($fallbackPreflight) {
                Write-Host "::error title=Unity $Version host prerequisite missing::Unity $Version native startup failed with exit $ExitCode. Preflight already installed VC++ (both 2010 and 2015-2022 generations) this job, so a DIFFERENT host DLL is missing -- inspect the Unity.exe imports above. Runbook: docs/runbooks/unity-runners-after-transfer.md."
            } else {
                Write-Host "::error title=Unity $Version host prerequisite missing::Unity $Version native startup failed with exit $ExitCode. Likely missing Microsoft Visual C++ Redistributables (2010 SP1 x64 ships MSVCP100.dll/MSVCR100.dll; 2015-2022 x64 ships VCRUNTIME140.dll/MSVCP140.dll -- both are required for Unity). Run scripts/unity/bootstrap-windows-runner.ps1. Runbook: docs/runbooks/unity-runners-after-transfer.md."
            }
        } catch {
            # Truly nothing more we can do; let the caller's throw fail loudly.
        }
    }
}

function Get-InstallDriveFreeSpaceText {
    # PURE-ish, best-effort, StrictMode-safe disk-headroom probe shared by the
    # pre-install diagnostic dump (Write-InstallDiagnostics) and the on-failure
    # wrap-immune summary (Write-ModuleInstallFailureDiagnostics). A multi-GB
    # module download that runs out of disk is a prime suspect for a slow/failed
    # install, so the free/total space belongs in BOTH places. Returns a single
    # human line (never throws, never $null) so callers can drop it straight into
    # an annotation.
    param([Parameter(Mandatory = $true)][string]$Root)

    try {
        $rootFull = [System.IO.Path]::GetFullPath($Root)
        $drive = [System.IO.Path]::GetPathRoot($rootFull)
        if (-not $drive) {
            return "install drive for '$Root': (undeterminable)"
        }
        $driveInfo = New-Object System.IO.DriveInfo($drive)
        $freeGb = [Math]::Round($driveInfo.AvailableFreeSpace / 1GB, 2)
        $totalGb = [Math]::Round($driveInfo.TotalSize / 1GB, 2)
        return "install drive $drive free space: $freeGb GB free of $totalGb GB total"
    } catch {
        return "install drive for '$Root': (query failed: $($_.Exception.Message))"
    }
}

function Write-ModuleInstallFailureDiagnostics {
    # WRAP-IMMUNE, single-line CI failure summary for ANY module-install failure
    # (a TIMEOUT kill OR a non-zero exit). PowerShell's ConciseView formatter
    # word-wraps a `throw` message at the console width, so the throw text alone is
    # an unreliable single-line annotation; a `Write-Host "::error::..."` line is
    # NOT wrapped, giving CI a stable, greppable failure summary AND a robust
    # assertion target. Additive only -- the caller still throws its full message.
    #
    # The summary names: the version, the failing verb/args, the outcome (exit code
    # OR "wall-clock timed out after Ns" OR "output heartbeat stalled after Ns" -- chosen
    # by the kill-state booleans, NOT the raw exit code, so a NATIVE 125 from the
    # Unity CLI is never misattributed as a heartbeat-stall kill), the LAST
    # meaningful progress message parsed from the captured output (the JSON
    # "msg" of the last progress line, else the last non-empty line -- via
    # Get-LastCliProgressMessage), and the install-drive free space (via the
    # shared Get-InstallDriveFreeSpaceText). Best-effort and StrictMode-safe:
    # never throws, @()-wraps the output capture.
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [string[]]$Output,
        [int]$ExitCode = -1,
        [string[]]$Arguments,
        [string]$Root,
        [switch]$TimedOut,
        [switch]$StallKilled,
        [switch]$TimedOutWallClock,
        [int]$TimeoutSeconds = 0,
        [int]$StallSeconds = 0
    )

    $argLine = if ($Arguments) { ($Arguments -join ' ') } else { '(unavailable)' }
    $lastProgress = Get-LastCliProgressMessage -Output $Output
    # Phrase the outcome from the KILL-STATE BOOLEANS supplied by the caller, NOT
    # from the raw exit code: a Unity CLI that ORGANICALLY exits with 124 or 125
    # would otherwise be misattributed to a wrapper-driven timeout/stall kill.
    # The -TimedOut switch remains accepted as the legacy "either kind of
    # wrapper-driven timeout" signal for backward compatibility with any caller
    # that has not migrated; the new switches WIN when supplied.
    $outcome = if ($StallKilled) {
        "output heartbeat stalled after $StallSeconds second(s)"
    } elseif ($TimedOutWallClock) {
        "wall-clock timed out after $TimeoutSeconds second(s)"
    } elseif ($TimedOut) {
        "timed out after $TimeoutSeconds second(s)"
    } else {
        "exit code $ExitCode"
    }
    $diskText = if ($Root) { Get-InstallDriveFreeSpaceText -Root $Root } else { 'install drive: (unknown root)' }

    Write-Host "::error::Unity $Version module install FAILED ($outcome). Verb/args: $argLine. Last progress message: $lastProgress. Disk: $diskText"
}

function Test-LooksLikeAbsolutePath {
    # True only for a Windows drive-letter path (C:\...) or a UNC path (\\...).
    # Guards the getter resolver against decorated/empty/relative output from a
    # beta CLI that might print a banner or a prompt instead of a bare path.
    param([string]$Value)

    if (-not $Value) {
        return $false
    }
    $trimmed = $Value.Trim()
    if ($trimmed.Length -lt 3) {
        return $false
    }
    if ($trimmed -match '^[A-Za-z]:[\\/]') {
        return $true
    }
    if ($trimmed.StartsWith('\\')) {
        return $true
    }
    if ($trimmed.StartsWith('/')) {
        return $true
    }

    return $false
}

function Get-UnityCliInstallRoot {
    # GETTER-based authoritative resolver. `unity install-path` with NO args is
    # a 0-arg getter that PRINTS the CLI's current editor install directory.
    # This reports the CLI's REAL install location regardless of whether our
    # best-effort SET succeeded, so discovery does not depend on the (uncertain)
    # set flag. Take the last non-empty path-like stdout line; ignore banners
    # and decorated output.
    $lines = Get-UnityCliOutput -Arguments @('install-path') -AcceptCapturedOutputOnTimeout
    if (-not $lines) {
        return $null
    }

    $candidate = $null
    foreach ($line in $lines) {
        if ($null -eq $line) {
            continue
        }
        $trimmed = ([string]$line).Trim()
        if ($trimmed.Length -eq 0) {
            continue
        }
        if (Test-LooksLikeAbsolutePath $trimmed) {
            $candidate = $trimmed
        }
    }

    if ($candidate) {
        Write-CiNotice "Unity CLI reports install root: $candidate"
    }

    return $candidate
}

function Set-UnityCliInstallPath {
    # BEST-EFFORT. Setting the install path is an OPTIMIZATION (it co-locates
    # editors under our chosen root), never a requirement: discovery falls back
    # to the getter-reported root and the candidate search. The SET flag for the
    # standalone CLI is NOT documented exactly (the Hub CLI uses `-s <dir>`; the
    # standalone CLI very likely mirrors `-s`/`--set`). Try `-s` first, then
    # `--set`; if both fail, emit a ::notice:: (NOT an error) and continue.
    param([Parameter(Mandatory = $true)][string]$Root)

    $normalizedRoot = $Root.TrimEnd([char[]]@('\', '/'))
    $confirmationPattern = '^All Unity Editors will be installed to\s+' + [regex]::Escape($normalizedRoot) + '[\\/]?$'
    if (Invoke-UnityCliSafe -Arguments @('install-path', '-s', $Root) -AcceptCapturedOutputPattern $confirmationPattern) {
        return
    }

    if (Invoke-UnityCliSafe -Arguments @('install-path', '--set', $Root) -AcceptCapturedOutputPattern $confirmationPattern) {
        return
    }

    Write-CiNotice "Could not set the Unity CLI install path to '$Root' (best-effort; the standalone CLI set flag may differ). Continuing; discovery will use the CLI-reported install root."
}

function Confirm-UnityCliManagedInstallRoot {
    param([Parameter(Mandatory = $true)][string]$Root)

    $cliRoot = Get-UnityCliInstallRoot
    if (-not $cliRoot) {
        # Emit a wrap-IMMUNE CI annotation BEFORE the throw. PowerShell's
        # ConciseView formatter word-wraps a thrown message at the console width
        # (splitting phrases across a `     | ` gutter), so the throw text alone is
        # an unreliable single-line annotation; Write-Host output is never wrapped,
        # giving CI a clean ::error:: line AND a stable assertion target. Additive
        # only -- the throw below still aborts with identical semantics.
        Write-Host "::error::CI-managed Unity provisioning cannot mutate editors because the Unity CLI did not report an install root after setting '$Root'."
        throw "CI-managed Unity provisioning cannot mutate editors because the Unity CLI did not report an install root after setting '$Root'."
    }
    if (-not (Test-IsPathInsideDirectory -Path $cliRoot -Directory $Root)) {
        # Wrap-immune CI annotation before the throw (see the note above): the
        # "outside the managed root" phrase is exactly the one PowerShell's
        # word-wrap was observed to split on the narrower Windows runner.
        Write-Host "::error::CI-managed Unity provisioning cannot mutate editors because the Unity CLI install root is outside the managed root. CLI root: '$cliRoot'. Managed root: '$Root'."
        throw "CI-managed Unity provisioning cannot mutate editors because the Unity CLI install root is outside the managed root. CLI root: '$cliRoot'. Managed root: '$Root'."
    }
    return $cliRoot
}

function Resolve-EditorFromCliJson {
    # DEFENSIVE parse of `unity editors -i --format json`. The JSON schema is
    # NOT documented for the standalone CLI, so this scans every object for an
    # entry whose version matches $Version and pulls ANY plausible path-like
    # field. ConvertFrom-Json is wrapped in try/catch: malformed/non-JSON output
    # (e.g. a banner-prefixed beta response) returns $null instead of throwing.
    param(
        [Parameter(Mandatory = $true)][string]$Version
    )

    $lines = Get-UnityCliOutput -Arguments @('editors', '-i', '--format', 'json')
    if (-not $lines) {
        return $null
    }

    $jsonText = ($lines -join "`n").Trim()
    if ($jsonText.Length -eq 0) {
        return $null
    }

    try {
        $parsed = $jsonText | ConvertFrom-Json
    } catch {
        Write-Host "::notice::Could not parse 'unity editors -i --format json' output as JSON; continuing with candidate search."
        return $null
    }

    if ($null -eq $parsed) {
        return $null
    }

    # Normalize to a flat list of objects whether the CLI returned a top-level
    # array, a single object, or the most-likely real schema: a single object
    # that WRAPS the editor array under a property (e.g. {"editors":[...]}).
    # For the wrapped case we keep the wrapper object itself as a candidate AND
    # descend into any property whose value is a non-string IEnumerable, flattening
    # those items into $entries so the version-field scan below sees the real
    # editor records instead of mis-treating the wrapper as the lone entry.
    $entries = New-Object System.Collections.Generic.List[object]
    if ($parsed -is [System.Collections.IEnumerable] -and $parsed -isnot [string]) {
        foreach ($item in $parsed) { $entries.Add($item) }
    } else {
        $entries.Add($parsed)
        foreach ($prop in $parsed.PSObject.Properties) {
            if ($prop.Value -is [System.Collections.IEnumerable] -and $prop.Value -isnot [string]) {
                foreach ($item in $prop.Value) { $entries.Add($item) }
            }
        }
    }

    $versionFields = @('version', 'editorVersion', 'unityVersion', 'name')
    $pathFields = @('path', 'location', 'installPath', 'executable', 'installation', 'editorPath')

    foreach ($entry in $entries) {
        if ($null -eq $entry) { continue }

        $matchesVersion = $false
        foreach ($vf in $versionFields) {
            $vv = $null
            try { $vv = $entry.$vf } catch { $vv = $null }
            if ($vv -and ([string]$vv).Trim() -eq $Version) {
                $matchesVersion = $true
                break
            }
        }
        if (-not $matchesVersion) { continue }

        foreach ($pf in $pathFields) {
            $pv = $null
            try { $pv = $entry.$pf } catch { $pv = $null }
            if (-not $pv) { continue }
            $pathValue = ([string]$pv).Trim()
            if ($pathValue.Length -eq 0) { continue }

            # The field may already be Unity.exe, or a directory we must probe.
            if ((Test-Path -LiteralPath $pathValue -PathType Leaf) -and
                $pathValue.ToLowerInvariant().EndsWith('unity.exe')) {
                return (Resolve-Path -LiteralPath $pathValue).Path
            }

            $exeProbe = @(
                (Join-Path $pathValue 'Editor\Unity.exe'),
                (Join-Path $pathValue 'Unity.exe')
            )
            foreach ($probe in $exeProbe) {
                if (Test-Path -LiteralPath $probe -PathType Leaf) {
                    return (Resolve-Path -LiteralPath $probe).Path
                }
            }
        }
    }

    return $null
}

function Resolve-InstalledEditor {
    # Layered discovery, in order:
    #   (a) under the getter-reported CLI install root (authoritative),
    #   (b) under the configured $InstallRoot (candidate search),
    #   (c) defensive parse of `unity editors -i --format json`.
    # Returns the absolute Unity.exe path, or $null if every strategy fails.
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Root,
        [switch]$ManagedOnly
    )

    $cliRoot = Get-UnityCliInstallRoot
    if ($cliRoot) {
        foreach ($candidate in @(
                (Join-Path $cliRoot "$Version\Editor\Unity.exe"),
                (Join-Path $cliRoot "$Version\Unity.exe"))) {
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                if ($ManagedOnly -and -not (Test-IsPathInsideDirectory -Path $candidate -Directory $Root)) {
                    Write-CiNotice "Ignoring Unity $Version from CLI install root because it is outside the managed install root: $candidate"
                    continue
                }
                return (Resolve-Path -LiteralPath $candidate).Path
            }
        }
    }

    $byCandidate = Find-UnityEditor -Version $Version -Root $Root -IncludeHostInstalls:(-not $ManagedOnly)
    if ($byCandidate) {
        return $byCandidate
    }

    $byJson = Resolve-EditorFromCliJson -Version $Version
    if ($byJson) {
        if ($ManagedOnly -and -not (Test-IsPathInsideDirectory -Path $byJson -Directory $Root)) {
            Write-CiNotice "Ignoring Unity $Version from CLI editor inventory because it is outside the managed install root: $byJson"
            return $null
        }
        return $byJson
    }

    return $null
}

function Test-Il2CppModulePresent {
    # Disk-authoritative, best-effort probe for whether Windows IL2CPP support is
    # already installed for a resolved editor. The standalone CLI's
    # `install-modules` output is not a reliable success source by itself. Disk
    # evidence is the success proof we accept after a non-zero module install.
    #
    # IMPORTANT: the exact on-disk layout VARIES BY UNITY VERSION (and has shifted
    # across the 2020->6000 lineage), so this probes concrete STANDALONE-SPECIFIC
    # player leaves under known Windows IL2CPP variations. Empty directories are
    # not enough proof: failed or partial module installs have left folder shells
    # behind without a usable player/toolchain payload.
    param([Parameter(Mandatory = $true)][string]$EditorPath)

    if (-not $EditorPath) {
        return $false
    }

    try {
        # $EditorPath looks like ...\<version>\Editor\Unity.exe; the editor data
        # root is its parent directory + 'Data'.
        $editorDir = Split-Path -Parent $EditorPath
        if (-not $editorDir) {
            return $false
        }
        $dataRoot = Join-Path $editorDir 'Data'
        $standaloneVariations = Join-Path $dataRoot 'PlaybackEngines\windowsstandalonesupport\Variations'

        $variationNames = @(
            'win64_player_development_il2cpp',
            'win64_player_nondevelopment_il2cpp'
        )
        foreach ($variationName in $variationNames) {
            $variation = Join-Path $standaloneVariations $variationName
            $leafCandidates = @(
                (Join-Path $variation 'WindowsPlayer.exe'),
                (Join-Path $variation 'UnityPlayer.dll'),
                (Join-Path $variation 'GameAssembly.dll')
            )
            foreach ($candidate in $leafCandidates) {
                if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                    return $true
                }
            }
            $payloadLeaves = @(
                Get-ChildItem -LiteralPath $variation -File -ErrorAction SilentlyContinue |
                    Where-Object { $_.Name -match '(?i)\.(dll|exe)$' }
            )
            if ($payloadLeaves.Count -gt 0) {
                return $true
            }
        }
    } catch {
        # Any unexpected probe error is non-fatal: treat as "inconclusive" (false).
        return $false
    }

    return $false
}

function Test-AnyUnityLeafPresent {
    param([Parameter(Mandatory = $true)][string[]]$Paths)

    foreach ($path in $Paths) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            return $true
        }
    }

    return $false
}

function Test-IsPathInsideDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Directory
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $fullDirectory = [System.IO.Path]::GetFullPath($Directory).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $isWindowsHost = [System.IO.Path]::DirectorySeparatorChar -eq '\'
    $comparison = if ($isWindowsHost -or $PSVersionTable.PSEdition -eq 'Desktop') {
        [System.StringComparison]::OrdinalIgnoreCase
    } else {
        [System.StringComparison]::Ordinal
    }

    return $fullPath.Equals($fullDirectory, $comparison) -or
        $fullPath.StartsWith($fullDirectory + [System.IO.Path]::DirectorySeparatorChar, $comparison) -or
        $fullPath.StartsWith($fullDirectory + [System.IO.Path]::AltDirectorySeparatorChar, $comparison)
}

function Get-MissingRequiredEditorPayloadPaths {
    param(
        [Parameter(Mandatory = $true)][string]$EditorPath,
        [string[]]$RelativePaths = @()
    )

    $editorRoot = Split-Path -Parent $EditorPath
    if (-not $editorRoot) {
        throw "Could not resolve the Unity editor root from '$EditorPath'."
    }

    $missing = New-Object System.Collections.Generic.List[string]
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($relativePath in @($RelativePaths)) {
        $relative = [string]$relativePath
        if ([string]::IsNullOrWhiteSpace($relative)) {
            throw 'Required editor payload paths must be non-empty relative paths.'
        }
        if ([System.IO.Path]::IsPathRooted($relative)) {
            throw "Required editor payload path must be relative to the Editor directory: '$relative'."
        }

        $candidate = [System.IO.Path]::GetFullPath((Join-Path $editorRoot $relative))
        if (-not (Test-IsPathInsideDirectory -Path $candidate -Directory $editorRoot)) {
            throw "Required editor payload path escapes the Editor directory: '$relative'."
        }
        if ($seen.Add($relative) -and -not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            $missing.Add($relative)
        }
    }

    return @($missing.ToArray())
}

function Get-UnityEditorInstallDirectory {
    param([Parameter(Mandatory = $true)][string]$EditorPath)

    $editorDir = Split-Path -Parent $EditorPath
    if (-not $editorDir) {
        return $null
    }

    $leaf = Split-Path -Leaf $editorDir
    if ($leaf -eq 'Editor') {
        return (Split-Path -Parent $editorDir)
    }

    return $editorDir
}

function Get-UnityProvisioningProfile {
    $profileVar = Get-Variable -Name UnityProvisioningProfile -Scope Script -ErrorAction SilentlyContinue
    if ($profileVar -and $profileVar.Value) {
        return [string]$profileVar.Value
    }
    return 'Full'
}

function Assert-UnityProvisioningProfile {
    param([Parameter(Mandatory = $true)][string]$Profile)

    if ($Profile -notin @('EditorOnly', 'StandaloneWindowsIl2Cpp', 'Android', 'Full')) {
        throw "Unknown Unity provisioning profile '$Profile'."
    }
}

function Get-UnityCiModuleSpec {
    # SINGLE SOURCE OF TRUTH for the CI Unity module set. Returns an ORDERED array
    # of [pscustomobject] rows (core tier first), each describing one module group:
    #   Id        - the module group identifier.
    #   Requested - $true if the bare id is passed to the standalone CLI's `-m`
    #               install list; $false if it is verified-on-disk ONLY (never
    #               requested).
    #   Verified  - $true if the group must be PROVEN present on disk after install.
    #   Tier      - 'core' (provisions reliably with the base editor) or 'android'
    #               (the heavy/flaky multi-GB Google download whose NDK unpack
    #               deterministically fails at ~93% on Windows).
    #   Profiles  - provisioning profiles that require this module group.
    #
    # WHY THIS EXISTS (and why everything DERIVES from it): the REQUESTED `-m` list,
    # the VERIFIED-on-disk groups, and TIER membership all derive from these rows so
    # they CANNOT DRIFT from one another -- the historical bug class where the
    # requested list and the verified list silently diverged. Add/remove/retier a
    # module HERE and every consumer follows.
    #
    # OpenJDK is Requested=$false (verified-only): the standalone beta CLI rejects
    # the bare id 'android-open-jdk' (it emits "Couldn't find module ... Did you
    # mean: android-open-jdk-11.0.14.1+1" because the real id is VERSION-PINNED and
    # that suffix drifts across Unity versions). OpenJDK instead arrives as a
    # DEPENDENCY of 'android-sdk-ndk-tools', so we PROVE it on disk but NEVER
    # request it.
    #
    # The 'android' tier (android + android-sdk-ndk-tools, with android-open-jdk as
    # its verified-only dependency) is requested only for Android/Full profiles.
    # Existing editors can still try a bounded Android-only repair first; exhaustion
    # escalates to profile-scoped managed quarantine/reinstall unless repair is
    # disabled.
    return @(
        [pscustomobject]@{ Id = 'windows-il2cpp';        Requested = $true;  Verified = $true; Tier = 'core';    Profiles = @('StandaloneWindowsIl2Cpp', 'Full') },
        [pscustomobject]@{ Id = 'webgl';                 Requested = $true;  Verified = $true; Tier = 'core';    Profiles = @('Full') },
        [pscustomobject]@{ Id = 'linux-mono';            Requested = $true;  Verified = $true; Tier = 'core';    Profiles = @('Full') },
        [pscustomobject]@{ Id = 'linux-il2cpp';          Requested = $true;  Verified = $true; Tier = 'core';    Profiles = @('Full') },
        [pscustomobject]@{ Id = 'android';               Requested = $true;  Verified = $true; Tier = 'android'; Profiles = @('Android', 'Full') },
        [pscustomobject]@{ Id = 'android-sdk-ndk-tools'; Requested = $true;  Verified = $true; Tier = 'android'; Profiles = @('Android', 'Full') },
        [pscustomobject]@{ Id = 'android-open-jdk';      Requested = $false; Verified = $true; Tier = 'android'; Profiles = @('Android', 'Full') }
    )
}

function Get-UnityCiModuleSpecForProfile {
    param([string]$Profile = $(Get-UnityProvisioningProfile))

    Assert-UnityProvisioningProfile -Profile $Profile
    return @(Get-UnityCiModuleSpec | Where-Object { $_.Profiles -contains $Profile })
}

function Get-UnityCiModuleIds {
    # REQUESTED module ids passed to the standalone Unity CLI's `-m` install list.
    # DERIVED from Get-UnityCiModuleSpec (the single source of truth) so it cannot
    # drift from the verified-on-disk groups or the tier membership.
    #
    # NOTE: this is intentionally DECOUPLED from Get-UnityCiVerifiedModuleGroups
    # (the on-disk verification list). The two answer different questions: "what do
    # we ASK the CLI to install" vs. "what must we PROVE is on disk". OpenJDK is
    # deliberately ABSENT here (Requested=$false in the spec) even though we verify
    # it on disk: the beta CLI rejects the version-pinned bare id, and OpenJDK
    # arrives as an 'android-sdk-ndk-tools' dependency instead. StrictMode-safe:
    # @()-wraps the derived list.
    param([string]$Profile = $(Get-UnityProvisioningProfile))

    return @(Get-UnityCiModuleSpecForProfile -Profile $Profile | Where-Object { $_.Requested } | ForEach-Object { $_.Id })
}

function Get-UnityCiVerifiedModuleGroups {
    # VERIFIED-on-disk module groups (the on-disk truth we require after any
    # install/repair). DERIVED from Get-UnityCiModuleSpec so it cannot drift from
    # the requested ids or the tiers. Iterated by Get-MissingUnityCiModuleGroups /
    # Test-UnityCiModuleGroupPresent. Includes 'android-open-jdk' (Verified=$true,
    # Requested=$false in the spec): OpenJDK arrives as an 'android-sdk-ndk-tools'
    # dependency and must be PROVEN present, not assumed. StrictMode-safe: @()-wraps.
    param([string]$Profile = $(Get-UnityProvisioningProfile))

    return @(Get-UnityCiModuleSpecForProfile -Profile $Profile | Where-Object { $_.Verified } | ForEach-Object { $_.Id })
}

function Get-UnityCiSkippedModuleGroups {
    param([string]$Profile = $(Get-UnityProvisioningProfile))

    Assert-UnityProvisioningProfile -Profile $Profile
    $selected = @(Get-UnityCiVerifiedModuleGroups -Profile $Profile)
    return @(Get-UnityCiModuleSpec | Where-Object { $_.Verified -and $selected -notcontains $_.Id } | ForEach-Object { $_.Id })
}

function Test-UnityProvisioningProfileIncludesAndroid {
    param([string]$Profile = $(Get-UnityProvisioningProfile))

    return (@(Get-UnityCiModuleSpecForProfile -Profile $Profile | Where-Object { $_.Tier -eq 'android' }).Count -gt 0)
}

function Get-UnityCiModuleIdsForTier {
    # REQUESTED ids for a single tier ('core' or 'android'), derived from the spec.
    # Used to drive the dedicated, bounded Android-only repair for existing editors
    # while fresh/full repair installs request Get-UnityCiModuleIds atomically.
    # Validates $Tier against the spec's known tiers and THROWS on an unknown one
    # (mirroring the throw in Get-UnityCiModuleTier) so a bogus tier can never
    # silently yield an empty -- and therefore malformed, id-less -- `-m` vector.
    # StrictMode-safe: @()-wraps the derived list.
    param(
        [Parameter(Mandatory = $true)][string]$Tier,
        [string]$Profile = $(Get-UnityProvisioningProfile)
    )

    $knownTiers = @(Get-UnityCiModuleSpec | ForEach-Object { $_.Tier } | Select-Object -Unique)
    if ($knownTiers -notcontains $Tier) {
        throw "Unknown Unity CI module tier '$Tier'."
    }

    return @(Get-UnityCiModuleSpecForProfile -Profile $Profile | Where-Object { $_.Requested -and $_.Tier -eq $Tier } | ForEach-Object { $_.Id })
}

function Get-UnityCiModuleTier {
    # Look up the Tier ('core'/'android') for a module group id from the spec, so a
    # missing-group list (which carries verified ids, including the verified-only
    # 'android-open-jdk') can be partitioned by tier. Throws on an unknown id,
    # mirroring the default-case error in Test-UnityCiModuleGroupPresent so the spec
    # and the on-disk switch cannot silently diverge. StrictMode-safe: uses
    # [pscustomobject] property access (not bare hashtable indexing).
    param([Parameter(Mandatory = $true)][string]$Id)

    foreach ($row in @(Get-UnityCiModuleSpec)) {
        if ($row.Id -eq $Id) {
            return $row.Tier
        }
    }
    throw "Unknown Unity CI module group '$Id'."
}

function Get-UnityCliModuleInstallArguments {
    # SINGLE SOURCE OF TRUTH for the Unity-CLI module-install argument vector.
    # ALL THREE module-install call sites (top-level `install`, the repair-path
    # `install`, and the `install-modules` module-add) route through this so it is
    # structurally impossible for one to carry `--accept-eula` while another omits
    # it -- the exact drift that broke every CI cell.
    #
    # `--accept-eula` is MANDATORY (never optional): the Android SDK/NDK/OpenJDK
    # modules carry license terms, and without the flag the standalone CLI aborts
    # the ENTIRE install with "One or more modules require license acceptance. Pass
    # --accept-eula ...". The failing CI log proved the `install` verb emits that
    # message too, so the flag is valid for BOTH verbs handled here.
    #
    # Verb handling (preserving the resilient beta-CLI arg shapes already in use):
    #   install          -> @('install', <version>, '--accept-eula', [--childModules], '-m', <ids...>)
    #   install-modules  -> @('install-modules', '-e', <version>, '--accept-eula', [--childModules], '-m', <ids...>)
    # --childModules is included whenever the requested ids include Android so the
    # CLI resolves SDK/NDK/OpenJDK dependencies under AndroidPlayer atomically.
    # NOTE: this builder is for the EULA-bearing module INSTALL only. The `-l`
    # listing diagnostic and `editors`/`install-path` getters are NOT module
    # installs and must keep their own (EULA-free) shapes; do not route them here.
    #
    # Optional -ModuleIds scopes the vector to a SUBSET of ids (e.g. the bounded
    # Android-only repair) WITHOUT bypassing this sole producer (the
    # `--accept-eula` + child-module + `-m` shape is still owned here for both
    # verbs). When -ModuleIds is omitted the behavior is UNCHANGED: the full
    # requested-id list (Get-UnityCiModuleIds).
    #
    # StrictMode-safe: @()-wraps the module-id capture so an empty list never
    # collapses to AutomationNull, and uses array `+` concatenation only.
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('install', 'install-modules')]
        [string]$Verb,

        [Parameter(Mandatory = $true)]
        [string]$Version,

        [string[]]$ModuleIds
    )

    [string[]]$moduleIds = if ($PSBoundParameters.ContainsKey('ModuleIds')) {
        @($ModuleIds | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    } else {
        @(Get-UnityCiModuleIds)
    }
    if ($null -eq $moduleIds) {
        $moduleIds = [string[]]@()
    }

    if ($moduleIds.Count -eq 0) {
        if ($Verb -eq 'install') {
            return @('install', $Version)
        }
        throw "Cannot build a Unity install-modules command for profile '$(Get-UnityProvisioningProfile)' because no module ids are selected."
    }

    $includeChildModules = ($moduleIds -contains 'android' -or $moduleIds -contains 'android-sdk-ndk-tools')

    if ($Verb -eq 'install-modules') {
        # `install-modules` targets an EXISTING editor, so it needs `-e <version>`.
        if ($includeChildModules) {
            return @('install-modules', '-e', $Version, '--accept-eula', '--childModules', '-m') + $moduleIds
        }
        return @('install-modules', '-e', $Version, '--accept-eula', '-m') + $moduleIds
    }

    # `install` provisions a fresh editor; the version is positional (no `-e`).
    if ($includeChildModules) {
        return @('install', $Version, '--accept-eula', '--childModules', '-m') + $moduleIds
    }
    return @('install', $Version, '--accept-eula', '-m') + $moduleIds
}

function Test-UnityCiModuleGroupPresent {
    param(
        [Parameter(Mandatory = $true)][string]$EditorPath,
        [Parameter(Mandatory = $true)][string]$Group
    )

    if (-not $EditorPath) {
        return $false
    }

    try {
        $editorDir = Split-Path -Parent $EditorPath
        if (-not $editorDir) {
            return $false
        }

        $dataRoot = Join-Path $editorDir 'Data'
        switch ($Group) {
            'windows-il2cpp' {
                return Test-Il2CppModulePresent -EditorPath $EditorPath
            }
            'webgl' {
                $webGlRoot = Join-Path $dataRoot 'PlaybackEngines\WebGLSupport'
                $hasEditorExtension = Test-Path -LiteralPath (Join-Path $webGlRoot 'UnityEditor.WebGL.Extensions.dll') -PathType Leaf
                $hasEmscriptenToolchain = Test-AnyUnityLeafPresent -Paths @(
                    (Join-Path $webGlRoot 'BuildTools\Emscripten\emscripten\emscripten-version.txt'),
                    (Join-Path $webGlRoot 'BuildTools\Emscripten\emscripten\emcc.py'),
                    (Join-Path $webGlRoot 'BuildTools\Emscripten\emscripten-version.txt'),
                    (Join-Path $webGlRoot 'BuildTools\Emscripten\emcc.py')
                )
                return $hasEditorExtension -and $hasEmscriptenToolchain
            }
            'android' {
                $androidRoot = Join-Path $dataRoot 'PlaybackEngines\AndroidPlayer'
                return Test-AnyUnityLeafPresent -Paths @(
                    (Join-Path $androidRoot 'UnityEditor.Android.Extensions.dll'),
                    (Join-Path $androidRoot 'Tools\Source.properties')
                )
            }
            'android-sdk-ndk-tools' {
                $androidRoot = Join-Path $dataRoot 'PlaybackEngines\AndroidPlayer'
                $sdk = Join-Path $androidRoot 'SDK'
                $ndk = Join-Path $androidRoot 'NDK'
                $hasAdb = Test-AnyUnityLeafPresent -Paths @(
                    (Join-Path $sdk 'platform-tools\adb.exe'),
                    (Join-Path $sdk 'platform-tools\adb')
                )
                $hasNdkProperties = Test-Path -LiteralPath (Join-Path $ndk 'source.properties') -PathType Leaf
                $llvmRoot = Join-Path $ndk 'toolchains\llvm\prebuilt'
                $hasLlvmClang = $false
                if (Test-Path -LiteralPath $llvmRoot -PathType Container) {
                    $clangLeaves = @(
                        Get-ChildItem -LiteralPath $llvmRoot -Recurse -File -ErrorAction SilentlyContinue |
                            Where-Object { $_.Name -in @('clang++', 'clang++.exe') } |
                            Select-Object -First 1
                    )
                    $hasLlvmClang = $clangLeaves.Count -gt 0
                }
                return $hasAdb -and $hasNdkProperties -and $hasLlvmClang
            }
            'android-open-jdk' {
                $androidRoot = Join-Path $dataRoot 'PlaybackEngines\AndroidPlayer'
                return Test-AnyUnityLeafPresent -Paths @(
                    (Join-Path $androidRoot 'OpenJDK\bin\java.exe'),
                    (Join-Path $androidRoot 'OpenJDK\bin\java')
                )
            }
            'linux-mono' {
                $linuxRoot = Join-Path $dataRoot 'PlaybackEngines\LinuxStandaloneSupport'
                $variationRoot = Join-Path $linuxRoot 'Variations'
                return Test-AnyUnityLeafPresent -Paths @(
                    (Join-Path $variationRoot 'linux64_player_development_mono\LinuxPlayer'),
                    (Join-Path $variationRoot 'linux64_player_development_mono\UnityPlayer.so'),
                    (Join-Path $variationRoot 'linux64_player_nondevelopment_mono\LinuxPlayer'),
                    (Join-Path $variationRoot 'linux64_player_nondevelopment_mono\UnityPlayer.so')
                )
            }
            'linux-il2cpp' {
                $linuxRoot = Join-Path $dataRoot 'PlaybackEngines\LinuxStandaloneSupport'
                $variationRoot = Join-Path $linuxRoot 'Variations'
                return Test-AnyUnityLeafPresent -Paths @(
                    (Join-Path $variationRoot 'linux64_player_development_il2cpp\LinuxPlayer'),
                    (Join-Path $variationRoot 'linux64_player_development_il2cpp\UnityPlayer.so'),
                    (Join-Path $variationRoot 'linux64_player_nondevelopment_il2cpp\LinuxPlayer'),
                    (Join-Path $variationRoot 'linux64_player_nondevelopment_il2cpp\UnityPlayer.so')
                )
            }
            default {
                throw "Unknown Unity CI module group '$Group'."
            }
        }
    } catch {
        return $false
    }
}

function Get-MissingUnityCiModuleGroups {
    param(
        [Parameter(Mandatory = $true)][string]$EditorPath,
        [string]$Profile = $(Get-UnityProvisioningProfile)
    )

    $missing = New-Object System.Collections.Generic.List[string]
    foreach ($group in @(Get-UnityCiVerifiedModuleGroups -Profile $Profile)) {
        if (-not (Test-UnityCiModuleGroupPresent -EditorPath $EditorPath -Group $group)) {
            $missing.Add($group)
        }
    }

    return @($missing.ToArray())
}

function Test-UnityCiModulesPresent {
    param(
        [Parameter(Mandatory = $true)][string]$EditorPath,
        [string]$Profile = $(Get-UnityProvisioningProfile)
    )

    $missing = @(Get-MissingUnityCiModuleGroups -EditorPath $EditorPath -Profile $Profile)
    return ($missing.Count -eq 0)
}

function Test-ProcessHasModuleUnderDirectory {
    # Best-effort, NEVER-THROWS cross-identity locker signal. Returns $true iff the
    # process with $ProcessId has at least one LOADED MODULE whose on-disk file path
    # is inside $Directory.
    #
    # WHY THIS EXISTS (the honest reason): the primary scoping signals in
    # Stop-StaleUnityProvisioningProcesses come from Win32_Process.CommandLine /
    # .ExecutablePath, both of which are read from the target process's PEB. PEB
    # memory is privilege-gated: for a process owned by ANOTHER identity (the Unity
    # CLI runs install/uninstall work as NetworkService on this runner) queried
    # WITHOUT elevation, BOTH come back empty -- so a NetworkService-owned locker is
    # invisible to command-line/image scoping. Get-Process surfaces the loaded-module
    # list (System.Diagnostics.Process.Modules) through a DIFFERENT API
    # (CreateToolhelp32Snapshot / EnumProcessModules) that is frequently readable for
    # a cross-identity process where the PEB is not. It is NOT guaranteed (a fully
    # protected/elevated process can still deny it, and the call can throw Access
    # Denied), hence DEFENSE-IN-DEPTH, not the primary fix: any failure is swallowed
    # and returns $false.
    #
    # Scoped to the caller-supplied $Directory (the VERSION dir, never the bare
    # managed root) so this can never become a cross-version collateral signal.
    #
    # TEST override: UH_UNITY_FAKE_PROCESS_MODULES is a ';'-separated list of
    # '<pid>=<path>[,<path>...]' entries; when set, the modules for $ProcessId are
    # read from it verbatim (no real Get-Process), so a hermetic test can prove the
    # loaded-module scoping branch on a non-Windows host. StrictMode-safe throughout.
    param(
        [Parameter(Mandatory = $true)][int]$ProcessId,
        [Parameter(Mandatory = $true)][string]$Directory
    )

    if ($ProcessId -le 0) {
        return $false
    }

    # Hermetic test path: parse the fake module table without touching real processes.
    $fakeTable = $env:UH_UNITY_FAKE_PROCESS_MODULES
    if (-not [string]::IsNullOrEmpty($fakeTable)) {
        try {
            foreach ($entry in ($fakeTable -split ';')) {
                $trimmedEntry = ([string]$entry).Trim()
                if ($trimmedEntry.Length -eq 0) {
                    continue
                }
                $eq = $trimmedEntry.IndexOf('=')
                if ($eq -lt 1) {
                    continue
                }
                $pidText = $trimmedEntry.Substring(0, $eq).Trim()
                $parsedPid = 0
                if (-not [int]::TryParse($pidText, [ref]$parsedPid)) {
                    continue
                }
                if ($parsedPid -ne $ProcessId) {
                    continue
                }
                $pathsText = $trimmedEntry.Substring($eq + 1)
                foreach ($modulePath in ($pathsText -split ',')) {
                    $candidate = ([string]$modulePath).Trim()
                    if ($candidate.Length -gt 0 -and (Test-IsPathInsideDirectory -Path $candidate -Directory $Directory)) {
                        return $true
                    }
                }
            }
        } catch {
            return $false
        }
        return $false
    }

    try {
        $proc = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
        if ($null -eq $proc) {
            return $false
        }
        $modules = $null
        try { $modules = $proc.Modules } catch { $modules = $null }
        if ($null -eq $modules) {
            return $false
        }
        foreach ($module in $modules) {
            if ($null -eq $module) {
                continue
            }
            $fileName = ''
            try { $fileName = [string]$module.FileName } catch { $fileName = '' }
            if ($fileName.Trim().Length -eq 0) {
                continue
            }
            if (Test-IsPathInsideDirectory -Path $fileName -Directory $Directory) {
                return $true
            }
        }
    } catch {
        # Access Denied / process exited / any other failure: best-effort means
        # "no positive signal", never an abort of the sweep.
        return $false
    }

    return $false
}

function Stop-StaleUnityProvisioningProcesses {
    param(
        [Parameter(Mandatory = $true)][string]$InstallRoot,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Reason
    )

    $matched = 0
    $stopped = 0
    $details = New-Object System.Collections.Generic.List[string]
    try {
        $rootFull = [System.IO.Path]::GetFullPath($InstallRoot)
        $versionDirFull = [System.IO.Path]::GetFullPath((Join-Path $rootFull $Version))
        $processes = @()
        try {
            # Win32_Process also carries ExecutablePath; we capture it to scope a
            # locker by its ON-DISK image path. NOTE the honest limitation: both
            # CommandLine and ExecutablePath are read from the target process's PEB,
            # which is privilege-gated -- for a process owned by ANOTHER identity (the
            # Unity CLI runs install/uninstall work as NetworkService here) queried
            # WITHOUT elevation, BOTH read back empty, so image-path scoping is ALSO
            # blind to a cross-identity locker. On the observed Unity 6000.3.16f1
            # standalone failure (run 26701943540) the first sweep matched 0 and every
            # move attempt failed; the logs do NOT identify the locker (no process
            # listing), so its identity is SUSPECTED, not established. The loaded-module
            # signal (Test-ProcessHasModuleUnderDirectory, consulted per process below)
            # is the best-effort cross-identity catch that does not depend on the PEB.
            $processes = @(Get-CimInstance Win32_Process -ErrorAction Stop)
        } catch {
            $processes = @(Get-Process -ErrorAction SilentlyContinue | ForEach-Object {
                    $exePath = ''
                    try { $exePath = [string]$_.Path } catch { $exePath = '' }
                    [pscustomobject]@{
                        ProcessId      = $_.Id
                        Name           = $_.ProcessName
                        CommandLine    = ''
                        ExecutablePath = $exePath
                    }
                })
        }

        foreach ($proc in $processes) {
            if ($null -eq $proc) {
                continue
            }
            $name = ''
            $processId = 0
            $commandLine = ''
            $executablePath = ''
            try { $name = [string]$proc.Name } catch { $name = '' }
            try { $processId = [int]$proc.ProcessId } catch { $processId = 0 }
            try { $commandLine = [string]$proc.CommandLine } catch { $commandLine = '' }
            try { $executablePath = [string]$proc.ExecutablePath } catch { $executablePath = '' }
            if ($processId -le 0 -or $processId -eq $PID) {
                continue
            }
            # VERSION-DIR image scope is the ONLY unconditional kill signal: a process
            # whose on-disk image lives inside THIS version's directory
            # (...\<root>\<version>\) is unambiguously the editor we are quarantining,
            # regardless of its name or an empty CommandLine (e.g. the editor's own
            # Unity.exe locking its own tree, or a helper unpacker under that dir).
            # Loaded-module scope (below) catches a locker whose image path the PEB
            # would not give us but whose loaded modules resolve under the version dir.
            $imageInsideVersionDir = ($executablePath.Trim().Length -gt 0) -and (Test-IsPathInsideDirectory -Path $executablePath -Directory $versionDirFull)

            # DELIBERATELY NOT a kill signal on its own: "image somewhere under the
            # shared managed root". The runner hosts 2021/2022/6000 editors side by
            # side under one root, and this sweep runs in the provision step BEFORE the
            # cross-version organization lock is acquired, so a sibling-version editor
            # can legitimately be running concurrently. Killing ANY Unity-named binary
            # under the root (the prior fix's `imageInsideRoot -and looksUnity` branch)
            # force-killed that sibling -- a cross-version collateral kill. A broad
            # under-root match is allowed ONLY when the command line ALSO ties the
            # process to THIS version/root (commandLineScoped), which a sibling version's
            # editor does not satisfy.
            $looksUnity = $name -match '(?i)^(unity|unity\.exe|unity hub|unity hub\.exe|unitycli|unitycli\.exe|unitysetup|unitysetup\.exe|unitydownloadassistant|unitydownloadassistant\.exe|unityhelper|unityhelper\.exe)$'

            $commandLineScoped = ($commandLine.IndexOf($Version, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) -or
                ($commandLine.IndexOf($versionDirFull, [System.StringComparison]::OrdinalIgnoreCase) -ge 0)

            # Defense-in-depth cross-identity signal (see Test-ProcessHasModuleUnderDirectory):
            # a process's LOADED MODULE paths are frequently readable via Get-Process
            # where the PEB CommandLine/ExecutablePath are NOT (those live in
            # privilege-gated PEB memory and read back empty for a NetworkService/SYSTEM
            # process queried without elevation). A module loaded from THIS version's
            # directory means the process is executing the editor we are quarantining.
            # Scoped to the VERSION dir (never the bare root) for the same anti-collateral
            # reason as above.
            $modulesUnderVersionDir = Test-ProcessHasModuleUnderDirectory -ProcessId $processId -Directory $versionDirFull

            # In-scope when EITHER the image / a loaded module runs from within THIS
            # version's directory (unconditional -- unambiguously ours), OR it looks
            # like a Unity process AND its command line genuinely ties it to THIS
            # version/dir. The bare under-root match is gone: a sibling-version editor
            # under the shared root with an empty CommandLine is now SPARED (it matches
            # none of these), while the editor's own Unity.exe locking its own version
            # dir is still caught. This narrows the prior blast radius without losing the
            # real, safe catch.
            $isScoped = $imageInsideVersionDir -or $modulesUnderVersionDir -or ($looksUnity -and $commandLineScoped)
            if (-not $isScoped) {
                continue
            }

            $matched++
            $details.Add("pid=$processId name=$name image=$executablePath modulesUnderVersionDir=$modulesUnderVersionDir command=$commandLine")
            try {
                Stop-Process -Id $processId -Force -ErrorAction Stop
                $stopped++
            } catch {
                $details.Add("pid=$processId stop failed: $($_.Exception.Message)")
            }
        }

        Write-CiNotice "Stale Unity provisioning process cleanup for Unity $Version ($Reason): matched $matched, stopped $stopped."
    } catch {
        $details.Add("cleanup failed: $($_.Exception.Message)")
        Write-Host "::notice::Stale Unity provisioning process cleanup failed for Unity ${Version}: $($_.Exception.Message)"
    } finally {
        Add-ProvisioningProcessCleanupEvent -Reason $Reason -Matched $matched -Stopped $stopped -Details @($details.ToArray())
    }
}

function Move-UnityInstallDirectoryToQuarantine {
    param(
        [Parameter(Mandatory = $true)][string]$InstallDirectory,
        [Parameter(Mandatory = $true)][string]$InstallRoot,
        [Parameter(Mandatory = $true)][string]$Version
    )

    if (-not $InstallDirectory -or -not (Test-Path -LiteralPath $InstallDirectory -PathType Container)) {
        return
    }

    if (-not (Test-IsPathInsideDirectory -Path $InstallDirectory -Directory $InstallRoot)) {
        throw "Refusing to auto-repair Unity $Version because the resolved editor install '$InstallDirectory' is outside the configured managed install root '$InstallRoot'. Remove or reinstall that editor manually, or set UNITY_EDITOR_INSTALL_ROOT to a CI-owned directory."
    }

    $quarantineRoot = Join-Path $InstallRoot '_quarantine'
    New-Item -ItemType Directory -Force -Path $quarantineRoot | Out-Null
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $suffix = [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $destination = Join-Path $quarantineRoot "$Version-$stamp-$suffix"

    Write-Host "::warning::Quarantining unmanaged or partial Unity $Version install before repair: $InstallDirectory -> $destination"
    Stop-StaleUnityProvisioningProcesses -InstallRoot $InstallRoot -Version $Version -Reason "before quarantining $InstallDirectory"
    # Move-Item against a Unity editor directory can fail transiently on Windows
    # with "The process cannot access the file '...' because it is being used by
    # another process." when Unity, an antivirus scanner, or the Windows indexer
    # still holds a handle on a file under the tree. Retry the move with backoff
    # so a momentary lock does not abort the whole repair; Invoke-WithRetry emits
    # a per-attempt ::warning:: and RETHROWS the last error if every attempt
    # fails, so a genuinely stuck directory still aborts loudly. Class rule: any
    # destructive dir op (Move/Remove/Rename) on a transiently-lockable Unity
    # editor directory on Windows goes through this retry helper.
    #
    # BEFORE EACH RETRY (attempt > 1) we re-run the stale-process sweep. The
    # observed Unity 6000.3.16f1 standalone failure (run 26701943540) was a held
    # handle on C:\Unity\Editors\6000.3.16f1\Editor where the FIRST sweep matched 0
    # and all three move attempts then failed identically. HONEST LIMITATION: the
    # logs contain NO process listing identifying the locker; the only Unity binary
    # visible in that run is the CLI at C:\Windows\ServiceProfiles\NetworkService\...
    # (NetworkService, OUTSIDE the managed root), whose PEB CommandLine/ExecutablePath
    # read back empty without elevation. The per-retry re-sweep + the loaded-module
    # signal (Test-ProcessHasModuleUnderDirectory) give the kill multiple shots as a
    # TRANSIENT handle (AV/indexer/an editor child under the version dir) releases,
    # which recovers the COMMON case. It does NOT recover a HARD lock by a non-Unity,
    # cross-identity, out-of-root process; that residual is runner-side and is
    # surfaced by the wrap-immune ::error:: below. The primary mitigation for the
    # exit-6 origin is the PROACTIVE atomic-reinstall route (Ensure-UnityCiModules
    # Step 1b), which avoids reaching this quarantine for a not-module-manageable
    # editor in the first place.
    $moveAttempt = [ref] 0
    $quarantineMoveAttempts = Get-EnsureEditorQuarantineMoveRetryAttempts
    try {
        Invoke-WithRetry -MaxAttempts $quarantineMoveAttempts -DelaySeconds (Get-EnsureEditorRetryDelaySeconds) -Action {
            $moveAttempt.Value++
            if ($moveAttempt.Value -gt 1) {
                Stop-StaleUnityProvisioningProcesses -InstallRoot $InstallRoot -Version $Version -Reason "retry $($moveAttempt.Value) before quarantining $InstallDirectory (a process is still holding the editor tree)"
            }
            Move-Item -LiteralPath $InstallDirectory -Destination $destination -Force
        } | Out-Null
    } catch {
        # Persistent lock after every sweep + retry. Emit a WRAP-IMMUNE ::error::
        # (a thrown message is word-wrapped by PowerShell's ConciseView at the
        # console width; a Write-Host "::error::..." line is not) so the operator
        # sees the exact locked path and the remediation instead of a wrapped
        # Move-Item stack trace. Then rethrow so the run still fails loudly.
        Write-Host ("::error::Could not quarantine Unity $Version install '$InstallDirectory' for repair after $quarantineMoveAttempts move attempt(s): a process is holding a handle on the editor tree and the stale-process sweep could not release it ($($_.Exception.Message)). " +
            "This blocks the automatic quarantine+reinstall that recovers a base editor whose modules cannot be added (Unity CLI 'install-modules' exit 6 / 'Try reinstalling this editor with Unity Hub'). " +
            "Remediation on the runner: ensure no Unity.exe/Unity Hub/indexer/antivirus process is holding C:\\...\\$Version\\Editor (Sysinternals handle64.exe `"$InstallDirectory`" finds the locker), then re-run; or manually delete '$InstallDirectory' and let ensure-editor.ps1 reinstall the editor with its modules.")
        throw
    }
}

function Move-UnityEditorInstallToQuarantine {
    param(
        [Parameter(Mandatory = $true)][string]$EditorPath,
        [Parameter(Mandatory = $true)][string]$InstallRoot,
        [Parameter(Mandatory = $true)][string]$Version
    )

    $installDirectory = Get-UnityEditorInstallDirectory -EditorPath $EditorPath
    if (-not $installDirectory) {
        return
    }

    Move-UnityInstallDirectoryToQuarantine -InstallDirectory $installDirectory -InstallRoot $InstallRoot -Version $Version
}

function Move-UnityVersionInstallToQuarantine {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$InstallRoot
    )

    $candidateRoots = New-Object System.Collections.Generic.List[string]
    $candidateRoots.Add($InstallRoot)
    try {
        $cliRoot = Get-UnityCliInstallRoot
        if ($cliRoot -and (Test-IsPathInsideDirectory -Path $cliRoot -Directory $InstallRoot)) {
            $candidateRoots.Add($cliRoot)
        }
    } catch {
        Write-Host "::notice::Could not query Unity CLI install root while quarantining Unity ${Version}: $($_.Exception.Message)"
    }

    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($root in @($candidateRoots.ToArray())) {
        if (-not $root) {
            continue
        }
        $installDirectory = Join-Path $root $Version
        $full = [System.IO.Path]::GetFullPath($installDirectory)
        if ($seen.Add($full)) {
            Move-UnityInstallDirectoryToQuarantine -InstallDirectory $full -InstallRoot $InstallRoot -Version $Version
        }
    }
}

function Invoke-UnityVersionUninstallForRepair {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Reason,
        [string]$InstallRoot
    )

    $uninstallResult = Invoke-UnityCliCapture -Arguments @('uninstall', $Version)
    if (-not $uninstallResult.Success) {
        $uninstallLines = @($uninstallResult.Output)
        $tailCount = [Math]::Min(12, $uninstallLines.Count)
        $tail = if ($tailCount -gt 0) {
            ($uninstallLines[($uninstallLines.Count - $tailCount)..($uninstallLines.Count - 1)] -join "`n")
        } else {
            '(no output captured)'
        }
        Write-CiNotice "Unity CLI uninstall for $Version did not complete cleanly before repair (exit code $($uninstallResult.ExitCode)); quarantining the install directory instead. Reason: $Reason Output tail:`n$tail"
        if ($InstallRoot) {
            Stop-StaleUnityProvisioningProcesses -InstallRoot $InstallRoot -Version $Version -Reason "failed uninstall before repair: $Reason"
        }
    }

    return $uninstallResult
}

function Install-UnityEditorWithCiModules {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$InstallRoot,
        [Parameter(Mandatory = $true)][string]$Reason,
        [string]$Profile = $(Get-UnityProvisioningProfile),
        [switch]$ManagedOnly
    )

    Assert-UnityProvisioningBudgetCanFit -Operation "fresh Unity $Version managed install" -MinimumSeconds 60
    $moduleIds = @(Get-UnityCiModuleIds -Profile $Profile)
    if ($ManagedOnly) {
        Confirm-UnityCliManagedInstallRoot -Root $InstallRoot | Out-Null
    }
    $moduleText = if ($moduleIds.Count -gt 0) { $moduleIds -join ', ' } else { '(editor only)' }
    Write-CiNotice "Repairing Unity $Version by installing a fresh CLI-managed editor with provisioning profile '$Profile' modules ($moduleText). Reason: $Reason"

    # Single source of truth for the (EULA-bearing) module-install arg vector,
    # scoped to the selected provisioning profile.
    $installArgs = @(Get-UnityCliModuleInstallArguments -Verb 'install' -Version $Version -ModuleIds $moduleIds)

    $resolved = $null
    for ($attempt = 1; $attempt -le 2; $attempt++) {
        $installResult = Invoke-UnityCliCapture -Arguments $installArgs
        if ($installResult.Success) {
            $resolved = Resolve-InstalledEditor -Version $Version -Root $InstallRoot -ManagedOnly:$ManagedOnly
            if ($resolved) {
                $script:ProvisioningEditorPath = $resolved
                break
            }
            if ($attempt -lt 2) {
                Write-InstalledEditorDiagnostics -Version $Version -Root $InstallRoot -Reason "Unity repair install exited 0, but Unity.exe could not be resolved afterward."
                Invoke-UnityVersionUninstallForRepair -Version $Version -Reason "Unity repair install exited 0, but Unity.exe could not be resolved afterward." -InstallRoot $InstallRoot | Out-Null
                Move-UnityVersionInstallToQuarantine -Version $Version -InstallRoot $InstallRoot
                Write-Host "::warning::Retrying Unity $Version repair install after successful CLI install left no resolvable Unity.exe."
                continue
            }
            break
        }

        $resolvedAfterFailure = Resolve-InstalledEditor -Version $Version -Root $InstallRoot -ManagedOnly:$ManagedOnly
        if ($resolvedAfterFailure) {
            Write-CiNotice "Unity repair install for $Version failed with exit code $($installResult.ExitCode), but Unity.exe is resolvable afterward; verifying modules against disk."
            $resolved = $resolvedAfterFailure
            $script:ProvisioningEditorPath = $resolved
            break
        }

        $installText = (@($installResult.Output) -join "`n")
        if ($installText -match '(?i)already installed|editor already installed|is already installed') {
            if ($attempt -lt 2) {
                Write-InstalledEditorDiagnostics -Version $Version -Root $InstallRoot -Reason "Unity repair install reported already-installed, but Unity.exe could not be resolved afterward."
                Invoke-UnityVersionUninstallForRepair -Version $Version -Reason "Unity repair install reported already-installed, but Unity.exe could not be resolved." -InstallRoot $InstallRoot | Out-Null
                Move-UnityVersionInstallToQuarantine -Version $Version -InstallRoot $InstallRoot
                Write-Host "::warning::Retrying Unity $Version repair install after clearing stale CLI metadata and quarantining the managed version directory."
                continue
            }
        }

        # Collapse consecutive identical lines (the Android NDK install can spam
        # thousands of identical progress lines) only for the terminal failure
        # path; recovered editors and retryable stale installs do not consume it.
        $tail = Get-CollapsedCliOutputTail -Output $installResult.Output -MaxLines 40
        Write-UnityCliInstallFailureAnnotation -Version $Version -Output $installResult.Output -ExitCode $installResult.ExitCode -Arguments $installArgs
        # Wrapper-driven kill state drives the diagnostic wording: the new
        # StallKilled / TimedOutWallClock fields distinguish a heartbeat-stall
        # kill from a wall-clock kill from a NATIVE exit code that happens to
        # equal 124 or 125 (which must NOT be misread as a wrapper kill). The
        # legacy exit-code classification is retained ONLY for the retryable
        # decision (both sentinels remain retryable, as before); the failure
        # summary now names the actual kill reason or the raw exit code.
        $installStallKilled = [bool]$installResult.StallKilled
        $installWallTimedOut = [bool]$installResult.TimedOutWallClock
        $installTimedOut = ($installStallKilled -or $installWallTimedOut)
        Write-ModuleInstallFailureDiagnostics -Version $Version -Output $installResult.Output -ExitCode $installResult.ExitCode -Arguments $installArgs -Root $InstallRoot -TimedOut:$installTimedOut -StallKilled:$installStallKilled -TimedOutWallClock:$installWallTimedOut -TimeoutSeconds (Get-EnsureEditorInstallTimeoutSeconds) -StallSeconds (Get-EnsureEditorProgressStallSeconds)
        Write-InstalledEditorDiagnostics -Version $Version -Root $InstallRoot -Reason "Unity repair install failed."
        throw "Unity $Version repair install with CI modules failed with exit code $($installResult.ExitCode). CLI output tail:`n$tail"
    }

    if (-not $resolved) {
        Write-InstalledEditorDiagnostics -Version $Version -Root $InstallRoot -Reason "Unity repair install completed, but Unity.exe could not be resolved afterward."
        throw "Unity $Version repair install completed, but Unity.exe could not be found afterward."
    }

    $missing = @(Get-MissingUnityCiModuleGroups -EditorPath $resolved -Profile $Profile)
    if ($missing.Count -gt 0) {
        throw "Unity $Version repair install completed at '$resolved', but required CI module groups for provisioning profile '$Profile' are still missing on disk after the atomic install: $($missing -join ', ')."
    }

    return $resolved
}

function Repair-UnityEditorWithCiModules {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$EditorPath,
        [Parameter(Mandatory = $true)][string]$InstallRoot,
        [Parameter(Mandatory = $true)][string]$Reason,
        [string]$Profile = $(Get-UnityProvisioningProfile),
        [switch]$ManagedOnly
    )

    return Invoke-WithUnityInstallLock -Version $Version -InstallRoot $InstallRoot -Action {
        Assert-UnityProvisioningBudgetCanFit -Operation "managed quarantine/reinstall for Unity $Version" -MinimumSeconds 60
        if ($ManagedOnly) {
            Confirm-UnityCliManagedInstallRoot -Root $InstallRoot | Out-Null
        }
        Invoke-UnityVersionUninstallForRepair -Version $Version -Reason $Reason -InstallRoot $InstallRoot | Out-Null

        if (Test-Path -LiteralPath $EditorPath -PathType Leaf) {
            Move-UnityEditorInstallToQuarantine -EditorPath $EditorPath -InstallRoot $InstallRoot -Version $Version
        }
        Move-UnityVersionInstallToQuarantine -Version $Version -InstallRoot $InstallRoot

        return Install-UnityEditorWithCiModules -Version $Version -InstallRoot $InstallRoot -Reason $Reason -Profile $Profile -ManagedOnly:$ManagedOnly
    }
}

function Get-NativeExitCodeDescription {
    param([Parameter(Mandatory = $true)][int]$ExitCode)

    $normalized = if ($ExitCode -lt 0) {
        [uint32]($ExitCode + 4294967296)
    } else {
        [uint32]$ExitCode
    }
    $hex = $normalized.ToString('X8')
    # Compare against the hex STRING form (not the literal 0xC0000135 token) because
    # PowerShell parses `0xC0000135` as Int32 -1073741515 and `[uint32]$normalized -eq
    # 0xC0000135` therefore coerces to Int32 -- $normalized (the unsigned value
    # 3221225781) and -1073741515 are NOT -eq. String compare on the canonical 8-char
    # hex avoids the int/uint conflation entirely and is what Test-IsNativeDllNotFound
    # also relies on.
    if ($hex -eq 'C0000135') {
        return "0x$hex / STATUS_DLL_NOT_FOUND"
    }
    if ($hex -eq '8007007E') {
        return "0x$hex / ERROR_MOD_NOT_FOUND"
    }

    return "0x$hex"
}

function Test-IsNativeDllNotFound {
    # TRUE iff $ExitCode normalizes to the Windows NTSTATUS 0xC0000135
    # (STATUS_DLL_NOT_FOUND): the OS loader could not resolve an imported DLL when
    # spawning Unity.exe. This is a HOST OS prerequisite failure (e.g. a missing
    # Microsoft Visual C++ Redistributable), NOT a Unity install issue, so the
    # caller must SKIP the managed reinstall path -- reinstalling Unity does NOT
    # add a DLL to the OS loader's search path. Implemented as a single-purpose
    # helper (instead of a bare `-eq` in the caller) so the int/uint comparison
    # bug fixed in Get-NativeExitCodeDescription cannot regress here either: we
    # compare on the canonical 8-char hex string of the uint32 value.
    param([Parameter(Mandatory = $true)][int]$ExitCode)

    $normalized = if ($ExitCode -lt 0) {
        [uint32]($ExitCode + 4294967296)
    } else {
        [uint32]$ExitCode
    }
    return ($normalized.ToString('X8') -eq 'C0000135')
}

function Get-UnityNativeImports {
    # Best-effort PE-import-table dump of Unity.exe. Returns a string[] of imported
    # DLL filenames (e.g. 'KERNEL32.dll', 'VCRUNTIME140.dll', 'VCRUNTIME140_1.dll',
    # 'MSVCP140.dll') or @() on ANY failure / non-PE file / missing PEReader type.
    # NEVER THROWS -- the caller (Write-UnityHostPrereqAnnotation) calls this from a
    # diagnostic path that must not itself fail the build.
    #
    # WHY: when Unity.exe is launched and the Windows loader exits the process with
    # 0xC0000135 (STATUS_DLL_NOT_FOUND), the loader does NOT tell us WHICH DLL it
    # could not resolve -- only that some import was missing. Listing every DLL
    # Unity.exe IMPORTS narrows the search: if `VCRUNTIME140.dll` / `MSVCP140.dll` /
    # `VCRUNTIME140_1.dll` appear in the list, the missing DLL is overwhelmingly
    # likely the Microsoft Visual C++ 2015-2022 Redistributable (x64). The CI
    # annotation can then NAME that prereq instead of telling the operator "some DLL
    # is missing -- good luck."
    #
    # PARSING STRATEGY: we use System.Reflection.PortableExecutable.PEReader (.NET
    # 5+ / pwsh 7) to read the import directory. The descriptor walk:
    #   1. PEHeaders.PEHeader.ImportTableDirectory -> RVA + Size of the import dir.
    #   2. GetSectionData(rva).GetReader() -> a BlobReader anchored AT the requested
    #      RVA (the reader is the section data sliced from that RVA to end of
    #      section, NOT from the section start).
    #   3. Each IMAGE_IMPORT_DESCRIPTOR is 20 bytes (5 x uint32): ILT, TimeStamp,
    #      ForwarderChain, NameRVA, FirstThunk. The descriptor sequence is
    #      terminated by an all-zero entry.
    #   4. For each non-zero NameRVA, GetSectionData(NameRVA).GetReader() yields the
    #      ASCII null-terminated DLL name.
    #
    # CRITICAL INT/UINT GOTCHA: PEReader.GetSectionData takes an Int32 rva, but the
    # ReadUInt32 calls return [uint32]. Passing the [uint32] to a [int]-typed
    # overload SILENTLY routes the call to the (string sectionName) overload (since
    # neither type matches exactly) and returns a 0-length block, which then throws
    # "Read out of bounds" on the first ReadByte. We explicitly `[int]`-cast every
    # RVA before calling GetSectionData to pin the int32 overload.
    #
    # TEST-ONLY override (same spirit as UH_UNITY_FAKE_LONGPATHS_ENABLED): when
    # UH_UNITY_FAKE_IMPORTS is non-empty, the comma-separated value is returned
    # verbatim WITHOUT touching the file. Lets hermetic tests prove the annotation
    # branch on Linux/macOS without smuggling a real PE binary into the repo.
    param([Parameter(Mandatory = $true)][string]$EditorPath)

    if (-not [string]::IsNullOrEmpty($env:UH_UNITY_FAKE_IMPORTS)) {
        $fake = New-Object 'System.Collections.Generic.List[string]'
        foreach ($entry in ($env:UH_UNITY_FAKE_IMPORTS -split ',')) {
            $trimmed = if ($null -eq $entry) { '' } else { ([string]$entry).Trim() }
            if (-not [string]::IsNullOrWhiteSpace($trimmed)) {
                [void]$fake.Add($trimmed)
            }
        }
        return @($fake.ToArray())
    }

    if ([string]::IsNullOrWhiteSpace($EditorPath)) {
        return @()
    }
    if (-not (Test-Path -LiteralPath $EditorPath -PathType Leaf)) {
        return @()
    }

    try {
        # Best-effort: pwsh 7 / .NET 5+ ship System.Reflection.PortableExecutable in
        # the default LoadContext, so the type literal resolves directly. PS 5.1
        # MAY need an explicit Add-Type for `System.Reflection.Metadata`; if that
        # fails too, the outer try/catch returns @() and the caller falls back to
        # an "(could not enumerate)" annotation.
        try {
            $null = [System.Reflection.PortableExecutable.PEReader]
        } catch {
            try {
                Add-Type -AssemblyName 'System.Reflection.Metadata' -ErrorAction SilentlyContinue
            } catch {
                # Ignored: outer catch returns @() if the type still cannot resolve.
            }
        }

        $stream = [System.IO.File]::OpenRead($EditorPath)
        try {
            # -ArgumentList is the explicit, StrictMode-friendly form; positional
            # `(, $stream)` works too but is harder to grep / understand at a glance.
            $pe = New-Object -TypeName 'System.Reflection.PortableExecutable.PEReader' -ArgumentList $stream
            try {
                $headers = $pe.PEHeaders
                # A non-PE file (e.g. a test stub with a shell shebang body) is
                # accepted by the PEReader constructor lazily; PEHeader is $null in
                # that case. Bail out cleanly.
                if ($null -eq $headers -or $null -eq $headers.PEHeader) {
                    return @()
                }
                $names = New-Object 'System.Collections.Generic.List[string]'

                # Inline helper: walk a sequence of fixed-size PE descriptors that
                # each carry a single DLL NameRVA at a known offset, terminated by
                # an all-zero descriptor. Used for BOTH the regular import
                # directory (20-byte IMAGE_IMPORT_DESCRIPTOR, NameRVA at offset 12)
                # and the delay-import directory (32-byte IMAGE_DELAYLOAD_DESCRIPTOR,
                # DllNameRVA at offset 4). Splitting the walk into a closure keeps
                # both passes byte-for-byte consistent and prevents a regression in
                # the existing import walk from sneaking past the delay-import
                # pass.
                $readDllNameAtRva = {
                    param([uint32]$NameRva)
                    if ($NameRva -eq 0) { return $null }
                    try {
                        $nameBlock = $pe.GetSectionData([int]$NameRva)
                        $nameReader = $nameBlock.GetReader()
                        $sb = New-Object 'System.Text.StringBuilder'
                        $byteCount = 0
                        # A reasonable DLL name is well under 256 chars; cap to
                        # prevent a corrupt RVA from consuming the entire
                        # section.
                        while ($nameReader.RemainingBytes -gt 0 -and $byteCount -lt 1024) {
                            $b = $nameReader.ReadByte()
                            if ($b -eq 0) {
                                break
                            }
                            # Restrict to printable ASCII to avoid emitting
                            # garbage if the RVA pointed somewhere other than a
                            # name table.
                            if ($b -ge 32 -and $b -lt 127) {
                                [void]$sb.Append([char]$b)
                            } else {
                                # Non-printable byte before terminator -- abandon
                                # this name; treat as parse failure.
                                $sb.Length = 0
                                break
                            }
                            $byteCount++
                        }
                        $name = $sb.ToString()
                        if (-not [string]::IsNullOrWhiteSpace($name)) {
                            return $name
                        }
                        return $null
                    } catch {
                        # A single bad descriptor must not abort the whole walk.
                        return $null
                    }
                }

                # PASS 1: regular import directory (IMAGE_DIRECTORY_ENTRY_IMPORT, slot 1).
                $importDir = $headers.PEHeader.ImportTableDirectory
                if ($null -ne $importDir -and $importDir.Size -gt 0 -and $importDir.RelativeVirtualAddress -gt 0) {
                    try {
                        $block = $pe.GetSectionData([int]$importDir.RelativeVirtualAddress)
                        $reader = $block.GetReader()
                        # Defensive caps: a corrupt or attacker-crafted PE could otherwise
                        # loop indefinitely (no terminator) or chase a name RVA that walks
                        # off the end of every section.
                        $maxEntries = 256
                        $loopCount = 0
                        while ($reader.RemainingBytes -ge 20 -and $loopCount -lt $maxEntries) {
                            $loopCount++
                            $importLookupTable = $reader.ReadUInt32()
                            $null = $reader.ReadUInt32() # TimeDateStamp (unused)
                            $null = $reader.ReadUInt32() # ForwarderChain (unused)
                            $nameRva = $reader.ReadUInt32()
                            $iatRva = $reader.ReadUInt32()
                            if ($importLookupTable -eq 0 -and $nameRva -eq 0 -and $iatRva -eq 0) {
                                # Standard PE terminator descriptor -- end of imports.
                                break
                            }
                            $name = & $readDllNameAtRva $nameRva
                            if ($name) {
                                [void]$names.Add($name)
                            }
                        }
                    } catch {
                        # Best-effort: a corrupt import directory must not abort
                        # the delay-import pass below.
                    }
                }

                # PASS 2: delay-import directory (IMAGE_DIRECTORY_ENTRY_DELAY_IMPORT,
                # slot 13). Unity may declare some imports via delay-load (e.g. plugins
                # bound at runtime via LoadLibrary). The OS loader still resolves them
                # at module-init time when they are referenced; a missing delay-loaded
                # DLL surfaces as 0xC0000135 just like a regular import does, so we
                # MUST include them in the resolution probe. The descriptor is the
                # IMAGE_DELAYLOAD_DESCRIPTOR record (32 bytes total):
                #     Attributes        (4 bytes, uint32)
                #     DllNameRVA        (4 bytes, uint32)   <-- the name we want
                #     ModuleHandleRVA   (4 bytes, uint32)
                #     DelayIATRVA       (4 bytes, uint32)
                #     DelayINT          (4 bytes, uint32)
                #     BoundDelayIT      (4 bytes, uint32)
                #     UnloadDelayIT     (4 bytes, uint32)
                #     TimeStamp         (4 bytes, uint32)
                # Terminated by an all-zero descriptor. Same int/uint cast gotcha
                # applies: GetSectionData takes Int32, ReadUInt32 returns UInt32,
                # so explicit [int]-cast pins the correct overload.
                try {
                    $delayDir = $headers.PEHeader.DelayImportTableDirectory
                } catch {
                    $delayDir = $null
                }
                if ($null -ne $delayDir -and $delayDir.Size -gt 0 -and $delayDir.RelativeVirtualAddress -gt 0) {
                    try {
                        $delayBlock = $pe.GetSectionData([int]$delayDir.RelativeVirtualAddress)
                        $delayReader = $delayBlock.GetReader()
                        $maxDelayEntries = 256
                        $delayLoop = 0
                        while ($delayReader.RemainingBytes -ge 32 -and $delayLoop -lt $maxDelayEntries) {
                            $delayLoop++
                            $attributes = $delayReader.ReadUInt32()
                            $dllNameRva = $delayReader.ReadUInt32()
                            $moduleHandleRva = $delayReader.ReadUInt32()
                            $delayIatRva = $delayReader.ReadUInt32()
                            $null = $delayReader.ReadUInt32() # DelayINT (unused)
                            $null = $delayReader.ReadUInt32() # BoundDelayIT (unused)
                            $null = $delayReader.ReadUInt32() # UnloadDelayIT (unused)
                            $null = $delayReader.ReadUInt32() # TimeStamp (unused)
                            if ($attributes -eq 0 -and $dllNameRva -eq 0 -and $moduleHandleRva -eq 0 -and $delayIatRva -eq 0) {
                                # All-zero terminator descriptor.
                                break
                            }
                            $name = & $readDllNameAtRva $dllNameRva
                            if ($name) {
                                # De-dup: if a DLL appears in BOTH the regular and
                                # delay-import directories (uncommon but possible),
                                # we only want it listed once in the resolution
                                # probe.
                                $alreadyPresent = $false
                                foreach ($existing in $names) {
                                    if ([string]::Equals($existing, $name, [System.StringComparison]::OrdinalIgnoreCase)) {
                                        $alreadyPresent = $true
                                        break
                                    }
                                }
                                if (-not $alreadyPresent) {
                                    [void]$names.Add($name)
                                }
                            }
                        }
                    } catch {
                        # Best-effort: a missing or malformed delay-import
                        # directory falls through to whatever we collected from
                        # the regular import pass.
                    }
                }

                return @($names.ToArray())
            } finally {
                $pe.Dispose()
            }
        } finally {
            $stream.Dispose()
        }
    } catch {
        # ANY failure -- type missing, file unreadable, malformed PE, BlobReader
        # exhausted -- returns an empty list. The annotation branch then prints
        # "(could not enumerate)" instead of named imports, and the build still
        # surfaces the underlying 0xC0000135 / STATUS_DLL_NOT_FOUND throw.
        return @()
    }
}

function Test-UnityImportResolution {
    # Resolve each Unity.exe import against the Windows loader search path so the
    # 0xC0000135 / STATUS_DLL_NOT_FOUND short-circuit can NAME the specific
    # missing DLL(s) rather than printing a truncated list of "things Unity.exe
    # imports". Returns a hashtable describing WHERE each import was found (or
    # that it was found NOWHERE).
    #
    # WHY: previously the short-circuit annotation listed the first 12 of ~36
    # Unity.exe imports with "(+24 more)". The actual missing DLL was usually in
    # the truncated tail; the operator had no way to identify it without RDP /
    # offline analysis. This helper enumerates every import against the same
    # search order the Windows loader uses (default = "safe DLL search mode" via
    # CreateProcess), so the annotation can flip from "here are some DLLs Unity
    # uses" to "DLL <X> is missing".
    #
    # SEARCH ORDER (matches Microsoft's documented order for default
    # CreateProcess loads):
    #   1. KnownDLLs (registry-pinned system DLLs loaded from System32 by name,
    #      bypassing the path search). Read once from
    #      HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\KnownDLLs.
    #   2. The directory of Unity.exe.
    #   3. %WINDIR%\System32.
    #   4. %WINDIR%.
    #   5. (Current directory -- not probed; CI invocations don't depend on it
    #      and probing CWD would add false-positive resolves.)
    #   6. Directories listed in %PATH%.
    # SysWOW64 is intentionally NOT probed: Unity.exe is 64-bit and SysWOW64 is
    # the 32-bit redirector target, so a 64-bit process would never load from
    # there anyway.
    #
    # KEY DESIGN CHOICES:
    # - PURE FILE-EXISTENCE PROBE. We do NOT actually call LoadLibrary -- doing
    #   so would re-trigger the same loader failure inside the diagnostic and
    #   potentially crash the diagnostic itself. Test-Path against the candidate
    #   path is the resolver we can run safely from PowerShell.
    # - RECORDS WHERE EACH IMPORT WAS FOUND. The hashtable carries both the
    #   resolved-paths-per-bucket and the missing list. Surfaces "resolved from
    #   PATH" anomalies -- a Unity-shipped DLL that resolves from PATH instead
    #   of the Unity install dir is a hint that another tool (e.g. a stale CUDA
    #   install) is shadowing the Unity copy.
    # - EditorDir RESOLVED VIA Split-Path -Parent $EditorPath so a non-default
    #   Unity install location still gets a correct probe directory.
    # - NEVER THROWS. Any failure inside the probe (unreadable registry,
    #   malformed PATH, missing %WINDIR%) falls through to "best-effort partial
    #   data".
    #
    # TEST-ONLY OVERRIDE (mirrors UH_UNITY_FAKE_IMPORTS):
    # UH_UNITY_FAKE_MISSING_IMPORTS = comma-separated DLL names FORCED into
    # the .missing bucket BEFORE any real probing. Lets hermetic tests on
    # Linux/macOS prove the "MISSING DLL(s):" annotation branch without a real
    # Unity install and without dropping a real PE binary in the repo.
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$EditorPath,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Imports
    )

    # Hashtable initialized with EVERY key the caller may inspect so a
    # StrictMode (Set-StrictMode -Version Latest) reader can't accidentally
    # probe an undefined property name and throw mid-annotation.
    # Named $buckets (not $result / $resolution) to keep this in-body
    # indexing visually distinct from the bare captures of
    # Test-UnityImportResolution / Invoke-UnityCliCapture in callers. The
    # in-body indexing is provably safe (we always initialize every key
    # above).
    $buckets = @{
        missing            = @()
        systemResolved     = @{}
        windowsResolved    = @{}
        unityResolved      = @{}
        pathResolved       = @{}
        knownDllsResolved  = @{}
    }

    try {
        # KnownDLLs registry probe: Windows-only. The values under
        # HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\KnownDLLs map
        # name -> DLL filename. Both keys ("CRYPT32" -> "crypt32.dll", or
        # "crypt32" -> "crypt32.dll") are possible across Windows versions; we
        # probe BOTH and accept matches in either direction.
        $knownDlls = @()
        $isWindowsHost = ([System.IO.Path]::DirectorySeparatorChar -eq '\')
        if ($isWindowsHost) {
            try {
                $knownDllsKey = 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\KnownDLLs'
                $knownDllsItem = Get-Item -LiteralPath $knownDllsKey -ErrorAction Stop
                foreach ($valueName in $knownDllsItem.GetValueNames()) {
                    $v = $knownDllsItem.GetValue($valueName)
                    if ($v -is [string] -and $v.Length -gt 0) {
                        $knownDlls += $v
                    }
                }
            } catch {
                # Registry unreadable / non-Windows / no KnownDLLs key. Fall through.
            }
        }

        # Test-only fake-missing list parsed ONCE (outside the per-import loop).
        $fakeMissing = @()
        if (-not [string]::IsNullOrEmpty($env:UH_UNITY_FAKE_MISSING_IMPORTS)) {
            $fakeMissing = @(
                $env:UH_UNITY_FAKE_MISSING_IMPORTS -split ',' |
                    ForEach-Object {
                        if ($null -eq $_) { '' } else { ([string]$_).Trim() }
                    } |
                    Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
            )
        }

        # Resolve search directories ONCE per call (NOT once per import).
        $editorDir = $null
        if (-not [string]::IsNullOrWhiteSpace($EditorPath)) {
            try {
                $editorDir = Split-Path -Parent $EditorPath
            } catch {
                $editorDir = $null
            }
        }
        $windowsDir = if (-not [string]::IsNullOrEmpty($env:WINDIR)) { $env:WINDIR } else { $null }
        $system32 = $null
        if ($windowsDir) {
            try {
                $system32 = Join-Path $windowsDir 'System32'
            } catch {
                $system32 = $null
            }
        }

        $pathEntries = @()
        if (-not [string]::IsNullOrEmpty($env:Path)) {
            # ';' splits Windows-style PATH; ':' is the POSIX form and is not
            # relevant here (we only resolve against a Windows loader's search
            # order). Split-but-trim, drop blanks.
            $pathEntries = @(
                $env:Path -split ';' |
                    ForEach-Object {
                        if ($null -eq $_) { '' } else { ([string]$_).Trim() }
                    } |
                    Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
            )
        }

        foreach ($import in $Imports) {
            if ($null -eq $import) { continue }
            $name = ([string]$import).Trim()
            if ([string]::IsNullOrWhiteSpace($name)) { continue }

            # TEST-ONLY override BEFORE any real probing so hermetic tests can
            # stage a deterministic missing bucket on any OS.
            if ($fakeMissing.Count -gt 0) {
                $forced = $false
                foreach ($f in $fakeMissing) {
                    if ([string]::Equals($f, $name, [System.StringComparison]::OrdinalIgnoreCase)) {
                        $forced = $true
                        break
                    }
                }
                if ($forced) {
                    $buckets.missing += $name
                    continue
                }
            }

            # KnownDLLs check FIRST: those load by name from System32 without
            # path search. KnownDLLs values are sometimes stored with the .dll
            # extension and sometimes without, so accept both shapes.
            $isKnown = $false
            foreach ($k in $knownDlls) {
                if ([string]::IsNullOrWhiteSpace($k)) { continue }
                $kTrim = ([string]$k).Trim()
                if ([string]::Equals($kTrim, $name, [System.StringComparison]::OrdinalIgnoreCase) -or
                    [string]::Equals(($kTrim + '.dll'), $name, [System.StringComparison]::OrdinalIgnoreCase) -or
                    [string]::Equals($kTrim, ($name -replace '\.dll$', ''), [System.StringComparison]::OrdinalIgnoreCase)) {
                    $isKnown = $true
                    break
                }
            }
            if ($isKnown) {
                $buckets.knownDllsResolved[$name] = 'KnownDLLs'
                continue
            }

            # Path-search buckets in loader order. Each entry is (bucketKey, dir).
            $searchDirs = New-Object 'System.Collections.Generic.List[object]'
            if ($editorDir) {
                [void]$searchDirs.Add(@{ bucket = 'unityResolved'; dir = $editorDir })
            }
            if ($system32) {
                [void]$searchDirs.Add(@{ bucket = 'systemResolved'; dir = $system32 })
            }
            if ($windowsDir) {
                [void]$searchDirs.Add(@{ bucket = 'windowsResolved'; dir = $windowsDir })
            }
            foreach ($p in $pathEntries) {
                [void]$searchDirs.Add(@{ bucket = 'pathResolved'; dir = $p })
            }

            $resolved = $false
            foreach ($candidate in $searchDirs) {
                $dir = $candidate.dir
                if ([string]::IsNullOrWhiteSpace($dir)) { continue }
                try {
                    $probe = Join-Path $dir $name
                } catch {
                    continue
                }
                try {
                    if (Test-Path -LiteralPath $probe -PathType Leaf) {
                        $bucket = $candidate.bucket
                        # First-hit wins (matches loader-search semantics); do not
                        # overwrite a later resolution onto an earlier bucket.
                        if (-not $buckets[$bucket].ContainsKey($name)) {
                            $buckets[$bucket][$name] = $probe
                        }
                        $resolved = $true
                        break
                    }
                } catch {
                    # Continue to the next candidate dir if Test-Path itself
                    # blows up on a malformed PATH entry.
                    continue
                }
            }
            if (-not $resolved) {
                $buckets.missing += $name
            }
        }
    } catch {
        # Outer-level safety net: even a catastrophic failure inside the resolver
        # must NEVER mask the underlying 0xC0000135 throw. Return whatever
        # partial data we already collected.
    }

    return $buckets
}

function Test-UnityImportLooksUnityShipped {
    # True iff $Name matches the heuristic for "Unity-shipped third-party
    # library" -- a DLL whose presence in the missing list points the operator
    # at "Unity install is partial/corrupt" rather than "host OS prereq is
    # missing". The patterns are deliberately broad (Unity ships dozens of
    # third-party libs under names that vary by version): if the operator sees a
    # false positive, the remediation hint ("reinstall Unity") is still safe
    # (auto-repair quarantines + reinstalls; on a healthy install it's a no-op).
    #
    # MAINTENANCE NOTE (R6, round-3 review):
    #   Last reviewed against Unity 2021.3 / 2022.3 / 6000.3 import lists from
    #   live CI run 70874414898 (date 2026-05-26). Revisit when bumping to a
    #   new Unity major (e.g. Unity 7) -- Unity's bundled third-party set
    #   evolves between major versions and a brand-new shipped DLL not
    #   matching any pattern below will fall through to the OS-prereq hint
    #   (which still produces a SAFE remediation: "run bootstrap"; the only
    #   loss is the more-specific "your Unity install is corrupt, reinstall"
    #   hint that would have been more accurate).
    #
    # We intentionally do NOT match VCRUNTIME140* / MSVCP140* / ucrtbase* /
    # KERNEL32* / api-ms-win-* / CRYPT32* / bcrypt* / ntdll* -- those are OS
    # prereqs whose remediation is the bootstrap script, not a Unity reinstall.
    #
    # R5 (round-3 review nit): the broader patterns (^optix.*\.dll$ and
    # ^.*compress.*\.dll$) subsume several narrower ones (^optix\.[\d\.]+\.dll$,
    # ^etccompress\.dll$, ^s3tcompress\.dll$, ^compress_bc7e\.dll$). The
    # narrower names are deliberately RETAINED below as in-line documentation
    # of the specific Unity-shipped DLLs we've actually observed -- they
    # serve as commit-archaeology breadcrumbs ("Unity actually ships these")
    # without changing behavior (first-hit-wins; redundant matches are no-ops).
    param([Parameter(Mandatory = $true)][string]$Name)

    if ([string]::IsNullOrWhiteSpace($Name)) { return $false }
    $lower = $Name.ToLowerInvariant()
    # Anchor on known Unity-shipped third-party DLLs and Unity-specific naming
    # conventions. Order is irrelevant; first hit wins. The narrower entries
    # are documentation as much as detection (see R5 maintenance note above).
    $patterns = @(
        '^libfbxsdk\.dll$',
        '^optix\.[\d\.]+\.dll$',     # documented variant of the broader optix*.dll
        '^optix.*\.dll$',
        '^openimagedenoise\.dll$',
        '^umbraoptimizer64\.dll$',
        '^.*compress.*\.dll$',
        '^freeimage\.dll$',
        '^winpixeventruntime\.dll$',
        '^ispc_texcomp\.dll$',
        '^etccompress\.dll$',         # documented variant; subsumed by *compress*
        '^s3tcompress\.dll$',         # documented variant; subsumed by *compress*
        '^compress_bc7e\.dll$'        # documented variant; subsumed by *compress*
    )
    foreach ($p in $patterns) {
        if ($lower -match $p) {
            return $true
        }
    }
    return $false
}

function Test-UnityNativeStartup {
    param(
        [Parameter(Mandatory = $true)][string]$EditorPath,
        [Parameter(Mandatory = $true)][string]$LogPath
    )

    $logDir = Split-Path -Parent $LogPath
    if ($logDir -and -not (Test-Path -LiteralPath $logDir -PathType Container)) {
        New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    }

    $probeArgs = @(
        '-version',
        '-batchmode',
        '-nographics',
        '-quit',
        '-logFile', '-'
    )

    Write-Host "::group::Unity editor startup provisioning probe"
    Write-Host "`"$EditorPath`" $($probeArgs -join ' ')"
    & $EditorPath @probeArgs 2>&1 |
        Tee-Object -FilePath $LogPath |
        ForEach-Object { Write-Host ([string]$_) }
    $exitCode = $LASTEXITCODE
    $description = Get-NativeExitCodeDescription -ExitCode $exitCode
    Write-Host "Unity startup provisioning probe exit code: $exitCode ($description)"
    Write-Host "::endgroup::"

    return [pscustomobject]@{
        Success     = ($exitCode -eq 0)
        ExitCode    = $exitCode
        Description = $description
    }
}

function Ensure-UnityNativeStartupHealthy {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$EditorPath,
        [Parameter(Mandatory = $true)][string]$InstallRoot,
        [string]$Profile = $(Get-UnityProvisioningProfile),
        [switch]$ManagedOnly
    )

    # Test harnesses spawn ensure-editor.ps1 against a stub Unity.exe; Windows CreateProcess refuses non-PE .exe, so opt-in skip the probe.
    if ($env:UH_UNITY_SKIP_NATIVE_STARTUP_PROBE -eq '1') {
        Write-CiNotice "Skipping Unity $Version native startup probe (UH_UNITY_SKIP_NATIVE_STARTUP_PROBE=1)."
        return $EditorPath
    }

    $probeRoot = Join-Path $InstallRoot '_probes'
    $probeLog = Join-Path $probeRoot "$Version-startup-probe.log"
    $result = Test-UnityNativeStartup -EditorPath $EditorPath -LogPath $probeLog
    if ($result.Success) {
        return $EditorPath
    }

    # SHORT-CIRCUIT: 0xC0000135 / STATUS_DLL_NOT_FOUND is a HOST OS prerequisite
    # failure -- the Windows loader could not resolve a DLL Unity.exe imports
    # (overwhelmingly the Microsoft Visual C++ Redistributables -- production
    # run 70874414898 identified MSVCP100 from the 2010 generation; the 2015-2022
    # generation is the other common culprit). A managed reinstall of Unity does
    # NOT help: the missing DLL is on the OS, not in the Unity install tree.
    # Wasting ~6 minutes per matrix cell on a reinstall that
    # cannot succeed delays the actionable failure mode and obscures the real
    # remediation. We therefore short-circuit BEFORE the UH_UNITY_DISABLE_EDITOR_REPAIR
    # check too: that flag is an operator opt-out of auto-repair, and on 0xC0000135
    # there is nothing TO repair via reinstall regardless of the flag. The
    # short-circuit emits a wrap-immune ::error:: annotation BEFORE throwing so the
    # actionable host-prereq guidance survives ConciseView word-wrap (the throw text
    # is reformatted by the runner's error formatter; the Write-Host line is not).
    #
    # OVERRIDE: UH_UNITY_FORCE_REINSTALL=1 lets the operator bypass the
    # short-circuit when the OPERATOR has determined (via the named-missing-DLL
    # annotation from a prior failed job) that the missing DLL is a Unity-shipped
    # third-party library and the install is corrupt rather than the OS being
    # broken. In that case a managed reinstall WILL fix it -- the asymmetry that
    # justified the short-circuit (missing DLL is on the OS, reinstall doesn't
    # help) is inverted. The bypass emits a CI notice so the override is visible
    # in the log, then falls through to the existing repair path.
    if (Test-IsNativeDllNotFound -ExitCode $result.ExitCode) {
        if ($env:UH_UNITY_FORCE_REINSTALL -eq '1') {
            Write-CiNotice "UH_UNITY_FORCE_REINSTALL=1: bypassing 0xC0000135 short-circuit; will attempt managed reinstall (caller asserts the failure is install corruption, not host prereq). If the reinstall fails to recover Unity startup, the post-repair short-circuit will fire."
        } else {
            Write-UnityHostPrereqAnnotation -Version $Version -ExitCode $result.ExitCode -Description $result.Description -EditorPath $EditorPath -ProbeLog $probeLog
            throw "Unity $Version native startup probe failed with exit code $($result.ExitCode) (0xC0000135 / STATUS_DLL_NOT_FOUND). This is a host OS prerequisite failure (the Windows loader could not find a DLL Unity.exe imports). The most likely cause is a missing Microsoft Visual C++ Redistributable: the 2010 SP1 generation ships MSVCP100.dll/MSVCR100.dll, and the 2015-2022 generation ships VCRUNTIME140.dll/MSVCP140.dll -- BOTH are required for Unity. Skipped managed reinstall (would not help: the missing DLL is on the OS, not in the Unity install). Probe log: $probeLog. Runbook: docs/runbooks/unity-runners-after-transfer.md (Windows host prerequisites). Remediation: run scripts/unity/bootstrap-windows-runner.ps1 on this runner (or trigger the runner-bootstrap workflow_dispatch from the Actions UI). If the missing DLL is Unity-shipped (libfbxsdk, optix, etc., per the MISSING DLL annotation above), set UH_UNITY_FORCE_REINSTALL=1 to bypass this short-circuit and retry with a managed reinstall."
        }
    }

    if ($env:UH_UNITY_DISABLE_EDITOR_REPAIR -eq '1') {
        throw "Unity $Version native startup probe failed with exit code $($result.ExitCode) ($($result.Description)), and UH_UNITY_DISABLE_EDITOR_REPAIR=1 disabled auto-repair. Probe log: $probeLog"
    }

    $repairReason = "native startup probe failed with exit code $($result.ExitCode) ($($result.Description)). Probe log: $probeLog"
    Write-Host "::warning::Unity $Version native startup probe failed before the license lock; attempting one managed reinstall."
    try {
        $repaired = Repair-UnityEditorWithCiModules -Version $Version -EditorPath $EditorPath -InstallRoot $InstallRoot -Reason $repairReason -Profile $Profile -ManagedOnly:$ManagedOnly
    } catch {
        $repairFailure = $_.Exception.Message
        if (-not (Test-UnityAtomicInstallFailureMayBePinnedToExistingEditor -Message $repairFailure -InstallRoot $InstallRoot -Version $Version)) {
            throw
        }

        Write-Host "::warning::Native-startup repair for Unity $Version could not replace the existing editor tree ($repairFailure); trying an alternate CI-managed install root."
        try {
            $repaired = Install-UnityEditorWithCiModulesInAlternateRoot -Version $Version -InstallRoot $InstallRoot -Reason "native-startup repair was pinned to the existing editor tree ($repairFailure)" -Profile $Profile -ManagedOnly:$ManagedOnly
        } catch {
            throw "Unity $Version native-startup repair was blocked at the existing editor tree ($repairFailure), and alternate-root repair also failed: $($_.Exception.Message)"
        }
    }
    # Repair-UnityEditorWithCiModules requests the selected provisioning profile,
    # then we re-run the disk-authoritative module check so a CLI success with
    # missing selected-profile children is still caught before the final native
    # startup probe.
    $repaired = Ensure-UnityCiModules -Version $Version -EditorPath $repaired -InstallRoot $InstallRoot -Profile $Profile -ManagedOnly:$ManagedOnly
    $repairProbe = Test-UnityNativeStartup -EditorPath $repaired -LogPath $probeLog
    if (-not $repairProbe.Success) {
        # POST-REPAIR HOST-PREREQ SHORT-CIRCUIT: a managed reinstall succeeded but
        # the editor STILL fails 0xC0000135 / STATUS_DLL_NOT_FOUND. This means the
        # host went south mid-job (a runtime DLL was deleted or the repair installer
        # wiped a prerequisite). Same operator-actionable annotation as the
        # first-probe short-circuit, but with -RepairAttempted so the message
        # reflects that the managed reinstall already ran (and did not help, as
        # expected for 0xC0000135 -- the missing DLL is on the OS).
        if (Test-IsNativeDllNotFound -ExitCode $repairProbe.ExitCode) {
            Write-UnityHostPrereqAnnotation -Version $Version -ExitCode $repairProbe.ExitCode -Description $repairProbe.Description -EditorPath $repaired -ProbeLog $probeLog -RepairAttempted
            throw "Unity $Version native startup probe still failed after managed reinstall with exit code $($repairProbe.ExitCode) ($($repairProbe.Description)). Host OS prerequisite damage. The managed reinstall did not help (as expected for 0xC0000135). Probe log: $probeLog. Runbook: docs/runbooks/unity-runners-after-transfer.md. Remediation: run scripts/unity/bootstrap-windows-runner.ps1 (or trigger .github/workflows/runner-bootstrap.yml)."
        }
        throw "Unity $Version native startup probe still failed after managed reinstall with exit code $($repairProbe.ExitCode) ($($repairProbe.Description)). This indicates host OS/runtime prerequisite damage rather than a package/test issue. Probe log: $probeLog"
    }

    return $repaired
}

function Test-WindowsLongPathSupport {
    # Best-effort probe of whether Windows long-path (>260 char) support is enabled.
    # Reads HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem!LongPathsEnabled.
    # Returns $true (enabled), $false (explicitly disabled), or $null (unknown /
    # non-Windows / unreadable). NEVER throws. This is a prime suspect for the
    # Android NDK unpack failure: NDK extraction produces very deep paths and a
    # disabled MAX_PATH can break the unzip mid-way.
    #
    # TEST-ONLY hermeticity override (same spirit as the other UH_UNITY_* test
    # knobs): the real registry value is uncontrolled by a test and differs per
    # runner, so honor UH_UNITY_FAKE_LONGPATHS_ENABLED FIRST -- '1' => $true,
    # '0' => $false -- before falling through to the real registry probe. This lets
    # the post-mortem MAX_PATH-warning test deterministically exercise both sides of
    # the guard on every OS without depending on the host registry.
    if ($env:UH_UNITY_FAKE_LONGPATHS_ENABLED -eq '1') {
        return $true
    }
    if ($env:UH_UNITY_FAKE_LONGPATHS_ENABLED -eq '0') {
        return $false
    }
    if ([System.IO.Path]::DirectorySeparatorChar -ne '\') {
        return $null
    }
    try {
        $value = Get-ItemPropertyValue -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem' -Name 'LongPathsEnabled' -ErrorAction Stop
        if ($null -eq $value) {
            return $null
        }
        return ([int]$value -ne 0)
    } catch {
        return $null
    }
}

function Get-DeepestPathLengthUnder {
    # Best-effort: the maximum full-path character length of any file/dir under
    # $Directory (0 if none / unreadable / missing). NEVER throws. Used by the
    # post-mortem to surface whether the Android NDK extraction produced paths at or
    # beyond the Windows MAX_PATH (260) limit.
    param([string]$Directory)

    if (-not $Directory -or -not (Test-Path -LiteralPath $Directory)) {
        return 0
    }
    try {
        $max = 0
        foreach ($item in @(Get-ChildItem -LiteralPath $Directory -Recurse -Force -ErrorAction SilentlyContinue)) {
            if ($null -eq $item) {
                continue
            }
            $len = ([string]$item.FullName).Length
            if ($len -gt $max) {
                $max = $len
            }
        }
        return $max
    } catch {
        return 0
    }
}

function Write-UnityModuleInstallPostMortem {
    # WRAP-IMMUNE, best-effort post-mortem for a failed CI module install. Emits
    # single-line `::notice::`/`::error::`/`::warning::` annotations (immune to
    # ConciseView word-wrap) describing the on-disk state of every verified module
    # group and, for the Android groups specifically, deep diagnostics about the
    # NDK/SDK payload and the Windows long-path/MAX_PATH state. NEVER throws.
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$EditorPath,
        [string]$Root,
        [string]$Profile = $(Get-UnityProvisioningProfile)
    )

    try {
        Write-Host "::notice::Unity $Version module install post-mortem for provisioning profile '$Profile' (disk is the source of truth):"

        foreach ($group in @(Get-UnityCiVerifiedModuleGroups -Profile $Profile)) {
            $present = Test-UnityCiModuleGroupPresent -EditorPath $EditorPath -Group $group
            $state = if ($present) { 'present' } else { 'MISSING' }
            Write-Host "::notice::  module group '$group': $state"
        }

        $editorDir = Split-Path -Parent $EditorPath
        if ($editorDir -and (Test-UnityProvisioningProfileIncludesAndroid -Profile $Profile)) {
            $androidRoot = Join-Path $editorDir 'Data\PlaybackEngines\AndroidPlayer'
            foreach ($payload in @('NDK', 'SDK')) {
                $payloadRoot = Join-Path $androidRoot $payload
                if (Test-Path -LiteralPath $payloadRoot -PathType Container) {
                    $fileCount = @(Get-ChildItem -LiteralPath $payloadRoot -Recurse -Force -File -ErrorAction SilentlyContinue).Count
                    $deepest = Get-DeepestPathLengthUnder -Directory $payloadRoot
                    Write-Host "::notice::  AndroidPlayer\$payload : exists, $fileCount file(s), deepest absolute path length $deepest"
                } else {
                    Write-Host "::notice::  AndroidPlayer\$payload : (absent)"
                }
            }

            $ndkProps = Join-Path $androidRoot 'NDK\source.properties'
            Write-Host "::notice::  NDK\source.properties present: $([bool](Test-Path -LiteralPath $ndkProps -PathType Leaf))"
            $clang = Test-AnyUnityLeafPresent -Paths @(
                (Join-Path $androidRoot 'NDK\toolchains\llvm\prebuilt\windows-x86_64\bin\clang++.exe'),
                (Join-Path $androidRoot 'NDK\toolchains\llvm\prebuilt\linux-x86_64\bin\clang++')
            )
            # A loose recursive probe too (toolchain host-arch dir name varies).
            if (-not $clang) {
                $llvmRoot = Join-Path $androidRoot 'NDK\toolchains\llvm\prebuilt'
                if (Test-Path -LiteralPath $llvmRoot -PathType Container) {
                    $clangLeaves = @(
                        Get-ChildItem -LiteralPath $llvmRoot -Recurse -File -ErrorAction SilentlyContinue |
                            Where-Object { $_.Name -in @('clang++', 'clang++.exe') } |
                            Select-Object -First 1
                    )
                    $clang = $clangLeaves.Count -gt 0
                }
            }
            Write-Host "::notice::  NDK clang++ present: $clang"
            $java = Test-AnyUnityLeafPresent -Paths @(
                (Join-Path $androidRoot 'OpenJDK\bin\java.exe'),
                (Join-Path $androidRoot 'OpenJDK\bin\java')
            )
            Write-Host "::notice::  OpenJDK java present: $java"

            $deepestNdk = Get-DeepestPathLengthUnder -Directory (Join-Path $androidRoot 'NDK')
            $longPaths = Test-WindowsLongPathSupport
            $longPathsText = if ($null -eq $longPaths) { 'unknown' } else { [string]$longPaths }
            Write-Host "::notice::  Windows long-path support (LongPathsEnabled): $longPathsText"
            if ($deepestNdk -ge 240 -and $longPaths -ne $true) {
                Write-Host "::warning::Unity $Version Android NDK extraction reached a deep path (deepest NDK path length $deepestNdk >= 240) while Windows long-path support is not enabled. NDK extraction likely hit the Windows MAX_PATH (260) limit. See docs/runbooks/unity-runners-after-transfer.md."
            }
        }

        if ($Root) {
            Write-Host "::notice::  $(Get-InstallDriveFreeSpaceText -Root $Root)"
        }
    } catch {
        Write-Host "::notice::Unity $Version module install post-mortem could not complete: $($_.Exception.Message)"
    }
}

function Clear-PartialAndroidModulePayload {
    # Best-effort removal of the partial heavy Android payload (the NDK and SDK
    # directories under AndroidPlayer) before a RETRY of the Android module install.
    # A failed NDK unpack can leave a half-written tree that confuses the next
    # attempt; clearing only the NDK/SDK dirs (NOT the whole editor) lets the retry
    # start clean WITHOUT a multi-GB editor re-download. SAFETY: operates ONLY
    # inside the resolved editor directory; never touches anything outside it. The
    # destructive Remove-Item is wrapped in Invoke-WithRetry for Windows lock
    # resilience (the indexer/Defender can transiently hold a handle). NEVER throws
    # (best-effort): a failed clear just means the retry runs against the partial
    # tree, which is no worse than not clearing.
    param([Parameter(Mandatory = $true)][string]$EditorPath)

    try {
        $editorDir = Split-Path -Parent $EditorPath
        if (-not $editorDir) {
            return
        }
        $androidRoot = Join-Path $editorDir 'Data\PlaybackEngines\AndroidPlayer'
        $cleared = New-Object System.Collections.Generic.List[string]
        foreach ($payload in @('NDK', 'SDK')) {
            $payloadRoot = Join-Path $androidRoot $payload
            if (Test-Path -LiteralPath $payloadRoot -PathType Container) {
                try {
                    Invoke-WithRetry -MaxAttempts 3 -DelaySeconds (Get-EnsureEditorRetryDelaySeconds) -Action {
                        Remove-Item -LiteralPath $payloadRoot -Recurse -Force
                    } | Out-Null
                    $cleared.Add($payload)
                } catch {
                    Write-Host "::notice::Could not clear partial Android payload '$payloadRoot' before retry: $($_.Exception.Message)"
                }
            }
        }
        if ($cleared.Count -gt 0) {
            Write-Host "::notice::Cleared partial Android module payload before retry under '$androidRoot': $($cleared.ToArray() -join ', ')."
        }
    } catch {
        Write-Host "::notice::Clear-PartialAndroidModulePayload best-effort cleanup failed: $($_.Exception.Message)"
    }
}

function Install-UnityAndroidModules {
    # DEDICATED, BOUNDED Android module install for existing editors -- the
    # heavy/flaky tier (android + android-sdk-ndk-tools, multi-GB Google download
    # whose NDK unpack fails deterministically at ~93% on Windows). This cheap
    # repair runs before the script escalates to a profile-scoped managed
    # reinstall.
    #
    # Loop up to Get-EnsureEditorAndroidInstallRetryAttempts times: before a retry
    # (attempt > 1), clear the partial NDK/SDK payload and back off (linear). Each
    # attempt requests the android tier via the sole-producer helper and runs the
    # capturing invoker. After each attempt, re-verify the android tier ON DISK
    # (disk is the truth: exit 6 with everything present is success). On a failed
    # attempt emit the targeted failure annotation + the wrap-immune summary. On
    # exhaustion, emit the post-mortem and escalate to quarantine/reinstall unless
    # UH_UNITY_DISABLE_EDITOR_REPAIR=1.
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$EditorPath,
        [string]$InstallRoot,
        [string]$Profile = $(Get-UnityProvisioningProfile),
        [switch]$ManagedOnly
    )

    if (-not (Test-UnityProvisioningProfileIncludesAndroid -Profile $Profile)) {
        throw "Provisioning profile '$Profile' does not include the Android module tier."
    }

    # Honor -ManagedOnly for consistency with every other install path (the base
    # install, the repair, and Ensure-UnityCiModules): refuse to mutate editors
    # outside the managed root before the install loop runs.
    if ($ManagedOnly) {
        Confirm-UnityCliManagedInstallRoot -Root $InstallRoot | Out-Null
    }

    $androidIds = @(Get-UnityCiModuleIdsForTier -Tier 'android' -Profile $Profile)
    $maxAttempts = Get-EnsureEditorAndroidInstallRetryAttempts
    $retryDelaySeconds = Get-EnsureEditorRetryDelaySeconds
    $installTimeout = Get-EnsureEditorInstallTimeoutSeconds

    # The (EULA-bearing) android-tier install vector, routed through the sole
    # producer (scoped to the android tier via -ModuleIds). Captured once and reused
    # for both the install call and the failure-annotation arg echo.
    $installArgs = @(Get-UnityCliModuleInstallArguments -Verb 'install-modules' -Version $Version -ModuleIds $androidIds)

    Write-CiNotice "Installing the Android CI module tier for Unity $Version in a dedicated, separately-retried step ($($androidIds -join ', ')); exhaustion escalates to managed quarantine/reinstall unless repair is disabled."

    for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
        if ($attempt -gt 1) {
            Clear-PartialAndroidModulePayload -EditorPath $EditorPath
            $sleep = $retryDelaySeconds * $attempt
            Write-Host "::warning::Android module install attempt $($attempt - 1) of $maxAttempts did not deliver the Android tier for Unity $Version. Retrying (attempt $attempt) in $sleep second(s) after clearing the partial payload."
            Start-Sleep -Seconds $sleep
        }

        $result = Invoke-UnityCliCapture -Arguments $installArgs

        # Disk is the source of truth: re-verify the android tier groups. If none
        # are missing, the install succeeded regardless of the CLI exit code (an
        # exit 6 with everything present is the idempotent no-op).
        $missingAndroid = @(Get-MissingUnityCiModuleGroups -EditorPath $EditorPath -Profile $Profile | Where-Object { (Get-UnityCiModuleTier $_) -eq 'android' })
        if ($missingAndroid.Count -eq 0) {
            Write-CiNotice "Android CI module tier for Unity $Version present on disk after attempt $attempt (CLI exit code $($result.ExitCode))."
            return $EditorPath
        }

        # This attempt did not deliver the android tier: emit the targeted
        # annotation + the wrap-immune summary so each failed attempt is diagnosable.
        Write-UnityCliInstallFailureAnnotation -Version $Version -Output $result.Output -ExitCode $result.ExitCode -Arguments $installArgs
        # Phrase the annotation from the WRAPPER-DRIVEN kill state, not the raw
        # exit code: a NATIVE 124/125 from the Unity CLI must not be misread as
        # a heartbeat-stall or wall-clock kill. The retry classifier is unchanged
        # -- both sentinels remain retryable via the existing throw flow.
        $androidStallKilled = [bool]$result.StallKilled
        $androidWallTimedOut = [bool]$result.TimedOutWallClock
        $androidTimedOut = ($androidStallKilled -or $androidWallTimedOut)
        Write-ModuleInstallFailureDiagnostics -Version $Version -Output $result.Output -ExitCode $result.ExitCode -Arguments $installArgs -Root $InstallRoot -TimedOut:$androidTimedOut -StallKilled:$androidStallKilled -TimedOutWallClock:$androidWallTimedOut -TimeoutSeconds $installTimeout -StallSeconds (Get-EnsureEditorProgressStallSeconds)
    }

    # Exhausted every bounded Android-only attempt. Existing editors get this
    # cheap repair first, but Android exhaustion is now treated as evidence that
    # the editor tree may be internally inconsistent. Unless the operator disabled
    # editor repair, escalate to the profile-scoped managed quarantine/reinstall path.
    $stillMissing = @(Get-MissingUnityCiModuleGroups -EditorPath $EditorPath -Profile $Profile | Where-Object { (Get-UnityCiModuleTier $_) -eq 'android' })
    Write-UnityModuleInstallPostMortem -Version $Version -EditorPath $EditorPath -Root $InstallRoot -Profile $Profile
    if ($env:UH_UNITY_DISABLE_EDITOR_REPAIR -eq '1') {
        throw "Unity $Version Android CI module install FAILED after $maxAttempts attempt(s): the Android tier groups are still missing on disk ($($stillMissing -join ', ')), and UH_UNITY_DISABLE_EDITOR_REPAIR=1 disabled escalation to managed quarantine/reinstall."
    }
    if (-not $InstallRoot) {
        throw "Unity $Version Android CI module install FAILED after $maxAttempts attempt(s): the Android tier groups are still missing on disk ($($stillMissing -join ', ')), and no managed install root was supplied for quarantine/reinstall."
    }

    Stop-StaleUnityProvisioningProcesses -InstallRoot $InstallRoot -Version $Version -Reason "Android-only repair exhausted before managed reinstall"
    Write-Host "::warning::Unity $Version Android-only repair exhausted after $maxAttempts attempt(s); escalating to managed quarantine/reinstall with provisioning profile '$Profile'."
    return Repair-UnityEditorWithCiModules -Version $Version -EditorPath $EditorPath -InstallRoot $InstallRoot -Reason "Android-only repair exhausted after $maxAttempts attempt(s); missing Android groups: $($stillMissing -join ', ')." -Profile $Profile -ManagedOnly:$ManagedOnly
}

function Test-TextIndicatesEditorNotModuleManageable {
    # PURE, StrictMode-safe classifier. Returns $true iff the supplied text carries
    # the Unity-CLI signal that the target editor is NOT module-manageable, i.e. that
    # `install-modules` will refuse to add anything to it. Observed verbatim on the
    # failing self-hosted run (Unity 6000.3.16f1 standalone, run 26701943540):
    #
    #   Error: No modules found for this editor.
    #   Module installation is only supported for editors installed with Unity Hub.
    #   Try reinstalling this editor with Unity Hub to use this feature.
    #
    # Matching ANY of these phrases is sufficient; the wording has drifted across
    # beta builds, so we accept the stable substrings rather than the whole block.
    # This is the PROACTIVE root-cause signal that lets the caller skip the doomed
    # `install-modules` -> exit 6 -> uninstall -> quarantine path and go straight to
    # the atomic `install <v> -m <modules>` reinstallation verb (the documented way
    # to add modules to such an editor; see Install-UnityEditorWithCiModules).
    param([string]$Text)

    $value = [string]$Text
    if ($value.Trim().Length -eq 0) {
        return $false
    }
    return ($value -match '(?i)only supported for editors installed with Unity Hub') -or
        ($value -match '(?i)reinstall(?:ing)? this editor with Unity Hub') -or
        ($value -match '(?i)No modules found for this editor')
}

function Test-UnityAtomicInstallFailureMayBePinnedToExistingEditor {
    param(
        [string]$Message,
        [string]$InstallRoot = '',
        [string]$Version = ''
    )

    $value = [string]$Message
    if ($value.Trim().Length -eq 0) {
        return $false
    }

    if (($value -match '(?i)already[-\s]?installed') -or
        ($value -match '(?i)editor already installed') -or
        ($value -match '(?i)is already installed') -or
        ($value -match '(?i)still missing on disk after the atomic install') -or
        ($value -match '(?i)required CI module groups .* still missing .* after the atomic install')) {
        return $true
    }

    if ($value -notmatch '(?i)being used by another process|cannot access the file|file lock|locked') {
        return $false
    }

    if (-not $InstallRoot -or -not $Version) {
        return $false
    }

    $normalizedMessage = $value.Replace('/', '\')
    $normalizedRoot = ([System.IO.Path]::GetFullPath($InstallRoot)).Replace('/', '\').TrimEnd('\')
    $canonicalVersionRoot = "$normalizedRoot\$Version"
    return ($normalizedMessage.IndexOf($canonicalVersionRoot, [System.StringComparison]::OrdinalIgnoreCase) -ge 0)
}

function Test-UnityEditorModuleManageable {
    # Best-effort, NON-THROWING probe: is the editor at $Version in a state where the
    # Unity CLI's `install-modules` can ADD modules to it? Returns a StrictMode-safe
    # hashtable @{ Manageable=[bool]; Reason=[string]; Output=[string[]] }.
    #
    # Manageable is $false ONLY when the `-l` listing text carries an explicit
    # not-module-manageable signal (Test-TextIndicatesEditorNotModuleManageable).
    # When the probe cannot run, times out, or returns ambiguous output, Manageable
    # defaults to $true: we must NOT route a healthy editor down the heavyweight
    # atomic-reinstall path on a flaky probe -- the existing disk-authoritative
    # install-modules flow already handles the normal "modules missing" case and an
    # exit 6 there still escalates correctly. Only a POSITIVE unmanageable signal
    # short-circuits to the atomic reinstall.
    #
    # Uses the timeout-capable capturing invoker bounded by the PROBE timeout (the
    # `-l` listing is a quick metadata read, not a multi-GB install -- it must NOT
    # inherit the 45-minute install wall-clock), and we read the captured output even
    # on a non-zero exit (Get-UnityCliOutput would discard it) so the signal text is
    # available regardless of the listing's exit code.
    param([Parameter(Mandatory = $true)][string]$Version)

    $result = $null
    try {
        $requestedTimeout = Get-EnsureEditorProbeTimeoutSeconds
        $effectiveTimeout = Get-EffectiveUnityCliTimeoutSeconds -RequestedSeconds $requestedTimeout
        $result = Invoke-UnityCliCaptureWithTimeout -Arguments @('install-modules', '-e', $Version, '-l') -TimeoutSeconds $effectiveTimeout -TimeoutKnob 'UH_ENSURE_EDITOR_PROBE_TIMEOUT_SECONDS'
    } catch {
        return @{ Manageable = $true; Reason = "module-manageability probe threw: $($_.Exception.Message)"; Output = @() }
    }

    $lines = @($result.Output | ForEach-Object { [string]$_ })
    $text = ($lines -join "`n")
    if (Test-TextIndicatesEditorNotModuleManageable -Text $text) {
        return @{
            Manageable = $false
            Reason     = 'Unity CLI reports this editor is not module-manageable (not installed via Hub/CLI registration); install-modules would fail with exit 6.'
            Output     = $lines
        }
    }

    return @{ Manageable = $true; Reason = ''; Output = $lines }
}

function Install-UnityEditorWithCiModulesInAlternateRoot {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$InstallRoot,
        [Parameter(Mandatory = $true)][string]$Reason,
        [string]$Profile = $(Get-UnityProvisioningProfile),
        [string[]]$RequiredEditorPayloadRelativePath = @(),
        [switch]$ManagedOnly
    )

    $alternateRoot = Get-UnityCiAlternateInstallRoot -InstallRoot $InstallRoot
    if (-not (Test-IsPathInsideDirectory -Path $alternateRoot -Directory $InstallRoot)) {
        throw "Refusing alternate-root Unity $Version repair because the alternate install root '$alternateRoot' is outside the managed install root '$InstallRoot'."
    }

    return Invoke-WithUnityInstallLock -Version $Version -InstallRoot $InstallRoot -Action {
        Assert-UnityProvisioningBudgetCanFit -Operation "alternate-root Unity $Version managed install" -MinimumSeconds 60
        $previousRoot = $null
        try {
            $previousRoot = Get-UnityCliInstallRoot
        } catch {
            Write-CiNotice "Could not query the current Unity CLI install root before alternate-root repair: $($_.Exception.Message)"
        }

        try {
            New-Item -ItemType Directory -Force -Path $alternateRoot | Out-Null
            $existingAlternate = Find-UnityEditor -Version $Version -Root $alternateRoot
            if ($existingAlternate) {
                $existingAlternateMissing = @(Get-MissingUnityCiModuleGroups -EditorPath $existingAlternate -Profile $Profile)
                if ($existingAlternateMissing.Count -eq 0) {
                    $existingAlternateMissingPayload = @(Get-MissingRequiredEditorPayloadPaths -EditorPath $existingAlternate -RelativePaths $RequiredEditorPayloadRelativePath)
                    if ($existingAlternateMissingPayload.Count -eq 0) {
                        Write-CiNotice "Using already repaired alternate-root CI editor for Unity $Version with provisioning profile '$Profile' and complete required payload: $existingAlternate"
                        return $existingAlternate
                    }

                    Write-Host "::warning::Quarantining payload-incomplete alternate-root Unity $Version editor before retrying alternate-root repair: $existingAlternate (missing payload: $($existingAlternateMissingPayload -join ', '))."
                } else {
                    Write-Host "::warning::Quarantining partial alternate-root Unity $Version editor before retrying alternate-root repair: $existingAlternate (missing modules: $($existingAlternateMissing -join ', '))."
                }

                Move-UnityVersionInstallToQuarantine -Version $Version -InstallRoot $alternateRoot
            }

            Write-Host "::warning::Installing Unity $Version into alternate CI-managed root '$alternateRoot' because the existing editor tree could not be repaired in place. Reason: $Reason"
            Set-UnityCliInstallPath -Root $alternateRoot
            Confirm-UnityCliManagedInstallRoot -Root $alternateRoot | Out-Null

            $resolved = Install-UnityEditorWithCiModules -Version $Version -InstallRoot $alternateRoot -Reason "alternate-root repair after existing editor repair was blocked: $Reason" -Profile $Profile -ManagedOnly:$true
            $missing = @(Get-MissingUnityCiModuleGroups -EditorPath $resolved -Profile $Profile)
            if ($missing.Count -gt 0) {
                throw "Alternate-root Unity $Version install completed at '$resolved', but required CI module groups for provisioning profile '$Profile' are still missing on disk: $($missing -join ', ')."
            }
            $missingPayload = @(Get-MissingRequiredEditorPayloadPaths -EditorPath $resolved -RelativePaths $RequiredEditorPayloadRelativePath)
            if ($missingPayload.Count -gt 0) {
                throw "Alternate-root Unity $Version install completed at '$resolved', but required editor payload is still missing on disk: $($missingPayload -join ', ')."
            }
            if ($ManagedOnly -and -not (Test-IsPathInsideDirectory -Path $resolved -Directory $InstallRoot)) {
                throw "Alternate-root Unity $Version install resolved outside the managed install root '$InstallRoot': $resolved"
            }

            $script:ProvisioningEditorPath = $resolved
            Write-CiNotice "Unity $Version alternate-root repair succeeded: $resolved"
            return $resolved
        } finally {
            $restoreRoot = if ($previousRoot) { $previousRoot } else { $InstallRoot }
            try {
                Set-UnityCliInstallPath -Root $restoreRoot
            } catch {
                Write-Host "::warning::Could not restore Unity CLI install path to '$restoreRoot' after alternate-root repair: $($_.Exception.Message)"
            }
        }
    }
}

function Install-UnityEditorModulesViaAtomicReinstall {
    # ROOT-CAUSE SIDESTEP for the not-module-manageable editor (the exit-6 failure):
    # add the required modules by re-running the ATOMIC `install <v> -m <modules>`
    # verb -- the Unity-documented "headless reinstallation ... to add modules during
    # installation" -- INSTEAD of `install-modules` (which exits 6 on such an editor)
    # followed by uninstall + quarantine.
    #
    # WHY TRY IN PLACE FIRST (the real win): the observed failure is that the
    # uninstall+quarantine path then fails because a process holds a handle on
    # ...\<version>\Editor, so the Move-Item cannot win and the whole run aborts.
    # Install-UnityEditorWithCiModules runs the atomic `install` verb DIRECTLY,
    # WITHOUT a preceding quarantine, so when the install can repair/overlay the
    # existing tree in place we never touch the locker at all. If that atomic
    # in-place install genuinely fails (e.g. the editor tree is corrupt and the CLI
    # cannot overlay it), we FALL BACK to the existing quarantine+reinstall path
    # (Repair-UnityEditorWithCiModules) -- which may still hit the locker, but that is
    # no worse than today and is reported honestly by the wrap-immune ::error:: there.
    #
    # Wrapped in the install lock so the in-place attempt and any fallback share one
    # critical section (Invoke-WithUnityInstallLock is re-entrant, so the nested
    # Repair-... call does not deadlock). StrictMode-safe.
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$EditorPath,
        [Parameter(Mandatory = $true)][string]$InstallRoot,
        [Parameter(Mandatory = $true)][string]$Reason,
        [string]$Profile = $(Get-UnityProvisioningProfile),
        [switch]$ManagedOnly
    )

    return Invoke-WithUnityInstallLock -Version $Version -InstallRoot $InstallRoot -Action {
        Write-CiNotice "Unity $Version is not module-manageable via install-modules; routing to the atomic 'install -m' reinstallation (in place, no quarantine) to add the required modules. Reason: $Reason"
        try {
            # In-place atomic install: NO quarantine first, so a held handle on the
            # editor tree is never even contended unless the CLI itself needs to
            # rewrite a locked file.
            return Install-UnityEditorWithCiModules -Version $Version -InstallRoot $InstallRoot -Reason $Reason -Profile $Profile -ManagedOnly:$ManagedOnly
        } catch {
            $inPlaceMessage = $_.Exception.Message
            if ($env:UH_UNITY_DISABLE_EDITOR_REPAIR -eq '1') {
                throw
            }
            $alternateFailureMessage = ''
            if (Test-UnityAtomicInstallFailureMayBePinnedToExistingEditor -Message $inPlaceMessage -InstallRoot $InstallRoot -Version $Version) {
                try {
                    Write-Host "::warning::Atomic in-place reinstall for Unity $Version did not deliver the required modules ($inPlaceMessage); trying an alternate CI-managed install root before touching the locked editor tree."
                    return Install-UnityEditorWithCiModulesInAlternateRoot -Version $Version -InstallRoot $InstallRoot -Reason "atomic in-place reinstall was pinned to the existing editor tree ($inPlaceMessage)" -Profile $Profile -ManagedOnly:$ManagedOnly
                } catch {
                    $alternateFailureMessage = $_.Exception.Message
                    Write-Host "::warning::Alternate-root repair for Unity $Version also failed ($alternateFailureMessage); falling back to quarantine + reinstall."
                }
            } else {
                Write-Host "::warning::Atomic in-place reinstall for Unity $Version failed for a reason that does not look pinned to the existing editor tree ($inPlaceMessage); falling back to quarantine + reinstall."
            }

            # The atomic in-place install and alternate-root install could not deliver
            # the modules. Fall back to the heavier quarantine+reinstall, which moves
            # the version dir aside first. HONEST CAVEAT: if a cross-identity process
            # holds a hard lock on the tree, that quarantine can still fail --
            # Move-UnityInstallDirectoryToQuarantine emits a wrap-immune ::error::
            # naming the residual as runner-side.
            $fallbackReason = "atomic in-place reinstall did not deliver required modules ($inPlaceMessage)"
            if ($alternateFailureMessage) {
                $fallbackReason += "; alternate-root repair also failed ($alternateFailureMessage)"
            }
            return Repair-UnityEditorWithCiModules -Version $Version -EditorPath $EditorPath -InstallRoot $InstallRoot -Reason $fallbackReason -Profile $Profile -ManagedOnly:$ManagedOnly
        }
    }
}

function Ensure-UnityCiModules {
    # IDEMPOTENT, disk-authoritative, TIER-AWARE CI module install. The standalone
    # beta CLI can return "No modules found to install." with exit code 6 when
    # modules are already present, and it cannot add modules to manually installed
    # editors. Classify the result against disk proof first; if required module
    # groups are missing, handle the CORE tier and the ANDROID tier separately.
    #   1. all groups present on disk                  -> done.
    #   2. core missing                                -> scoped install-modules; if
    #      still missing and repair enabled            -> quarantine/reinstall (core).
    #   3. android missing                             -> dedicated bounded retry;
    #      exhaustion escalates to managed quarantine/reinstall unless disabled.
    #   4. still missing after repair is disabled       -> throw with post-mortem.
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$EditorPath,
        [string]$InstallRoot,
        [string]$Profile = $(Get-UnityProvisioningProfile),
        [switch]$ManagedOnly
    )

    $moduleIds = @(Get-UnityCiModuleIds -Profile $Profile)
    $verifiedGroups = @(Get-UnityCiVerifiedModuleGroups -Profile $Profile)

    if ($ManagedOnly) {
        Confirm-UnityCliManagedInstallRoot -Root $InstallRoot | Out-Null
    }

    if ($moduleIds.Count -eq 0 -and $verifiedGroups.Count -eq 0) {
        Write-CiNotice "Provisioning profile '$Profile' requires the Unity editor only; skipping Unity module install and verification."
        return $EditorPath
    }

    # Best-effort listing diagnostic (unchanged): the beta listing format may not
    # contain every literal module id, so a mismatch only warns and never aborts.
    $listLines = @(Get-UnityCliOutput -Arguments @('install-modules', '-e', $Version, '-l'))
    if ($listLines.Count -gt 0) {
        $listText = ($listLines -join "`n")
        $missingFromList = @($moduleIds | Where-Object { $listText -notmatch [regex]::Escape($_) })
        if ($missingFromList.Count -gt 0) {
            Write-Host "::warning::Unity $Version module listing did not contain every required CI module id ($($missingFromList -join ', ')). Proceeding; the install result below is classified against the on-disk module layout."
        }
    } else {
        Write-CiNotice "Could not list installable modules for Unity $Version (best-effort); proceeding with required CI module ids: $($moduleIds -join ', ')."
    }

    # Step 1: verify everything in the selected profile. If all present, nothing
    # to do.
    $missing = @(Get-MissingUnityCiModuleGroups -EditorPath $EditorPath -Profile $Profile)
    if ($missing.Count -eq 0) {
        Write-CiNotice "All required Unity CI module groups for provisioning profile '$Profile' already present on disk for Unity $Version; nothing to install."
        return $EditorPath
    }

    if ($InstallRoot) {
        $alternateEditor = Find-UnityCiAlternateEditorWithCiModules -Version $Version -InstallRoot $InstallRoot -Profile $Profile
        if ($alternateEditor) {
            return $alternateEditor
        }
    }

    # Step 1b (ROOT-CAUSE SIDESTEP): modules ARE missing. Before driving the
    # `install-modules` path (which exits 6 -> uninstall -> quarantine -> can be
    # blocked by a locker, the observed 6000.3.16f1 standalone failure), PROACTIVELY
    # ask whether this editor is even module-manageable. If the CLI reports it is not
    # (it was not installed via Hub/CLI registration), `install-modules` cannot help;
    # route straight to the atomic `install -m` reinstallation, which adds the modules
    # IN PLACE (no quarantine, so the locker is never contended) and only falls back
    # to quarantine+reinstall if the in-place install genuinely fails.
    #
    # Gated to the cases where the atomic reinstall can actually run: a managed
    # install root is available and editor repair is not disabled. An operator can
    # force the legacy install-modules-first path via
    # UH_UNITY_DISABLE_PROACTIVE_MODULE_REINSTALL=1 (defense for an unforeseen
    # regression on a future CLI build). The disk-authoritative install-modules flow
    # below remains the path for a module-MANAGEABLE editor that is merely missing a
    # module (the common, healthy case).
    $proactiveDisabled = $env:UH_UNITY_DISABLE_PROACTIVE_MODULE_REINSTALL -eq '1'
    $repairDisabledForProactive = $env:UH_UNITY_DISABLE_EDITOR_REPAIR -eq '1'
    if (-not $proactiveDisabled -and -not $repairDisabledForProactive -and $InstallRoot) {
        $manageability = Test-UnityEditorModuleManageable -Version $Version
        if (-not $manageability.Manageable) {
            Write-Host "::warning::Unity $Version editor is not module-manageable ($($manageability.Reason)). Missing module groups: $($missing -join ', '). Skipping the install-modules -> exit 6 -> quarantine path and reinstalling atomically with the required modules instead."
            return Install-UnityEditorModulesViaAtomicReinstall -Version $Version -EditorPath $EditorPath -InstallRoot $InstallRoot -Reason "editor not module-manageable; missing groups: $($missing -join ', ')" -Profile $Profile -ManagedOnly:$ManagedOnly
        }
    }

    # Step 2: determine whether any CORE-tier group is missing so it can be handled
    # with the heavy reinstall/repair strategy. The android-tier partition is
    # deliberately NOT computed here: Step 4 re-derives it from disk AFTER the core
    # repair (which can reinstall the whole editor), so a Step-2 snapshot would be
    # stale -- disk is the source of truth.
    $missingCore = @($missing | Where-Object { (Get-UnityCiModuleTier $_) -eq 'core' })

    # Step 3: install the CORE tier if any core group is missing. This runs the
    # existing heavy/classify-then-decide path, but SCOPED to the core tier ids.
    if ($missingCore.Count -gt 0) {
        # Single source of truth for the (EULA-bearing) `install-modules` arg vector,
        # scoped to the CORE tier. Captured ONCE and reused for the install call and
        # the failure-annotation arg echo.
        $installArgs = @(Get-UnityCliModuleInstallArguments -Verb 'install-modules' -Version $Version -ModuleIds (Get-UnityCiModuleIdsForTier -Tier 'core' -Profile $Profile))

        # Attempt the install via the capturing (non-throwing) path so we can inspect
        # BOTH the exit code AND the output text before deciding whether it was fatal.
        $result = Invoke-UnityCliCapture -Arguments $installArgs

        # Re-verify the core tier on disk (disk is the source of truth; an exit 6
        # with everything present is the idempotent no-op).
        $missingCoreAfter = @(Get-MissingUnityCiModuleGroups -EditorPath $EditorPath -Profile $Profile | Where-Object { (Get-UnityCiModuleTier $_) -eq 'core' })
        if ($missingCoreAfter.Count -gt 0) {
            # Tail of the captured output for diagnostics (collapsed first so the
            # Android NDK progress spam does not bury the tail).
            $tail = Get-CollapsedCliOutputTail -Output $result.Output -MaxLines 20

            # The install genuinely did not deliver the required core modules. Emit a
            # targeted, high-signal annotation + the wrap-immune summary BEFORE we
            # repair or throw, so the root cause is obvious in the CI log.
            Write-UnityCliInstallFailureAnnotation -Version $Version -Output $result.Output -ExitCode $result.ExitCode -Arguments $installArgs
            # Phrase from wrapper-driven kill state so a NATIVE 124/125 from the
            # Unity CLI is not misread as a stall or wall-clock kill. Retryable
            # classification (both sentinels) is unchanged via the throw flow.
            $moduleAddStallKilled = [bool]$result.StallKilled
            $moduleAddWallTimedOut = [bool]$result.TimedOutWallClock
            $moduleAddTimedOut = ($moduleAddStallKilled -or $moduleAddWallTimedOut)
            Write-ModuleInstallFailureDiagnostics -Version $Version -Output $result.Output -ExitCode $result.ExitCode -Arguments $installArgs -Root $InstallRoot -TimedOut:$moduleAddTimedOut -StallKilled:$moduleAddStallKilled -TimedOutWallClock:$moduleAddWallTimedOut -TimeoutSeconds (Get-EnsureEditorInstallTimeoutSeconds) -StallSeconds (Get-EnsureEditorProgressStallSeconds)

            $repairDisabled = $env:UH_UNITY_DISABLE_EDITOR_REPAIR -eq '1'
            if ($repairDisabled) {
                throw "Unity $Version is missing required CORE CI module groups ($($missingCoreAfter -join ', ')), and UH_UNITY_DISABLE_EDITOR_REPAIR=1 disabled auto-repair. CLI output tail:`n$tail"
            }

            if ($InstallRoot) {
                # Quarantine + reinstall with the selected provisioning profile.
                $EditorPath = Repair-UnityEditorWithCiModules -Version $Version -EditorPath $EditorPath -InstallRoot $InstallRoot -Reason "required CORE CI module groups missing ($($missingCoreAfter -join ', ')). CLI output tail:`n$tail" -Profile $Profile -ManagedOnly:$ManagedOnly
            } else {
                throw "Unity $Version 'install-modules' failed with exit code $($result.ExitCode), and required CORE CI module groups are missing on disk ($($missingCoreAfter -join ', ')). CLI output tail:`n$tail"
            }
        } else {
            Write-CiNotice "Core Unity CI module tier present on disk for Unity $Version (CLI exit code $($result.ExitCode))."
        }
    }

    # Step 4: re-verify and, if any ANDROID-tier group is still missing, install it
    # via the dedicated, bounded Android step. That step can escalate to managed
    # quarantine/reinstall only after its editor-preserving attempts are exhausted.
    $missingAndroid = @(Get-MissingUnityCiModuleGroups -EditorPath $EditorPath -Profile $Profile | Where-Object { (Get-UnityCiModuleTier $_) -eq 'android' })
    if ($missingAndroid.Count -gt 0) {
        return Install-UnityAndroidModules -Version $Version -EditorPath $EditorPath -InstallRoot $InstallRoot -Profile $Profile -ManagedOnly:$ManagedOnly
    }

    # Step 5: final verification across all tiers.
    $finalMissing = @(Get-MissingUnityCiModuleGroups -EditorPath $EditorPath -Profile $Profile)
    if ($finalMissing.Count -gt 0) {
        Write-UnityModuleInstallPostMortem -Version $Version -EditorPath $EditorPath -Root $InstallRoot -Profile $Profile
        throw "Unity $Version CI module install completed, but required module groups for provisioning profile '$Profile' are still missing on disk: $($finalMissing -join ', ') (see the post-mortem above)."
    }

    return $EditorPath
}

function Add-WindowsIl2CppModule {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$EditorPath,
        [string]$InstallRoot,
        [string]$Profile = $(Get-UnityProvisioningProfile),
        [switch]$ManagedOnly
    )

    return Ensure-UnityCiModules -Version $Version -EditorPath $EditorPath -InstallRoot $InstallRoot -Profile $Profile -ManagedOnly:$ManagedOnly
}

function Write-InstalledEditorDiagnostics {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Reason
    )

    Write-Host "::group::Unity editor resolution diagnostics"
    Write-Host "Reason: $Reason"
    Write-Host "Requested Unity version: $Version"
    Write-Host "Configured install root: $Root"
    try {
        $cliRoot = Get-UnityCliInstallRoot
        if ($cliRoot) {
            Write-Host "Unity CLI reported install root: $cliRoot"
        } else {
            Write-Host "Unity CLI reported install root: (unavailable)"
        }
    } catch {
        Write-Host "::notice::Could not query Unity CLI install root: $($_.Exception.Message)"
    }

    try {
        Write-Host "Known Unity.exe candidate paths:"
        foreach ($candidate in Get-UnityEditorCandidates -Version $Version -Root $Root) {
            $exists = Test-Path -LiteralPath $candidate -PathType Leaf
            Write-Host "  [$exists] $candidate"
        }
    } catch {
        Write-Host "::notice::Could not enumerate Unity.exe candidates: $($_.Exception.Message)"
    }

    try {
        Write-Host "Installed Unity editors reported by CLI:"
        Invoke-UnityCliSafe -Arguments @('editors', '-i') | Out-Null
    } catch {
        Write-Host "::notice::Could not query installed Unity editors: $($_.Exception.Message)"
    }
    Write-Host "::endgroup::"
}

function Write-InstallDiagnostics {
    # Pre-install diagnostic dump so the NEXT base-install failure is debuggable.
    # The observed failure (6000.0.32f1, ~34 minutes, exit 6, almost no output)
    # gave us nothing to act on. This emits, inside a collapsible ::group:::
    #   * the resolved CLI path actually being invoked,
    #   * the CLI version (best-effort), and
    #   * free disk space on the install drive (a likely culprit for a slow/failed
    #     multi-GB editor download).
    # Every probe is wrapped in try/catch and StrictMode-safe: a diagnostic must
    # NEVER abort the bootstrap it is meant to help debug.
    param([Parameter(Mandatory = $true)][string]$Root)

    Write-Host "::group::Unity CLI install diagnostics"
    try {
        Write-Host "Resolved Unity CLI path: $script:UnityCliPath"
    } catch {
        Write-Host "::notice::Could not report the resolved Unity CLI path: $($_.Exception.Message)"
    }

    try {
        Write-Host "Unity CLI version: $(Get-UnityCliVersionText)"
    } catch {
        Write-Host "::notice::Could not query the Unity CLI version: $($_.Exception.Message)"
    }

    # Free space on the drive of the install root. A multi-GB editor download that
    # runs out of disk would explain a long, output-starved failure. Reuses the
    # shared probe so the pre-install dump and the on-failure summary agree.
    Write-Host (Get-InstallDriveFreeSpaceText -Root $Root)
    Write-Host "::endgroup::"
}

function Get-ProvisioningDiagnosticsPath {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Version,
        [string]$Path
    )

    if ($Path -and $Path.Trim().Length -gt 0) {
        if ((Test-Path -LiteralPath $Path -PathType Container) -or [string]::IsNullOrEmpty([System.IO.Path]::GetExtension($Path))) {
            return (Join-Path $Path 'ensure-editor-summary.json')
        }
        return $Path
    }
    return (Join-Path (Join-Path $Root '_diagnostics') "$Version-provisioning-summary.json")
}

function Write-UnityProvisioningSummary {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Version,
        [string]$Path,
        [string]$EditorPath
    )

    $jsonPath = Get-ProvisioningDiagnosticsPath -Root $Root -Version $Version -Path $Path
    $textPath = [System.IO.Path]::ChangeExtension($jsonPath, '.txt')
    try {
        $dir = Split-Path -Parent $jsonPath
        if ($dir -and -not (Test-Path -LiteralPath $dir -PathType Container)) {
            New-Item -ItemType Directory -Force -Path $dir | Out-Null
        }

        # Local copy of the current provisioning profile. Named
        # $provisioningProfile (NOT the auto-variable $PROFILE, which holds
        # the PowerShell startup-script path) so this helper does not
        # shadow that built-in even though PowerShell variable names are
        # case-insensitive.
        $provisioningProfile = Get-UnityProvisioningProfile
        $modulePresence = [ordered]@{}
        foreach ($group in @(Get-UnityCiModuleSpec | Where-Object { $_.Verified } | ForEach-Object { $_.Id })) {
            $present = $false
            if ($EditorPath -and (Test-Path -LiteralPath $EditorPath -PathType Leaf)) {
                $present = Test-UnityCiModuleGroupPresent -EditorPath $EditorPath -Group $group
            }
            $modulePresence[$group] = $present
        }
        $requiredModulePresence = [ordered]@{}
        foreach ($group in @(Get-UnityCiVerifiedModuleGroups -Profile $provisioningProfile)) {
            $requiredModulePresence[$group] = $modulePresence[$group]
        }

        $commandClasses = @($script:ProvisioningCommandClasses | Sort-Object)
        $summary = [ordered]@{
            generatedUtc              = [DateTime]::UtcNow.ToString('o')
            unityVersion              = $Version
            provisioningProfile       = $provisioningProfile
            cliPath                   = $script:UnityCliPath
            cliVersion                = $(if ($script:UnityCliVersionText) { $script:UnityCliVersionText } else { '(not queried)' })
            installRoot               = $Root
            editorPath                = $EditorPath
            ciManagedOnly             = [bool]$CiManagedOnly
            attemptedCommandClasses   = $commandClasses
            desiredModules            = @(Get-UnityCiModuleIds -Profile $provisioningProfile)
            verifiedModules           = @(Get-UnityCiVerifiedModuleGroups -Profile $provisioningProfile)
            skippedModuleGroups       = @(Get-UnityCiSkippedModuleGroups -Profile $provisioningProfile)
            modulePresence            = $modulePresence
            requiredModulePresence    = $requiredModulePresence
            provisioningBudgetSeconds = $script:ProvisioningBudgetSeconds
            remainingBudgetSeconds    = Get-RemainingUnityProvisioningBudgetSeconds
            timeoutEvents             = @($script:ProvisioningTimeoutEvents.ToArray())
            processCleanupEvents      = @($script:ProvisioningProcessCleanupEvents.ToArray())
            finalClassification       = $script:ProvisioningFinalClassification
        }

        $summary | ConvertTo-Json -Depth 8 -Compress | Set-Content -LiteralPath $jsonPath -Encoding UTF8
        $moduleText = ($modulePresence.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ', '
        $textLines = @(
            "Unity provisioning summary",
            "classification=$script:ProvisioningFinalClassification",
            "provisioningProfile=$provisioningProfile",
            "unityVersion=$Version",
            "cliPath=$script:UnityCliPath",
            "cliVersion=$($summary.cliVersion)",
            "installRoot=$Root",
            "editorPath=$EditorPath",
            "attemptedCommandClasses=$($commandClasses -join ',')",
            "desiredModules=$($summary.desiredModules -join ',')",
            "verifiedModules=$($summary.verifiedModules -join ',')",
            "skippedModuleGroups=$($summary.skippedModuleGroups -join ',')",
            "modulePresence=$moduleText",
            "timeoutEvents=$($script:ProvisioningTimeoutEvents.Count)",
            "processCleanupEvents=$($script:ProvisioningProcessCleanupEvents.Count)"
        )
        $textLines | Set-Content -LiteralPath $textPath -Encoding UTF8
    } catch {
        Write-Host "::warning::Failed to write Unity provisioning diagnostics summary: $($_.Exception.Message)"
    }
}

Initialize-UnityProvisioningBudget
Write-CiNotice "Unity editor provisioning profile: $ProvisioningProfile."

try {
New-Item -ItemType Directory -Force -Path $InstallRoot | Out-Null

$editor = Find-UnityEditor -Version $UnityVersion -Root $InstallRoot -IncludeHostInstalls:(-not $CiManagedOnly)
if ($RequireHealthyExisting) {
    if (-not $editor) {
        Write-InstalledEditorDiagnostics -Version $UnityVersion -Root $InstallRoot -Reason "RequireHealthyExisting was set and no existing Unity editor was found."
        throw "Unity $UnityVersion is not installed under '$InstallRoot' and RequireHealthyExisting is set. CI test jobs do not install or repair Unity in-job; run scripts/unity/maintain-windows-runner.ps1 or dispatch .github/workflows/runner-bootstrap.yml to provision the editor and required modules first."
    }

    $script:ProvisioningEditorPath = $editor
    $missingModules = @(Get-MissingUnityCiModuleGroups -EditorPath $editor -Profile $ProvisioningProfile)
    if ($missingModules.Count -gt 0) {
        $alternateEditor = Find-UnityCiAlternateEditorWithCiModules -Version $UnityVersion -InstallRoot $InstallRoot -Profile $ProvisioningProfile
        if ($alternateEditor) {
            $editor = $alternateEditor
            $script:ProvisioningEditorPath = $editor
            $missingModules = @(Get-MissingUnityCiModuleGroups -EditorPath $editor -Profile $ProvisioningProfile)
        }
    }

    if ($missingModules.Count -gt 0) {
        Write-InstalledEditorDiagnostics -Version $UnityVersion -Root $InstallRoot -Reason "RequireHealthyExisting was set and required module groups are missing: $($missingModules -join ', ')."
        throw "Unity $UnityVersion is missing required CI module groups for provisioning profile '$ProvisioningProfile': $($missingModules -join ', '). CI test jobs fail fast instead of installing modules in-job; run scripts/unity/maintain-windows-runner.ps1 or dispatch .github/workflows/runner-bootstrap.yml to repair this editor."
    }

    $missingPayload = @(Get-MissingRequiredEditorPayloadPaths -EditorPath $editor -RelativePaths $RequiredEditorPayloadRelativePath)
    if ($missingPayload.Count -gt 0) {
        $alternateEditor = Find-UnityCiAlternateEditorWithCiModules -Version $UnityVersion -InstallRoot $InstallRoot -Profile $ProvisioningProfile
        if ($alternateEditor) {
            $alternateMissingPayload = @(Get-MissingRequiredEditorPayloadPaths -EditorPath $alternateEditor -RelativePaths $RequiredEditorPayloadRelativePath)
            if ($alternateMissingPayload.Count -eq 0) {
                Write-CiNotice "Using reusable alternate-root CI editor with complete required payload for Unity ${UnityVersion}: $alternateEditor"
                $editor = $alternateEditor
                $script:ProvisioningEditorPath = $editor
                $missingPayload = @()
            } else {
                Write-CiNotice "Alternate-root CI editor for Unity $UnityVersion is also missing required editor payload: $($alternateMissingPayload -join ', ')."
            }
        }
    }
    if ($missingPayload.Count -gt 0) {
        Write-InstalledEditorDiagnostics -Version $UnityVersion -Root $InstallRoot -Reason "RequireHealthyExisting was set and required editor payload is missing: $($missingPayload -join ', ')."
        throw "Unity $UnityVersion required editor payload is missing: $($missingPayload -join ', '). CI test jobs fail fast instead of repairing when RequireHealthyExisting is set."
    }

    if ($env:UH_UNITY_SKIP_NATIVE_STARTUP_PROBE -eq '1') {
        Write-CiNotice "Skipping Unity $UnityVersion native startup probe (UH_UNITY_SKIP_NATIVE_STARTUP_PROBE=1)."
    } else {
        $probeRoot = Join-Path $InstallRoot '_probes'
        $probeLog = Join-Path $probeRoot "$UnityVersion-startup-probe.log"
        $startupResult = Test-UnityNativeStartup -EditorPath $editor -LogPath $probeLog
        if (-not $startupResult.Success) {
            if (Test-IsNativeDllNotFound -ExitCode $startupResult.ExitCode) {
                Write-UnityHostPrereqAnnotation -Version $UnityVersion -ExitCode $startupResult.ExitCode -Description $startupResult.Description -EditorPath $editor -ProbeLog $probeLog
            }
            throw "Unity $UnityVersion native startup probe failed with exit code $($startupResult.ExitCode) ($($startupResult.Description)) and RequireHealthyExisting is set. CI test jobs do not repair Unity in-job. Run scripts/unity/maintain-windows-runner.ps1 or dispatch .github/workflows/runner-bootstrap.yml. Probe log: $probeLog"
        }
    }
    $script:ProvisioningEditorPath = $editor
    $script:ProvisioningFinalClassification = 'success'
    Write-CiNotice "Unity editor resolved by healthy-existing preflight: $editor"
    Write-Output $editor
    return
}
if (-not $editor) {
    Ensure-UnityCli | Out-Null
    Set-UnityCliInstallPath -Root $InstallRoot
    if ($CiManagedOnly) {
        Confirm-UnityCliManagedInstallRoot -Root $InstallRoot | Out-Null
    }

    Write-CiNotice "Installing Unity Editor $UnityVersion on the self-hosted Windows runner."
    # Single source of truth for the (EULA-bearing) module-install arg vector.
    # Fresh installs request the selected profile's desired modules atomically so
    # Android dependencies are resolved with the editor install when Android is in
    # scope, instead of through an amplified outer retry.
    $installArgs = @(Get-UnityCliModuleInstallArguments -Verb 'install' -Version $UnityVersion -ModuleIds (Get-UnityCiModuleIds -Profile $ProvisioningProfile))

    # Emit diagnostics BEFORE the (potentially 30+ minute) install so the logs
    # carry the CLI path/version + disk headroom even if the install then stalls.
    Write-InstallDiagnostics -Root $InstallRoot

    # The base install has been observed to fail flakily (exit 6 after a long run
    # with almost no output) AND to HANG until the job is cancelled. Each attempt
    # is now bounded by the install timeout (Invoke-UnityCliCapture delegates to
    # the timeout runner), so a hang is killed and classified as a retryable
    # failure that Invoke-WithRetry can re-attempt. The attempt count is sourced
    # from the override-aware helper (default 2 -- two attempts fit inside the
    # 180-minute Provision-Unity-Editor step budget even for a slow install), and
    # the CAPTURING invoker makes a final failure THROW with the CLI output tail +
    # exit code. Output is streamed live, per line, by Invoke-UnityCliCapture (each
    # line is echoed the instant it arrives), never silently buffered.
    $retryDelaySeconds = Get-EnsureEditorRetryDelaySeconds
    $installRetryAttempts = Get-EnsureEditorInstallRetryAttempts

    $recoveredEditor = Invoke-WithUnityInstallLock -Version $UnityVersion -InstallRoot $InstallRoot -Action {
        Invoke-WithRetry -MaxAttempts $installRetryAttempts -DelaySeconds $retryDelaySeconds -Action {
            $installResult = Invoke-UnityCliCapture -Arguments $installArgs
            if (-not $installResult.Success) {
                $installLines = @($installResult.Output)
                $installText = ($installLines -join "`n")
                # Collapse consecutive identical lines (the Android NDK install can
                # spam thousands of identical progress lines) so the tail is READABLE.
                $installTail = Get-CollapsedCliOutputTail -Output $installResult.Output -MaxLines 40

                $resolvedAfterFailure = Resolve-InstalledEditor -Version $UnityVersion -Root $InstallRoot -ManagedOnly:$CiManagedOnly
                if ($resolvedAfterFailure) {
                    Write-CiNotice "Unity CLI '$($installArgs -join ' ')' failed with exit code $($installResult.ExitCode), but Unity.exe is resolvable afterward; treating the install as already present."
                    Write-CiNotice "Verifying required CI modules after recovered editor install."
                    return $resolvedAfterFailure
                }

                if ($installText -match '(?i)already installed|editor already installed|is already installed') {
                    Write-InstalledEditorDiagnostics -Version $UnityVersion -Root $InstallRoot -Reason "Unity CLI reported the editor is already installed, but Unity.exe could not be resolved afterward."
                    Invoke-UnityVersionUninstallForRepair -Version $UnityVersion -Reason "Unity CLI reported an already-installed editor, but Unity.exe could not be resolved." -InstallRoot $InstallRoot | Out-Null
                    # Stop stale provisioning processes ONLY when the wrapper
                    # actually killed the install (heartbeat-stall OR wall-clock
                    # deadline). A NATIVE 124/125 from the Unity CLI is a clean
                    # exit and leaves no stale tree to clean up; running the
                    # stale-process sweep on a native exit would be wasted work
                    # at best and a footgun against an unrelated Unity instance
                    # at worst.
                    if ($installResult.StallKilled -or $installResult.TimedOutWallClock) {
                        Stop-StaleUnityProvisioningProcesses -InstallRoot $InstallRoot -Version $UnityVersion -Reason "timed out already-installed install before quarantine"
                    }
                    Move-UnityVersionInstallToQuarantine -Version $UnityVersion -InstallRoot $InstallRoot
                    throw "Unity CLI '$($installArgs -join ' ')' reported an already-installed editor with exit code $($installResult.ExitCode), but Unity.exe could not be found. Uninstalled any CLI metadata and quarantined the managed version directory as partial or corrupt before retry. CLI output tail:`n$installTail"
                }

                Write-UnityCliInstallFailureAnnotation -Version $UnityVersion -Output $installResult.Output -ExitCode $installResult.ExitCode -Arguments $installArgs
                # Phrase from wrapper-driven kill state so a NATIVE 124/125 from
                # the Unity CLI is not misread as a stall or wall-clock kill.
                # The retry classifier is unchanged -- both sentinels remain
                # retryable via the existing throw flow.
                $baseInstallStallKilled = [bool]$installResult.StallKilled
                $baseInstallWallTimedOut = [bool]$installResult.TimedOutWallClock
                $baseInstallTimedOut = ($baseInstallStallKilled -or $baseInstallWallTimedOut)
                Write-ModuleInstallFailureDiagnostics -Version $UnityVersion -Output $installResult.Output -ExitCode $installResult.ExitCode -Arguments $installArgs -Root $InstallRoot -TimedOut:$baseInstallTimedOut -StallKilled:$baseInstallStallKilled -TimedOutWallClock:$baseInstallWallTimedOut -TimeoutSeconds (Get-EnsureEditorInstallTimeoutSeconds) -StallSeconds (Get-EnsureEditorProgressStallSeconds)
                Write-InstalledEditorDiagnostics -Version $UnityVersion -Root $InstallRoot -Reason "Unity CLI install failed and Unity.exe could not be resolved afterward."
                throw "Unity CLI '$($installArgs -join ' ')' failed with exit code $($installResult.ExitCode). CLI output tail:`n$installTail"
            }

            return $null
        }
    }

    if ($recoveredEditor) {
        $editor = $recoveredEditor
    } else {
        $editor = Resolve-InstalledEditor -Version $UnityVersion -Root $InstallRoot -ManagedOnly:$CiManagedOnly
    }
    if ($editor) {
        $script:ProvisioningEditorPath = $editor
    }
    if (-not $editor) {
        Write-InstalledEditorDiagnostics -Version $UnityVersion -Root $InstallRoot -Reason "Unity CLI install completed, but Unity.exe could not be resolved afterward."
        Move-UnityVersionInstallToQuarantine -Version $UnityVersion -InstallRoot $InstallRoot
        $editor = Install-UnityEditorWithCiModules -Version $UnityVersion -InstallRoot $InstallRoot -Reason "Unity CLI install completed, but Unity.exe could not be resolved afterward; quarantined the managed version directory and retrying with a fresh install." -Profile $ProvisioningProfile -ManagedOnly:$CiManagedOnly
        $script:ProvisioningEditorPath = $editor
    }
    $editor = Ensure-UnityCiModules -Version $UnityVersion -EditorPath $editor -InstallRoot $InstallRoot -Profile $ProvisioningProfile -ManagedOnly:$CiManagedOnly
    $script:ProvisioningEditorPath = $editor
} else {
    Ensure-UnityCli | Out-Null
    Set-UnityCliInstallPath -Root $InstallRoot
    if ($CiManagedOnly) {
        Confirm-UnityCliManagedInstallRoot -Root $InstallRoot | Out-Null
    }
    Write-CiNotice "Ensuring required CI modules are installed for Unity $UnityVersion."
    $script:ProvisioningEditorPath = $editor
    $editor = Ensure-UnityCiModules -Version $UnityVersion -EditorPath $editor -InstallRoot $InstallRoot -Profile $ProvisioningProfile -ManagedOnly:$CiManagedOnly
    $script:ProvisioningEditorPath = $editor
}

$editor = Ensure-UnityNativeStartupHealthy -Version $UnityVersion -EditorPath $editor -InstallRoot $InstallRoot -Profile $ProvisioningProfile -ManagedOnly:$CiManagedOnly
$script:ProvisioningEditorPath = $editor
$missingPayload = @(Get-MissingRequiredEditorPayloadPaths -EditorPath $editor -RelativePaths $RequiredEditorPayloadRelativePath)
if ($missingPayload.Count -gt 0) {
    if ($env:UH_UNITY_DISABLE_EDITOR_REPAIR -eq '1') {
        throw "Unity $UnityVersion required editor payload is missing ($($missingPayload -join ', ')), and UH_UNITY_DISABLE_EDITOR_REPAIR=1 disabled required-payload auto-repair."
    }

    Ensure-UnityCli | Out-Null
    Set-UnityCliInstallPath -Root $InstallRoot
    if ($CiManagedOnly) {
        Confirm-UnityCliManagedInstallRoot -Root $InstallRoot | Out-Null
    }

    $repairReason = "required editor payload is missing: $($missingPayload -join ', ')."
    try {
        $editor = Repair-UnityEditorWithCiModules -Version $UnityVersion -EditorPath $editor -InstallRoot $InstallRoot -Reason $repairReason -Profile $ProvisioningProfile -ManagedOnly:$CiManagedOnly
    } catch {
        $repairFailure = $_.Exception.Message
        if (-not (Test-UnityAtomicInstallFailureMayBePinnedToExistingEditor -Message $repairFailure -InstallRoot $InstallRoot -Version $UnityVersion)) {
            throw
        }

        Write-Host "::warning::Required-payload repair for Unity $UnityVersion could not replace the existing editor tree ($repairFailure); trying an alternate CI-managed install root."
        try {
            $editor = Install-UnityEditorWithCiModulesInAlternateRoot -Version $UnityVersion -InstallRoot $InstallRoot -Reason "required-payload repair was pinned to the existing editor tree ($repairFailure)" -Profile $ProvisioningProfile -RequiredEditorPayloadRelativePath $RequiredEditorPayloadRelativePath -ManagedOnly:$CiManagedOnly
        } catch {
            throw "Unity $UnityVersion required-payload repair was blocked at the existing editor tree ($repairFailure), and alternate-root repair also failed: $($_.Exception.Message)"
        }
    }
    $script:ProvisioningEditorPath = $editor
    $editor = Ensure-UnityNativeStartupHealthy -Version $UnityVersion -EditorPath $editor -InstallRoot $InstallRoot -Profile $ProvisioningProfile -ManagedOnly:$CiManagedOnly
    $script:ProvisioningEditorPath = $editor

    $missingAfterRepair = @(Get-MissingRequiredEditorPayloadPaths -EditorPath $editor -RelativePaths $RequiredEditorPayloadRelativePath)
    if ($missingAfterRepair.Count -gt 0) {
        throw "Unity $UnityVersion repair completed, but required editor payload is missing: $($missingAfterRepair -join ', ')."
    }
}
$script:ProvisioningFinalClassification = 'success'
Write-CiNotice "Unity editor resolved: $editor"
Write-Output $editor
} catch {
    try {
        $editorVar = Get-Variable -Name editor -Scope Local -ErrorAction SilentlyContinue
        if ($editorVar -and $editorVar.Value) {
            $script:ProvisioningEditorPath = [string]$editorVar.Value
        }
    } catch { }
    $script:ProvisioningFinalClassification = "failed: $($_.Exception.Message)"
    throw
} finally {
    Write-UnityProvisioningSummary -Root $InstallRoot -Version $UnityVersion -Path $DiagnosticsPath -EditorPath $script:ProvisioningEditorPath
}
