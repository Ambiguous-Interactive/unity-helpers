#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Test runner for lint-concurrent-cache-fill.ps1.

.DESCRIPTION
    Proves the linter both ways against synthetic fixtures:
    - Exits 0 for a cache filled through GetOrAdd or TryAdd.
    - Exits 1 for a cache filled through the indexer after a TryGetValue miss.
    - Exits 0 for an indexer write inside `#if SINGLE_THREADED`, where the field is a plain
      Dictionary -- the false positive that made the first raw sweep four times too big.
    - Exits 1 for that same write in the `#else` branch, so the preprocessor tracking cannot be
      passing by simply ignoring every conditional.
    - Exits 0 for a write marked `// concurrent-overwrite:`, whether the marker is on the write
      line or anywhere in the contiguous comment block above it.
    - Exits 1 when the marker is separated from the write by a blank line, so the exemption cannot
      be inherited from an unrelated comment further up the file.
    - Finds a cache whose generic argument list csharpier wrapped onto several lines.
    - Exits 1 for a non-`static` lambda handed to GetOrAdd, and 0 once it is `static`.
    - Exits 0 for a nested lambda inside the constructed value, which is not a cache factory and
      cannot be `static` when it closes over the factory's own parameter.
    - Exits 0 for a cache factory whose value type carries a comma inside a generic argument list,
      which an earlier draft split as if it were an argument separator.

.PARAMETER VerboseOutput
    Show verbose per-test diagnostics.

.EXAMPLE
    pwsh -NoProfile -File scripts/tests/test-lint-concurrent-cache-fill.ps1
#>
param(
    [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:TestsPassed = 0
$script:TestsFailed = 0

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$lintScriptPath = Join-Path $repoRoot 'scripts/lint-concurrent-cache-fill.ps1'
$tempBase = if ($env:TEMP) { $env:TEMP } elseif ($env:TMPDIR) { $env:TMPDIR } else { '/tmp' }
$tempRoot = Join-Path $tempBase "test-lint-concurrent-cache-fill-$(Get-Random)"

function Write-Info($msg) {
    if ($VerboseOutput) { Write-Host "[test-lint-concurrent-cache-fill] $msg" -ForegroundColor Cyan }
}

# The linter derives its repo root from its own location, so a fixture repo is a tempdir with the
# script copied into scripts/ and the source under Runtime/.
function Invoke-LintOnSource {
    param([Parameter(Mandatory = $true)][string]$Source)

    $root = Join-Path $tempRoot "repo-$(Get-Random)"
    New-Item -ItemType Directory -Path (Join-Path $root 'scripts') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $root 'Runtime') -Force | Out-Null
    Copy-Item -LiteralPath $lintScriptPath -Destination (Join-Path $root 'scripts/lint-concurrent-cache-fill.ps1')
    Set-Content -LiteralPath (Join-Path $root 'Runtime/Fixture.cs') -Value $Source -NoNewline

    $output = & pwsh -NoProfile -File (Join-Path $root 'scripts/lint-concurrent-cache-fill.ps1') -VerboseOutput 2>&1 | Out-String
    return @{ ExitCode = $LASTEXITCODE; Output = $output }
}

function Assert-Lint {
    param(
        [Parameter(Mandatory = $true)][string]$TestName,
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][int]$ExpectedExitCode
    )

    $result = Invoke-LintOnSource -Source $Source
    if ($result.ExitCode -eq $ExpectedExitCode) {
        Write-Host "  [PASS] $TestName" -ForegroundColor Green
        $script:TestsPassed++
    } else {
        Write-Host "  [FAIL] $TestName" -ForegroundColor Red
        Write-Host "         expected exit $ExpectedExitCode, got $($result.ExitCode)" -ForegroundColor Yellow
        Write-Host "         $($result.Output)" -ForegroundColor Yellow
        $script:TestsFailed++
    }
    Write-Info $result.Output
}

$atomic = @'
namespace Fixture
{
    using System;
    using System.Collections.Concurrent;

    internal static class Cache
    {
        private static readonly ConcurrentDictionary<Type, string> Names = new();
        private static readonly ConcurrentDictionary<Type, byte> Unsupported = new();

        internal static string Get(Type type)
        {
            Unsupported.TryAdd(type, 0);
            return Names.GetOrAdd(type, static t => t.FullName);
        }
    }
}
'@

$indexerFill = @'
namespace Fixture
{
    using System;
    using System.Collections.Concurrent;

    internal static class Cache
    {
        private static readonly ConcurrentDictionary<Type, string> Names = new();

        internal static string Get(Type type)
        {
            if (!Names.TryGetValue(type, out string cached))
            {
                cached = type.FullName;
                Names[type] = cached;
            }

            return cached;
        }
    }
}
'@

$singleThreadedBranch = @'
namespace Fixture
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;

    internal static class Cache
    {
#if SINGLE_THREADED
        private static readonly Dictionary<Type, string> Names = new();
#else
        private static readonly ConcurrentDictionary<Type, string> Names = new();
#endif

        internal static string Get(Type type)
        {
#if SINGLE_THREADED
            if (!Names.TryGetValue(type, out string cached))
            {
                cached = type.FullName;
                Names[type] = cached;
            }

            return cached;
#else
            return Names.GetOrAdd(type, static t => t.FullName);
#endif
        }
    }
}
'@

