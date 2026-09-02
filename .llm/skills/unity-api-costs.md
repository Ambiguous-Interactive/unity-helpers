# Skill: Unity API Costs

<!-- trigger: GetComponents, Unity null, array pool, PooledArray, SystemArrayPool, WallstopArrayPool | Measured costs of Unity and pool APIs | Performance -->

**Trigger**: Before clearing a buffer you hand to Unity, null-testing a component, or renting an
array.

Every number here was measured on `6000.4.6f1` through the MCP bridge, not reasoned about. Re-measure
rather than extending a claim to an API this file does not name.

---

## Every list-taking `Get*Components` overload clears the list for you

Pre-filling each buffer with sentinels and querying:

```text
GetComponents(Type, list):                prefilled 3 -> 1
GetComponents<T>(list):                   prefilled 2 -> 3   (the object's 3 components)
GetComponentsInChildren<T>(bool, list):   prefilled 3 -> 2
GetComponentsInParent<T>(bool, list):     prefilled 3 -> 2
ZERO-MATCH GetComponents(Type, list):     prefilled 2 -> 0
ZERO-MATCH GetComponentsInChildren<T>:    prefilled 2 -> 0
```

The zero-match rows are the ones that matter: a query that finds nothing still empties the list, so a
stale result cannot survive.

**So a `.Clear()` before one of these is dead code.** A `.Clear()` that guards a path returning the
buffer _without_ querying is not — say which it is at the site, or the next sweep deletes a real one.
Both live examples are in the package: `GetComponentsOfType`'s clear guards the
`isInterface && !allowInterfaces` early return, and `Helpers.cs`'s guards a `target` matching neither
`switch` case.

---

## `UnityEngine.Object`'s `!=` is a native aliveness check

20M iterations, best-of-three, the managed compare winning every round:

```text
UnityEngine.Object operator!= : 3.380 ns/op
managed reference compare     : 0.578 ns/op   -> 5.84x
```

**So a helper that has already established liveness should return a `bool` with an `out`, never the
object for the caller to null-test.** `TryFirstMatchingComponent` over `FirstMatchingComponent`.

**This is not licence to replace Unity null checks with `is not null`.** Unity's operator is the only
thing that detects a _destroyed_ object. The check that first establishes liveness, and any defensive
check on a reference of unknown provenance, must stay — `UnityMainThreadDispatcher.TryGetInstance` is
the producing check and is correct as written. Only a _re-ask_ of a question a `bool` already
answered is removable. Swept `Runtime/` and `Editor/`: 42 candidate sites, all but a handful managed
types where the compare is already cheap, and exactly one genuine re-ask.

