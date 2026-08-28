<#
.SYNOPSIS
    Validates a release tag against strict semver and this package's package.json metadata.

.DESCRIPTION
    Extracted from the release.yml verify-tag job so the tag contract is unit-testable
    (issue #360). The checks and their order match the embedded bash they replaced:

      1. The tag is non-empty.
      2. The tag is a single line (no CR/LF).
      3. The tag is strict, unprefixed X.Y.Z semver.
      4. The source ref is non-empty and a single line.
      5. package.json declares non-empty name and version.
      6. The tag equals the package.json version.

    On success the package name, package version, and tag are appended to
    $GITHUB_OUTPUT (when set) as package-name, package-version, and tag.

.PARAMETER Tag
    The release version/tag being published, e.g. 3.5.2.

.PARAMETER SourceRef
    The branch, tag, or commit the release builds from.

.PARAMETER PackageJsonPath
    Path to the package.json to validate against.

.EXAMPLE
    pwsh -NoProfile -File scripts/ci/verify-release-tag.ps1 -Tag "3.5.2" -SourceRef "main" -PackageJsonPath package.json
#>
[CmdletBinding(PositionalBinding = $false)]
Param(
    # AllowEmptyString keeps empty inputs binding so the explicit checks below can emit the exact
    # ::error:: messages the release workflow printed before this logic was extracted; a bare
    # Mandatory string would reject '' at bind time with a less actionable message.
    [Parameter(Mandatory = $true)]
    [AllowEmptyString()]
    [string]$Tag,
    [Parameter(Mandatory = $true)]
    [AllowEmptyString()]
    [string]$SourceRef,
    [Parameter(Mandatory = $true)][string]$PackageJsonPath,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$UnboundArguments
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# PositionalBinding = $false plus this sibling is what makes stray pwsh -File values land here
# instead of silently binding into a named parameter. See .llm/context.md PWS005 notes.
if ($UnboundArguments -and 0 -lt $UnboundArguments.Count) {
    [Console]::Error.WriteLine(
        "Unbound arguments: $($UnboundArguments -join ', '). Pass -Tag, -SourceRef, and " +
        "-PackageJsonPath explicitly."
    )
    exit 64
}

function Write-ReleaseTagError {
    param([Parameter(Mandatory = $true)][string]$Message)
    Write-Host "::error::$Message"
    exit 1
}

if (0 -eq $Tag.Length) {
    Write-ReleaseTagError 'Release version is required.'
}
if ($Tag -match "[`r`n]") {
    Write-ReleaseTagError 'Release version must be a single line.'
}
if ($Tag -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    Write-ReleaseTagError 'Release tags must use unprefixed X.Y.Z semver.'
}
if (0 -eq $SourceRef.Length) {
    Write-ReleaseTagError 'Release source ref is required.'
}
if ($SourceRef -match "[`r`n]") {
    Write-ReleaseTagError 'Release source ref must be a single line.'
}

if (-not (Test-Path -LiteralPath $PackageJsonPath -PathType Leaf)) {
    Write-ReleaseTagError 'package.json name/version is missing.'
}
$packageManifest = Get-Content -LiteralPath $PackageJsonPath -Raw | ConvertFrom-Json
$packageName = [string]$packageManifest.name
$packageVersion = [string]$packageManifest.version
if ([string]::IsNullOrEmpty($packageName) -or [string]::IsNullOrEmpty($packageVersion)) {
    Write-ReleaseTagError 'package.json name/version is missing.'
}
if ($Tag -ne $packageVersion) {
    Write-ReleaseTagError "Tag $Tag does not match package.json version $packageVersion."
}

if (-not [string]::IsNullOrEmpty($env:GITHUB_OUTPUT)) {
    $outputs = @(
        "package-name=$packageName",
        "package-version=$packageVersion",
        "tag=$Tag"
    )
    [System.IO.File]::AppendAllText(
        $env:GITHUB_OUTPUT,
        (($outputs -join "`n") + "`n"),
        (New-Object System.Text.UTF8Encoding($false))
    )
}

Write-Host "Release tag $Tag matches ${packageName}@${packageVersion}."
