#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+f\d+$')]
    [string]$UnityVersion,

    [Parameter(Mandatory = $true)]
    [ValidateSet('editmode', 'playmode', 'standalone')]
    [string]$TestMode,

    [Parameter(Mandatory = $true)]
    [string]$AssemblyNames,

    [Parameter(Mandatory = $true)]
    [string]$ArtifactsPath,

    [string]$RepoRoot = $(if ($env:GITHUB_WORKSPACE) { $env:GITHUB_WORKSPACE } else { (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path }),

    [string]$ProjectPath,

    [string]$UnityEditorPath = $env:UNITY_EDITOR_PATH,

    [string]$UnityInstallRoot = $(if ($env:UNITY_EDITOR_INSTALL_ROOT) { $env:UNITY_EDITOR_INSTALL_ROOT } else { 'C:\Unity\Editors' }),

    [string]$TestCategory = $(if ($env:UH_UNITY_TEST_CATEGORY) { $env:UH_UNITY_TEST_CATEGORY } else { '' }),

    [switch]$IncludeComparisons,

    # Install the third-party DI-container packages (Reflex / VContainer / Zenject-
    # Extenject) from .github/integration-packages.json + the OpenUPM scoped
    # registry into the ephemeral manifest, so the Runtime/Integrations and
    # Tests/{Editor,Runtime}/Integrations asmdefs (gated on REFLEX_PRESENT /
    # VCONTAINER_PRESENT / ZENJECT_PRESENT versionDefines) compile and their tests
    # run. The integration test ASSEMBLIES must additionally be added to
    # -AssemblyNames by the caller (compute-unity-assemblies include-integrations).
    [switch]$IncludeIntegrations,

    # Extra GLOBAL scripting define symbols compiled into EVERY assembly (asmdef
    # assemblies included), e.g. SINGLE_THREADED to exercise the single-threaded
    # code paths. Empty by default so the DEFAULT (multi-threaded) behavior is
    # unchanged. Applied via a configure pass that sets PlayerSettings scripting
    # defines and lets Unity persist them BEFORE the -runTests pass loads the
    # project, because Unity in -batchmode does NOT recompile when defines change
    # mid-run -- the symbols must be in place from editor startup (Unity issue
    # tracker: define edits before project open are honored from 2021.1+, which the
    # Unity-6-only single-threaded leg satisfies). See New-ConfiguratorSource and
    # the configure-pass dispatch below.
    [string[]]$AdditionalScriptingDefines = @(),

    [switch]$ReleaseCodeOptimization,

    [ValidateSet('IL2CPP', 'Mono2x')]
    [string]$StandaloneScriptingBackend = 'IL2CPP',

    [switch]$ReleasePlayerBuild,

    [switch]$GenerateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# PowerShell 7.4 introduced $PSNativeCommandUseErrorActionPreference (stabilizing
# the native-error experimental feature). Its default is $false on current builds,
# so `& <native>` does NOT throw on a non-zero exit and our explicit checks run as
# written. However, a host profile or a future/different build could enable it,
# which would make `& <native>` THROW on a non-zero exit BEFORE our explicit
# `$LASTEXITCODE` check runs -- short-circuiting Invoke-UnityEditor's exit-code
# diagnostic and making the best-effort license return rely on its catch block
# instead of finishing. Pinning it $false makes LASTEXITCODE-based handling
# authoritative and identical across hosts/versions. (PS 5.1 lacks this variable;
# assigning it there is harmless, and the assignment is StrictMode-safe.)
$PSNativeCommandUseErrorActionPreference = $false

$PackageName = 'com.wallstop-studios.unity-helpers'
# TODO(unity-helpers): test-framework version reconciliation. DxMessaging pinned
# com.unity.test-framework 1.4.5; unity-helpers' existing
# scripts/unity/create-test-project.sh pins 1.1.33. The harness here builds its
# OWN manifest (New-ManifestJson) independent of create-test-project.sh, so this
# value is the version Unity resolves for the ephemeral CI project. Kept at 1.4.5
# (the harness-proven value) -- a maintainer should confirm whether unity-helpers
# requires 1.1.33 (to match create-test-project.sh) or 1.4.5 before the first
# self-hosted run. The performance package (3.4.2) is required by the
# *.Tests.Runtime.Performance assembly the unity-benchmarks workflow opts into.
$TestFrameworkVersion = '1.4.5'
$PerformanceFrameworkVersion = '3.4.2'
# TODO(unity-helpers): unity-helpers ships NO analyzers today (no Editor/Analyzers/
# directory, no RoslynAnalyzer-labeled assets), so this required-DLL roster is
# EMPTY and the analyzer copy/assert/diagnostic functions are no-ops (see
# Copy-UnityHelpersAnalyzersToAssets). The .gitignore reserves Editor/Analyzers/
# *.dll|*.pdb for a future analyzer; when one ships, add its RoslynAnalyzer-labeled
# DLL names here (and to $RoslynAnalyzerLabeledDllNames) and port DxMessaging's
# analyzer-copy bodies so the generator is registered at the first compile.
$RequiredUnityHelpersAnalyzerDllNames = @()

function Write-CiError {
    param([Parameter(Mandatory = $true)][string]$Message)
    Write-Host "::error::$Message"
}

function Write-CiNotice {
    param([Parameter(Mandatory = $true)][string]$Message)
    Write-Host "::notice::$Message"
}

# SINGLE SOURCE OF TRUTH for the catastrophic-pattern list that both
# Write-UnityCatastrophicErrorAnnotations (new ::error:: annotation surface)
# AND Write-UnityResultFailureDiagnostics (older line-numbered selected-line
# printer) scan for. Each entry has:
#   Label    : human-readable label written into the GitHub group/error line
#   Pattern  : the Select-String pattern (regex when UseSimple=false, literal
#              substring when UseSimple=true)
#   UseSimple: whether to invoke Select-String -SimpleMatch (literal substring,
#              cheaper) or as a regex
# Keeping this at $script: scope keeps the array deterministic and shared
# even when callers run from inside a try/finally or a child function.
#
# Patterns covered:
#   - PrecompiledAssemblyException -- "Multiple precompiled assemblies with
#     the same name" (the analyzer-DLL duplicate that motivated this
#     diagnostic; the runtime auto-copy that caused it has been removed).
#   - CompilationFailedException -- generic compile-failure path.
#   - error CS\d+ -- compiler errors (CS0246, CS0103, CS0117, etc).
#   - warning CS8032 -- "An instance of analyzer cannot be created" (analyzer
#     failed to instantiate; same class of issue).
$script:CatastrophicPatterns = @(
    @{ Label = 'PrecompiledAssemblyException'; Pattern = 'PrecompiledAssemblyException'; UseSimple = $true }
    @{ Label = 'CompilationFailedException'; Pattern = 'CompilationFailedException'; UseSimple = $true }
    @{ Label = 'Multiple precompiled assemblies with the same name'; Pattern = 'Multiple precompiled assemblies with the same name'; UseSimple = $true }
    @{ Label = 'error CS\d+'; Pattern = 'error CS\d+'; UseSimple = $false }
    @{ Label = 'warning CS8032'; Pattern = 'warning CS8032'; UseSimple = $false }
)

# CLASS-OF-ISSUE DIAGNOSTIC: when Unity exits non-zero, the operator's next
# question is "WHY did Unity fail?". The most common silent-killer answers are
# catastrophic compile-time errors -- the editor exits before running tests at
# all, leaving no NUnit XML. Surface these patterns as `::error::` annotations
# directly from the runner script so they ALWAYS show up in both the runner log
# and GitHub's error summary, independent of whether the workflow-level verify
# step also runs. Reusable at top-level so additional call sites can adopt it.
# Patterns come from the single-source-of-truth $script:CatastrophicPatterns
# array above; see Write-UnityResultFailureDiagnostics for the second consumer.
function Write-UnityCatastrophicErrorAnnotations {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$LogPath,
        [int]$MaxPerPattern = 5
    )

    if (-not $LogPath -or -not (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
        return
    }

    foreach ($entry in $script:CatastrophicPatterns) {
        try {
            if ($entry.UseSimple) {
                $hits = @(
                    Select-String -LiteralPath $LogPath -SimpleMatch -Pattern $entry.Pattern -ErrorAction SilentlyContinue |
                        Select-Object -First $MaxPerPattern
                )
            } else {
                $hits = @(
                    Select-String -LiteralPath $LogPath -Pattern $entry.Pattern -ErrorAction SilentlyContinue |
                        Select-Object -First $MaxPerPattern
                )
            }
        } catch {
            # Best-effort; never throw from a diagnostic helper -- the caller is
            # already in the middle of a throw path.
            continue
        }

        if ($hits.Count -lt 1) {
            continue
        }

        Write-Host "::group::Catastrophic pattern: $($entry.Label)"
        foreach ($hit in $hits) {
            $line = $hit.Line.Trim()
            Write-Host "::error::Pattern detected -- $($entry.Label):: $line"
            Write-Host "  $($hit.Path):$($hit.LineNumber): $line"
        }
        Write-Host "::endgroup::"
    }
}

# CLASS-OF-ISSUE DIAGNOSTIC: a CS1069 "type ... forwarded to assembly
# 'UnityEngine.<X>Module'" (or its "Enable the built in package" sibling) and a
# CS0234 "'UI' does not exist in the namespace 'UnityEngine'" both mean the
# project manifest is MISSING a UnityEngine module/package the code uses -- the
# editor then fails compilation before any test runs and emits no NUnit
# results.xml. The raw CS#### line names the ASSEMBLY but NOT the UPM package id a
# human must add, so this best-effort scanner translates each missing module into
# the EXACT id to add to .github/unity-test-project-modules.json. The mapping is
# the (stable) Unity rule "UnityEngine.<Name>Module -> com.unity.modules.<name
# lowercased>" plus the one special case UnityEngine.UI -> com.unity.ugui, so it
# needs no lookup table and degrades gracefully for a module not seen before.
# NEVER throws (the caller is already on a failure path).
function Write-UnityMissingModuleAnnotations {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$LogPath,
        [int]$MaxModules = 25
    )

    if (-not $LogPath -or -not (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
        return
    }

    $found = New-Object 'System.Collections.Generic.HashSet[string]'
    try {
        foreach ($hit in @(Select-String -LiteralPath $LogPath -Pattern 'forwarded to assembly [''"]?UnityEngine\.(\w+)Module' -ErrorAction SilentlyContinue)) {
            foreach ($m in $hit.Matches) {
                [void]$found.Add('com.unity.modules.' + $m.Groups[1].Value.ToLowerInvariant())
            }
        }
        # UnityEngine.UI (Image/Slider/ColorBlock) lives in the bundled com.unity.ugui
        # package, NOT a com.unity.modules.* built-in, and surfaces as CS0234 rather
        # than the CS1069 forward above.
        if (@(Select-String -LiteralPath $LogPath -Pattern "namespace name 'UI' does not exist in the namespace 'UnityEngine'" -ErrorAction SilentlyContinue).Count -gt 0) {
            [void]$found.Add('com.unity.ugui')
        }
    } catch {
        return
    }

    if ($found.Count -lt 1) {
        return
    }

    $modules = @($found | Sort-Object | Select-Object -First $MaxModules)
    Write-Host "::group::Missing Unity module dependencies"
    Write-Host "::error::Compilation referenced UnityEngine module(s) absent from the test-project manifest. Add to .github/unity-test-project-modules.json: $($modules -join ', ')"
    foreach ($id in $modules) {
        Write-Host "  - $id"
    }
    Write-Host "Both scripts/unity/run-ci-tests.ps1 (New-ManifestJson) and scripts/unity/create-test-project.sh consume that single source."
    Write-Host "::endgroup::"
}

function Test-UnityPackageManagerTransientFailure {
    param([string]$LogPath)

    if (-not $LogPath -or -not (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
        return $false
    }

    try {
        $logText = Get-Content -LiteralPath $LogPath -Raw
    } catch {
        return $false
    }

    if (-not $logText) {
        return $false
    }

    return (
        $logText -match 'Cancelled resolving packages' -or
        $logText -match 'Failed to resolve packages:\s+operation cancelled' -or
        $logText -match 'IPCStream \(Upm-[^)]+\): IPC stream failed to read'
    )
}

function Write-UnityPackageManagerTransientFailureWarnings {
    param(
        [string]$LogPath,
        [int]$MaxLines = 12
    )

    if (-not $LogPath -or -not (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
        return
    }

    $patterns = @(
        'Cancelled resolving packages',
        'Failed to resolve packages:\s+operation cancelled',
        'IPCStream \(Upm-[^)]+\): IPC stream failed to read'
    )

    try {
        $matches = @(
            Select-String -LiteralPath $LogPath -Pattern $patterns -ErrorAction SilentlyContinue |
                Select-Object -First $MaxLines
        )
    } catch {
        return
    }

    foreach ($match in $matches) {
        $line = ConvertTo-SingleLineDiagnostic -Text $match.Line
        Write-Host "::warning::Unity Package Manager transient package-resolution signal: $line"
    }
}

function Clear-UnityPackageManagerRetryState {
    param([Parameter(Mandatory = $true)][string]$Project)

    $packageCachePath = Join-Path $Project 'Library\PackageCache'
    $packageManagerPath = Join-Path $Project 'Library\PackageManager'
    $tempPath = Join-Path $Project 'Temp'
    $paths = @(
        $packageCachePath,
        $packageManagerPath,
        $tempPath
    )

    foreach ($envName in @('UPM_CACHE_ROOT', 'UPM_NPM_CACHE_PATH')) {
        $value = [Environment]::GetEnvironmentVariable($envName)
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            $paths += $value
        }
    }

    Write-Host "::group::Unity Package Manager retry cleanup"
    foreach ($path in ($paths | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)) {
        try {
            if (Test-Path -LiteralPath $path) {
                Write-Host "Removing $path"
                Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop
            } else {
                Write-Host "Already absent: $path"
            }
            New-Item -ItemType Directory -Force -Path $path -ErrorAction Stop | Out-Null
        } catch {
            Write-Host "::warning::Could not clear Unity Package Manager retry path '${path}': $($_.Exception.Message)"
        }
    }
    Write-Host "::endgroup::"
}

function Write-UnityPackageManagerDiagnostics {
    param(
        [string]$Project,
        [string]$LogPath
    )

    Write-Host "::group::Unity Package Manager diagnostics"
    try {
        foreach ($envName in @('UPM_CACHE_ROOT', 'UPM_NPM_CACHE_PATH', 'UPM_GIT_LFS_CACHE_PATH')) {
            Write-Host "${envName}: $([Environment]::GetEnvironmentVariable($envName))"
        }

        if ($Project) {
            foreach ($relativePath in @('Packages\manifest.json', 'Packages\packages-lock.json')) {
                $file = Join-Path $Project $relativePath
                if (Test-Path -LiteralPath $file -PathType Leaf) {
                    Write-Host "${relativePath}:"
                    Get-Content -LiteralPath $file -ErrorAction SilentlyContinue |
                        ForEach-Object { Write-Host "  $_" }
                } else {
                    Write-Host "${relativePath}: (missing)"
                }
            }

            $packageCache = Join-Path $Project 'Library\PackageCache'
            Write-Host "Library PackageCache: $packageCache"
            if (Test-Path -LiteralPath $packageCache -PathType Container) {
                Get-ChildItem -LiteralPath $packageCache -Force -ErrorAction SilentlyContinue |
                    Sort-Object Name |
                    Select-Object -First 80 |
                    ForEach-Object {
                        $kind = if ($_.PSIsContainer) { 'dir ' } else { 'file' }
                        Write-Host ("  [{0}] {1}" -f $kind, $_.Name)
                    }
            } else {
                Write-Host "  (missing)"
            }
        }

        if ($LogPath -and (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
            Write-Host "Package Manager failure log hits:"
            Select-String -LiteralPath $LogPath -Pattern @(
                'IPCStream \(Upm-[^)]+\): IPC stream failed to read',
                'Failed to resolve packages',
                'Cancelled resolving packages'
            ) -ErrorAction SilentlyContinue |
                Select-Object -First 40 |
                ForEach-Object {
                    Write-Host ("  line {0}: {1}" -f $_.LineNumber, $_.Line.Trim())
                }
        }
    } catch {
        Write-Host "::warning::Could not collect Unity Package Manager diagnostics: $($_.Exception.Message)"
    }
    Write-Host "::endgroup::"
}

function Invoke-UnityEditorTestsWithPackageManagerRetry {
    param(
        [Parameter(Mandatory = $true)][string]$EditorPath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$LogPath,
        [Parameter(Mandatory = $true)][string]$ResultsPath,
        [Parameter(Mandatory = $true)][string]$Project
    )

    $runExit = Invoke-UnityEditor `
        -EditorPath $EditorPath `
        -Arguments $Arguments `
        -Label $Label `
        -LogPath $LogPath

    if ((Test-Path -LiteralPath $ResultsPath -PathType Leaf) -or
        -not (Test-UnityPackageManagerTransientFailure -LogPath $LogPath)) {
        return $runExit
    }

    Write-CiWarning "Unity Package Manager canceled package resolution before NUnit results existed; clearing UPM state and retrying once."
    Write-UnityPackageManagerTransientFailureWarnings -LogPath $LogPath
    $firstAttemptLogPath = Join-Path (Split-Path -Parent $LogPath) ("{0}.first-attempt.log" -f [System.IO.Path]::GetFileNameWithoutExtension($LogPath))
    try {
        Copy-Item -LiteralPath $LogPath -Destination $firstAttemptLogPath -Force -ErrorAction Stop
        Write-CiNotice "Saved first failed Unity log before retry: $firstAttemptLogPath"
    } catch {
        Write-CiWarning "Could not preserve first failed Unity log before retry: $($_.Exception.Message)"
    }
    Clear-UnityPackageManagerRetryState -Project $Project

    if (Test-Path -LiteralPath $ResultsPath -PathType Leaf) {
        Remove-Item -LiteralPath $ResultsPath -Force
    }

    return Invoke-UnityEditor `
        -EditorPath $EditorPath `
        -Arguments $Arguments `
        -Label "$Label (retry 1 after UPM cancellation)" `
        -LogPath $LogPath
}

# Collapse any run of whitespace (including CR/LF) to a single space and trim, so
# a multi-line NUnit <failure>/<message> renders as ONE line. GitHub `::error::`
# annotations are single-line: an embedded newline silently truncates the
# annotation at the first line break, so the whole message must be flattened
# before it is emitted. Mirrors the `.Trim()` collapse the catastrophic-pattern
# scanner applies to each matched log line.
function ConvertTo-SingleLineDiagnostic {
    param([string]$Text)
    if (-not $Text) {
        return ''
    }
    return (($Text -replace '\s+', ' ').Trim())
}

# Holder for the ::stop-commands::<token> ... ::<token>:: fence token that wraps
# caller-controlled raw multi-line dumps (NUnit <message>/<stack-trace>). GitHub
# parses every stdout line for `::command::` directives; fencing the raw body
# disables that processing so an assertion message containing a line like
# `::error file=...::` or `::set-output name=x::` cannot inject a spurious
# workflow command. The token is NOT a fixed literal: a crafted message
# containing the exact `::<literal>::` close line could otherwise end the fence
# early and re-enable injection. Instead a FRESH random token is generated per
# enumeration via New-WorkflowCommandStopToken (mirroring GitHub's own
# @actions/core, which uses a random per-invocation delimiter) and the SAME
# value is used for the opening and closing fence lines. The matching fence in
# .github/actions/verify-unity-results/action.yml uses the same scheme.
$script:WorkflowCommandStopToken = $null

# Generate a fresh, unpredictable stop-commands fence token. A GUID 'N' form is
# 32 hex chars with no separators, so it can never collide with caller text and
# is regenerated each call so it is neither predictable nor committed.
function New-WorkflowCommandStopToken {
    return ('uh-stop-commands-{0}' -f [guid]::NewGuid().ToString('N'))
}

# Resolve an NUnit test-case / test-suite node's display name using
# XmlElement.GetAttribute, which returns '' for an ABSENT attribute instead of
# THROWING under Set-StrictMode -Version Latest (the dynamic `$node.fullname`
# property accessor throws "The property 'fullname' cannot be found" when the
# attribute is missing, which would degrade the whole failed-test enumeration to
# a generic warning for any NUnit XML lacking a fullname). Prefers fullname, then
# name, then a final '(unnamed test)' fallback.
function Get-NUnitNodeFullName {
    param([Parameter(Mandatory = $true)]$Node)

    $fullName = $Node.GetAttribute('fullname')
    if (-not $fullName) {
        $fullName = $Node.GetAttribute('name')
    }
    if (-not $fullName) {
        $fullName = '(unnamed test)'
    }
    return $fullName
}

# DIAGNOSTIC: when a Unity test run reports failures, the operator's next question
# is "WHICH tests failed and WHY?". The aggregate `failed=N` count alone is not
# actionable -- a real 2021.3 PlayMode run failed 1 of 697 tests and the logs
# never named it. This best-effort helper enumerates each failed test from the
# NUnit3 results XML and emits BOTH:
#   - a single-line `::error::` GitHub annotation per failed test (label +
#     fullname + first line of the failure message), and
#   - a `::group::Failed test: <fullname>` ... `::endgroup::` console block with
#     the full multi-line message and stack trace.
# It NEVER throws (the caller is already on a throw path; a diagnostic error must
# not mask the real test failure) and follows the structure of the other
# best-effort scanners (Write-UnityCatastrophicErrorAnnotations /
# Write-UnityResultFailureDiagnostics).
#
# Two classes of failed node are enumerated:
#   (1) Failed leaf cases: //test-case[@result='Failed'] -- the ordinary
#       assertion failure.
#   (2) Failed suites that carry their OWN direct <failure> child:
#       //test-suite[@result='Failed'] with a direct <failure> element. This is
#       the OneTimeSetUp / OneTimeTearDown failure shape (e.g.
#       SuiteWallClockBudgetTest's [OneTimeTearDown] Assert.Fail) -- a suite can
#       carry its OWN teardown failure message EVEN WHEN it also has a failed
#       child case, so we report on the direct <failure> regardless of failed
#       descendants. The fullname de-dup keeps a suite distinct from its child
#       cases (suite fullname differs from case fullname), so this never
#       double-prints; an aggregate-only suite (no direct <failure>) is still
#       skipped because its failure is just the roll-up of the child cases.
# De-duplicated by fullname so the same logical node is never printed twice, and
# capped at the first $MaxFailures (a truncation notice is printed -- no silent
# cap). Attribute reads use XmlElement.GetAttribute (returns '' when absent,
# never throws) so a results.xml lacking a fullname/name attribute does NOT
# degrade the whole enumeration to a generic warning under Set-StrictMode.
function Write-UnityFailedTestAnnotations {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Xml,
        [Parameter(Mandatory = $true)][string]$Label,
        [int]$MaxFailures = 50
    )

    try {
        $failedCases = @($Xml.SelectNodes("//test-case[@result='Failed']"))
        $failedSuites = @($Xml.SelectNodes("//test-suite[@result='Failed']"))

        # A failed suite is reported on its OWN merits whenever it carries a
        # direct <failure> child element. This captures the OneTimeSetUp /
        # OneTimeTearDown failure message even when the suite ALSO has a failed
        # descendant case (the teardown's own message would otherwise be lost).
        # An aggregate-only suite (no direct <failure>, just a roll-up of failed
        # children) is skipped. The fullname de-dup below keeps the suite
        # distinct from its child cases, so this never double-prints.
        $ownFailureSuites = @(
            foreach ($suite in $failedSuites) {
                $directFailure = $suite.SelectSingleNode('failure')
                if ($directFailure) {
                    $suite
                }
            }
        )

        $failedNodes = @($failedCases) + @($ownFailureSuites)
        if ($failedNodes.Count -lt 1) {
            return
        }

        # De-duplicate by fullname (fallback name) so the same logical test is
        # never printed twice.
        $seen = New-Object 'System.Collections.Generic.HashSet[string]'
        $uniqueNodes = New-Object 'System.Collections.Generic.List[object]'
        foreach ($node in $failedNodes) {
            $fullName = Get-NUnitNodeFullName -Node $node
            if ($seen.Add($fullName)) {
                $uniqueNodes.Add($node)
            }
        }

        $totalFailed = $uniqueNodes.Count
        $shown = @($uniqueNodes | Select-Object -First $MaxFailures)
        foreach ($node in $shown) {
            $fullName = Get-NUnitNodeFullName -Node $node

            $failureNode = $node.SelectSingleNode('failure')
            $message = ''
            $stackTrace = ''
            if ($failureNode) {
                $messageNode = $failureNode.SelectSingleNode('message')
                if ($messageNode) {
                    $message = $messageNode.InnerText
                }
                $stackNode = $failureNode.SelectSingleNode('stack-trace')
                if ($stackNode) {
                    $stackTrace = $stackNode.InnerText
                }
            }

            $firstMessageLine = ConvertTo-SingleLineDiagnostic -Text $message
            # The single-line ::error:: annotation stays OUTSIDE the fence so it
            # is still processed as a GitHub annotation. ConvertTo-SingleLineDiagnostic
            # already flattens it to one line, so an embedded `::error::`/`::set-output::`
            # token cannot start a NEW directive on its own line here.
            Write-Host "::error::${Label} failed test: $fullName -- $firstMessageLine"

            Write-Host "::group::Failed test: $fullName"
            # SECURITY: the raw NUnit <message>/<stack-trace> are caller-controlled
            # (an assertion message can contain ANY text). GitHub parses every
            # stdout line for `::command::` directives, so a message line like
            # `::error file=...::` or `::set-output name=x::` would inject a
            # spurious workflow command. Fence the raw multi-line dump with
            # ::stop-commands::<token> ... ::<token>:: so command processing is
            # disabled for the enclosed lines. The token is a FRESH random GUID
            # per dump (never a fixed literal) so a crafted message containing
            # the exact `::<literal>::` close line cannot end the fence early and
            # re-enable injection. The ::group::/::endgroup:: markers stay OUTSIDE
            # the fence so they are still processed.
            $script:WorkflowCommandStopToken = New-WorkflowCommandStopToken
            Write-Host "::stop-commands::$script:WorkflowCommandStopToken"
            if ($message) {
                Write-Host "Message:"
                Write-Host $message
            } else {
                Write-Host "Message: (none recorded)"
            }
            if ($stackTrace) {
                Write-Host "Stack trace:"
                Write-Host $stackTrace
            }
            Write-Host "::$script:WorkflowCommandStopToken::"
            Write-Host "::endgroup::"
        }

        if ($totalFailed -gt $shown.Count) {
            $omitted = $totalFailed - $shown.Count
            Write-CiNotice "${Label}: $omitted additional failed test(s) not shown (showing first $($shown.Count) of $totalFailed)."
        }
    } catch {
        # Best-effort; a diagnostic must never mask the real test failure.
        Write-Host "::warning::Could not enumerate failed tests for ${Label}: $($_.Exception.Message)"
    }
}

function Resolve-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    $executionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

function Assert-RepoRoot {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath (Join-Path $Path 'package.json') -PathType Leaf)) {
        throw "Repo root '$Path' does not contain package.json."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $Path 'Runtime') -PathType Container)) {
        throw "Repo root '$Path' does not contain Runtime/."
    }
}

