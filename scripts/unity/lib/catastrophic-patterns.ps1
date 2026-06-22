#!/usr/bin/env pwsh
# Single source of truth for the Unity "catastrophic pattern" list.
#
# These regexes/substrings, when present in a Unity editor or player log, indicate a
# CATASTROPHIC failure (compile error, bad UPM manifest, headless coroutine stall, native
# Mono abort) rather than an ordinary test failure -- the editor typically exits before
# producing a usable NUnit results.xml, so they must be surfaced as the real cause.
#
# THREE call sites consume this list:
#   - scripts/unity/run-ci-tests.ps1                       ($script:CatastrophicPatterns)
#   - .github/actions/verify-unity-results/action.yml      ($patterns)
#   - .github/actions/dump-unity-log-tail/action.yml       ($patterns)
#
# They previously held byte-identical inline copies that DRIFTED (the `Package [id] cannot
# be found` entry once fell out of both action files) and tripped yamllint line-length (>200)
# on the long Label strings. Dot-source this file and call Get-CatastrophicPatterns instead
# of re-declaring the array. Enforced mechanically by
# scripts/tests/test-catastrophic-pattern-sync.ps1 (which now asserts single-sourcing, not
# textual equality).
#
# Each entry: @{ Label = <human-readable>; Pattern = <regex or substring>; UseSimple = <bool> }
#   UseSimple = $true  -> Select-String -SimpleMatch (literal substring)
#   UseSimple = $false -> Select-String regex

function Get-CatastrophicPatterns {
    [CmdletBinding()]
    param()

    return @(
        @{ Label = 'PrecompiledAssemblyException'; Pattern = 'PrecompiledAssemblyException'; UseSimple = $true }
        @{ Label = 'CompilationFailedException'; Pattern = 'CompilationFailedException'; UseSimple = $true }
        @{ Label = 'Multiple precompiled assemblies with the same name'; Pattern = 'Multiple precompiled assemblies with the same name'; UseSimple = $true }
        @{ Label = 'error CS\d+'; Pattern = 'error CS\d+'; UseSimple = $false }
        @{ Label = 'warning CS8032'; Pattern = 'warning CS8032'; UseSimple = $false }
        @{ Label = 'Package [id] cannot be found (bad/missing UPM manifest id)'; Pattern = 'Package \[[^\]]+\] cannot be found'; UseSimple = $false }
        @{ Label = 'WaitForEndOfFrame yielded under -batchmode (UnityTest hangs headless; writes total=0 results.xml)'; Pattern = 'WaitForEndOfFrame, which is not evoked in batchmode'; UseSimple = $true }
        @{ Label = 'Fatal error in the Mono runtime (native abort mid-run; usually leaves a misleading total=0 results.xml)'; Pattern = 'fatal error in the mono runtime'; UseSimple = $true }
        @{ Label = 'Mono crash executing native code (native or managed boundary abort)'; Pattern = 'Got a UNKNOWN while executing native code'; UseSimple = $true }
        @{ Label = 'IL2CPP/AOT missing code (no ahead-of-time code for a closed generic; names the exact unrootable type, e.g. StructValueChecker`1)'; Pattern = 'no ahead of time \(AOT\) code was generated'; UseSimple = $false }
        @{ Label = 'ExecutionEngineException (IL2CPP/AOT generic-instantiation failure on a standalone player)'; Pattern = 'System.ExecutionEngineException'; UseSimple = $true }
    )
}
