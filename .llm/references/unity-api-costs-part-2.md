# unity-api-costs - Part 2

## Split Content

## A disposed `SerializedObject` throws a DIFFERENT exception per editor version

Measured 2026-08-31 by a test that passed locally and failed CI. Calling `Update()` on a
`SerializedObject` after `Dispose()`:

| editor        | exception                                                                  |
| ------------- | -------------------------------------------------------------------------- |
| `6000.4.6f1`  | `NullReferenceException`                                                   |
| `2022.3.45f1` | `ArgumentNullException: Value cannot be null. Parameter name: _unity_self` |

Both come from the native `_unity_self` marshalling of a released handle, and which one surfaces is
the editor's business. `Assert.Throws<T>` matches the EXACT type, so pinning either one is a green
local run and a red matrix leg. **Assert that it throws -- `Assert.Catch` -- not which exception
says so.** Same shape as `Scene.handle` below: an answer confirmed in one editor is not a fact about
every editor CI runs (#553).

`Dispose()` itself is idempotent on both, so a second call needs no guard.

## `Scene.handle` changes type at Unity 6000.5

It is an `int` up to 6000.4 and a `SceneHandle` from 6000.5, where the implicit conversion to `int`
is obsolete-as-an-**error**. Compare `Scene` values (`==`, `IsValid()`) rather than caching a handle.

No local gate catches it -- `typecheck:unity` is on 2021.3 reference assemblies and the MCP editor
on 6000.4 -- so it costs a full Unity matrix run to find. Same class as
[#553](https://github.com/Ambiguous-Interactive/unity-helpers/issues/553), one version further out.

## `EditorApplication.delayCall` is a tick an unattended editor may never reach

Measured on 6000.4.6f1, twice, three sessions apart.

`WProtoSubtypeTagAutoAssign` found it first: a call queued on `delayCall` was **still pending
minutes after the reload that queued it**, so its manifest was never written even though the work
itself was correct. Its class doc names the conditions -- "a background window, **a CI editor driven
over a socket**". An editor nobody is clicking in does not necessarily pump the tick at all.

`TestRunReporter` shipped the same shape in session 245 and a reviewer caught it before it merged:
it re-registered its Test Runner `ICallbacks` on `delayCall` after the domain reload a PlayMode run
causes. Two ways to lose: the Test Runner can broadcast `RunFinished` while the domain is still
loading, and -- worse -- the feature exists **for** a CI editor driven over a socket, the exact case
the first measurement says may never tick.

**So register in the callback Unity invokes, not on a tick you hope for.** The
`[InitializeOnLoadMethod]` body, a static constructor, or `AssemblyReloadEvents.afterAssemblyReload`
are callbacks Unity calls; `delayCall` is a queue it drains when something drives the editor. Keep
`delayCall` for the **retry** path only, where the alternative is acting on a project that is still
compiling.

**The one honest reason to defer is a dependency that genuinely is not ready yet**, and it has to be
handled as a retry rather than a replacement. `FailedTestsExporter` defers because its registration
reads project settings that may be unavailable during load, and reading them too early returns
`false` and silently registers nothing -- the opposite failure, in the same place
([#684](https://github.com/Ambiguous-Interactive/unity-helpers/issues/684)). Try immediately, and
retry on the tick; never only on the tick.

## Subscribing to a finished `AsyncOperation.completed` fires SYNCHRONOUSLY

`AsyncOperation.completed` is not a field-like event. Its `add` accessor is hand-written:
when `isDone` already reads true it invokes the handler inside the `+=` and **never stores it**;
`InvokeCompletionEvent` nulls `m_completeCallback` after firing, which is why the field reads null
afterwards.

Measured two ways for
[#700](https://github.com/Ambiguous-Interactive/unity-helpers/issues/700): behaviourally on
6000.4.6f1 (the handler ran between the log before the `+=` and the log after it, and the backing
field stayed null), and by decoding `add_completed`'s IL out of a real `2021.3.45f1` editor image --
the version the affected `#if !UNITY_2023_1_OR_NEWER` path actually ships to. Both are
`isDone ? Invoke(this) : Delegate.Combine(...)`. 2022.3 is bracketed by the two, not measured.

**So an awaiter must register its continuation BEFORE it subscribes.** `AsyncOperationAwaiter`
does, and reversing those two statements would drain an empty continuation table and only then add
a continuation nothing would run -- a permanent hang on the package's own minimum editor. The
consequence to state in a doc: in that window the await resumes **on the calling stack**, not on a
later frame.

The control that mattered: the repo's pinned `UnityEngine.Modules` 2021.3.33 NuGet reference
assembly decodes every method body as `ldnull; throw`, so reading a body out of it measures
nothing. Only a real editor image answers this -- [#553](https://github.com/Ambiguous-Interactive/unity-helpers/issues/553) again, one instrument further out.