function ConvertTo-UnityFileUriPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    return ($Path -replace '\\', '/')
}

function Initialize-UnityCacheEnvironment {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Version
    )

    $cacheRoot = Join-Path $Root ".artifacts\unity\cache\$Version"
    $upmRoot = Join-Path $cacheRoot 'upm'
    $npmRoot = Join-Path $cacheRoot 'npm'
    $gitLfsRoot = Join-Path $cacheRoot 'git-lfs'
    $localUnityCaches = if ($env:LOCALAPPDATA) {
        Join-Path $env:LOCALAPPDATA 'Unity\Caches'
    } else {
        Join-Path $cacheRoot 'localappdata\Unity\Caches'
    }

    foreach ($path in @($cacheRoot, $upmRoot, $npmRoot, $gitLfsRoot, $localUnityCaches)) {
        New-Item -ItemType Directory -Force -Path $path | Out-Null
    }

    $env:UPM_CACHE_ROOT = $upmRoot
    $env:UPM_NPM_CACHE_PATH = $npmRoot
    $env:UPM_GIT_LFS_CACHE_PATH = $gitLfsRoot
    $env:UPM_ENABLE_GIT_LFS_CACHE = 'true'

    Write-Host "::group::Unity cache environment"
    Write-Host "LOCALAPPDATA Unity caches: $localUnityCaches"
    Write-Host "UPM_CACHE_ROOT: $env:UPM_CACHE_ROOT"
    Write-Host "UPM_NPM_CACHE_PATH: $env:UPM_NPM_CACHE_PATH"
    Write-Host "UPM_GIT_LFS_CACHE_PATH: $env:UPM_GIT_LFS_CACHE_PATH"
    Write-Host "::endgroup::"
}

# Read+parse a package-manifest single-source JSON (the OpenUPM registry + pinned
# packages used to extend the ephemeral manifest). Shared by the comparison and
# integration legs, which read DIFFERENT files of the SAME shape:
#   .github/comparison-packages.json  (benchmark comparison deps; not present in
#                                      unity-helpers today)
#   .github/integration-packages.json (DI-container integration deps)
# Kept DRY so both legs parse identically and a missing/typo'd source fails loudly
# with the file path rather than silently producing an empty manifest extension.
function Get-PackageManifestSource {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [Parameter(Mandatory = $true)][string]$Kind
    )
    $path = Join-Path $Root $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "$Kind packages single source not found: $path"
    }
    return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
}

function Get-ComparisonPackages {
    param([Parameter(Mandatory = $true)][string]$Root)
    return Get-PackageManifestSource -Root $Root -RelativePath '.github/comparison-packages.json' -Kind 'Comparison'
}

function Get-IntegrationPackages {
    param([Parameter(Mandatory = $true)][string]$Root)
    return Get-PackageManifestSource -Root $Root -RelativePath '.github/integration-packages.json' -Kind 'Integration'
}

# Read the SINGLE SOURCE OF TRUTH for the UnityEngine built-in modules + editor-
# bundled packages (com.unity.ugui) the ephemeral test project must declare so the
# package's Runtime/Editor code AND its test fixtures compile. Shared with
# scripts/unity/create-test-project.sh so the two manifest generators cannot drift
# (that drift -- this generator declared ZERO modules -- is what failed every
# matrix leg: the editor could not compile and emitted no NUnit results.xml).
# Returns an [ordered] id->version map. Fails LOUDLY with the file path if the
# source is missing or has no 'modules' object, rather than silently producing a
# module-less manifest (the exact regression this guards against).
function Get-UnityTestProjectModules {
    param([Parameter(Mandatory = $true)][string]$Root)
    $source = Get-PackageManifestSource -Root $Root -RelativePath '.github/unity-test-project-modules.json' -Kind 'Unity test-project module'
    $modulesNode = $source.PSObject.Properties['modules']
    # Guard BOTH a missing 'modules' key AND a present-but-null value (JSON
    # "modules": null), so a malformed edit fails with this clear message rather
    # than an opaque StrictMode "property cannot be found on null" throw below.
    if (-not $modulesNode -or $null -eq $modulesNode.Value) {
        throw "unity-test-project-modules.json is missing or has a null 'modules' object; cannot generate the test-project manifest."
    }
    $modules = [ordered]@{}
    foreach ($prop in $modulesNode.Value.PSObject.Properties) {
        $modules[$prop.Name] = $prop.Value
    }
    if ($modules.Count -lt 1) {
        throw "unity-test-project-modules.json 'modules' object is empty; the test project would fail to compile."
    }
    return $modules
}

