# DurableFile regression tests

This pure .NET NUnit project compiles the repository's actual
[DurableFile](../../Runtime/Core/Helper/DurableFile.cs),
[FileHelper](../../Runtime/Core/Helper/FileHelper.cs),
[SemaphoreLease](../../Runtime/Core/Threading/SemaphoreLease.cs), and
[DisposalLease](../../Runtime/Utils/DisposalLease.cs) sources through project links. It does not copy,
rewrite, or inject test hooks into those production files.

The project-local `File` type occupies the helper namespace only in this test assembly. It forwards
filesystem calls to `System.IO.File`, except explicitly simulated `NotSupportedException`,
`PlatformNotSupportedException`, and optional move failures. Tests perform real staging writes,
flushes, ownership acquisition, cleanup, supported replacement, and creation on the host filesystem.
These capability failures are controlled simulations, not evidence that a Unity player or native
filesystem rejects atomic replacement.

`MathDependency` supplies only the integer `PositiveMod` dependency needed to choose a semaphore gate.
Its arithmetic body follows [WallMath](../../Runtime/Core/Helper/WallMath.cs); it is a test dependency,
not the production math library under test. The real production semaphore and disposal leases remain
linked. No Unity objects or Unity APIs are involved.

The nonparallel fixture exercises seven public publication paths: text, encoded text, bytes, copy,
async text, async bytes, and async copy. It checks both unsupported exception forms, whether a later
move would succeed or fail, supported existing-file replacement, first creation, and supported retry
following a rejected replacement. It asserts exact previous bytes, returned errors, absence of
fallback deletion, staged-file cleanup, and release of staging ownership.

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
