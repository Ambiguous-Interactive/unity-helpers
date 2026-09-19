# High-Performance C# Concurrency and Pooling

## Thread Safety Patterns

Use conditional compilation for thread-safe vs single-threaded builds:

```csharp
#if SINGLE_THREADED
private static readonly Dictionary<Type, object> Cache = new();
#else
using System.Collections.Concurrent;
private static readonly ConcurrentDictionary<Type, object> Cache = new();
#endif
```

### Populating a cache must be atomic

Memoizing into a `ConcurrentDictionary` with `TryGetValue`, then building on a miss, then storing
through the **indexer** is not atomic. Two callers racing a first use each build, and each returns a
different instance than the one that ends up cached. When the build has a side effect -- a probe
call, a registration, a log -- it happens twice.

```csharp
// WRONG: last-write-wins, and the value you return may not be the value that was cached
if (!Cache.TryGetValue(key, out Value cached))
{
    cached = Build(key);
    Cache[key] = cached;
}
return cached;

// RIGHT: one winner, and every caller gets it. The state-taking overload keeps the
// lambda `static`, so no closure is allocated for the argument the factory needs.
return Cache.GetOrAdd(key, static (k, arg) => Build(k, arg), argument);
```

`GetOrAdd`'s factory may still run more than once under contention -- documented and unavoidable --
but only one result is stored and returned, which is the property that matters. Keep the plain
`TryGetValue`-then-indexer form **only** under `SINGLE_THREADED`, where the field is a plain
`Dictionary` and there is no race to lose.

`scripts/lint-concurrent-cache-fill.ps1` enforces this. It tracks preprocessor state, so the
`SINGLE_THREADED` form does not trip it, and a deliberate last-writer-wins overwrite is exempted by
a `concurrent-overwrite: <why>` marker on the write line or anywhere in the contiguous comment above
it -- in either the `//` or the `/* */` form, since rule 8 requires the block form for a multi-line
reason. Do not reach for that marker to silence a fill: it is for a write that _must_ replace an
earlier answer, such as an explicit registration overriding what inference cached.

**It catches one direction only, and the other is easier to get wrong.** Nothing flags a
`GetOrAdd`/`TryAdd` that should have stayed an overwrite, and that conversion is silent and
permanent. Session 217 nearly shipped one: a drawer cached a constructor factory, and when that
factory returned `null` it fell back to `Activator` and **overwrote** the cache; as `TryAdd` the
overwrite became a no-op, so the first call worked and every later one hit the broken factory and
returned `false`. The test is not "is the value deterministic" but **"can this key already hold
something this write is meant to replace?"** -- i.e. does any path reach the write _after_ something
already stored under that key, rather than only through an early `TryGetValue` miss.

### A cache factory must be `static`, and a method group is not free

The factory is built **before** the call, so whatever it costs is paid on every call, cache hit
included. 400,000 warm hits per shape on 6000.4.6f1, control moved 30.6 MB:

| factory shape                            | bytes per call |
| ---------------------------------------- | -------------: |
| lambda capturing a method parameter      |      **115.8** |
| method group (`GetOrAdd(key, Build)`)    |      **106.3** |
| `static` lambda + state-taking overload  |            0.0 |
| cached `static readonly Func<...>` field |            0.1 |

- **Mark every cache-factory lambda `static`** (the linter enforces this). It is not cheaper -- a
  non-capturing lambda is cached in a static field either way. It makes the compiler **reject** a
  capture (`CS8820`), so the expensive shape stops compiling. That sweep found five capturing
  factories in `Runtime` and two in `Editor` that nothing else reported.
- **Never pass a method group.** C# 9 does not cache the conversion (C# 11 does), so
  `GetOrAdd(key, Build)` allocates every call. Hold it in a `static readonly Func<...>`. No source
  linter can catch this -- it is lexically identical to a local holding a delegate -- so the shipped
  **`WUH001`** analyzer does, with the semantic model. It covers `ConcurrentDictionary`,
  `ConditionalWeakTable` and this package's own `DictionaryExtensions`
  (`GetOrAdd`/`GetOrElse`/`AddOrUpdate`), which is how a plain `Dictionary` is reached.

**Measure the capture in a method, not a loop.** The first run of that table read **0 B/call** for
the capturing lambda, because a captured _local_ beside the loop gets one display class. Real code
captures a **parameter**, so a fresh one is built per call. Reproduce the method, not the shape.

### A monitor around a cache read is the whole call when the answer is "nothing to do"