function New-ManifestJson {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [switch]$IncludeComparisons,
        [switch]$IncludeIntegrations,
        [string]$RepoRoot
    )

    $packagePath = ConvertTo-UnityFileUriPath -Path $Root
    $dependencies = [ordered]@{
        'com.unity.test-framework' = $TestFrameworkVersion
        'com.unity.test-framework.performance' = $PerformanceFrameworkVersion
    }

    # UNCONDITIONAL (every leg -- editmode/playmode/standalone, single-threaded,
    # comparison, integration): the package's required UnityEngine built-in modules
    # must be in the project or the editor fails compilation BEFORE any test runs.
    # Sourced from the shared single-source file so this generator and
    # create-test-project.sh cannot drift. The package.json deliberately does NOT
    # carry these (dual npm+UPM file; `npm ci` would fail to resolve com.unity.*),
    # so the test-project manifest is their only home.
    $moduleRoot = if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $Root } else { $RepoRoot }
    foreach ($module in (Get-UnityTestProjectModules -Root $moduleRoot).GetEnumerator()) {
        $dependencies[$module.Key] = $module.Value
    }

    # Add the local package after the Unity modules (the -IncludeComparisons /
    # -IncludeIntegrations legs may append further dependencies below this).
    $dependencies[$PackageName] = "file:$packagePath"

    $manifest = [ordered]@{
        dependencies = $dependencies
        testables = @($PackageName)
    }

    # Accumulate the OpenUPM scoped-registry scopes contributed by whichever opt-in
    # legs are active. Both comparison and integration legs use the SAME OpenUPM
    # registry (package.openupm.com); if both were ever active together their
    # scopes are merged into a SINGLE scopedRegistries entry (Unity would otherwise
    # see two registries with the same URL). The non-opt-in legs add NOTHING here,
    # so their manifest stays byte-for-byte identical to before (no scopedRegistries
    # key, no extra dependencies) and their Library cache/reliability are unchanged.
    $registryName = $null
    $registryUrl = $null
    $registryScopes = New-Object System.Collections.Generic.List[string]

    # ONLY the comparison legs (-IncludeComparisons) get the OpenUPM scoped
    # registry, pinned comparison packages, and comparison-package-required Unity
    # built-in modules, read from the single source .github/comparison-packages.json.
    if ($IncludeComparisons) {
        if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
            throw "New-ManifestJson -IncludeComparisons requires -RepoRoot (the comparison-packages.json single source)."
        }
        $comparisons = Get-ComparisonPackages -Root $RepoRoot
        foreach ($pkg in $comparisons.packages.PSObject.Properties) {
            $dependencies[$pkg.Name] = $pkg.Value
        }
        $builtInPackages = $comparisons.PSObject.Properties['unityBuiltInPackages']
        if (-not $builtInPackages) {
            throw "comparison-packages.json is missing unityBuiltInPackages; cannot generate the comparison manifest."
        }
        foreach ($pkg in $builtInPackages.Value.PSObject.Properties) {
            $dependencies[$pkg.Name] = $pkg.Value
        }
        $reg = $comparisons.registry
        $registryName = $reg.name
        $registryUrl = $reg.url
        foreach ($scope in @($reg.scopes)) {
            if (-not $registryScopes.Contains($scope)) {
                $registryScopes.Add($scope)
            }
        }
    }

    # ONLY the integration legs (-IncludeIntegrations) get the OpenUPM scoped
    # registry + the pinned DI-container packages (Reflex / VContainer / Zenject-
    # Extenject) from .github/integration-packages.json. Installing them is what
    # makes the Runtime/Integrations + Tests/{Editor,Runtime}/Integrations asmdefs
    # (REFLEX_PRESENT / VCONTAINER_PRESENT / ZENJECT_PRESENT versionDefines) compile
    # and their tests run. unityBuiltInPackages is OPTIONAL here (the DI packages
    # are pure-managed and pull no extra Unity modules).
    if ($IncludeIntegrations) {
        if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
            throw "New-ManifestJson -IncludeIntegrations requires -RepoRoot (the integration-packages.json single source)."
        }
        $integrations = Get-IntegrationPackages -Root $RepoRoot
        foreach ($pkg in $integrations.packages.PSObject.Properties) {
            $dependencies[$pkg.Name] = $pkg.Value
        }
        $builtInPackages = $integrations.PSObject.Properties['unityBuiltInPackages']
        if ($builtInPackages) {
            foreach ($pkg in $builtInPackages.Value.PSObject.Properties) {
                $dependencies[$pkg.Name] = $pkg.Value
            }
        }
        $reg = $integrations.registry
        if (-not $registryName) {
            $registryName = $reg.name
            $registryUrl = $reg.url
        }
        foreach ($scope in @($reg.scopes)) {
            if (-not $registryScopes.Contains($scope)) {
                $registryScopes.Add($scope)
            }
        }
    }

    if ($registryScopes.Count -gt 0) {
        # Ordered so ConvertTo-Json emits name/url/scopes deterministically (matches
        # the committed local-parity manifest field order and keeps the CI-log diff
        # of the generated manifest stable run-to-run).
        $manifest['scopedRegistries'] = @(
            [ordered]@{
                name = $registryName
                url = $registryUrl
                scopes = @($registryScopes.ToArray())
            }
        )
    }

    return ($manifest | ConvertTo-Json -Depth 8)
}

function New-ConfiguratorSource {
    param([string]$Backend = 'IL2CPP')

    # NOTE: this is a DOUBLE-quoted here-string so $Backend interpolates into the
    # generated C#. Every LITERAL C# dollar sign (the Debug.Log interpolated
    # string) is therefore backtick-escaped (`$). The LIVE code uses the
    # parameterized scripting backend (ScriptingImplementation.<Backend>), the
    # non-deprecated ApiCompatibilityLevel.NET_Standard (which targets .NET Standard
    # 2.1), CompilationPipeline.codeOptimization = Release, and disables managed
    # stripping so the test assemblies + [Preserve] callback survive a Release Mono
    # player build. This is an invariant of the
    # generated configurator; no automated contract test pins it anymore.
    @"
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

public static class UhCiTestConfigurator
{
    public static void Apply()
    {
        // Prove Release editor code optimization for every Unity CI leg. Set FIRST
        // so the effective value is logged below.
        UnityEditor.Compilation.CompilationPipeline.codeOptimization = UnityEditor.Compilation.CodeOptimization.Release;

        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);

        // GLOBAL scripting define injection (e.g. SINGLE_THREADED). The runner hands
        // the requested defines in via UH_ADDITIONAL_SCRIPTING_DEFINES (semicolon-
        // delimited). They are set on the Standalone NamedBuildTarget -- the only
        // build-target group every CI leg (editmode/playmode/standalone) uses -- so
        // they apply to ALL assemblies, asmdef assemblies INCLUDED (global scripting
        // defines, not an Assets/csc.rsp that only reaches the predefined assembly).
        // Unity in -batchmode does NOT recompile when defines change mid-run, so the
        // runner runs this configure pass in a SEPARATE editor invocation that
        // persists the defines to ProjectSettings.asset (AssetDatabase.SaveAssets
        // below); the subsequent -runTests invocation then loads the project with
        // the defines in place from startup, and its FIRST compile sees them. When
        // the env var is empty this is a no-op, so the DEFAULT (no-extra-defines)
        // behavior and the existing comparison/standalone legs are unchanged.
        ApplyAdditionalScriptingDefines();

        // The scripting backend is parameterized: the runner passes the IL2CPP or
        // the Mono backend for the Mono perf leg via -Backend.
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.$Backend);
        // Use the non-deprecated ApiCompatibilityLevel.NET_Standard (targets .NET
        // Standard 2.1). The deprecated 2.0 form and the non-existent 2.1 enum
        // member are intentionally NOT used.
        PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Standalone, ApiCompatibilityLevel.NET_Standard);
        // Disable managed code stripping so IncludeTestAssemblies + the [Preserve]
        // standalone TestRunCallback survive a NON-development (Release) Mono player
        // build; otherwise the stripper can drop the test code from the player.
        PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Standalone, ManagedStrippingLevel.Disabled);
        // Pin the IL2CPP C++ compiler configuration to Release explicitly. An
        // ephemeral CI project has no committed default for this setting, and the
        // published benchmark numbers must come from Release-optimized native code
        // (matching what a shipped Release player runs), so the pin removes the
        // variable instead of trusting any implicit default. Harmless under Mono.
        PlayerSettings.SetIl2CppCompilerConfiguration(BuildTargetGroup.Standalone, Il2CppCompilerConfiguration.Release);

        // Print the EFFECTIVE Unity config so the artifact log PROVES Mono/IL2CPP
        // + .NET Standard 2.1 + Release for this run.
        PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone, out string[] effectiveDefines);
        Debug.Log(`$"UH perf config: backend={PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone)}, api={PlayerSettings.GetApiCompatibilityLevel(BuildTargetGroup.Standalone)}, codeOpt={UnityEditor.Compilation.CompilationPipeline.codeOptimization}, il2cppConfig={PlayerSettings.GetIl2CppCompilerConfiguration(BuildTargetGroup.Standalone)}, defines=[{string.Join(`";`", effectiveDefines ?? new string[0])}]");

        // Persist the PlayerSettings mutations (scripting backend/api/stripping AND
        // any injected scripting defines) to ProjectSettings.asset so the SEPARATE
        // -runTests editor invocation that follows this configure pass loads them
        // from startup. A clean -batchmode quit normally flushes settings, but the
        // explicit save removes that dependency and is the load-bearing step for the
        // editmode/playmode single-threaded leg (where this configure pass is the
        // ONLY place the defines get persisted before the test invocation compiles).
        AssetDatabase.SaveAssets();

        // Write a success marker as the FINAL action so the runner can treat the
        // CONFIGURED PROJECT -- not Unity's process exit code -- as the source of
        // truth. Unity can crash in a BACKGROUND thread (for example the
        // DirectoryMonitor file-watcher's teardown) DURING shutdown, AFTER Apply()
        // has fully completed and the editor logged "Batchmode quit successfully
        // invoked"; that returns a crash exit code (0xC0000005 STATUS_ACCESS_VIOLATION)
        // for a run whose configuration work actually succeeded. A fresh marker
        // proves Apply() ran to completion regardless of the shutdown exit code. The
        // marker path is handed in via UH_CONFIGURE_MARKER_PATH (mirrors how the
        // standalone build modifier receives UH_PLAYER_BUILD_PATH).
        string markerPath = Environment.GetEnvironmentVariable("UH_CONFIGURE_MARKER_PATH");
        if (!string.IsNullOrEmpty(markerPath))
        {
            string dir = Path.GetDirectoryName(markerPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(markerPath, "UhCiTestConfigurator.Apply completed");
        }
    }

    // Union the requested global scripting defines (UH_ADDITIONAL_SCRIPTING_DEFINES,
    // semicolon-delimited) with whatever is already set for the Standalone group and
    // write them back via the non-deprecated SetScriptingDefineSymbols(NamedBuildTarget,
    // string[]) API (the BuildTargetGroup overload is obsolete in Unity 6). Order is
    // preserved and duplicates are dropped. A null/empty env var leaves the existing
    // defines untouched (no-op), so a normal leg's compilation is byte-for-byte
    // unchanged. NamedBuildTarget.Standalone exists in 2021.2+, so this compiles on
    // every CI Unity version even though only the Unity-6 leg passes extra defines.
    private static void ApplyAdditionalScriptingDefines()
    {
        string raw = Environment.GetEnvironmentVariable("UH_ADDITIONAL_SCRIPTING_DEFINES");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        string[] requested = raw
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(d => d.Trim())
            .Where(d => d.Length > 0)
            .ToArray();
        if (requested.Length == 0)
        {
            return;
        }

        NamedBuildTarget target = NamedBuildTarget.Standalone;
        PlayerSettings.GetScriptingDefineSymbols(target, out string[] existing);

        List<string> merged = new List<string>(existing ?? new string[0]);
        foreach (string define in requested)
        {
            if (!merged.Contains(define))
            {
                merged.Add(define);
            }
        }

        PlayerSettings.SetScriptingDefineSymbols(target, merged.ToArray());
        Debug.Log(`$"UH additional scripting defines applied to {target.TargetName}: requested=[{string.Join(`";`", requested)}] effective=[{string.Join(`";`", merged)}]");
    }
}
"@
}

# STANDALONE ONLY. The Editor-side type that severs the test player's outbound
# PlayerConnection/Profiler TCP dependency at build time AND makes the editor's
# `-runTests` build step terminate. Emitted into Assets/Editor/ of the standalone
# CI project by Initialize-EphemeralProject. It mirrors Unity's documented
# "Split build and run" example (vendored com.unity.test-framework
# TestPlayerBuildModifierAttribute.cs): ITestPlayerBuildModifier rewrites the
# BuildPlayerOptions, IPostBuildCleanup exits the editor after the build.
#
# CRITICAL: clearing BuildOptions.AutoRunPlayer ALONE is NOT enough. The CLI
# `-runTests` path registers Executer.ExitIfRunIsCompleted on
# EditorApplication.update, which returns early while TestRunnerApi.IsRunActive()
# is true; for a player run that flag clears only on the PlayerConnection
# runFinished message. With the player never launched the message never arrives,
# so the editor idles forever. The PostBuildCleanup exit (run AFTER the build via
# ExecutePostBuildCleanupMethods) is mandatory.
function New-StandaloneBuildModifierSource {
    param([bool]$DevelopmentBuild = $false)

    # The Development BuildOptions flag is opt-in only. Unity CI defaults to a true
    # Release/non-development player; the compatibility -ReleasePlayerBuild switch is
    # retained at the script boundary but Release is the unconditional contract.
    # CRITICAL: the Unity Test Framework's PlayerLauncher hands ModifyOptions a
    # BuildPlayerOptions that ALREADY carries BuildOptions.Development, so the
    # Release path must actively CLEAR the flag -- merely not adding it leaves the
    # player a development build (Debug.isDebugBuild=true; published runs reported
    # "x64 Debug" platform strings until this strip landed). Every OTHER option (clearing
    # AutoRunPlayer/ConnectToHost/ConnectWithProfiler, |= IncludeTestAssemblies, the
    # UH_PLAYER_BUILD_PATH redirect, and the PostBuildCleanup exit) is REQUIRED for
    # the split-build test execution and is emitted unconditionally. This is a
    # DOUBLE-quoted here-string so $developmentOption interpolates; the generated C#
    # contains no other dollar signs or backticks, so nothing else needs escaping.
    $developmentOption = if ($DevelopmentBuild) { '        playerOptions.options |= BuildOptions.Development;' } else { '        playerOptions.options &= ~BuildOptions.Development;' }
    @"
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.TestTools;
using UnityEngine;
using UnityEngine.TestTools;

[assembly: TestPlayerBuildModifier(typeof(UhCiStandaloneBuildModifier))]
[assembly: PostBuildCleanup(typeof(UhCiStandaloneBuildModifier))]

// Mirrors the documented Unity "Split build and run" example. Clearing
// AutoRunPlayer alone is NOT enough: the CLI -runTests path registers
// Executer.ExitIfRunIsCompleted on EditorApplication.update, which returns early
// while TestRunnerApi.IsRunActive() is true; for a player run that flag only
// clears on the PlayerConnection runFinished message, which never arrives when
// the player is not launched. PostBuildCleanup is the framework's hook (run after
// the build) to exit the editor cleanly.
public sealed class UhCiStandaloneBuildModifier : ITestPlayerBuildModifier, IPostBuildCleanup
{
    private static bool s_Armed;
    private static readonly EditorApplication.CallbackFunction s_Exit = () => EditorApplication.Exit(0);

    public BuildPlayerOptions ModifyOptions(BuildPlayerOptions playerOptions)
    {
        playerOptions.options &= ~BuildOptions.AutoRunPlayer;
        playerOptions.options &= ~BuildOptions.ConnectToHost;
        playerOptions.options &= ~BuildOptions.ConnectWithProfiler;
        playerOptions.options |= BuildOptions.IncludeTestAssemblies;
$developmentOption
        string outPath = Environment.GetEnvironmentVariable("UH_PLAYER_BUILD_PATH");
        if (!string.IsNullOrEmpty(outPath))
        {
            string dir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            playerOptions.locationPathName = outPath;
        }
        return playerOptions;
    }

    public void Cleanup()
    {
        if (s_Armed)
        {
            return;
        }
        s_Armed = true;
        if (Environment.GetCommandLineArgs().Any(a => a == "-runTests"))
        {
            EditorApplication.update += s_Exit;
        }
    }
}
"@
}

# STANDALONE ONLY. The player-side [assembly:TestRunCallback] that REPLACES the
# editor's need to receive results over PlayerConnection/TCP. On RunFinished it
# serializes the NUnit result to NUnit-compatible XML (mirroring Unity's
# ResultsWriter.WriteResultsToXml) at the path from the -uhTestResults <path>
# command-line arg, then Application.Quit(0 pass / 1 fail / 2 no-path / 3 write
# error). Emitted into Assets/UhCiStandaloneTestCallback/ with its own .asmdef.
# [Preserve] keeps the type for IL2CPP.
#
# On the PLAYER, ITestResult.ResultState is a NUnit.Framework.Interfaces.ResultState
# OBJECT, so we call .ToString() (the editor adaptor does the same). The single
# results channel is -uhTestResults; there is NO environment-variable fallback and
# NO per-user-data-folder silent-loss fallback.
function New-StandaloneTestCallbackSource {
    @'
using System;
using System.IO;
using System.Xml;
using NUnit.Framework.Interfaces;
using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.TestRunner;

[assembly: TestRunCallback(typeof(UhCiStandaloneTestCallback))]

[Preserve]
internal sealed class UhCiStandaloneTestCallback : ITestRunCallback
{
    public void RunStarted(ITest testsToRun)
    {
    }

    public void TestStarted(ITest test)
    {
    }

    public void TestFinished(ITestResult result)
    {
    }

    public void RunFinished(ITestResult result)
    {
        string path = ResolveResultsPath();
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogError("UH: standalone test player received no -uhTestResults <path>; not writing results.");
            Application.Quit(2);
            return;
        }
        int exitCode;
        try
        {
            WriteNUnitXml(result, path);
            exitCode = result.FailCount > 0 ? 1 : 0;
            int total = result.PassCount + result.FailCount + result.SkipCount + result.InconclusiveCount;
            Debug.LogFormat(
                LogType.Log,
                LogOption.NoStacktrace,
                null,
                "UH: wrote standalone results to {0} (total={1} passed={2} failed={3} skipped={4})",
                path,
                total,
                result.PassCount,
                result.FailCount,
                result.SkipCount);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            exitCode = 3;
        }
        Application.Quit(exitCode);
    }

    private static string ResolveResultsPath()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "-uhTestResults", StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }
        return null;
    }

    private static void WriteNUnitXml(ITestResult result, string filePath)
    {
        string dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        XmlWriterSettings settings = new XmlWriterSettings
        {
            Indent = true,
            NewLineOnAttributes = false
        };
        using (StreamWriter sw = File.CreateText(filePath))
        using (XmlWriter xw = XmlWriter.Create(sw, settings))
        {
            int total = result.PassCount + result.FailCount + result.SkipCount + result.InconclusiveCount;
            TNode run = new TNode("test-run");
            run.AddAttribute("id", "2");
            run.AddAttribute("testcasecount", total.ToString());
            run.AddAttribute("result", result.ResultState.ToString());
            run.AddAttribute("total", total.ToString());
            run.AddAttribute("passed", result.PassCount.ToString());
            run.AddAttribute("failed", result.FailCount.ToString());
            run.AddAttribute("inconclusive", result.InconclusiveCount.ToString());
            run.AddAttribute("skipped", result.SkipCount.ToString());
            run.AddAttribute("asserts", result.AssertCount.ToString());
            run.AddAttribute("engine-version", "3.5.0.0");
            run.AddAttribute("clr-version", Environment.Version.ToString());
            run.AddAttribute("start-time", result.StartTime.ToString("u"));
            run.AddAttribute("end-time", result.EndTime.ToString("u"));
            run.AddAttribute("duration", result.Duration.ToString());
            run.ChildNodes.Add(result.ToXml(true));
            run.WriteTo(xw);
        }
    }
}
'@
}

