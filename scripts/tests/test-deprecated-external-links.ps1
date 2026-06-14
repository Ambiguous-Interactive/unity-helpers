Param(
    [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:TestsPassed = 0
$script:TestsFailed = 0
$script:FailedTests = @()

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$currentScriptRelativePath = ((Resolve-Path $PSCommandPath).Path.Substring($repoRoot.Length)).TrimStart(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar
).Replace('\', '/')

if ([string]::IsNullOrWhiteSpace($currentScriptRelativePath)) {
    throw "Failed to resolve the current script path relative to the repository root."
}

function Write-Info {
    param([string]$Message)

    if ($VerboseOutput) {
        Write-Host "[test-deprecated-external-links] $Message" -ForegroundColor Cyan
    }
}

function Write-TestResult {
    param(
        [string]$TestName,
        [bool]$Passed,
        [string]$Message = ''
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

function Find-TrackedMatches {
    param(
        [string]$Url,
        [string[]]$ExcludePaths = @()
    )

    Push-Location $repoRoot
    try {
        $normalizedExcludePaths = @(
            $ExcludePaths |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                ForEach-Object { $_.Replace('\\', '/') } |
                Sort-Object -Unique
        )

        $matches = @(& git grep -n --full-name --fixed-strings -- $Url 2>$null)
        $exitCode = $LASTEXITCODE
    } finally {
        Pop-Location
    }

    if ($exitCode -eq 1) {
        return @()
    }

    if ($exitCode -ne 0) {
        throw "git grep failed for $Url with exit code $exitCode"
    }

    return @(
        $matches |
            Where-Object {
                $matchPath = ($_ -split ':', 2)[0]
                $normalizedExcludePaths -notcontains $matchPath
            }
    )
}

function Get-SearchExcludePaths {
    param([pscustomobject]$Case)

    $paths = @($currentScriptRelativePath)

    if ($Case.PSObject.Properties['SearchExcludePaths']) {
        $paths += @($Case.SearchExcludePaths)
    }

    return @(
        $paths |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Sort-Object -Unique
    )
}

function Format-MatchSummary {
    param([string[]]$Matches)

    $matchCount = $Matches.Count
    $fileCount = @(
        $Matches |
            ForEach-Object { ($_ -split ':', 2)[0] } |
            Sort-Object -Unique
    ).Count
    $matchPreview = ($Matches | Select-Object -First 5) -join '; '

    if ($matchCount -le 5) {
        return "$matchCount match(es) across $fileCount file(s): $matchPreview"
    }

    return "$matchCount match(es) across $fileCount file(s). First matches: $matchPreview"
}

Write-Host "Testing deprecated external links..." -ForegroundColor White
Write-Info "Ignoring rule-definition files during tracked searches: $currentScriptRelativePath"

$deprecatedLinkCases = @(
    [pscustomobject]@{
        Name = 'XorShiftPaperUsesDoi'
        DeprecatedUrl = 'https://www.jstatsoft.org/article/view/v008i14'
        ReplacementUrl = 'https://doi.org/10.18637/jss.v008.i14'
        Reason = 'The publisher URL times out intermittently in GitHub Actions; use the stable DOI resolver instead.'
    }
    [pscustomobject]@{
        Name = 'MitLicenseUsesCanonicalOpenSourceUrl'
        DeprecatedUrl = 'https://opensource.org/licenses/MIT'
        ReplacementUrl = 'https://opensource.org/license/MIT'
        Reason = 'Use the current opensource.org canonical MIT URL to avoid redirects.'
    }
    # PR references must use /pull/N, not /issues/N. These numbers are pull
    # requests; the issues/N -> pull/N redirect does NOT fire through the
    # wallstop -> Ambiguous-Interactive org-transfer redirect, so the /issues/
    # form 404s for a reader while the /pull/ form resolves. Guarding the exact
    # known-bad URLs keeps them from creeping back into docs or source comments.
    [pscustomobject]@{
        Name = 'Pr234UsesPullPath'
        DeprecatedUrl = 'https://github.com/wallstop/unity-helpers/issues/234'
        ReplacementUrl = 'https://github.com/wallstop/unity-helpers/pull/234'
        Reason = 'PR #234: /issues/234 404s through the org-transfer redirect; use the /pull/ path.'
    }
    [pscustomobject]@{
        Name = 'Pr209UsesPullPath'
        DeprecatedUrl = 'https://github.com/wallstop/unity-helpers/issues/209'
        ReplacementUrl = 'https://github.com/wallstop/unity-helpers/pull/209'
        Reason = 'PR #209: /issues/209 404s through the org-transfer redirect; use the /pull/ path.'
    }
    [pscustomobject]@{
        Name = 'Pr175UsesPullPath'
        DeprecatedUrl = 'https://github.com/wallstop/unity-helpers/issues/175'
        ReplacementUrl = 'https://github.com/wallstop/unity-helpers/pull/175'
        Reason = 'PR #175: /issues/175 404s through the org-transfer redirect; use the /pull/ path.'
    }
    [pscustomobject]@{
        Name = 'UnityRandomArticleUsesScriptReference'
        # Unity removed this blog article on BOTH the old (blog.unity.com) and new
        # (unity.com/blog) domains -- every variant now 404s. Steer authors to the
        # stable UnityEngine.Random scripting reference, which is the authoritative
        # live source for the type the perf table cites. Both dead variants are
        # listed so either one is caught.
        DeprecatedUrl = 'https://unity.com/blog/technology/random-numbers-on-the-gpu'
        ReplacementUrl = 'https://docs.unity3d.com/ScriptReference/Random.html'
        Reason = 'The Unity blog article was removed (all domain variants 404); link the stable UnityEngine.Random scripting reference instead.'
    }
    [pscustomobject]@{
        Name = 'UnityRandomArticleOldBlogDomainUsesScriptReference'
        DeprecatedUrl = 'https://blog.unity.com/technology/random-numbers-on-the-gpu'
        ReplacementUrl = 'https://docs.unity3d.com/ScriptReference/Random.html'
        Reason = 'The Unity blog article was removed (all domain variants 404); link the stable UnityEngine.Random scripting reference instead.'
    }
    [pscustomobject]@{
        Name = 'ManagedCodeStrippingUsesCanonicalManualPath'
        DeprecatedUrl = 'https://docs.unity3d.com/Manual/ManagedCodeStripping.html'
        ReplacementUrl = 'https://docs.unity3d.com/Manual/managed-code-stripping.html'
        Reason = 'Use the current Unity Manual slug to avoid redirects.'
    }
    [pscustomobject]@{
        Name = 'UnityForumUsesCurrentDiscussionsUrl'
        DeprecatedUrl = 'https://forum.unity.com/'
        ReplacementUrl = 'https://discussions.unity.com/'
        Reason = 'Unity Forum now redirects to Unity Discussions.'
    }
    [pscustomobject]@{
        Name = 'CodespacesSecretsUsesCurrentDocsPath'
        DeprecatedUrl = 'https://docs.github.com/en/codespaces/managing-your-codespaces/managing-encrypted-secrets-for-your-codespaces'
        ReplacementUrl = 'https://docs.github.com/en/codespaces/managing-your-codespaces/managing-your-account-specific-secrets-for-github-codespaces'
        Reason = 'Use the current GitHub Docs path to avoid redirects.'
    }
    [pscustomobject]@{
        Name = 'UnityLtsBadgeUsesStableReleaseHub'
        DeprecatedUrl = 'https://unity.com/releases/2021-lts'
        ReplacementUrl = 'https://docs.unity3d.com/2021.3/Documentation/Manual/UnityManual.html'
        Reason = 'Use the versioned Unity 2021.3 manual landing page instead of the Unity marketing redirect.'
    }
    [pscustomobject]@{
        Name = 'ExtenjectUsesCurrentZenjectRepository'
        DeprecatedUrl = 'https://github.com/svermeulen/Extenject'
        ReplacementUrl = 'https://github.com/modesttree/Zenject'
        Reason = 'The legacy Extenject repository now redirects to the maintained Zenject repository.'
    }
    [pscustomobject]@{
        Name = 'ProtobufNetAvoidsBrokenIl2CppFragment'
        DeprecatedUrl = 'https://github.com/protobuf-net/protobuf-net#il2cpp'
        ReplacementUrl = 'https://protobuf-net.github.io/protobuf-net/'
        Reason = 'The upstream repository no longer exposes an IL2CPP fragment; use the stable documentation landing page instead.'
    }
)

foreach ($case in $deprecatedLinkCases) {
    $excludePaths = @(Get-SearchExcludePaths -Case $case)
    Write-Info "Scanning for deprecated URL: $($case.DeprecatedUrl) (excluding: $($excludePaths -join ', '))"
    $matches = @(Find-TrackedMatches -Url $case.DeprecatedUrl -ExcludePaths $excludePaths)

    if ($matches.Count -eq 0) {
        Write-TestResult -TestName $case.Name -Passed $true
        continue
    }

    $message = "$($case.Reason) Replace with $($case.ReplacementUrl). $(Format-MatchSummary -Matches $matches)"
    Write-TestResult -TestName $case.Name -Passed $false -Message $message
}

Write-Host ''
Write-Host "Tests passed: $script:TestsPassed" -ForegroundColor Green
Write-Host "Tests failed: $script:TestsFailed" -ForegroundColor $(if ($script:TestsFailed -gt 0) { 'Red' } else { 'Green' })

if ($script:FailedTests.Count -gt 0) {
    Write-Host "Failed tests: $($script:FailedTests -join ', ')" -ForegroundColor Yellow
    exit 1
}

exit 0