A `lock` costs ~15 ns more than a `ConcurrentDictionary` read -- nothing beside the work most callers
then do, and everything beside the work a caller does when the answer is `false`.
`RelationalComponentAssigner.HasRelationalAssignments` was the case: `AssignHierarchy` asks it per
component, and most components have no relational field, so for them the lookup _is_ the call.
Best of three on 6000.4.6f1:

| shape                              |    cost |
| ---------------------------------- | ------: |
| `lock` + `Dictionary.TryGetValue`  | 23.1 ns |
| `ConcurrentDictionary.TryGetValue` |  7.7 ns |

That 15.4 ns was **26%** of a non-relational component: `AssignHierarchy` over 601 of them went from
59.7 to 41.5 ns each. Before assuming a lock is negligible, ask what the call does when the cache
says "no".

A guard the factory cannot express -- "only build when I have a live probe" -- still belongs in front
of `GetOrAdd`, because a factory handed a bad argument would cache a refusal for a reason that has
nothing to do with the key.

For primitives, use `Volatile` or `Interlocked`:

```csharp
int newValue = Interlocked.Increment(ref _counter);
int current = Volatile.Read(ref _counter);
Volatile.Write(ref _counter, newValue);
```

---

## Quick Checklist

Before submitting any code, verify:

- [ ] No LINQ in hot paths, and no closures capturing variables
- [ ] `foreach` over concrete collections; a counting loop only for the four cases above
- [ ] Temporary collections use `Buffers<T>`, arrays the matching pool (a `[ThreadStatic]` scratch
      is a call-site-specific optimization, not a default -- see below)
- [ ] No reflection on code we control (use `internal` + `[InternalsVisibleTo]`)
- [ ] Value types where appropriate; hash codes cached for dictionary keys
- [ ] Editor code caches `GUIContent`/`GUIStyle`
- [ ] `[MethodImpl(MethodImplOptions.AggressiveInlining)]` on hot paths
- [ ] Thread safety uses the conditional compilation pattern
- [ ] No duplicated code - extract common patterns to abstractions

---

## A Pool Lease Is the Default; a Hand-Rolled Scratch Needs Its Own Number

`Buffers<T>` is the default for a temporary collection. Replacing it with a `[ThreadStatic]` list is
an optimization, and its result **does not transfer between call sites** -- measured both ways in
this repository, on the same editor, in the same family of methods:

| call site                     | whole call |                               `Buffers<T>` lease vs `[ThreadStatic]` |
| ----------------------------- | ---------: | -------------------------------------------------------------------: |
| sibling relational assignment |    ~1.0 µs |                                    lease **7% slower** (session 215) |
| child relational assignment   |    ~4.7 µs | lease **2% faster**, 0.976-0.983x over three orderings (session 216) |

The lease's cost is roughly fixed, so it is a large fraction of a cheap call and a negligible one of
an expensive call. That is the whole explanation, and it is why "we hand-rolled a scratch in the
neighbouring method" is not a reason to hand-roll one here.

So: reach for `Buffers<T>`. Specialize only with a number for **that** call site, run in more than
one ordering, and leave the number in a comment where the next reader will find it.

## Related Skills

- [avoid-allocations](../skills/avoid-allocations.md) - Value types, closures, IEquatable, hash codes, boxing (MANDATORY companion)
- [use-pooling](../skills/use-pooling.md) - Collection and buffer pooling patterns (MANDATORY companion)
- [avoid-reflection](../skills/avoid-reflection.md) - Direct access patterns, ReflectionHelpers
- [defensive-programming](../skills/defensive-programming.md) - Error handling patterns (MANDATORY companion)
- [unity-performance-patterns](../skills/unity-performance-patterns.md) - Unity-specific optimizations (MANDATORY for Unity code)
- [gc-architecture-unity](../skills/gc-architecture-unity.md) - Unity GC architecture details
- [profile-debug-performance](../skills/profile-debug-performance.md) - Profiling and debugging performance
- [use-array-pool](../skills/use-array-pool.md) - Array pool selection guide
- [refactor-to-zero-alloc](../skills/refactor-to-zero-alloc.md) - Migration guide for existing code
- [performance-audit](../skills/performance-audit.md) - Performance review checklist
- [create-editor-tool](../skills/create-editor-tool.md) - Editor-specific patterns

## References

- [forbidden-patterns](./forbidden-patterns.md) - Consolidated forbidden/recommended patterns table