$elseBranchFill = @'
namespace Fixture
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;

    internal static class Cache
    {
#if SINGLE_THREADED
        private static readonly Dictionary<Type, string> Names = new();
#else
        private static readonly ConcurrentDictionary<Type, string> Names = new();
#endif

        internal static string Get(Type type)
        {
#if SINGLE_THREADED
            return Names.TryGetValue(type, out string cached) ? cached : null;
#else
            Names[type] = type.FullName;
            return Names[type];
#endif
        }
    }
}
'@

$markedOverwrite = @'
namespace Fixture
{
    using System;
    using System.Collections.Concurrent;

    internal static class Cache
    {
        private static readonly ConcurrentDictionary<Type, string> Names = new();

        internal static void Register(Type type, string name)
        {
            Names[type] = name; // concurrent-overwrite: an explicit registration must beat inference
        }
    }
}
'@

$markedOverwriteBlockAbove = @'
namespace Fixture
{
    using System;
    using System.Collections.Concurrent;

    internal static class Cache
    {
        private static readonly ConcurrentDictionary<Type, string> Names = new();

        internal static void Register(Type type, string name)
        {
            // concurrent-overwrite: an explicit registration must replace whatever inference
            // cached earlier, and that reason does not fit on the write line.
            Names[type] = name;
        }
    }
}
'@

$markerSeparatedByBlankLine = @'
namespace Fixture
{
    using System;
    using System.Collections.Concurrent;

    internal static class Cache
    {
        private static readonly ConcurrentDictionary<Type, string> Names = new();

        internal static void Register(Type type, string name)
        {
            // concurrent-overwrite: this comment belongs to something else entirely.

            Names[type] = name;
        }
    }
}
'@

$nonStaticFactory = @'
namespace Fixture
{
    using System;
    using System.Collections.Concurrent;

    internal static class Cache
    {
        private static readonly ConcurrentDictionary<Type, string> Names = new();

        internal static string Get(Type type)
        {
            return Names.GetOrAdd(type, t => t.FullName);
        }
    }
}
'@

$staticFactory = @'
namespace Fixture
{
    using System;
    using System.Collections.Concurrent;

    internal static class Cache
    {
        private static readonly ConcurrentDictionary<Type, string> Names = new();

        internal static string Get(Type type)
        {
            return Names.GetOrAdd(type, static t => t.FullName);
        }
    }
}
'@

$nestedLambdaInValue = @'
namespace Fixture
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;

    internal sealed class Holder<T>
    {
        internal Holder(Func<T> make, Action<T> onRelease) { }
    }

    internal static class Cache
    {
        private static readonly ConcurrentDictionary<
            IComparer<int>,
            Holder<SortedSet<int>>
        > Pools = new();

        internal static Holder<SortedSet<int>> Get(IComparer<int> comparer)
        {
            return Pools.GetOrAdd(
                comparer,
                static inComparer => new Holder<SortedSet<int>>(
                    () => new SortedSet<int>(inComparer),
                    onRelease: set => set.Clear()
                )
            );
        }
    }
}
'@

$genericCommaInValueType = @'
namespace Fixture
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;

    internal static class Cache
    {
        private static readonly ConcurrentDictionary<
            Type,
            Dictionary<string, int>
        > Maps = new();

        internal static Dictionary<string, int> Get(Type type)
        {
            return Maps.GetOrAdd(type, static _ => new Dictionary<string, int>());
        }
    }
}
'@

$wrappedDeclaration = @'
namespace Fixture
{
    using System;
    using System.Collections.Concurrent;

    internal static class Cache
    {
        private static readonly ConcurrentDictionary<
            Type,
            Func<object>
        > Factories = new();

        internal static void Fill(Type type, Func<object> factory)
        {
            Factories[type] = factory;
        }
    }
}
'@

try {
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

    Write-Host "[test-lint-concurrent-cache-fill] Running..." -ForegroundColor Cyan
    Assert-Lint -TestName 'GetOrAdd and TryAdd pass' -Source $atomic -ExpectedExitCode 0
    Assert-Lint -TestName 'Indexer fill after a miss fails' -Source $indexerFill -ExpectedExitCode 1
    Assert-Lint -TestName 'Indexer fill inside SINGLE_THREADED passes' -Source $singleThreadedBranch -ExpectedExitCode 0
    Assert-Lint -TestName 'Indexer fill in the #else branch still fails' -Source $elseBranchFill -ExpectedExitCode 1
    Assert-Lint -TestName 'Marked deliberate overwrite passes' -Source $markedOverwrite -ExpectedExitCode 0
    Assert-Lint -TestName 'Marker in the comment block above passes' -Source $markedOverwriteBlockAbove -ExpectedExitCode 0
    Assert-Lint -TestName 'Marker cut off by a blank line does not exempt' -Source $markerSeparatedByBlankLine -ExpectedExitCode 1
    Assert-Lint -TestName 'Wrapped generic declaration is still found' -Source $wrappedDeclaration -ExpectedExitCode 1
    Assert-Lint -TestName 'Non-static cache factory fails' -Source $nonStaticFactory -ExpectedExitCode 1
    Assert-Lint -TestName 'Static cache factory passes' -Source $staticFactory -ExpectedExitCode 0
    Assert-Lint -TestName 'Nested lambda inside the value is not a factory' -Source $nestedLambdaInValue -ExpectedExitCode 0
    Assert-Lint -TestName 'Comma inside a generic value type is not an argument split' -Source $genericCommaInValueType -ExpectedExitCode 0

    Write-Host "[test-lint-concurrent-cache-fill] $script:TestsPassed passed, $script:TestsFailed failed." -ForegroundColor Cyan
    if ($script:TestsFailed -gt 0) { exit 1 }
} finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
