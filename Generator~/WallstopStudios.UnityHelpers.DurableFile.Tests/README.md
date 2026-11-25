# DurableFile regression tests

This pure .NET NUnit project compiles the repository's actual
[DurableFile](../../Runtime/Core/Helper/DurableFile.cs),
[FileHelper](../../Runtime/Core/Helper/FileHelper.cs),
[SemaphoreLease](../../Runtime/Core/Threading/SemaphoreLease.cs), and
[DisposalLease](../../Runtime/Utils/DisposalLease.cs) sources through project links. It does not copy,
rewrite, or inject test hooks into those production files.

The project-local `File` type occupies the helper namespace only in this test assembly. It forwards
filesystem calls to `System.IO.File` and simulates specific replacement and move failures.
The test-local `RuntimeInformation` forwards platform queries to the host by default. The Windows
failure cases select the Windows branch and model the on-disk outcomes that Microsoft documents
for replacement errors 1176 and 1177. Tests perform real staging writes,
flushes, ownership acquisition, cleanup, supported replacement, and creation on the host filesystem.
These capability failures are controlled simulations, not evidence that a Unity player or native
filesystem rejects atomic replacement.

`MathDependency` supplies only the integer `PositiveMod` dependency needed to choose a semaphore gate.
Its arithmetic body follows [WallMath](../../Runtime/Core/Helper/WallMath.cs); it is a test dependency,
not the production math library under test. The real production semaphore and disposal leases remain
linked. No Unity objects or Unity APIs are involved.

The nonparallel fixture exercises eight public publication paths: text, encoded text, whole and
ranged bytes, copy, async text, async bytes, and async copy. It checks both unsupported exception forms, whether a later
move would succeed or fail, supported existing-file replacement, first creation, and supported retry
following a rejected replacement. It asserts exact previous bytes, returned errors, absence of
fallback deletion, staged-file cleanup, and release of staging ownership. The Windows controls
check that a failed replacement leaves the old file in place, restores the backup, or retains the
backup when a competing destination blocks restoration. They also check that another write
leaves the retained backup intact.

The conditional text tests create a competing file immediately after the real absence read.
They check that late creation reports a conflict and preserves the competing bytes, and that
publication succeeds with a clean staging directory when the destination stays absent. They cover
the host publication path, including exclusive rename on supporting Unix filesystems, and the
Windows branch through the platform selector. The success controls also cover destination names
that fit on disk but would overflow the filename limit if used as a staging-name prefix.

`FileHelperInitializationTests` starts the same assembly as an isolated executable on 64-bit Linux.
Its child lowers its own `RLIMIT_FSIZE` and ignores `SIGXFSZ` before calling the actual source-linked
initializer. Both buffered 128-byte writes and direct 65,536-byte writes exceed limits of zero, one,
and 63 bytes. The parent verifies failure leaves the destination absent and no staging files, then
retries without a limit and checks exact contents. These are real kernel write/flush refusals rather
than injected failure branches; the parent process and other tests retain their original limits.
Null initialization, an existing file, and a directory collision are also covered. These host results
remain distinct from Unity player qualification.

Run from the repository root:

```bash
dotnet test Generator~/WallstopStudios.UnityHelpers.DurableFile.Tests/WallstopStudios.UnityHelpers.DurableFile.Tests.csproj -c Release --nologo
```

Native Unity tests retain responsibility for Unity-runtime and player-platform behavior. This project
covers the deterministic exception path without claiming native platform qualification.
