#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Fails when one [Conditional] method calls another [Conditional] method.

.DESCRIPTION
    [Conditional] is resolved against the preprocessor symbols of the assembly that *calls* the
    method, not the one that defines it. So when a [Conditional] method's body calls another
    [Conditional] method, that inner call is decided by how THIS package was compiled -- and a
    release build of the package empties the outer method even for a consumer whose own assembly
    defined the symbol. The outer method survives, does its work, and produces nothing.

    That failure is invisible: it compiles, it ships, and it only shows up as a missing log in
    somebody else's player build. The rule is to route through a non-conditional core instead
    (see WallstopStudiosLogger's Log*Core methods and Helpers.LogNotAssignedCore).

.NOTES
    Deliberately source-based rather than IL-based: the set of conditional methods is small and
    the check has to run on a plain ubuntu-latest without Unity or a compiled assembly.
#>
[CmdletBinding()]
param([switch]$VerboseOutput)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$scanRoots = @('Runtime', 'Editor')
$conditionalAttributePattern = '\[\s*(?:System\.Diagnostics\.)?Conditional\s*\('
$methodSignaturePattern = '^\s*(?:public|internal|protected|private)[^\r\n=;]*?\b(\w+)\s*(?:<[^>()]*>)?\s*\('

function Write-Info($message) {
    if ($VerboseOutput) { Write-Host "[lint-conditional-call-chains] $message" -ForegroundColor Cyan }
}

# Returns every method that carries at least one [Conditional] attribute, as
# @{ Name; File; Line; Body }. A method's body is taken from its opening brace to the matching
# close, tracked by depth so nested blocks do not end it early.
function Get-ConditionalMethods {
    param([Parameter(Mandatory = $true)][string[]]$Files)

    $found = @()
    foreach ($file in $Files) {
        [string[]]$lines = Get-Content -LiteralPath $file
        $relative = $file.Substring($repoRoot.Length).TrimStart('\', '/').Replace('\', '/')

        for ($i = 0; $i -lt $lines.Length; $i++) {
            if ($lines[$i] -notmatch $conditionalAttributePattern) {
                continue
            }

            # Walk past the remaining attributes to the signature.
            $signatureIndex = $i
            while (
                $signatureIndex -lt $lines.Length -and
                ($lines[$signatureIndex].Trim().StartsWith('[') -or $lines[$signatureIndex].Trim().Length -eq 0)
            ) {
                $signatureIndex++
            }

            if ($signatureIndex -ge $lines.Length -or $lines[$signatureIndex] -notmatch $methodSignaturePattern) {
                continue
            }

            $methodName = $Matches[1]

            # Skip to the body's opening brace, then capture until it closes.
            $bodyIndex = $signatureIndex
            while ($bodyIndex -lt $lines.Length -and $lines[$bodyIndex] -notmatch '\{') {
                $bodyIndex++
            }

            $body = ''
            $depth = 0
            $started = $false
            for ($j = $bodyIndex; $j -lt $lines.Length; $j++) {
                $line = $lines[$j]
                $depth += ([regex]::Matches($line, '\{')).Count
                $depth -= ([regex]::Matches($line, '\}')).Count
                if ($started) {
                    $body += $line + "`n"
                }
                $started = $true
                if ($depth -le 0) {
                    break
                }
            }

            $found += [pscustomobject]@{
                Name = $methodName
                File = $relative
                Line = $signatureIndex + 1
                Body = $body
            }
            $i = $signatureIndex
        }
    }

    return $found
}

$files = @()
foreach ($scanRoot in $scanRoots) {
    $rootPath = Join-Path $repoRoot $scanRoot
    if (-not (Test-Path -LiteralPath $rootPath)) {
        continue
    }
    $files += @(Get-ChildItem -LiteralPath $rootPath -Recurse -File -Filter '*.cs' | ForEach-Object { $_.FullName })
}

$conditionalMethods = @(Get-ConditionalMethods -Files ($files | Sort-Object))
if ($conditionalMethods.Count -eq 0) {
    Write-Host "[lint-conditional-call-chains] OK: no [Conditional] methods found." -ForegroundColor Green
    exit 0
}

$conditionalNames = [System.Collections.Generic.HashSet[string]]::new(
    [string[]]@($conditionalMethods | Select-Object -ExpandProperty Name -Unique),
    [System.StringComparer]::Ordinal
)

# Matching is by method name, so a call to an unrelated method that happens to share one has to
# be excluded explicitly. UnityEngine.Debug.LogError is the case that exists today: same name as
# this package's [Conditional] LogError extension, entirely unrelated, and correct where it is
# used. Receivers listed here are types outside this package, so a call through them can never be
# the chain this linter is looking for.
$externalReceivers = @(
    'Debug',
    'UnityEngine.Debug',
    'Assert',
    'Assert.IsTrue',
    'UnityEngine.Assertions.Assert',
    'Console',
    'Trace',
    'Debug.unityLogger'
)

$failed = $false
foreach ($method in $conditionalMethods) {
    $body = $method.Body
    foreach ($externalReceiver in $externalReceivers) {
        $body = $body -replace ("(?<![\w.])" + [regex]::Escape($externalReceiver) + "\s*\.\s*\w+\s*\("), 'ExternalCall('
    }

    foreach ($calleeName in $conditionalNames) {
        if ($calleeName -eq $method.Name) {
            continue
        }

        if ($body -match "(?<![\w.])$([regex]::Escape($calleeName))\s*\(|\.\s*$([regex]::Escape($calleeName))\s*\(") {
            Write-Host "::error file=$($method.File),line=$($method.Line)::[Conditional] method '$($method.Name)' calls [Conditional] method '$calleeName'. The inner call is resolved against THIS package's symbols, so a release build of the package empties '$($method.Name)' even for a consumer that defined the symbol. Route through a non-conditional core instead (see WallstopStudiosLogger.Log*Core)."
            $failed = $true
        }
    }
}

Write-Info "Checked $($conditionalMethods.Count) [Conditional] method(s): $(($conditionalMethods | Select-Object -ExpandProperty Name -Unique | Sort-Object) -join ', ')."

if ($failed) {
    exit 1
}

Write-Host "[lint-conditional-call-chains] OK: no [Conditional] method calls another." -ForegroundColor Green