# STANDALONE ONLY. The asmdef for the player-side test callback above. Referencing
# UnityEngine.TestRunner is MANDATORY: TestRunCallbackListener.GetAllCallbacks only
# scans assemblies that reference UnityEngine.TestRunner. overrideReferences +
# precompiledReferences=nunit.framework.dll gives the callback the NUnit types;
# defineConstraints UNITY_INCLUDE_TESTS keeps it out of non-test builds. This must
# be a PLAYER assembly (NOT under Assets/Editor/), so includePlatforms is empty.
function New-StandaloneTestCallbackAsmdef {
    @'
{
    "name": "UhCiStandaloneTestCallback",
    "references": [
        "UnityEngine.TestRunner"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": true,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ]
}
'@
}

# TODO(unity-helpers): NO-OP. DxMessaging ships RoslynAnalyzer/source-generator
# DLLs under Editor/Analyzers/ and the harness pre-copies them into the ephemeral
# project's Assets/Plugins so the generator is registered at the first compile.
# unity-helpers ships NO analyzers today: the repo has no Editor/Analyzers/ dir
# and no RoslynAnalyzer-labeled assets (the .gitignore RESERVES Editor/Analyzers/
# *.dll|*.pdb for a future analyzer, but none exists yet). The original
# Assert/Copy/diagnostic functions THREW when those DLLs were absent, which would
# hard-fail every unity-helpers CI run. They are neutralized to safe no-ops below
# so the proven harness flow (manifest, configurator, standalone split-build,
# license, catastrophic-pattern scanning, exit-code handling) is otherwise
# preserved. If/when unity-helpers ships analyzers under Editor/Analyzers/, port
# DxMessaging's bodies for these three functions (Assert-/Copy-/Write-AnalyzerSetupDiagnostics)
# and add the labeled DLL names to $RoslynAnalyzerLabeledDllNames.
$RoslynAnalyzerLabeledDllNames = @()

function Assert-UnityHelpersAnalyzerDllsPresent {
    param([Parameter(Mandatory = $true)][string]$Root)

    # NO-OP (see TODO above): unity-helpers ships no analyzer DLLs, so there is
    # nothing to assert. $Root is accepted to keep the call-site signature stable.
    $null = $Root
}

# NO-OP (see TODO above). DxMessaging pre-created the SAME
# Assets/Plugins/Editor/WallstopStudios.DxMessaging/ analyzer copy that its
# Editor/SetupCscRsp.cs makes at editor load, BEFORE Unity launched, so the source
# generator was registered exactly once at the first compile. unity-helpers ships
# no analyzers, so this copy is skipped entirely.
function Copy-UnityHelpersAnalyzersToAssets {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Project
    )

    Assert-UnityHelpersAnalyzerDllsPresent -Root $Root

    # No analyzers to copy. Return immediately; the rest of the original body
    # (DLL enumeration, .meta authoring, RoslynAnalyzer labeling) is intentionally
    # not ported because there is no Editor/Analyzers/ source to copy from.
    $null = $Project
}

function Write-AnalyzerSetupDiagnostics {
    param(
        [Parameter(Mandatory = $true)][string]$Project,
        [string]$LogPath,
        [Parameter(Mandatory = $true)][string]$Label
    )

    # TODO(unity-helpers): NO-OP (see Copy-UnityHelpersAnalyzersToAssets above).
    # DxMessaging asserted here that the pre-created Assets/Plugins analyzer copy
    # was RoslynAnalyzer-labeled AND Editor-excluded, THROWING otherwise. With no
    # analyzers shipped there is nothing to verify, so emit a single notice and
    # return. When unity-helpers ships analyzers, port DxMessaging's body here.
    $null = $Project
    $null = $LogPath
    Write-Host "::notice::unity-helpers ships no analyzers; skipping analyzer setup diagnostics ($Label)."
}

function Initialize-EphemeralProject {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Mode,
        [string]$Path,
        [switch]$IncludeComparisons,
        [switch]$IncludeIntegrations,
        [string]$Backend = 'IL2CPP',
        [bool]$DevelopmentBuild = $false,
        [string]$RepoRoot
    )

    # The comparison/integration package single sources live at the repo root.
    # Default to -Root when no explicit -RepoRoot is threaded (the package source
    # root is the repo root in this harness), so New-ManifestJson can read them.
    if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
        $RepoRoot = $Root
    }

    $project = if ($Path) {
        Resolve-FullPath -Path $Path
    } else {
        Join-Path $Root ".artifacts\unity\projects\$Version-$Mode"
    }

    New-Item -ItemType Directory -Force -Path (Join-Path $project 'Packages') | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $project 'ProjectSettings') | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $project 'Assets\Editor') | Out-Null

    New-ManifestJson -Root $Root -IncludeComparisons:$IncludeComparisons -IncludeIntegrations:$IncludeIntegrations -RepoRoot $RepoRoot |
        Set-Content -LiteralPath (Join-Path $project 'Packages\manifest.json') -Encoding UTF8
    "m_EditorVersion: $Version`n" |
        Set-Content -LiteralPath (Join-Path $project 'ProjectSettings\ProjectVersion.txt') -Encoding UTF8
    New-ConfiguratorSource -Backend $Backend |
        Set-Content -LiteralPath (Join-Path $project 'Assets\Editor\UhCiTestConfigurator.cs') -Encoding UTF8

    # Pre-create the Assets/Plugins analyzer copy (NO-OP for unity-helpers, which
    # ships no analyzers -- see Copy-UnityHelpersAnalyzersToAssets). Kept as a call
    # so the flow matches DxMessaging's and so the body becomes load-bearing again
    # the moment unity-helpers ships a RoslynAnalyzer under Editor/Analyzers/.
    Copy-UnityHelpersAnalyzersToAssets -Root $Root -Project $project

    # STANDALONE ONLY: generate the split-build helpers that sever the test
    # player's PlayerConnection/TCP result streaming (the 10060 hang on multi-NIC
    # self-hosted runners). The Editor-side build modifier clears the player's
    # outbound-connection BuildOptions and exits the editor after the build; the
    # player-side TestRunCallback writes NUnit XML to -uhTestResults and quits.
    # Written idempotently (only when missing or changed), exactly like
    # Copy-UnityHelpersAnalyzersToAssets, so reruns against the cached project do
    # not needlessly invalidate Unity's import cache. editmode/playmode never emit
    # these files (the local single -runTests path is untouched).
    if ($Mode -eq 'standalone') {
        $standaloneFiles = @(
            @{ Path = (Join-Path $project 'Assets\Editor\UhCiStandaloneBuildModifier.cs'); Content = (New-StandaloneBuildModifierSource -DevelopmentBuild $DevelopmentBuild) },
            @{ Path = (Join-Path $project 'Assets\UhCiStandaloneTestCallback\UhCiStandaloneTestCallback.cs'); Content = (New-StandaloneTestCallbackSource) },
            @{ Path = (Join-Path $project 'Assets\UhCiStandaloneTestCallback\UhCiStandaloneTestCallback.asmdef'); Content = (New-StandaloneTestCallbackAsmdef) }
        )
        foreach ($file in $standaloneFiles) {
            $dir = Split-Path -Parent $file.Path
            if ($dir -and -not (Test-Path -LiteralPath $dir -PathType Container)) {
                New-Item -ItemType Directory -Force -Path $dir | Out-Null
            }
            $needsWrite = -not (Test-Path -LiteralPath $file.Path -PathType Leaf)
            if (-not $needsWrite) {
                # Compare EOL-trailing-tolerantly: Set-Content appends a trailing
                # newline that the here-string content lacks, so a naive `-ne` would
                # rewrite on every run and needlessly bust Unity's import cache.
                $existing = Get-Content -LiteralPath $file.Path -Raw
                $needsWrite = ($existing.TrimEnd("`r", "`n") -ne $file.Content.TrimEnd("`r", "`n"))
            }
            if ($needsWrite) {
                Set-Content -LiteralPath $file.Path -Value $file.Content -Encoding UTF8
            }
        }
        Write-Host "::group::unity-helpers standalone split-build helpers"
        Write-Host "Generated the standalone build modifier + player TestRunCallback under $project (file-based results; no PlayerConnection)."
        foreach ($file in $standaloneFiles) {
            Write-Host "  $($file.Path)"
        }
        Write-Host "::endgroup::"
    }

    return $project
}

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

