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