There is a correctness reason to prefer the `Try` shape too: returning a `Component` from a
`bool`-shaped position is what let a search result be silently discarded in
[#529](https://github.com/Ambiguous-Interactive/unity-helpers/issues/529). An `out` makes that
unrepresentable.

---

## Renting an array: `SystemArrayPool` unless the consumer needs a PRECISE length

Owner rule, PR #557.

| pool                       | length                   | cleared                   | bucket per size                  |
| -------------------------- | ------------------------ | ------------------------- | -------------------------------- |
| `SystemArrayPool<T>`       | **at least** the request | on request (`clearArray`) | no — wraps the shared pool       |
| `WallstopArrayPool<T>`     | exactly                  | on release                | **permanent, per distinct size** |
| `WallstopFastArrayPool<T>` | exactly                  | never                     | **permanent, per distinct size** |

All three hand back a `PooledArray<T>` handle, so all three are a `using` rather than a
`try`/`finally`. `SystemArrayPool` is the default. The exact-size pools keep a bucket forever per
distinct size, which their own class doc lists under **"UNSAFE uses (will leak memory)"** — a bounded
size is not sufficient reason to use one.

**"Precise" means the API rejects a longer array, and that is worth measuring:**

```text
Texture2D.SetPixels32(oversized)      -> ArgumentException: size of data to be written is
                                         outside the target buffer bounds
RectTransform.GetWorldCorners(len 8)  -> accepted; it needs four OR MORE
```

So `SpriteSheetExtractor` is the one justified exact-size rent in the package, and `GetWorldCorners`
— which looks like it needs exactly four — does not.

Go to `System.Buffers.ArrayPool<T>.Shared` unwrapped only for a buffer whose lifetime is not scoped,
as `PooledBufferStream` does while growing.

**Read a pool's `<remarks>` on the CLASS, not just the `Get` overload you are calling.** Session 222
put six rents on the exact-size pool while citing that pool's own documentation for the parts that
suited the change; the warning was in the class remarks directly above.

## `Debug.LogError` is ~400x a relational assignment, so a miss is not a benchmark

Measured on `6000.4.6f1` through the MCP bridge. A `[SiblingComponent]` collection field resolves in
**~1.0 us fixed plus ~0.037 us per sibling** (1 sibling 1.015 us, 64 siblings 3.346 us, against a
control that read 0.305-0.309 us across all ten measurements). The same call with **nothing to bind**
costs **366-431 us**, because `LogMissingComponentError` reaches `Debug.LogError` and Unity captures
a stack trace there.

Two things follow. A benchmark whose fixture has no matching sibling is measuring the console rather
than the assignment path — the reading is three orders of magnitude out and looks like a catastrophic
regression. And a scene where many objects carry a non-`Optional` relational field nothing satisfies
pays that on every assignment, which is a load-time stall with a cause that reads as "some errors in
the console".

The corollary for optimization work: at realistic sibling counts the **fixed** ~1.0 us is about two
thirds of the call, so per-element work is not where the remaining headroom is. Three collection
shapes measured within 9% of each other, and `List<T>` — the one paying a non-generic `IList.Add`
per element — was the fastest of the three ([#529](https://github.com/Ambiguous-Interactive/unity-helpers/issues/529)).

## `implicit operator bool` makes a `Component` legal in any boolean position

`UnityEngine.Object` declares it, so **no `Component`-shaped expression is ever a type error in a
boolean position** -- not in a `return`, an `if`, an `&&` or a `!`. A `bool`-returning method that
ends `return FindTheThing(...);` compiles, converts the found object to `true`, and **discards it**.

Read a `bool` method with an `out` parameter as one unit. If a path returns without writing the
`out`, the caller gets a stale value and the compiler will not say so: definite assignment is
satisfied by any earlier write, including one a failed filter has since invalidated.

That shipped in 3.5.1. Every relational field with `IncludeInactive = false` bound the disabled
candidate ahead of the enabled one
([#529](https://github.com/Ambiguous-Interactive/unity-helpers/issues/529)).

## `is null` is a CLR test, so it walks past a destroyed object

`UnityEngine.Object` overloads `==` to report a destroyed object as null. A **pattern** does not use
that overload, so `row is null` and `row is not null` are CLR null tests and answer "alive" about an
object whose native half is gone -- the same defect as `?.`, in syntax that looks like a null check
rather than an operator. `WUH003` reports both, from session 243; before that the rule was written
down and enforced by nothing.

A **type** pattern is a different thing and is deliberately unreported: `row is Sprite` matches a
destroyed object too, so it is not a null test at all and there is nothing to correct. That is also
why `WUH012` refuses to accept one as a guard on a serialized row.

Use `!= null`, or `Objects.NotNull` / `Objects.Null`, which go through the overload.

## A Unity asset path is project-relative; `System.IO` is not

`AssetDatabase.GetAssetPath` returns `Assets/Foo.asset`. Reading that through `File` works only while
the process working directory is the project root -- Unity sets it there, so the dependency is
invisible until something changes it, and then the failure is **silent**: the read throws, the caller
skips the file, and the scan reports clean.

Resolve with `AuthoredAssetPaths.ToFileSystemPath` for the filesystem and keep the asset path for the
AssetDatabase and for the report; `ToAssetPath` maps back so a finding still names a path a reader
can click. Name the parameter `filePath` wherever it reaches `System.IO` -- `assetPath` is what
invited the defect in the first place (#665).

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
