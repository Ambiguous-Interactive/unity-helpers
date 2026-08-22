# Skill: High-Performance C

<!-- trigger: performance, allocation, gc, memory, optimize | ALL code - zero allocation patterns | Core -->

**Trigger**: When implementing ANY new feature, fixing bugs, or writing editor tooling. This applies to ALL code in this repository.

---

## Core Philosophy

**Every code path should be allocation-free in steady state.** This includes:

- Runtime gameplay code
- Editor tooling and inspectors (called every frame when visible)
- Bug fixes (must not regress performance)
- Test utilities (may run thousands of iterations)

### Why Zero-Allocation Matters in Unity

Unity's Boehm garbage collector scans the entire heap on every collection, causes frame stutters, and never compacts memory. At 60 FPS with 1KB/frame = **3.6 MB/minute** of garbage, triggering frequent GC pauses.

See [gc-architecture-unity](./gc-architecture-unity.md) for detailed GC architecture information.

**Never duplicate code - build abstractions.** When you see repetitive patterns:

- Extract to lightweight, reusable abstractions
- Prefer `readonly struct` or `static` methods for zero allocation
- Apply SOLID principles - single responsibility, open/closed extension
- Use composition to build complex behavior from simple pieces

---

## Abstraction Guidelines

### When to Abstract

- **Two or more occurrences** - If you write similar code twice, extract it
- **Complex logic** - Encapsulate non-obvious algorithms behind clear interfaces
- **Cross-cutting concerns** - Logging, caching, validation patterns

### How to Abstract (Zero Allocation)

```csharp
// ✅ Value-type abstraction - no heap allocation
public readonly struct ValidationResult
{
    public readonly bool IsValid;
    public readonly string ErrorMessage;
    private readonly int _hash;

    public ValidationResult(bool isValid, string errorMessage = null)
    {
        IsValid = isValid;
        ErrorMessage = errorMessage;
        _hash = Objects.HashCode(isValid, errorMessage);
    }
}

// ✅ Static utility methods - no allocation
public static class CollectionExtensions
{
    public static bool TryGetFirst<T>(this IList<T> list, out T result)
    {
        if (list.Count > 0)
        {
            result = list[0];
            return true;
        }
        result = default;
        return false;
    }
}

// ✅ Generic constraint-based abstraction
public static void ProcessAll<T>(IList<T> items) where T : IProcessable
{
    for (int i = 0; i < items.Count; i++)
    {
        items[i].Process();
    }
}
```

### Abstraction Anti-Patterns

```csharp
// ❌ Class when struct suffices - unnecessary allocation
public class ValidationResult { }

// ❌ Closure-capturing delegate factory
public Func<T> CreateGetter<T>(T value) => () => value;  // Allocates!

// ❌ Over-abstraction - adds complexity without value
public interface IStringProvider { string GetString(); }
public class ConstantStringProvider : IStringProvider { ... }  // Just use the string!
```

---

## Aggressive Inlining for Hot Paths

Mark frequently-called small methods:

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public override int GetHashCode() => _hash;

[MethodImpl(MethodImplOptions.AggressiveInlining)]
public bool Equals(FastVector2Int other)
{
    return _hash == other._hash && x == other.x && y == other.y;
}

