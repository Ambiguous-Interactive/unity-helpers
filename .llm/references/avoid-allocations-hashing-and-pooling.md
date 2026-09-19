# Avoid Allocations: Hashing And Pooling

## Always Use Objects.HashCode

**ALWAYS use `Objects.HashCode` instead of hand-rolled hash implementations.** Hand-rolled implementations are error-prone, inconsistent, and harder to maintain.

```csharp
// ❌ BAD: Hand-rolled hash code with magic primes
public override int GetHashCode()
{
    int hash = 17;
    hash = hash * 31 + X.GetHashCode();
    hash = hash * 31 + Y.GetHashCode();
    hash = hash * 31 + Name?.GetHashCode() ?? 0;
    return hash;
}

// ❌ BAD: XOR-based hash (poor distribution)
public override int GetHashCode()
{
    return X.GetHashCode() ^ Y.GetHashCode() ^ (Name?.GetHashCode() ?? 0);
}

// ❌ BAD: Using System.HashCode.Combine (non-deterministic, varies per AppDomain)
public override int GetHashCode()
{
    return HashCode.Combine(X, Y, Name);
}

// ✅ GOOD: Use Objects.HashCode (deterministic, Unity-aware, consistent)
public override int GetHashCode()
{
    return Objects.HashCode(X, Y, Name);
}
```

**Why Objects.HashCode is required:**

| Feature                    | Hand-Rolled | System.HashCode | Objects.HashCode |
| -------------------------- | ----------- | --------------- | ---------------- |
| Deterministic              | Sometimes   | No (randomized) | Yes              |
| Unity null-aware           | No          | No              | Yes              |
| Handles destroyed objects  | No          | No              | Yes              |
| Consistent across sessions | Maybe       | No              | Yes              |
| Up to 20 parameters        | Manual      | 8 max           | Yes              |
| Span/collection support    | Manual      | Limited         | Yes              |

**Key benefits of Objects.HashCode:**

1. **Deterministic** - Same inputs always produce same hash (critical for serialization, networking, replay systems)
2. **Unity-aware** - Correctly handles destroyed `UnityEngine.Object` instances (returns consistent sentinel value)
3. **Null-safe** - Handles null values without exceptions
4. **Consistent API** - Overloads for 1-20 parameters, plus `SpanHashCode` and `EnumerableHashCode`
5. **FNV-1a algorithm** - Good distribution, battle-tested

**Usage patterns:**

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

// Single value
int hash = Objects.HashCode(value);

// Multiple values (up to 20)
int hash = Objects.HashCode(x, y, z, name, type);

// Span of values
ReadOnlySpan<int> values = stackalloc int[] { 1, 2, 3, 4, 5 };
int hash = Objects.SpanHashCode(values);

// Collection/enumerable
int hash = Objects.EnumerableHashCode(myList);

// Cache in readonly struct for hot-path usage
public readonly struct CachedKey : IEquatable<CachedKey>
{
    public readonly int X;
    public readonly int Y;
    private readonly int _hash;

    public CachedKey(int x, int y)
    {
        X = x;
        Y = y;
        _hash = Objects.HashCode(x, y);  // Compute once at construction
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => _hash;
}
```

---

## List Pre-allocation

Lists grow by **doubling capacity**, causing allocations and copies:

```csharp
// ❌ BAD: Multiple reallocations as list grows
List<Enemy> enemies = new List<Enemy>();
for (int i = 0; i < 10000; i++)
{
    enemies.Add(GetEnemy(i));  // Reallocates at 4, 8, 16, 32...
}

// ✅ GOOD: Pre-allocate when size is known
List<Enemy> enemies = new List<Enemy>(10000);
for (int i = 0; i < 10000; i++)
{
    enemies.Add(GetEnemy(i));  // No reallocations
}
```

**Memory impact**: Without pre-allocation, lists average **33% wasted capacity** and during resize temporarily use **3x memory**.

---

## Quick Checklist

Before submitting code, verify:

- [ ] No closures capturing variables in hot paths
- [ ] No delegate assignments inside loops
- [ ] No `params` method calls in loops (chain 2-arg overloads)
- [ ] Hot-path loops iterate the concrete collection, not an `IEnumerable<T>`/`IList<T>` reference
- [ ] Structs implement `IEquatable<T>`
- [ ] Enum dictionary keys use custom comparer or cast to int
- [ ] Hash codes use `Objects.HashCode()`, not hand-rolled
- [ ] Lists pre-allocated when size is known
- [ ] `AddRange` used instead of `foreach` + `Add` for copying

---

## Related Skills

- [high-performance-csharp](../skills/high-performance-csharp.md) - Core performance philosophy and patterns
- [use-pooling](../skills/use-pooling.md) - Collection and buffer pooling patterns
- [use-array-pool](../skills/use-array-pool.md) - Array pool selection guide
- [gc-architecture-unity](../skills/gc-architecture-unity.md) - Unity GC architecture details
- [refactor-to-zero-alloc](../skills/refactor-to-zero-alloc.md) - Migration guide for existing code