function Get-AcceleratorArguments {
    param(
        [string]$Endpoint,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Mode
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
    Write-Host "::add-mask::$($Endpoint.Trim())"
    Write-Host "::add-mask::$normalized"

    return @(
        '-EnableCacheServer',
        '-cacheServerEndpoint', $normalized,
        '-cacheServerNamespacePrefix', "unity-helpers-$Version-$Mode",
        '-cacheServerEnableDownload', 'true',
        '-cacheServerEnableUpload', 'true'
    )
}

function Invoke-UnityLicenseActivate {
    param(
        [Parameter(Mandatory = $true)][string]$EditorPath,
        [Parameter(Mandatory = $true)][string]$Serial,
        [Parameter(Mandatory = $true)][string]$Email,
        [Parameter(Mandatory = $true)][string]$Password,
        [Parameter(Mandatory = $true)][string]$LogPath
    )

    # Classic SERIAL activation: a single editor invocation that activates the
    # paid Unity seat and immediately quits. This MUST succeed before the test
    # run, so unlike the return path it THROWS on a non-zero exit -- a failed
    # activation means the test editor would launch unlicensed and fail opaquely.
    $logDir = Split-Path -Parent $LogPath
    if ($logDir -and -not (Test-Path -LiteralPath $logDir -PathType Container)) {
        New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    }

    # SECURITY: the serial/email/password ride in the argument array, so this site
    # must NEVER echo the args (no "...$activateArgs..." Write-Host). The caller
    # passes a $LogPath that lives under a NON-uploaded temp dir (RUNNER_TEMP /
    # system temp), never under $ArtifactsPath, so the credentials cannot leak into
    # an uploaded artifact.
    $activateArgs = @(
        '-quit',
        '-batchmode',
        '-nographics',
        '-serial', $Serial,
        '-username', $Email,
        '-password', $Password,
        '-logFile', '-'
    )

    Write-Host "::group::Activate Unity license (serial)"
    # Unity.exe is a Windows GUI-subsystem binary: PowerShell's `&` does NOT wait
    # for it or set $LASTEXITCODE unless its stdout is consumed via the pipeline.
    # `-logFile -` puts the Unity log on stdout and `| Tee-Object` forces the wait,
    # sets $LASTEXITCODE, and persists the (non-uploaded) temp log. (Proven idiom;
    # see Invoke-UnityEditor.)
    & $EditorPath @activateArgs 2>&1 | Tee-Object -FilePath $LogPath
    $exitCode = $LASTEXITCODE
    Write-Host "::endgroup::"
    if ($exitCode -ne 0) {
        # The message names the failure and the (non-uploaded) log path ONLY -- it
        # must never embed the serial/email/password values.
        throw "Unity license activation failed with exit code $exitCode. See the activation log at $LogPath (not uploaded as an artifact)."
    }

    Write-CiNotice 'Activated the Unity license (serial).'
}

function Test-UnityLicenseReturnLogShowsEntitlementReturned {
    param(
        [Parameter(Mandatory = $true)][string]$LogPath
    )

    try {
        if (-not (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
            return $false
        }

        $returnedEntitlement = Select-String `
            -LiteralPath $LogPath `
            -Pattern 'Successfully returned the entitlement license' `
            -SimpleMatch `
            -Quiet
        $legacyFileUnavailable = Select-String `
            -LiteralPath $LogPath `
            -Pattern 'Serial number unavailable for ULF return' `
            -SimpleMatch `
            -Quiet
        return $returnedEntitlement -and $legacyFileUnavailable
    } catch {
        return $false
    }
}

function Invoke-UnityLicenseReturn {
    param(
        [Parameter(Mandatory = $true)][string]$EditorPath,
        [Parameter(Mandatory = $true)][string]$Email,
        [Parameter(Mandatory = $true)][string]$Password,
        [Parameter(Mandatory = $true)][string]$LogPath
    )

    # Best-effort, defense-in-depth: this MUST NEVER throw. The license is also
    # returned by the workflow if:always() step (a backstop for a hard-killed
    # editor that never reaches this finally) and by the NEXT run's
    # return-at-start (which reclaims a seat leaked by a prior force-killed run on
    # this persistent self-hosted runner).
    try {
        $logDir = Split-Path -Parent $LogPath
        if ($logDir -and -not (Test-Path -LiteralPath $logDir -PathType Container)) {
            New-Item -ItemType Directory -Force -Path $logDir | Out-Null
        }

        # SECURITY: email/password ride in the argument array; never echo the args
        # and keep the return log in the NON-uploaded temp dir, never under
        # $ArtifactsPath.
        $returnArgs = @(
            '-quit',
            '-batchmode',
            '-nographics',
            '-returnlicense',
            '-username', $Email,
            '-password', $Password,
            '-logFile', '-'
        )

        Write-Host "::group::Return Unity license (serial)"
        # Same Tee-Object wait + $LASTEXITCODE idiom as Invoke-UnityLicenseActivate
        # / Invoke-UnityEditor (a bare `&` would not wait for the GUI-subsystem
        # binary). `-logFile -` puts the log on stdout; Tee-Object DOES persist it
        # to $LogPath, but the caller keeps $LogPath under the NON-uploaded temp dir
        # (RUNNER_TEMP / system temp), so it stays out of any UPLOADED ARTIFACT and
        # the account fragments Unity may print cannot leak into uploads.
        & $EditorPath @returnArgs 2>&1 | Tee-Object -FilePath $LogPath
        $exitCode = $LASTEXITCODE
        Write-Host "::endgroup::"

        if ($exitCode -ne 0) {
            if (Test-UnityLicenseReturnLogShowsEntitlementReturned -LogPath $LogPath) {
                Write-CiNotice "Unity returned the entitlement license, then exited with code $exitCode while skipping legacy ULF return; treating the seat return as successful."
            } else {
                Write-Host "::warning::Unity license return exited with code $exitCode; the workflow if:always() return step and the next run's return-at-start are the backstops for the leaked seat."
            }
        } else {
            Write-CiNotice 'Returned the Unity license (serial).'
        }
    } catch {
        Write-Host "::warning::Unity license return failed: $($_.Exception.Message). The workflow if:always() return step and the next run's return-at-start are the backstops."
    }
}

function Get-StandaloneTestPlayerTimeoutSeconds {
    # Single source of truth for the TOTAL wall-clock timeout applied to the
    # DIRECTLY-LAUNCHED standalone test player (Invoke-StandaloneTestPlayer). The
    # player runs ~700 runtime tests headless in single-digit minutes; the 30 min
    # default is a generous backstop so a player that hangs (e.g. a residual
    # connection dial-out or a deadlocked test) is tree-killed instead of running
    # until the 120-minute GitHub step is cancelled. Mirrors ensure-editor.ps1
    # Get-EnsureEditorInstallTimeoutSeconds EXACTLY: honors
    # UH_STANDALONE_PLAYER_TIMEOUT_SECONDS; a non-integer or NEGATIVE override is
    # ignored with a ::warning:: and the default is used; 0 is the explicit OPT-OUT
    # (unbounded wait). StrictMode-safe: no collection reads.
    param([int]$Default = 1800)

    if ($env:UH_STANDALONE_PLAYER_TIMEOUT_SECONDS) {
        $parsed = 0
        if (
            [int]::TryParse($env:UH_STANDALONE_PLAYER_TIMEOUT_SECONDS, [ref]$parsed) -and
            $parsed -ge 0
        ) {
            return $parsed
        }
        Write-Host "::warning::Ignoring invalid UH_STANDALONE_PLAYER_TIMEOUT_SECONDS='$env:UH_STANDALONE_PLAYER_TIMEOUT_SECONDS'; using $Default second(s)."
    }
    return $Default
}

function Get-StandaloneBuildTimeoutSeconds {
    # Single source of truth for the TOTAL wall-clock timeout applied to the editor
    # BUILD step that produces the standalone IL2CPP test player. The IL2CPP build
    # is the long pole; the 45 min default matches the install default and comfortably
    # exceeds a slow-but-progressing build, so a build that idles forever (e.g. the
    # PostBuildCleanup exit never fired because the modifier failed to compile and
    # AutoRunPlayer stayed set) is tree-killed instead of consuming the 120-minute
    # GitHub step. Mirrors ensure-editor.ps1 Get-EnsureEditorInstallTimeoutSeconds
    # EXACTLY: honors UH_STANDALONE_BUILD_TIMEOUT_SECONDS; a non-integer or NEGATIVE
    # override is ignored with a ::warning:: and the default is used; 0 is the
    # explicit OPT-OUT (unbounded wait). StrictMode-safe: no collection reads.
    param([int]$Default = 2700)

    if ($env:UH_STANDALONE_BUILD_TIMEOUT_SECONDS) {
        $parsed = 0
        if (
            [int]::TryParse($env:UH_STANDALONE_BUILD_TIMEOUT_SECONDS, [ref]$parsed) -and
            $parsed -ge 0
        ) {
            return $parsed
        }
        Write-Host "::warning::Ignoring invalid UH_STANDALONE_BUILD_TIMEOUT_SECONDS='$env:UH_STANDALONE_BUILD_TIMEOUT_SECONDS'; using $Default second(s)."
    }
    return $Default
}

function ConvertTo-ProcessArgumentLine {
    # MIRROR of scripts/unity/ensure-editor.ps1 ConvertTo-ProcessArgumentLine
    # (run-ci-tests.ps1 does not import that script, so the helper is copied here
    # verbatim). Builds a single Windows command-line argument string from an array,
    # quoting any argument containing whitespace or a quote and escaping embedded
    # backslashes/quotes per the CommandLineToArgvW rules. Used by
    # Invoke-ProcessWithTreeKillTimeout (it assigns ProcessStartInfo.Arguments, the
    # single command-line string form, NOT the per-element argument-list property
    # the contract forbids).
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

function Invoke-ProcessWithTreeKillTimeout {
    # GENERALIZED hard tree-kill watchdog, STRUCTURALLY IDENTICAL to
    # scripts/unity/ensure-editor.ps1 Invoke-UnityCliCaptureWithTimeout (the proven
    # resilience core). It launches $FilePath with $Arguments via
    # System.Diagnostics.Process + ProcessStartInfo, drains BOTH stdout and stderr
    # from a MAIN-THREAD ReadLineAsync poll loop (live echo via Write-Host + Tee to
    # $LogPath), enforces an absolute UTC deadline, and on a breach $proc.Kill($true)
    # tree-kills the whole process tree (the Unity editor build spawns child
    # processes -- IL2CPP/bee -- and the player may too, so a bare Kill() would orphan
    # them). The process is held in a try/finally that kills it on ANY throw between
    # launch and reap, so a pwsh cancellation cannot leave an orphaned editor/player.
    #
    # WHY a Process and NOT `& <exe>`: the call operator cannot be interrupted -- a
    # hung child runs until the whole job is killed. WHY the main-thread poll loop:
    # every line is echoed LIVE the instant it arrives (no silent multi-minute build
    # console) AND both pipes are continuously drained so neither can fill and
    # back-pressure the child (the classic full-pipe-buffer deadlock is impossible).
    # A Process.Start() launch is NOT an `&`/`.` call, so it does not trip the
    # powershell-unity-process-wait-safety parser rule; the contract test additionally
    # forbids a bare empty-parens WaitForExit and the per-element argument-list
    # property here, both of which this implementation avoids.
    #
    # Returns a StrictMode-safe hashtable @{ ExitCode; TimedOut }. The caller throws
    # on $TimedOut or a non-zero $ExitCode; the FILE written by the player is the
    # source of truth for pass/fail.
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments,
        [int]$TimeoutSeconds = 1800,
        [Parameter(Mandatory = $true)][string]$LogPath,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $logDir = Split-Path -Parent $LogPath
    if ($logDir -and -not (Test-Path -LiteralPath $logDir -PathType Container)) {
        New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    }

    # Sentinel exit code for a wall-clock timeout kill. 124 mirrors GNU coreutils
    # `timeout`; it is non-zero so the caller's "exit != 0 -> fail" path applies.
    $timeoutExitCode = 124

    Write-Host "::group::$Label"
    Write-Host "`"$FilePath`" $($Arguments -join ' ')"

    $buffer = New-Object System.Collections.Generic.List[string]

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
    try {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $FilePath
        $psi.Arguments = ConvertTo-ProcessArgumentLine -Arguments $Arguments
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true

        $proc = New-Object System.Diagnostics.Process
        $proc.StartInfo = $psi

        [void]$proc.Start()

        $outReader = $proc.StandardOutput
        $errReader = $proc.StandardError
        $oTask = $outReader.ReadLineAsync()
        $eTask = $errReader.ReadLineAsync()

        if ($hasDeadline) {
            $deadline = [DateTime]::UtcNow.AddMilliseconds([double]$timeoutMs)
        } else {
            $deadline = [DateTime]::MaxValue
        }

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
                    $eTask = $errReader.ReadLineAsync()
                }
                $progressed = $true
            }

            if ([DateTime]::UtcNow -ge $deadline) {
                # HUNG (or a quick-exit child whose grandchild still holds the pipe
                # open, so EOF never arrives): tree-kill the WHOLE process tree.
                $timedOut = $true
                try {
                    $proc.Kill($true)
                } catch {
                    try { $proc.Kill() } catch { }
                }
                break
            }

            if (-not $progressed) {
                Start-Sleep -Milliseconds 50
            }
        }

        # Reap so ExitCode is valid; bounded so a stuck reap cannot hang the harness.
        $reaped = $proc.WaitForExit(5000)

        # Drain any reads that completed during/after the kill so no pre-kill output
        # is dropped.
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
            $exit = $timeoutExitCode
        } elseif ($reaped -and $proc.HasExited) {
            $exit = $proc.ExitCode
        } else {
            $exit = $timeoutExitCode
            $timedOut = $true
        }
    } catch {
        $message = "Process watchdog '$Label' threw: $($_.Exception.Message)"
        Write-Host "::warning::$message"
        $buffer.Add($message)
        $exit = -1
    } finally {
        # If we are unwinding on a throw/cancellation and the process is still alive,
        # tree-kill it so a cancelled step never orphans the editor/player.
        if ($proc -and -not $proc.HasExited) {
            try { $proc.Kill($true) } catch { }
        }
        if ($proc) { $proc.Dispose() }
    }

    Write-Host "::endgroup::"

    # Persist the captured (already-streamed) output to $LogPath for diagnostics.
    try {
        Set-Content -LiteralPath $LogPath -Value (@($buffer.ToArray()) -join "`n") -Encoding UTF8
    } catch {
        Write-Host "::warning::Could not persist '$Label' log to ${LogPath}: $($_.Exception.Message)"
    }

    return @{
        ExitCode = $exit
        TimedOut = [bool]$timedOut
    }
}

function Invoke-StandaloneTestPlayer {
    # RUN the editor-built standalone IL2CPP test player DIRECTLY (no
    # PlayerConnection): the player-side TestRunCallback writes NUnit XML to the
    # -uhTestResults path and quits 0/1/2/3. The exe is launched under the hard
    # tree-kill watchdog so a hung player is killed long before the GitHub step is
    # cancelled. Returns @{ ExitCode; TimedOut }. The FILE is the source of truth: the
    # caller validates results.xml and treats a watchdog timeout as fatal ONLY when no
    # usable results file was written (a player can finish writing results in
    # RunFinished and then have Application.Quit deferred in -batchmode IL2CPP, which
    # the watchdog would otherwise turn into a spurious failure). Exit 2 (the player got
    # no -uhTestResults arg -- a harness-contract violation) is still thrown here.
    #
    # ONE results channel: -uhTestResults. There is NO environment-variable handoff
    # and NO per-user-data-folder fallback.
    param(
        [Parameter(Mandatory = $true)][string]$EditorBuiltExePath,
        [Parameter(Mandatory = $true)][string]$ResultsPath,
        [Parameter(Mandatory = $true)][string]$LogPath,
        [int]$TimeoutSeconds = 1800
    )

    $playerArgs = @(
        '-batchmode',
        '-nographics',
        '-logFile', '-',
        '-uhTestResults', $ResultsPath
    )

    $result = Invoke-ProcessWithTreeKillTimeout `
        -FilePath $EditorBuiltExePath `
        -Arguments $playerArgs `
        -TimeoutSeconds $TimeoutSeconds `
        -LogPath $LogPath `
        -Label 'Run standalone test player'

    # Exit 2 means the player received no -uhTestResults arg (a harness-contract
    # violation -- the harness always passes it), so no file can exist: fail fast.
    if ($result.ExitCode -eq 2) {
        throw "Standalone test player reported no -uhTestResults path (exit 2); no results were written. See the player log at $LogPath."
    }

    # Do NOT throw on a watchdog timeout here. A player can write a complete results
    # file in its RunFinished callback and then have Application.Quit deferred/ignored
    # in -batchmode -nographics IL2CPP; the watchdog then tree-kills it (TimedOut) even
    # though the results are valid. The caller validates the FILE (the source of truth)
    # and decides, so a deferred-quit run is not turned into a spurious failure.
    return @{ ExitCode = $result.ExitCode; TimedOut = $result.TimedOut }
}

function Invoke-UnityEditor {
    param(
        [Parameter(Mandatory = $true)][string]$EditorPath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$LogPath
    )

    # Unity.exe is a Windows GUI-subsystem binary. PowerShell's `&` launches such
    # executables ASYNCHRONOUSLY: it does NOT wait for them and does NOT set
    # $LASTEXITCODE. Callers therefore pass `-logFile -` (Unity logs to stdout) so
    # that consuming the process's stdout via the pipeline forces PowerShell to
    # BLOCK until the process exits AND reliably sets $LASTEXITCODE. Tee-Object both
    # streams the log live to the CI console and persists it to $LogPath. This is
    # the proven idiom from scripts/unity/run-tests.ps1.
    $logDir = Split-Path -Parent $LogPath
    if ($logDir -and -not (Test-Path -LiteralPath $logDir -PathType Container)) {
        New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    }

    Write-Host "::group::$Label"
    Write-Host "`"$EditorPath`" $($Arguments -join ' ')"
    # Stream Unity's output LIVE to the console AND persist it to $LogPath, but route
    # it to the HOST (Out-Host) so it never enters this function's success stream:
    # the function RETURNS the exit code, and a bare `| Tee-Object` would otherwise
    # collect every streamed log line into the caller's `$x = Invoke-UnityEditor`
    # capture (turning the return value into an Object[] of log lines + the code).
    # Consuming the process's stdout via the pipeline still forces PowerShell to
    # BLOCK until the GUI-subsystem Unity.exe exits and to set $LASTEXITCODE.
    & $EditorPath @Arguments 2>&1 | Tee-Object -FilePath $LogPath | Out-Host
    $exitCode = $LASTEXITCODE
    Write-Host "::endgroup::"
    if ($exitCode -ne 0) {
        # Proactively surface catastrophic compile-time failure patterns
        # (PrecompiledAssemblyException, CompilationFailedException, CS####,
        # CS8032) as ::error:: annotations so the operator sees the root cause
        # in BOTH the runner log AND GitHub's error summary, independent of
        # whether the workflow-level verify step also fires. On a benign
        # shutdown-race crash the log matches no catastrophic pattern, so this
        # is a no-op; on a real compile failure it names the root cause.
        Write-UnityCatastrophicErrorAnnotations -LogPath $LogPath
        # If that compile failure was a missing UnityEngine module (CS1069 forward
        # / CS0234 'UI'), name the exact package id to add to the shared manifest
        # source so the fix is one obvious edit, not a CS-error guessing game.
        Write-UnityMissingModuleAnnotations -LogPath $LogPath
    }
    # RETURN the exit code; do NOT throw on a non-zero value. The DURABLE ARTIFACT
    # the invocation produces (the configure marker / the built player exe / the
    # NUnit results.xml) is the source of truth, validated by the caller. Unity
    # can crash in a BACKGROUND thread (for example the DirectoryMonitor file
    # watcher) DURING shutdown AFTER the artifact is fully written, returning a
    # crash exit code for an otherwise-successful run; gating on the artifact (not
    # the exit code) makes those benign shutdown-race crashes non-fatal while a
    # missing/invalid artifact still fails loudly.
    return $exitCode
}

# The Windows NTSTATUS codes a Unity batch process most commonly exits WITH when
# it crashes or aborts. Keyed by the canonical 8-char uppercase hex of the
# UNSIGNED exit code. This is the single source of truth for both the human
# description (Get-NativeExitCodeDescription) and the "is this a native crash
# code" classifier (Test-NativeCrashExitCode). Crash codes (the 0xC000xxxx
# family) are EXACTLY the benign post-work shutdown-race exits the
# artifact-is-source-of-truth gate tolerates when the durable artifact is valid.
$script:NativeExitCodeDescriptions = [ordered]@{
    'C0000005' = 'STATUS_ACCESS_VIOLATION'
    'C000001D' = 'STATUS_ILLEGAL_INSTRUCTION'
    'C0000017' = 'STATUS_NO_MEMORY'
    'C00000FD' = 'STATUS_STACK_OVERFLOW'
    'C0000135' = 'STATUS_DLL_NOT_FOUND'
    'C0000139' = 'STATUS_ENTRYPOINT_NOT_FOUND'
    'C0000374' = 'STATUS_HEAP_CORRUPTION'
    'C0000409' = 'STATUS_STACK_BUFFER_OVERRUN'
    'C0000420' = 'STATUS_ASSERTION_FAILURE'
}

function ConvertTo-UnsignedExitHex {
    # Canonical 8-char uppercase hex of an exit code, normalizing the negative
    # Int32 form PowerShell yields for a high-bit NTSTATUS (for example -1073741819
    # -> 'C0000005'). Compare against this STRING form, never the 0xC0000005 token:
    # PowerShell parses `0xC0000005` as a NEGATIVE Int32, so a numeric -eq against
    # the unsigned value silently fails (the int/uint conflation this whole helper
    # exists to avoid).
    param([Parameter(Mandatory = $true)][int]$ExitCode)
    $normalized = if ($ExitCode -lt 0) {
        [uint32]($ExitCode + 4294967296)
    } else {
        [uint32]$ExitCode
    }
    return $normalized.ToString('X8')
}

function Test-NativeCrashExitCode {
    # True when the exit code is a native Windows CRASH/abort NTSTATUS (the
    # 0xC000xxxx severity-error family), i.e. a process the OS terminated rather
    # than a value the app returned (0..255). Used ONLY to phrase the benign-exit
    # ::warning:: accurately; the pass/fail decision is gated on the durable
    # artifact, never on this classifier.
    param([Parameter(Mandatory = $true)][int]$ExitCode)
    $hexBare = ConvertTo-UnsignedExitHex -ExitCode $ExitCode
    if ($script:NativeExitCodeDescriptions.Contains($hexBare)) {
        return $true
    }
    # The 0xC000xxxx NTSTATUS family (STATUS_SEVERITY_ERROR + facility 0) covers
    # the native crash/abort statuses a Unity batch process exits with. This is a
    # best-effort classifier for the warning text ONLY; pass/fail is gated on the
    # durable artifact, so a status outside this prefix is at worst a missing
    # "(a native crash code)" note, never a wrong verdict.
    return ($hexBare -like 'C0*')
}

function Get-NativeExitCodeDescription {
    param([Parameter(Mandatory = $true)][int]$ExitCode)

    $hexBare = ConvertTo-UnsignedExitHex -ExitCode $ExitCode
    $hex = "0x$hexBare"
    if ($script:NativeExitCodeDescriptions.Contains($hexBare)) {
        return "$hex / $($script:NativeExitCodeDescriptions[$hexBare])"
    }

    return $hex
}

function Get-UnityCrashSignature {
    # Best-effort: scan a captured Unity log for the signature of a BACKGROUND-thread
    # crash that fired DURING shutdown, AFTER the batch work completed. Returns a
    # short human description (for the benign-exit ::warning::) or '' when no crash
    # signature is present. NEVER throws -- a diagnostic must not mask the real
    # decision (which is gated on the durable artifact, not on this scan).
    param([string]$LogPath)

    if (-not $LogPath -or -not (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
        return ''
    }
    try {
        $logText = Get-Content -LiteralPath $LogPath -Raw
    } catch {
        return ''
    }
    if (-not $logText) {
        return ''
    }

    # The editor reached the end of batch execution before the crash -> the crash
    # is in teardown, not in the work. (-quit prints this; -runTests prints the
    # "Exiting batchmode successfully" variant.)
    $cleanShutdown = ($logText -match 'Batchmode quit successfully invoked' -or
        $logText -match 'Exiting batchmode successfully')

    # A known benign Windows shutdown-race: the DirectoryMonitor file-watcher
    # thread faulting while the editor tears down. This is the crash observed on
    # the 6000.3 standalone configure pass.
    if ($logText -match 'DirectoryMonitor') {
        $suffix = if ($cleanShutdown) { ' after a clean batch shutdown' } else { '' }
        return "Unity DirectoryMonitor file-watcher thread crash during shutdown$suffix"
    }
    if ($logText -match 'Crash!!!') {
        $suffix = if ($cleanShutdown) { ' after a clean batch shutdown' } else { '' }
        return "Unity native crash during shutdown$suffix"
    }
    if ($cleanShutdown) {
        return 'Unity completed its batch work (clean shutdown logged) before exiting non-zero'
    }
    return ''
}

function Write-UnityBenignExitWarning {
    # Emit a single ::warning:: when a Unity batch invocation produced a VALID
    # durable artifact but still exited non-zero or was tree-killed by the
    # watchdog. Decodes the exit code (for example 0xC0000005 /
    # STATUS_ACCESS_VIOLATION) and names any crash signature found in the log, so
    # the benign post-work shutdown crash stays VISIBLE and trackable in CI without
    # failing the job. The artifact -- already validated by the caller -- is the
    # source of truth; this only narrates why a non-zero exit was tolerated.
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [int]$ExitCode = 0,
        [switch]$TimedOut,
        [string]$LogPath
    )

    $cause = if ($TimedOut) {
        'was tree-killed by the watchdog (likely a deferred Application.Quit)'
    } else {
        $description = Get-NativeExitCodeDescription -ExitCode $ExitCode
        $crashNote = if (Test-NativeCrashExitCode -ExitCode $ExitCode) { ' (a native crash code)' } else { '' }
        "exited with code $ExitCode / $description$crashNote"
    }
    $signature = Get-UnityCrashSignature -LogPath $LogPath
    $signatureNote = if ($signature) { " Crash signature: $signature." } else { '' }
    Write-Host "::warning::${Label}: Unity $cause AFTER producing a valid result artifact; honoring the artifact as the source of truth and treating this as a benign post-work shutdown crash.$signatureNote"
}

function Test-UnityConfigureMarker {
    # Validate the standalone-configure SUCCESS MARKER as the source of truth for
    # the configure pass (UhCiTestConfigurator.Apply writes it as its final
    # action). Returns '' when the marker exists and is FRESH for this run, else a
    # short reason string (mirrors Test-StandalonePlayerBuildOutput's contract).
    # A fresh marker proves Apply() ran to completion even if Unity then crashed in
    # a background thread during shutdown and returned a crash exit code.
    param(
        [Parameter(Mandatory = $true)][string]$MarkerPath,
        [Parameter(Mandatory = $true)][datetime]$StartedUtc
    )

    if (-not (Test-Path -LiteralPath $MarkerPath -PathType Leaf)) {
        return 'configure marker was not written (UhCiTestConfigurator.Apply did not run to completion)'
    }
    $marker = Get-Item -LiteralPath $MarkerPath
    if ($marker.LastWriteTimeUtc -lt $StartedUtc.AddSeconds(-5)) {
        return "stale configure marker; LastWriteTimeUtc=$($marker.LastWriteTimeUtc.ToString('o'))"
    }
    return ''
}

function Invoke-UnityNativeStartupProbe {
    param(
        [Parameter(Mandatory = $true)][string]$EditorPath,
        [Parameter(Mandatory = $true)][string]$LogPath
    )

    $logDir = Split-Path -Parent $LogPath
    if ($logDir -and -not (Test-Path -LiteralPath $logDir -PathType Container)) {
        New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    }

    Write-Host "::group::Unity native startup diagnostics"
    Write-Host "Runner name: $env:RUNNER_NAME"
    Write-Host "Runner OS: $env:RUNNER_OS"
    Write-Host "Runner architecture: $env:RUNNER_ARCH"
    Write-Host "Unity editor path: $EditorPath"
    try {
        $editorItem = Get-Item -LiteralPath $EditorPath
        Write-Host "Unity editor file version: $($editorItem.VersionInfo.FileVersion)"
        Write-Host "Unity editor product version: $($editorItem.VersionInfo.ProductVersion)"
    } catch {
        Write-Host "::notice::Could not read Unity editor version info: $($_.Exception.Message)"
    }

    Write-Host "Unity licensing client inventory:"
    $licensingClientCandidates = New-Object System.Collections.Generic.List[string]
    foreach ($root in @(${env:ProgramFiles}, ${env:ProgramFiles(x86)})) {
        if ($root -and $root.Trim().Length -gt 0) {
            $licensingClientCandidates.Add(
                (Join-Path $root 'Common Files\Unity\UnityLicensingClient\Unity.Licensing.Client.exe')
            )
        }
    }
    if ($env:LOCALAPPDATA -and $env:LOCALAPPDATA.Trim().Length -gt 0) {
        $licensingClientCandidates.Add(
            (Join-Path $env:LOCALAPPDATA 'Unity\Unity.Licensing.Client\Unity.Licensing.Client.exe')
        )
    }
    foreach ($candidate in $licensingClientCandidates) {
        $exists = Test-Path -LiteralPath $candidate -PathType Leaf
        Write-Host "  [$exists] $candidate"
    }

    $probeArgs = @(
        '-version',
        '-batchmode',
        '-nographics',
        '-quit',
        '-logFile', '-'
    )

    Write-Host "`"$EditorPath`" $($probeArgs -join ' ')"
    & $EditorPath @probeArgs 2>&1 | Tee-Object -FilePath $LogPath
    $exitCode = $LASTEXITCODE
    $description = Get-NativeExitCodeDescription -ExitCode $exitCode
    Write-Host "Unity native startup probe exit code: $exitCode ($description)"
    Write-Host "::endgroup::"

    if ($exitCode -ne 0) {
        throw "Unity native startup probe failed with exit code $exitCode ($description) after the pre-lock healthy-existing editor check. CI Unity jobs do not repair editors in-job; run scripts/unity/maintain-windows-runner.ps1 or dispatch .github/workflows/runner-bootstrap.yml, then retry. See the streamed probe log above (also saved to $LogPath)."
    }
}

# CLASS-OF-ISSUE GUARD: the defect this whole change fixes is a single analyzer
# DLL handed to the compiler from MORE THAN ONE path (the Assets/Plugins copy plus
# a duplicate registration). That is invisible in a raw csc command line, so this
# best-effort scanner reads the Unity compile log, collects every analyzer the
# compiler was given (-a:/-analyzer:, quoted or not), and -- when the SAME DLL file
# name came from more than one distinct path -- names the offending DLL and every
# path. It catches a regression of the project-generation fix loudly. NEVER throws
# (the caller is already on a throw path).
function Write-DuplicateAnalyzerDiagnostics {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$LogPath)

    if (-not $LogPath -or -not (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
        return
    }

    try {
        # -a:"path" / -a:path / -analyzer:"path" / -analyzer:path. Captured lazily
        # up to the first '.dll' so an unquoted, space-separated token does not
        # swallow the next argument.
        $pattern = '-(?:a|analyzer):"?([^"\r\n]+?\.dll)"?(?:"|\s|$)'
        $pathsByName = @{}
        $hits = @(
            Select-String -LiteralPath $LogPath -Pattern $pattern -AllMatches -ErrorAction SilentlyContinue
        )
        foreach ($hit in $hits) {
            foreach ($match in $hit.Matches) {
                $fullPath = $match.Groups[1].Value.Trim() -replace '\\', '/'
                if (-not $fullPath) {
                    continue
                }
                $name = Split-Path -Leaf $fullPath
                if (-not $pathsByName.ContainsKey($name)) {
                    $pathsByName[$name] = New-Object 'System.Collections.Generic.HashSet[string]'
                }
                [void]$pathsByName[$name].Add($fullPath)
            }
        }

        $duplicates = @($pathsByName.GetEnumerator() | Where-Object { $_.Value.Count -gt 1 })
        if ($duplicates.Count -lt 1) {
            return
        }

        Write-Host "::group::Duplicate analyzer registration"
        foreach ($entry in $duplicates) {
            $joinedPaths = (@($entry.Value) | Sort-Object) -join '; '
            Write-CiError ("Analyzer/source-generator '$($entry.Key)' was handed to the compiler from " +
                "$($entry.Value.Count) distinct paths: $joinedPaths. A source generator that runs more than " +
                "once emits each member twice (CS0102) and duplicate precompiled assemblies are rejected " +
                "outright. The harness must register each analyzer DLL EXACTLY ONCE (the pre-created " +
                "Assets/Plugins copy); it must NOT also wire one via csc.rsp.")
        }
        Write-Host "::endgroup::"
    } catch {
        Write-Host "::warning::Could not scan for duplicate analyzer registration: $($_.Exception.Message)"
    }
}

function Write-UnityResultFailureDiagnostics {
    param(
        [string]$LogPath,
        [string]$Project,
        [Parameter(Mandatory = $true)][string]$Label
    )

    Write-Host "::group::Unity result failure diagnostics ($Label)"
    try {
        if ($LogPath -and (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
            Write-Host "Unity log path: $LogPath"
            # Compose this function's scan list as:
            #   (catastrophic patterns from the shared $script:CatastrophicPatterns
            #    array; ONLY the regex-form entries, since Select-String's
            #    -Pattern overload is regex when -SimpleMatch is absent)
            # plus this function's local additions (Aborting/Exiting/No tests/
            # TestRunner/results.xml/assemblyNames) -- the latter are NOT
            # catastrophic-class patterns and are intentionally NOT in the
            # shared array. This keeps the "single source of truth" rule for
            # the overlapping patterns (error CS\d+, warning CS8032) without
            # changing the function's overall scan behavior.
            $catastrophicRegexes = @(
                foreach ($entry in $script:CatastrophicPatterns) {
                    if (-not $entry.UseSimple) {
                        $entry.Pattern
                    }
                }
            )
            $localDiagnosticPatterns = @(
                'Aborting batchmode',
                'Exiting batchmode successfully',
                'No tests',
                'TestRunner',
                'IPCStream \(Upm-[^)]+\): IPC stream failed to read',
                'Failed to resolve packages',
                'Cancelled resolving packages',
                'results\.xml',
                'assemblyNames'
            )
            $diagnosticPatterns = @($catastrophicRegexes) + @($localDiagnosticPatterns)
            $matches = @(
                Select-String -LiteralPath $LogPath -Pattern $diagnosticPatterns -ErrorAction SilentlyContinue |
                    Select-Object -First 80
            )
            if ($matches.Count -gt 0) {
                Write-Host "Selected Unity log lines:"
                foreach ($match in $matches) {
                    Write-Host ("  line {0}: {1}" -f $match.LineNumber, $match.Line.Trim())
                }
            } else {
                Write-Host "No targeted diagnostic lines matched in the Unity log."
            }

            $logText = Get-Content -LiteralPath $LogPath -Raw
            if ($logText -match 'warning CS8032') {
                Write-CiError "Unity could not instantiate one or more unity-helpers analyzers/source generators (CS8032). Check that Editor/Analyzers DLLs target the Roslyn version supported by this Unity editor."
            }
            if ($logText -match 'error CS0315' -and $logText -match 'Simple(?:Untargeted|Targeted|Broadcast)Message') {
                Write-CiError "Message fixture compile errors followed missing generated interfaces. This usually means the unity-helpers source generator did not load."
            }
            if ($logText -match 'Exiting batchmode successfully') {
                Write-CiError "Unity exited with code 0 but did not write NUnit results. Check the selected assembly list, test platform, and TestRunner log lines above."
            }
            if (Test-UnityPackageManagerTransientFailure -LogPath $LogPath) {
                Write-CiError "Unity Package Manager canceled package resolution before tests started. This is a CI/Unity package-resolution failure, not a unity-helpers test assertion."
                Write-UnityPackageManagerDiagnostics -Project $Project -LogPath $LogPath
            }

            # Name a duplicate analyzer registration (the same generator/analyzer
            # DLL fed to csc from two paths) -- the precise root cause of the
            # "Multiple precompiled assemblies" / CS0102 duplicate-'MessageType'
            # failures this harness change fixes.
            Write-DuplicateAnalyzerDiagnostics -LogPath $LogPath
        } else {
            Write-Host "Unity log path unavailable or missing: $LogPath"
        }

        if ($Project) {
            $analyzerCopyDir = Join-Path $Project 'Assets\Plugins\Editor\WallstopStudios.UnityHelpers'
            Write-Host "Pre-created analyzer copy dir exists: $(Test-Path -LiteralPath $analyzerCopyDir -PathType Container)"
            $scriptAssemblies = Join-Path $Project 'Library\ScriptAssemblies'
            if (Test-Path -LiteralPath $scriptAssemblies -PathType Container) {
                Write-Host "Script assemblies present:"
                Get-ChildItem -LiteralPath $scriptAssemblies -Filter '*.dll' -ErrorAction SilentlyContinue |
                    Select-Object -ExpandProperty Name |
                    Sort-Object |
                    ForEach-Object { Write-Host "  $_" }
            } else {
                Write-Host "Script assemblies directory missing: $scriptAssemblies"
            }
        }
    } catch {
        Write-Host "::warning::Could not collect Unity result failure diagnostics: $($_.Exception.Message)"
    }
    Write-Host "::endgroup::"
}

function Write-UnityRunFailureDiagnostics {
    # Emit the combined analyzer-setup + result-failure diagnostics for a Unity
    # batch invocation whose DURABLE ARTIFACT validation failed (missing configure
    # marker / invalid player exe / missing-or-invalid results.xml). This is the
    # failure-path diagnostics bundle the retired Invoke-UnityEditorWithFailureDiagnostics
    # wrapper used to emit on a thrown non-zero exit; it now fires from the
    # artifact-validation failure branch (the exit code is no longer the trigger).
    # Two callers: the configure marker-validation failure and the standalone build
    # exe-validation failure (the latter then also emits Write-StandaloneBuildOutputDiagnostics).
    # The editmode/playmode + standalone-player paths get the result-failure half
    # directly from Test-NUnitResults, which calls Write-UnityResultFailureDiagnostics.
    param(
        [Parameter(Mandatory = $true)][string]$Project,
        [Parameter(Mandatory = $true)][string]$LogPath,
        [Parameter(Mandatory = $true)][string]$CscLabel,
        [Parameter(Mandatory = $true)][string]$DiagnosticsLabel
    )

    Write-AnalyzerSetupDiagnostics -Project $Project -LogPath $LogPath -Label $CscLabel
    Write-UnityResultFailureDiagnostics -LogPath $LogPath -Project $Project -Label $DiagnosticsLabel
}

function Write-StandaloneDirectorySnapshot {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$Path,
        [int]$MaxEntries = 60
    )

    try {
        Write-Host "${Label}: $Path"
        if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
            Write-Host "  (missing)"
            return
        }

        $entries = @(
            Get-ChildItem -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue |
                Sort-Object FullName |
                Select-Object -First $MaxEntries
        )
        if ($entries.Count -lt 1) {
            Write-Host "  (empty)"
            return
        }

        foreach ($entry in $entries) {
            $kind = if ($entry.PSIsContainer) { 'dir ' } else { 'file' }
            $length = if ($entry.PSIsContainer) { '' } else { " $($entry.Length) bytes" }
            Write-Host "  [$kind] $($entry.FullName)$length"
        }
    } catch {
        Write-Host "::warning::Could not snapshot ${Label}: $($_.Exception.Message)"
    }
}

function Write-StandaloneBuildOutputDiagnostics {
    param(
        [Parameter(Mandatory = $true)][string]$Project,
        [Parameter(Mandatory = $true)][string]$ExpectedExe,
        [string]$LogPath,
        [datetime]$BuildStartedUtc
    )

    Write-Host "::group::Standalone player build output diagnostics"
    try {
        Write-Host "Expected exe: $ExpectedExe"
        Write-Host "UH_PLAYER_BUILD_PATH: $env:UH_PLAYER_BUILD_PATH"
        Write-Host "Build started UTC: $($BuildStartedUtc.ToString('o'))"

        $expectedDir = Split-Path -Parent $ExpectedExe
        Write-StandaloneDirectorySnapshot -Label 'Expected output directory' -Path $expectedDir
        Write-StandaloneDirectorySnapshot -Label 'Project Build directory' -Path (Join-Path $Project 'Build')
        Write-StandaloneDirectorySnapshot -Label 'Project Temp\UhTestPlayer directory' -Path (Join-Path $Project 'Temp\UhTestPlayer')
        Write-StandaloneDirectorySnapshot -Label 'Project Temp\PlayerWithTests directory' -Path (Join-Path $Project 'Temp\PlayerWithTests')

        Write-Host "Discovered executable candidates under Build/Temp:"
        $candidateRoots = @(
            Join-Path $Project 'Build',
            Join-Path $Project 'Temp'
        )
        $candidates = @(
            foreach ($root in $candidateRoots) {
                if (Test-Path -LiteralPath $root -PathType Container) {
                    Get-ChildItem -LiteralPath $root -Recurse -Filter '*.exe' -File -ErrorAction SilentlyContinue
                }
            }
        )
        if ($candidates.Count -lt 1) {
            Write-Host "  (none)"
        } else {
            foreach ($candidate in ($candidates | Sort-Object FullName | Select-Object -First 40)) {
                Write-Host ("  {0} ({1} bytes, LastWriteTimeUtc={2:o})" -f $candidate.FullName, $candidate.Length, $candidate.LastWriteTimeUtc)
            }
        }

        if ($LogPath -and (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
            $logText = Get-Content -LiteralPath $LogPath -Raw
            Write-Host "Build log markers:"
            foreach ($marker in @(
                    'UhCiStandaloneBuildModifier',
                    'UH_PLAYER_BUILD_PATH',
                    'UhTestPlayer',
                    'PlayerWithTests',
                    'AutoRunPlayer',
                    'CopyFiles'
                )) {
                Write-Host "  ${marker}: $($logText.Contains($marker))"
            }
            Write-Host "Build log tail:"
            Get-Content -LiteralPath $LogPath -Tail 80 -ErrorAction SilentlyContinue |
                ForEach-Object { Write-Host "  $_" }
        } else {
            Write-Host "Build log missing: $LogPath"
        }
    } catch {
        Write-Host "::warning::Could not collect standalone player build diagnostics: $($_.Exception.Message)"
    }
    Write-Host "::endgroup::"
}

function Test-StandalonePlayerBuildOutput {
    param(
        [Parameter(Mandatory = $true)][string]$ExpectedExe,
        [Parameter(Mandatory = $true)][datetime]$BuildStartedUtc
    )

    if (-not (Test-Path -LiteralPath $ExpectedExe -PathType Leaf)) {
        return "missing exe"
    }

    $exe = Get-Item -LiteralPath $ExpectedExe
    if ($exe.LastWriteTimeUtc -lt $BuildStartedUtc.AddSeconds(-5)) {
        return "stale exe; LastWriteTimeUtc=$($exe.LastWriteTimeUtc.ToString('o'))"
    }

    $dataDir = Join-Path (Split-Path -Parent $ExpectedExe) ("{0}_Data" -f [System.IO.Path]::GetFileNameWithoutExtension($ExpectedExe))
    if (-not (Test-Path -LiteralPath $dataDir -PathType Container)) {
        return "missing player data directory: $dataDir"
    }

    return ''
}

function Test-NUnitResults {
    # The NUnit results.xml is the SOLE source of truth for editmode/playmode and
    # the standalone player run. $UnityExitCode is the process exit code of the
    # editor/player that produced the file; it is ADVISORY only -- a valid passing
    # results.xml means the run succeeded EVEN IF the process then exited non-zero
    # (a benign background-thread shutdown-race crash after RunFinished already
    # wrote the file). A missing/invalid/failing file still fails loudly, and the
    # exit code is folded into the diagnostics so a crash-before-results is named.
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Label,
        [string]$LogPath,
        [string]$Project,
        [int]$UnityExitCode = 0
    )

    $exitNote = if ($UnityExitCode -ne 0) {
        " Unity exited $UnityExitCode / $(Get-NativeExitCodeDescription -ExitCode $UnityExitCode) (the results FILE, not the exit code, is the source of truth)."
    } else {
        ''
    }

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Write-CiError "No NUnit results XML exists at $Path for $Label.$exitNote"
        Write-UnityResultFailureDiagnostics -LogPath $LogPath -Project $Project -Label $Label
        throw "Unity did not produce NUnit results for $Label.$exitNote"
    }

    [xml]$xml = Get-Content -LiteralPath $Path -Raw
    $run = $xml.SelectSingleNode('//test-run')
    if (-not $run) {
        Write-CiError "NUnit results at $Path do not contain a <test-run> element.$exitNote"
        Write-UnityResultFailureDiagnostics -LogPath $LogPath -Project $Project -Label $Label
        throw "Invalid NUnit results for $Label."
    }

    $total = [int]$run.total
    $passed = [int]$run.passed
    $failed = [int]$run.failed
    $skipped = [int]$run.skipped

    Write-Host "Results: total=$total passed=$passed failed=$failed skipped=$skipped"
    if ($total -lt 1) {
        Write-CiError "0 tests ran for $Label -- check assembly selection and package testables.$exitNote"
        throw "0 tests ran for $Label."
    }
    if ($failed -gt 0) {
        # Enumerate WHICH tests failed (fullname + message + stack) BEFORE the
        # throw so the operator sees the actionable detail, not just the count.
        # Best-effort inside the helper's own try/catch -- it never masks the
        # real failure below. ($exitNote is intentionally omitted here: when tests
        # genuinely failed, the named failing tests ARE the actionable signal and
        # the producing process's exit code is noise. The exit note is folded into
        # the missing-file / invalid / zero-test branches above, where the exit
        # code IS the most informative remaining clue.)
        Write-UnityFailedTestAnnotations -Xml $xml -Label $Label
        Write-CiError "$failed tests failed for $Label."
        throw "$failed tests failed for $Label."
    }

    # PASS. If the producing process exited non-zero despite the valid passing
    # file, narrate the benign post-work shutdown crash (and KEEP it green).
    if ($UnityExitCode -ne 0) {
        Write-UnityBenignExitWarning -Label $Label -ExitCode $UnityExitCode -LogPath $LogPath
    }
    Write-CiNotice "${Label}: total=$total passed=$passed failed=$failed skipped=$skipped"
}

# Run the UhCiTestConfigurator.Apply configure pass (a SEPARATE -executeMethod
# editor invocation) and validate the success marker it writes as its final action.
# The CONFIGURED PROJECT -- proven by a FRESH marker -- is the source of truth, NOT
# Unity's process exit code: Unity can crash in a background thread during shutdown
# AFTER Apply() fully completes (e.g. the DirectoryMonitor file-watcher faulting,
# returning 0xC0000005) for a configure that actually succeeded; a MISSING marker is
# a real failure that throws with the usual diagnostics. Apply() also persists any
# UH_ADDITIONAL_SCRIPTING_DEFINES onto the Standalone group's scripting defines and
# saves the project, so a SUBSEQUENT -runTests invocation loads them from startup
# (Unity does not recompile mid-run in -batchmode). Shared by the standalone path
# (always) and the editmode/playmode path (only when extra defines are requested).
function Invoke-UnityConfigurePass {
    param(
        [Parameter(Mandatory = $true)][string]$EditorPath,
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$MarkerPath,
        [Parameter(Mandatory = $true)][string]$LogPath,
        [Parameter(Mandatory = $true)][string]$Label,
        [string[]]$ExtraArguments = @()
    )

    if (Test-Path -LiteralPath $MarkerPath -PathType Leaf) {
        Remove-Item -LiteralPath $MarkerPath -Force
    }
    $env:UH_CONFIGURE_MARKER_PATH = $MarkerPath
    $configureStartedUtc = [DateTime]::UtcNow
    $configureArgs = @(
        '-quit',
        '-batchmode',
        '-nographics',
        '-projectPath', $ProjectPath,
        '-buildTarget', 'StandaloneWindows64',
        '-executeMethod', 'UhCiTestConfigurator.Apply',
        '-logFile', '-'
    ) + @($ExtraArguments)
    $configureExit = Invoke-UnityEditor `
        -EditorPath $EditorPath `
        -Arguments $configureArgs `
        -Label $Label `
        -LogPath $LogPath
    # The configurator has run; drop the marker-path env var so it cannot be
    # inherited by later child processes (only Apply reads it).
    Remove-Item -LiteralPath Env:\UH_CONFIGURE_MARKER_PATH -ErrorAction SilentlyContinue
    $configureProblem = Test-UnityConfigureMarker -MarkerPath $MarkerPath -StartedUtc $configureStartedUtc
    if (-not [string]::IsNullOrWhiteSpace($configureProblem)) {
        Write-UnityRunFailureDiagnostics `
            -Project $ProjectPath `
            -LogPath $LogPath `
            -CscLabel $Label `
            -DiagnosticsLabel $Label
        throw "$Label failed ($configureProblem; Unity exit code $configureExit / $(Get-NativeExitCodeDescription -ExitCode $configureExit)). See the streamed Unity log above (also saved to $LogPath)."
    }
    if ($configureExit -ne 0) {
        Write-UnityBenignExitWarning -Label $Label -ExitCode $configureExit -LogPath $LogPath
    }
    Write-AnalyzerSetupDiagnostics -Project $ProjectPath -LogPath $LogPath -Label $Label
}

$RepoRoot = Resolve-FullPath -Path $RepoRoot
Assert-RepoRoot -Path $RepoRoot
$ArtifactsPath = Resolve-FullPath -Path $ArtifactsPath
New-Item -ItemType Directory -Force -Path $ArtifactsPath | Out-Null

Initialize-UnityCacheEnvironment -Root $RepoRoot -Version $UnityVersion

# Release is now the repo-wide Unity CI contract. The historical switches remain
# accepted for workflow/back-compat, but the effective mode is always Release:
# editor/test compilations get -releaseCodeOptimization, and standalone generated
# players omit BuildOptions.Development.
$UseReleaseCodeOptimization = $true
$UseReleasePlayerBuild = $true

# Normalize the requested extra scripting defines to a clean, de-duplicated,
# semicolon-joined string ONCE here. The configurator C# reads this exact value
# from UH_ADDITIONAL_SCRIPTING_DEFINES; computing it up front keeps the env var,
# the diagnostic logging, and the configure-pass dispatch decision all consistent.
$AdditionalScriptingDefinesList = @(
    @($AdditionalScriptingDefines) |
        ForEach-Object { if ($null -ne $_) { ([string]$_).Trim() } } |
        Where-Object { $_ -and $_.Length -gt 0 } |
        Select-Object -Unique
)
$AdditionalScriptingDefinesJoined = ($AdditionalScriptingDefinesList -join ';')

$ProjectPath = Initialize-EphemeralProject -Root $RepoRoot -Version $UnityVersion -Mode $TestMode -Path $ProjectPath -IncludeComparisons:$IncludeComparisons -IncludeIntegrations:$IncludeIntegrations -Backend $StandaloneScriptingBackend -DevelopmentBuild:(-not $UseReleasePlayerBuild) -RepoRoot $RepoRoot
$LibraryPath = Join-Path $ProjectPath 'Library'
New-Item -ItemType Directory -Force -Path $LibraryPath | Out-Null

Write-Host "::group::Ephemeral Unity project"
Write-Host "RepoRoot: $RepoRoot"
Write-Host "ProjectPath: $ProjectPath"
Write-Host "LibraryPath: $LibraryPath"
Write-Host "ArtifactsPath: $ArtifactsPath"
Write-Host "IncludeComparisons: $IncludeComparisons"
Write-Host "IncludeIntegrations: $IncludeIntegrations"
Write-Host "AdditionalScriptingDefines: $AdditionalScriptingDefinesJoined"
Write-Host "StandaloneScriptingBackend: $StandaloneScriptingBackend"
Write-Host "ReleasePlayerBuild: $UseReleasePlayerBuild"
Write-Host "ReleaseCodeOptimization: $UseReleaseCodeOptimization"
Write-Host "Manifest:"
Get-Content -LiteralPath (Join-Path $ProjectPath 'Packages\manifest.json')
Write-Host "Pre-created analyzer copy (Assets/Plugins/Editor/WallstopStudios.UnityHelpers):"
$analyzerCopyDir = Join-Path $ProjectPath 'Assets\Plugins\Editor\WallstopStudios.UnityHelpers'
if (Test-Path -LiteralPath $analyzerCopyDir -PathType Container) {
    Get-ChildItem -LiteralPath $analyzerCopyDir -File |
        Select-Object -ExpandProperty Name |
        Sort-Object |
        ForEach-Object { Write-Host "  $_" }
} else {
    Write-Host "  (missing)"
}
Write-Host "::endgroup::"

if ($GenerateOnly) {
    Write-CiNotice "Generated ephemeral Unity project only: $ProjectPath"
    exit 0
}

if (-not $UnityEditorPath -or $UnityEditorPath.Trim().Length -eq 0) {
    $ensureEditor = Join-Path $PSScriptRoot 'ensure-editor.ps1'
    $provisioningProfile = if ($TestMode -eq 'standalone') { 'StandaloneWindowsIl2Cpp' } else { 'EditorOnly' }
    $ensureArgs = @{
        UnityVersion         = $UnityVersion
        InstallRoot          = $UnityInstallRoot
        ProvisioningProfile = $provisioningProfile
    }
    if ($env:GITHUB_ACTIONS -eq 'true') {
        $ensureArgs.RequireHealthyExisting = $true
    }
    $UnityEditorPath = (& $ensureEditor @ensureArgs | Select-Object -Last 1)
}

if (-not (Test-Path -LiteralPath $UnityEditorPath -PathType Leaf)) {
    throw "Unity editor not found: $UnityEditorPath"
}

# Export the resolved editor path so a workflow if:always() step (which runs in a
# SEPARATE process after this one exits) can run `Unity.exe -returnlicense` to
# return the seat as defense-in-depth.
if ($env:GITHUB_ENV) {
    Add-Content -LiteralPath $env:GITHUB_ENV -Value "UNITY_EDITOR_PATH=$UnityEditorPath"
}

# Classic SERIAL activation: the paid seat is activated from UNITY_SERIAL +
# UNITY_EMAIL + UNITY_PASSWORD and explicitly returned on EVERY exit path so the
# seat is never leaked. All three credentials are required together; we test each
# with IsNullOrWhiteSpace so a blank-but-set secret counts as missing.
$hasLicenseCreds = (
    -not [string]::IsNullOrWhiteSpace($env:UNITY_SERIAL) -and
    -not [string]::IsNullOrWhiteSpace($env:UNITY_EMAIL) -and
    -not [string]::IsNullOrWhiteSpace($env:UNITY_PASSWORD)
)
# In CI all three credentials are MANDATORY: a missing one means the editor would
# launch unlicensed and fail opaquely. The error names the missing VARS (never
# their values). Locally, missing creds is fine -- we assume the machine is
# already licensed (Hub sign-in / a local .ulf) and simply skip activate/return.
if ($env:GITHUB_ACTIONS -eq 'true' -and -not $hasLicenseCreds) {
    $missing = @()
    if ([string]::IsNullOrWhiteSpace($env:UNITY_SERIAL)) { $missing += 'UNITY_SERIAL' }
    if ([string]::IsNullOrWhiteSpace($env:UNITY_EMAIL)) { $missing += 'UNITY_EMAIL' }
    if ([string]::IsNullOrWhiteSpace($env:UNITY_PASSWORD)) { $missing += 'UNITY_PASSWORD' }
    throw "Serial Unity activation requires UNITY_SERIAL, UNITY_EMAIL, and UNITY_PASSWORD in CI. Missing or empty: $($missing -join ', ')."
}

# Array-wrap the capture so it is ALWAYS an array under Set-StrictMode -Version
# Latest. Get-AcceleratorArguments `return @()` on its empty path emits ZERO
# objects, so a bare `$x = Get-Foo` assigns AutomationNull (the empty array
# unwraps to nothing). Then reading `$x.Count` THROWS "property 'Count' cannot be
# found on this object" under StrictMode 2.0+ (verified on pwsh 7.6.1). @(...)
# forces Count 0 when empty so the read is safe. (The later `... + $x` concat was
# fine either way: `+` DROPS the empty/AutomationNull capture rather than adding
# it -- only a LITERAL $null operand would add a spurious element.)
$acceleratorArgs = @(Get-AcceleratorArguments -Endpoint $env:UNITY_ACCELERATOR_ENDPOINT -Version $UnityVersion -Mode $TestMode)
if ($acceleratorArgs.Count -gt 0) {
    Write-CiNotice "Unity Accelerator enabled for namespace unity-helpers-$UnityVersion-$TestMode (endpoint normalized at the script boundary; value masked)."
} else {
    Write-CiNotice "Unity Accelerator disabled; UNITY_ACCELERATOR_ENDPOINT is unset."
}

$testPlatform = switch ($TestMode) {
    'editmode' { 'EditMode' }
    'playmode' { 'PlayMode' }
    'standalone' { 'StandaloneWindows64' }
}

$categoryArgs = @()
if (-not [string]::IsNullOrWhiteSpace($TestCategory)) {
    $categoryArgs = @('-testCategory', $TestCategory)
    Write-CiNotice "Unity test category filter enabled: $TestCategory"
} else {
    Write-CiNotice "Unity test category filter disabled."
}

$resultsPath = Join-Path $ArtifactsPath 'results.xml'
$logPath = Join-Path $ArtifactsPath 'unity.log'
$configureLogPath = Join-Path $ArtifactsPath 'configure.log'
$startupProbeLogPath = Join-Path $ArtifactsPath 'unity-startup-probe.log'
# The standalone-configure SUCCESS MARKER: UhCiTestConfigurator.Apply writes it
# as its final action (path handed in via UH_CONFIGURE_MARKER_PATH). A fresh
# marker is the source of truth that the configure pass completed -- even if Unity
# then crashed in a background thread during shutdown and returned a crash exit
# code -- so we never fail a successful configure on a benign teardown crash.
$configureMarkerPath = Join-Path $ArtifactsPath 'configure-complete.marker'

# STANDALONE split-build artifacts. The built IL2CPP player goes under a stable
# per-run project Build directory, not project Temp: Unity's test player build
# pipeline can populate Temp/PlayerWithTests or copy through Temp and then clean
# it before this script's post-build assertion runs. The player still stays out
# of $ArtifactsPath because a full IL2CPP player is hundreds of MB; only the
# small player log and NUnit XML are uploaded.
$standaloneExe = Join-Path $ProjectPath 'Build\UhTestPlayer\UhTestPlayer.exe'
$playerLogPath = Join-Path $ArtifactsPath 'player.log'

# Hand the requested global scripting defines to the configurator C#
# (UhCiTestConfigurator.ApplyAdditionalScriptingDefines reads this env var). Set it
# unconditionally -- empty when none requested, in which case the configurator's
# define injection is a no-op -- so the SAME env var feeds both the standalone
# configure pass (already run for every standalone leg) and the editmode/playmode
# configure pass dispatched below. The configurator unions these onto the Standalone
# group's defines, which apply to ALL assemblies (asmdef assemblies included).
$env:UH_ADDITIONAL_SCRIPTING_DEFINES = $AdditionalScriptingDefinesJoined
if ($AdditionalScriptingDefinesList.Count -gt 0) {
    Write-CiNotice "Global scripting defines requested for asmdef compilation: $AdditionalScriptingDefinesJoined"
}

# Activation/return carry the serial/email/password in their argument arrays and
# Unity may echo account/serial fragments into the activation log, so these logs
# MUST NOT live under $ArtifactsPath (the workflow uploads that as an artifact and
# the credentials would leak). Write them to a NON-uploaded temp dir instead.
$licenseLogDir = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [System.IO.Path]::GetTempPath() }
$activateLogPath = Join-Path $licenseLogDir "unity-activate-$UnityVersion-$TestMode.log"
$returnLogPath = Join-Path $licenseLogDir "unity-return-$UnityVersion-$TestMode.log"

# Return-at-start (defense-in-depth): reclaim a seat that a PRIOR force-killed run
# on this persistent self-hosted runner may have leaked before its own finally /
# the workflow if:always() step could run. Best-effort and never throws; if no
# seat is held this is a harmless no-op. Done BEFORE the activate so we start each
# run from a clean licensing state.
if ($hasLicenseCreds) {
    Invoke-UnityLicenseReturn -EditorPath $UnityEditorPath -Email $env:UNITY_EMAIL -Password $env:UNITY_PASSWORD -LogPath $returnLogPath
}

try {
    Invoke-UnityNativeStartupProbe -EditorPath $UnityEditorPath -LogPath $startupProbeLogPath

    # Activate the paid seat BEFORE configure/run so the test editor launches
    # licensed. Activation THROWS on failure (caught by this try's finally, which
    # still returns the seat). Skipped locally when creds are absent (the machine
    # is assumed already licensed).
    if ($hasLicenseCreds) {
        Invoke-UnityLicenseActivate -EditorPath $UnityEditorPath -Serial $env:UNITY_SERIAL -Email $env:UNITY_EMAIL -Password $env:UNITY_PASSWORD -LogPath $activateLogPath
    }

    if ($TestMode -eq 'standalone') {
        # CONFIGURE the standalone IL2CPP project (scripting backend/api/stripping,
        # Release code optimization, and any injected scripting defines). Marker-gated
        # via the shared Invoke-UnityConfigurePass.
        Invoke-UnityConfigurePass `
            -EditorPath $UnityEditorPath `
            -ProjectPath $ProjectPath `
            -MarkerPath $configureMarkerPath `
            -LogPath $configureLogPath `
            -Label 'Configure standalone IL2CPP project' `
            -ExtraArguments $acceleratorArgs
    } elseif ($AdditionalScriptingDefinesList.Count -gt 0) {
        # EDITMODE/PLAYMODE with extra global scripting defines (e.g. SINGLE_THREADED):
        # run the SAME configure pass FIRST so UhCiTestConfigurator.Apply persists the
        # defines onto the Standalone group and saves the project. The -runTests
        # invocation below then loads the project with the defines already in place,
        # so its first compile of the asmdef assemblies sees them (Unity does not
        # recompile mid-run in -batchmode). Without requested defines this pass is
        # skipped entirely, so the default editmode/playmode flow is unchanged (no
        # extra editor launch, byte-for-byte identical behavior).
        Invoke-UnityConfigurePass `
            -EditorPath $UnityEditorPath `
            -ProjectPath $ProjectPath `
            -MarkerPath $configureMarkerPath `
            -LogPath $configureLogPath `
            -Label "Configure $UnityVersion $TestMode scripting defines" `
            -ExtraArguments $acceleratorArgs
    }

    if ($TestMode -eq 'standalone') {
        # STANDALONE SPLIT BUILD + FILE-BASED RESULTS (zero PlayerConnection
        # dependency). The legacy `-runTests -testPlatform StandaloneWindows64` flow
        # had the built player stream NUnit results back to the editor over
        # PlayerConnection/TCP; on the self-hosted runners' multi-NIC networks the
        # player cannot reach the editor's listener (TcpProtobufClient errorcode
        # 10060) and the editor's run never completes, hanging the 120-minute step.
        # Instead we (2a) BUILD the player via the editor -- the generated
        # UhCiStandaloneBuildModifier clears AutoRunPlayer|ConnectToHost|
        # ConnectWithProfiler and IPostBuildCleanup exits the editor after the build
        # -- then (2b) RUN the built exe directly, where the generated
        # UhCiStandaloneTestCallback writes NUnit XML to -uhTestResults and quits,
        # then (2c) validate the FILE (the source of truth). Both 2a and 2b run under
        # the hard tree-kill watchdog so neither can hang to the step timeout.

        # (2a) BUILD. Set UH_PLAYER_BUILD_PATH so the modifier redirects the player
        # output to a known path under the project's Build dir, then build with
        # -runTests (so PlayerLauncher's ModifyBuildOptions reflection path fires) but
        # NO -quit (the editor must reach PostBuildCleanup, which arms the exit).
        $env:UH_PLAYER_BUILD_PATH = $standaloneExe
        $standaloneBuildStartedUtc = [DateTime]::UtcNow
        $standaloneExeDir = Split-Path -Parent $standaloneExe
        if ($standaloneExeDir -and (Test-Path -LiteralPath $standaloneExeDir -PathType Container)) {
            Remove-Item -LiteralPath $standaloneExeDir -Recurse -Force
        }
        if ($standaloneExeDir) {
            New-Item -ItemType Directory -Force -Path $standaloneExeDir | Out-Null
        }
        if (Test-Path -LiteralPath $playerLogPath -PathType Leaf) {
            Remove-Item -LiteralPath $playerLogPath -Force
        }
        $buildArgs = @(
            '-batchmode',
            '-nographics',
            '-projectPath', $ProjectPath,
            '-runTests',
            '-testPlatform', 'StandaloneWindows64',
            '-testResults', $resultsPath,
            '-assemblyNames', $AssemblyNames,
            '-releaseCodeOptimization',
            '-buildTarget', 'StandaloneWindows64',
            '-logFile', '-'
        ) + $categoryArgs + $acceleratorArgs

        $buildResult = Invoke-ProcessWithTreeKillTimeout `
            -FilePath $UnityEditorPath `
            -Arguments $buildArgs `
            -TimeoutSeconds (Get-StandaloneBuildTimeoutSeconds) `
            -LogPath $logPath `
            -Label "Build standalone IL2CPP test player (Unity $UnityVersion)"

        # POST-BUILD ASSERT (the BUILT PLAYER EXE is the source of truth): the exe
        # MUST exist at UH_PLAYER_BUILD_PATH, be fresh for this build, and include
        # its companion _Data directory. A non-zero build exit code OR a watchdog
        # tree-kill is fatal ONLY when the exe is missing/stale/incomplete: Unity can
        # crash in a background thread during shutdown AFTER the player is fully
        # built, or defer Application.Quit in -batchmode IL2CPP (the watchdog then
        # tree-kills an already-finished build). Validating the exe FIRST -- before
        # consulting the exit code -- keeps those benign post-build crashes from
        # turning a good build red, while a genuinely failed build (which leaves no
        # fresh, complete exe) still fails loudly with full diagnostics.
        $standaloneBuildProblem = Test-StandalonePlayerBuildOutput `
            -ExpectedExe $standaloneExe `
            -BuildStartedUtc $standaloneBuildStartedUtc
        if (-not [string]::IsNullOrWhiteSpace($standaloneBuildProblem)) {
            Write-UnityRunFailureDiagnostics `
                -Project $ProjectPath `
                -LogPath $logPath `
                -CscLabel "$UnityVersion standalone build" `
                -DiagnosticsLabel "Unity $UnityVersion standalone build"
            Write-StandaloneBuildOutputDiagnostics `
                -Project $ProjectPath `
                -ExpectedExe $standaloneExe `
                -LogPath $logPath `
                -BuildStartedUtc $standaloneBuildStartedUtc
            if ($buildResult.TimedOut) {
                throw "Standalone test-player build timed out and the process tree was killed before producing a valid player at $standaloneExe ($standaloneBuildProblem). Raise the limit via UH_STANDALONE_BUILD_TIMEOUT_SECONDS (0 disables the timeout). See the build log at $logPath."
            }
            throw "Editor build produced invalid unity-helpers test player output at $standaloneExe ($standaloneBuildProblem; build exit code $($buildResult.ExitCode) / $(Get-NativeExitCodeDescription -ExitCode $buildResult.ExitCode)). The build modifier may not have run, Unity may have cleaned a Temp output, or a stale player was detected. See the build log at $logPath."
        }
        # The exe is valid. If the build process nonetheless exited non-zero or was
        # tree-killed, narrate the benign post-build shutdown crash and keep going.
        if ($buildResult.TimedOut -or $buildResult.ExitCode -ne 0) {
            Write-UnityBenignExitWarning -Label "Build standalone IL2CPP test player (Unity $UnityVersion)" -ExitCode $buildResult.ExitCode -TimedOut:$buildResult.TimedOut -LogPath $logPath
        }

        # MISSED-CASE GUARD: even when the exe exists, scan the build log for the
        # signatures of a NON-redirected AutoRun build (PlayerWithTests /
        # AutoRunPlayer = True). If present, the modifier did not fully take and a
        # live run may still attempt the 10060 dial-out -- surface a ::warning::.
        if (Test-Path -LiteralPath $logPath -PathType Leaf) {
            $buildLogText = Get-Content -LiteralPath $logPath -Raw
            if ($buildLogText -match 'PlayerWithTests' -or $buildLogText -match 'options\.AutoRunPlayer = True') {
                Write-Host "::warning::Standalone build log mentions PlayerWithTests / AutoRunPlayer = True; the UhCiStandaloneBuildModifier may not have fully suppressed the player auto-run. If the player run hangs on a TcpProtobufClient 10060, verify the modifier compiled."
            }
        }

        # Delete any STALE results file before the player runs, so the
        # timeout-honors-file branch below can only honor results THIS player run
        # wrote -- never a prior run's leftover. (Defensive for local re-runs against
        # the same -ArtifactsPath; CI checkout already cleans the gitignored
        # .artifacts tree per job.)
        if (Test-Path -LiteralPath $resultsPath -PathType Leaf) {
            Remove-Item -LiteralPath $resultsPath -Force
        }

        # (2b) RUN the built exe directly (no PlayerConnection), under the watchdog.
        $playerTimeoutSeconds = Get-StandaloneTestPlayerTimeoutSeconds
        $playerResult = Invoke-StandaloneTestPlayer `
            -EditorBuiltExePath $standaloneExe `
            -ResultsPath $resultsPath `
            -LogPath $playerLogPath `
            -TimeoutSeconds $playerTimeoutSeconds

        # A watchdog timeout is fatal ONLY when the player wrote no results. If the
        # results file exists, honor it as the source of truth (Application.Quit can be
        # deferred in -batchmode -nographics IL2CPP after RunFinished already wrote the
        # file) and fall through to Test-NUnitResults; otherwise fail with the timeout.
        $playerExitForValidation = $playerResult.ExitCode
        if ($playerResult.TimedOut) {
            if (Test-Path -LiteralPath $resultsPath -PathType Leaf) {
                Write-Host "::warning::Standalone test player exceeded the ${playerTimeoutSeconds}s watchdog and was tree-killed, but it had already written $resultsPath; honoring that results file as the source of truth (Application.Quit was likely deferred in -batchmode IL2CPP). Raise UH_STANDALONE_PLAYER_TIMEOUT_SECONDS if this recurs."
                # The inline timeout warning above is the single, correctly-phrased
                # notice for this case; pass exit 0 to the validator so it does NOT
                # re-warn and MISLABEL the watchdog timeout (sentinel 124) as a
                # native shutdown crash.
                $playerExitForValidation = 0
            } else {
                throw "Standalone test player timed out after $playerTimeoutSeconds second(s) and was tree-killed before writing any results to $resultsPath. Raise the limit via UH_STANDALONE_PLAYER_TIMEOUT_SECONDS (0 disables the timeout). See the player log at $playerLogPath."
            }
        }

        # (2c) VALIDATE the FILE (the source of truth). The player log carries the
        # diagnostics for a missing/empty file (its stdout no longer flows through
        # unity.log). The player exit code is advisory only: a valid passing file
        # with a non-zero player exit gets a benign-crash ::warning::, not a failure.
        Test-NUnitResults -Path $resultsPath -Label "Unity $UnityVersion standalone" -LogPath $playerLogPath -Project $ProjectPath -UnityExitCode $playerExitForValidation
    } else {
        # MUST NOT include '-quit' alongside '-runTests': per the Unity Editor manual
        # (https://docs.unity3d.com/Manual/EditorCommandLineArguments.html), if the
        # Editor is running tests with -runTests, -quit causes it to QUIT IMMEDIATELY
        # before in-progress tests can complete -- the editor exits 0 having written
        # no results.xml.
        $testArgs = @(
            '-batchmode',
            '-nographics',
            '-projectPath', $ProjectPath,
            '-runTests',
            '-testPlatform', $testPlatform,
            '-testResults', $resultsPath,
            '-assemblyNames', $AssemblyNames,
            '-releaseCodeOptimization',
            '-logFile', '-'
        )
        $testArgs = $testArgs + $categoryArgs + $acceleratorArgs

        # Delete any STALE results file first so the file validation below can only
        # honor results THIS run wrote (defensive for local re-runs; CI checkout
        # already cleans the gitignored .artifacts tree per job).
        if (Test-Path -LiteralPath $resultsPath -PathType Leaf) {
            Remove-Item -LiteralPath $resultsPath -Force
        }

        # Run the editor; capture (do NOT throw on) its exit code. The NUnit
        # results.xml is the source of truth: Test-NUnitResults fails loudly on a
        # missing/invalid/failing file AND folds the exit code into its diagnostics,
        # but PASSES a valid run that exited non-zero only because Unity crashed in a
        # background thread during shutdown AFTER RunFinished wrote the file.
        $runExit = Invoke-UnityEditorTestsWithPackageManagerRetry `
            -EditorPath $UnityEditorPath `
            -Arguments $testArgs `
            -Label "Run Unity $UnityVersion $TestMode tests" `
            -LogPath $logPath `
            -ResultsPath $resultsPath `
            -Project $ProjectPath
        Write-AnalyzerSetupDiagnostics -Project $ProjectPath -LogPath $logPath -Label "$UnityVersion $TestMode test compile"
        Test-NUnitResults -Path $resultsPath -Label "Unity $UnityVersion $TestMode" -LogPath $logPath -Project $ProjectPath -UnityExitCode $runExit
    }
} finally {
    # Deterministic RETURN of the seat on EVERY exit path (clean exit, throw, or a
    # kill that still unwinds this finally). The workflow if:always() step is the
    # additional backstop for a hard-killed process that never reaches this finally,
    # and the NEXT run's return-at-start reclaims anything still leaked. Best-effort
    # and never throws, so it cannot mask a real test failure.
    if ($hasLicenseCreds) {
        Invoke-UnityLicenseReturn -EditorPath $UnityEditorPath -Email $env:UNITY_EMAIL -Password $env:UNITY_PASSWORD -LogPath $returnLogPath
    }
}
