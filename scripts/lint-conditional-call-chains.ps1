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
$scanRoots = @('Runtime', 'Editor', 'Styles')
$conditionalAttributePattern = '\[\s*(?:System\.Diagnostics\.)?Conditional\s*\('
$methodSignaturePattern = '^\s*(?:public|internal|protected|private)[^\r\n=;]*?\b(\w+)\s*(?:<[^>()]*>)?\s*\('

function Write-Info($message) {
    if ($VerboseOutput) { Write-Host "[lint-conditional-call-chains] $message" -ForegroundColor Cyan }
}

# Returns every method that carries at least one [Conditional] attribute, as
# @{ Name; File; Line; Body }.
#
# Scans SIGNATURES and looks backwards for the attribute, rather than scanning attributes and
# walking forwards. Walking forwards has to guess what may sit between the attribute and the
# signature, and anything it fails to anticipate -- a comment, a multi-line attribute argument
# list -- drops the method with no signal. For a linter whose whole purpose is catching an
# invisible failure, under-covering silently is the worst outcome available.
function Get-ConditionalMethods {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Files,
        [Parameter(Mandatory = $true)][ref]$UnparsedCount
    )

    $found = @()
    foreach ($file in $Files) {
        [string[]]$lines = [System.IO.File]::ReadAllLines($file)
        $relative = $file.Substring($repoRoot.Length).TrimStart('\', '/').Replace('\', '/')

        for ($i = 0; $i -lt $lines.Length; $i++) {
            if ($lines[$i] -notmatch $methodSignaturePattern) {
                continue
            }

            $methodName = $Matches[1]

            # Walk backwards over the attribute/comment block immediately above the signature,
            # accumulating it and testing the WHOLE block. Testing line by line misses an
            # attribute whose argument list spans lines. Square-bracket debt (']' seen minus '['
            # seen, walking up) says whether we are still inside an attribute; only once it is
            # settled does a non-attribute line end the block.
            $isConditional = $false
            $attributeBlock = ''
            $bracketDebt = 0
            for ($back = $i - 1; $back -ge 0; $back--) {
                $candidate = $lines[$back].Trim()
                if ($candidate.Length -eq 0) {
                    continue
                }
                if ($candidate.StartsWith('//') -or $candidate.StartsWith('*') -or $candidate.StartsWith('/*')) {
                    continue
                }

                $bracketDebt += ([regex]::Matches($candidate, '\]')).Count
                $bracketDebt -= ([regex]::Matches($candidate, '\[')).Count
                $attributeBlock = $candidate + "`n" + $attributeBlock

                if ($attributeBlock -match $conditionalAttributePattern) {
                    $isConditional = $true
                    break
                }

                if ($bracketDebt -le 0 -and -not $candidate.StartsWith('[')) {
                    break
                }
            }

            if (-not $isConditional) {
                continue
            }

            # Expression-bodied member: the body is the rest of the statement.
            $signatureTail = $lines[$i]
            $arrowIndex = $signatureTail.IndexOf('=>', [StringComparison]::Ordinal)
            $braceIndex = $signatureTail.IndexOf('{', [StringComparison]::Ordinal)
            if ($arrowIndex -ge 0 -and ($braceIndex -lt 0 -or $arrowIndex -lt $braceIndex)) {
                $body = $signatureTail.Substring($arrowIndex + 2)
                $j = $i
                while ($body -notmatch ';' -and $j + 1 -lt $lines.Length) {
                    $j++
                    $body += "`n" + $lines[$j]
                }
                $found += [pscustomobject]@{
                    Name = $methodName
                    File = $relative
                    Line = $i + 1
                    Body = $body
                }
                continue
            }

            # Block body: from the opening brace to the matching close, by depth.
            $bodyIndex = $i
            while ($bodyIndex -lt $lines.Length -and $lines[$bodyIndex] -notmatch '\{') {
                if ($lines[$bodyIndex] -match ';\s*$' -and $bodyIndex -gt $i) {
                    # An abstract/partial/extern declaration has no body to inspect.
                    break
                }
                $bodyIndex++
            }

            if ($bodyIndex -ge $lines.Length -or $lines[$bodyIndex] -notmatch '\{') {
                $UnparsedCount.Value++
                Write-Host "::warning file=$relative,line=$($i + 1)::Could not locate a body for [Conditional] method '$methodName'; it was NOT checked for calls into other [Conditional] methods."
                continue
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
                Line = $i + 1
                Body = $body
            }
        }
    }

    return $found
}

$files = @()
foreach ($scanRoot in $scanRoots) {
    $rootPath = Join-Path $repoRoot $scanRoot
    if (-not (Test-Path -LiteralPath $rootPath)) {
        # Renaming a scan root used to remove it from the check silently (#556).
        Write-Host "[lint-conditional-call-chains] ERROR: scan root not found: $scanRoot. If it moved, update `$scanRoots in the same commit." -ForegroundColor Red
        exit 1
    }
    $files += @(Get-ChildItem -LiteralPath $rootPath -Recurse -File -Filter '*.cs' | ForEach-Object { $_.FullName })
}

$unparsed = 0
$conditionalMethods = @(Get-ConditionalMethods -Files @($files | Sort-Object) -UnparsedCount ([ref]$unparsed))
if ($conditionalMethods.Count -eq 0) {
    # This repository declares [Conditional] methods, so finding none means the scan stopped
    # working, not that the code is clean (#556).
    Write-Host "[lint-conditional-call-chains] ERROR: no [Conditional] methods found across $($files.Count) file(s). The scan matched nothing, so a pass here would mean nothing." -ForegroundColor Red
    exit 1
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
    'Math',
    'Mathf',
    'System.Math',
    'Debug',
    'UnityEngine.Debug',
    'Assert',
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

Write-Host "[lint-conditional-call-chains] OK: no [Conditional] method calls another ($($conditionalMethods.Count) checked, $unparsed unparsed)." -ForegroundColor Green