[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static bool operator ==(FastVector2Int lhs, FastVector2Int rhs)
{
    return lhs.Equals(rhs);
}
```

---

## String Building Best Practices

String operations are a common source of allocations. Choose the right approach based on context:

| Context                   | Recommended Approach        | Example                       |
| ------------------------- | --------------------------- | ----------------------------- |
| Hot paths (Update, loops) | `StringBuilder` via pooling | `Buffers.StringBuilder.Get()` |
| Two strings               | Direct `+` is fine          | `firstName + lastName`        |
| 3+ parts, non-hot path    | String interpolation        | `$"{name}: {value}"`          |
| Building in loops         | **Always** `StringBuilder`  | See below                     |
| Format with many args     | `StringBuilder`             | Avoids `params` allocation    |

```csharp
// ❌ BAD: Concatenation in loop - O(n^2) allocations!
string result = "";
for (int i = 0; i < items.Count; i++)
{
    result += items[i].Name;  // New string each iteration!
}

// ✅ GOOD: StringBuilder with pooling - zero allocation
using var lease = Buffers.StringBuilder.Get(out StringBuilder sb);
for (int i = 0; i < items.Count; i++)
{
    sb.Append(items[i].Name);
}
string result = sb.ToString();
```

---

## Editor Tooling Requirements

Editor code runs every frame when inspectors are visible. Apply ALL performance patterns:

```csharp
// ❌ Allocates every OnGUI call
public override void OnInspectorGUI()
{
    List<string> options = GetOptions().ToList();  // Allocation!
    int selected = EditorGUILayout.Popup("Option", current, options.ToArray());  // More allocations!
}

// ✅ Cache everything, pool temporaries
private static readonly GUIContent TitleContent = new GUIContent("Option");
private string[] _cachedOptions;
private int _cachedOptionsHash;

public override void OnInspectorGUI()
{
    int currentHash = ComputeOptionsHash();
    if (_cachedOptions == null || _cachedOptionsHash != currentHash)
    {
        using var lease = Buffers<string>.List.Get(out List<string> options);
        GetOptions(options);
        _cachedOptions = options.ToArray();  // Only allocate when data changes
        _cachedOptionsHash = currentHash;
    }

    int selected = EditorGUILayout.Popup(TitleContent, current, _cachedOptions);
}
```

**Cache GUIContent, GUIStyle, and computed values:**

```csharp
private static readonly GUIContent Label = new GUIContent("Label", "Tooltip");
private static readonly GUIStyle BoxStyle = new GUIStyle("box");
private static readonly Color HighlightColor = new Color(0.3f, 0.6f, 1f);
```

---

## LINQ Forbidden Patterns

### LINQ vs Native Collection Methods (CRITICAL DISTINCTION)

**NOT all methods ending in common names are LINQ.** This is a critical distinction:

| Method                            | Is LINQ? | Class                    | Allocates?                | Action                                 |
| --------------------------------- | -------- | ------------------------ | ------------------------- | -------------------------------------- |
| `list.ToArray()`                  | NO       | `List<T>`                | Yes (result only)         | **KEEP** - uses optimized `Array.Copy` |
| `list.ToList()`                   | NO       | `List<T>` (copy)         | Yes (result only)         | **KEEP** - optimized copy constructor  |
| `enumerable.ToArray()`            | YES      | `System.Linq.Enumerable` | Yes + iterator            | **ELIMINATE** - allocates iterator     |
| `enumerable.ToList()`             | YES      | `System.Linq.Enumerable` | Yes + iterator            | **ELIMINATE** - allocates iterator     |
| `.Where()`, `.Select()`, `.Any()` | YES      | `System.Linq.Enumerable` | Yes (iterator + delegate) | **ELIMINATE**                          |
| `.First()`, `.FirstOrDefault()`   | YES      | `System.Linq.Enumerable` | Yes (iterator)            | **ELIMINATE**                          |
| `.ToDictionary()`, `.ToHashSet()` | YES      | `System.Linq.Enumerable` | Yes + iterator            | **ELIMINATE**                          |
| `.OrderBy()`, `.GroupBy()`        | YES      | `System.Linq.Enumerable` | Yes (multiple)            | **ELIMINATE**                          |

**Rule of thumb:** If the source type is `List<T>`, `T[]`, `Dictionary<K,V>`, or other concrete collection, check if the method is native to that type. If the source is `IEnumerable<T>` or the result of a LINQ operation, it's a LINQ extension method.

### Forbidden Hot Path Patterns

| Pattern                              | Problem                         | Alternative                         |
| ------------------------------------ | ------------------------------- | ----------------------------------- |
| LINQ (`.Where`, `.Select`, `.Any`)   | Iterator + delegate allocation  | `for` loop                          |
| `string.Format()` / interpolation    | String allocation               | `StringBuilder` or cache            |
| `new List<T>()`                      | Heap allocation                 | `Buffers<T>.List.Get()`             |
| Lambda capturing locals              | Closure allocation              | Static lambda or explicit loop      |
| Boxing (`object x = struct`)         | Heap allocation                 | Generic methods                     |
| `foreach` on `List<T>` (Mono)        | Enumerator boxing (24 bytes)    | `for` with indexer                  |
| `params` methods                     | Array allocation per call       | Chain 2-arg overloads               |
| Reflection                           | Slow, fragile, uncached         | Direct access, interfaces, generics |
| Hand-rolled hash codes (`* 31`, XOR) | Inconsistent, non-deterministic | `Objects.HashCode()`                |

---

## Removing From a List Nobody Reads in Order

`List<T>.RemoveAt(i)` shifts every element after `i`. When the list's order is not observed, use
`IListExtensions.RemoveAtSwapBack(i)`, which moves the last element into the hole instead.

```csharp
// ✅ CORRECT - a set of checked-out items; Dispose drains all of it, lookup scans all of it
_inFlight.RemoveAtSwapBack(index);

// ❌ WRONG for the same list - pays O(n) to preserve an order nothing reads
_inFlight.RemoveAt(index);
```

Both conditions must hold before swapping:

1. **No caller observes the order** — not the enumeration, not a query result, not a test's
   `CollectionAssert.AreEqual`. Check the tests before changing a container's removal, because the
   order a data structure never promised is often the order a fixture asserts.
2. **The index is not already the last one** — `RemoveAt(list.Count - 1)` shifts nothing and is
   already O(1); swapping there is noise.

Do **not** swap in a list whose order is the meaning: a stack (`_colorStack`, `_materialStack`), a
ring buffer's contents, a breadcrumb trail, a purge queue ordered by return time, or a hull. And
inside a **forward** loop that keeps iterating, a swap-back moves an unvisited element into the
current index — either iterate backwards or re-test the same index.

---

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

For primitives, use `Volatile` or `Interlocked`:

```csharp
private static int _counter;

// Thread-safe increment
int newValue = Interlocked.Increment(ref _counter);

// Thread-safe read/write
int current = Volatile.Read(ref _counter);
Volatile.Write(ref _counter, newValue);
```

---

## Quick Checklist

Before submitting any code, verify:

- [ ] No LINQ in hot paths
- [ ] No closures capturing variables
- [ ] All temporary collections use `Buffers<T>`
- [ ] All temporary arrays use appropriate pool
- [ ] No reflection on code we control (use `internal` + `[InternalsVisibleTo]`)
- [ ] Value types used where appropriate
- [ ] Hash codes cached for dictionary keys
- [ ] Editor code caches GUIContent/GUIStyle
- [ ] `[MethodImpl(MethodImplOptions.AggressiveInlining)]` on hot paths
- [ ] Thread safety uses conditional compilation pattern
- [ ] No duplicated code - extract common patterns to abstractions

---

## Related Skills

- [avoid-allocations](./avoid-allocations.md) - Value types, closures, IEquatable, hash codes, boxing (MANDATORY companion)
- [use-pooling](./use-pooling.md) - Collection and buffer pooling patterns (MANDATORY companion)
- [avoid-reflection](./avoid-reflection.md) - Direct access patterns, ReflectionHelpers
- [defensive-programming](./defensive-programming.md) - Error handling patterns (MANDATORY companion)
- [unity-performance-patterns](./unity-performance-patterns.md) - Unity-specific optimizations (MANDATORY for Unity code)
- [gc-architecture-unity](./gc-architecture-unity.md) - Unity GC architecture details
- [profile-debug-performance](./profile-debug-performance.md) - Profiling and debugging performance
- [use-array-pool](./use-array-pool.md) - Array pool selection guide
- [refactor-to-zero-alloc](./refactor-to-zero-alloc.md) - Migration guide for existing code
- [performance-audit](./performance-audit.md) - Performance review checklist
- [create-editor-tool](./create-editor-tool.md) - Editor-specific patterns

## References

- [forbidden-patterns](../references/forbidden-patterns.md) - Consolidated forbidden/recommended patterns table
