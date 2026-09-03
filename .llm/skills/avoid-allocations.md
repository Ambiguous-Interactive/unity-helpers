# Skill: Avoid Allocations

<!-- trigger: allocation, boxing, closure, lambda, IEquatable, hash, hashcode | Avoiding heap allocations and boxing | Performance -->

**Trigger**: When writing code that could cause heap allocations or boxing, especially in hot paths.

---

## When to Use

- Implementing new data structures or value types
- Writing code in hot paths (Update, OnGUI, tight loops)
- Working with collections that use equality comparisons
- Creating lambdas or delegates
- Implementing GetHashCode() overrides
- Using enums as dictionary keys

---

## When NOT to Use

- One-time initialization code where allocation is acceptable
- Editor-only code that runs infrequently (but still apply patterns if called every frame)

---

## Value Types and readonly struct

Use `readonly struct` for small data containers to avoid heap allocations:

```csharp
// ✅ Value type with cached hash
public readonly struct FastVector2Int : IEquatable<FastVector2Int>
{
    public readonly int x;
    public readonly int y;
    private readonly int _hash;

    public FastVector2Int(int x, int y)
    {
        this.x = x;
        this.y = y;
        _hash = Objects.HashCode(x, y);  // Compute once
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => _hash;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(FastVector2Int other)
    {
        return _hash == other._hash && x == other.x && y == other.y;
    }
}
```

**When to use structs:**

- Data under ~16 bytes
- No inheritance needed
- Short-lived or frequently created
- Used as dictionary keys (cache the hash!)

---

## Index a Struct Array Element Once, With a `ref` Local

`array[i].Field` recomputes the address and re-runs the bounds check every time, so a run of them
against one element repeats that work. A `ref` local does it once.

```csharp
ref Bucket bucket = ref _buckets[index]; // one bounds check, one address computation
bucket.Peak = value;
bucket.Sum += value;
bucket.Count++;
```

**The reference aliases the array, so it lives only as long as that array does.** Take it _below_
anything that can replace the field -- an `Array.Resize`, including one inside a helper; a
caller-supplied callback that could re-enter; a bounds test the accesses depend on.
`RestorableGlobal` shows both: `BorrowCore` takes its reference after `TakeSlot()`, `TakeSlot` takes
its own after the resize, and `ReleaseCore` takes its below the caller's comparer, because
`lock (_gate)` is a re-entrant `Monitor` and a re-entrant borrow reaches `Array.Resize`.

**Prove the hazard before citing it.** `Cache.SetUnlocked` also calls user code between accesses to
one element, but `_lock` is a `ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion)` in both
compilation modes, so a re-entrant mutator throws before reaching `Grow` -- its repeated indexing is
conservative, not required. `List<T>` cannot do this at all; `CollectionsMarshal.AsSpan` is .NET 5+.

**There is deliberately no analyzer** (owner review, PR #695): nothing static can see whether a
callee replaces the array, and a `WUH###` is capped at warning-and-suppressible so it is never wrong
in a way that costs the reader -- here, a silently lost write.

---

## No Closures in Hot Paths

Closures allocate heap objects. Never use them in hot paths:

```csharp
// ❌ Captures searchId - allocates closure
Item found = list.Find(item => item.Id == searchId);

// ❌ Captures 'this' - allocates closure
items.RemoveAll(x => x.Owner == this);

// ✅ Explicit loop - zero allocation
Item found = null;
for (int i = 0; i < list.Count; i++)
{
    if (list[i].Id == searchId)
    {
        found = list[i];
        break;
    }
}
```

**For cached delegates, use `static` lambdas:**

```csharp
// ✅ Static lambda - no capture, single allocation at class load
private static readonly Func<Type, FieldInfo[]> FieldsFactory =
    static type => type.GetFields(BindingFlags.Instance | BindingFlags.Public);

// Usage in ConcurrentDictionary
fields = FieldCache.GetOrAdd(type, FieldsFactory);
```

---

## Delegate Assignment in Loops

Assigning a delegate in a loop allocates each iteration:

```csharp
// ❌ BAD: Allocates 52 bytes per iteration (13MB for 256K iterations!)
for (int i = 0; i < count; i++)
{
    Func<int> fn = MyFunction;  // Boxing each iteration
    result += fn();
}

// ✅ GOOD: Assign once outside loop
Func<int> fn = MyFunction;
for (int i = 0; i < count; i++)
{
    result += fn();
}
```

---

## Params Array Trap

Methods with `params` allocate an array every call:

```csharp
// ❌ BAD: Allocates 36 bytes per call
Mathf.Max(a, b, c);  // Calls Max(params int[] args)

// ✅ GOOD: Chain 2-argument overloads (zero allocation)
Mathf.Max(Mathf.Max(a, b), c);
```

---

## Foreach Boxing on Collections

The boxing is decided by the **static type being iterated**, not by `foreach`. A concrete collection
exposes a struct enumerator and allocates nothing; an interface-typed reference forces
`IEnumerator<T>`, which is boxed once per loop.

```csharp
// ❌ BAD: 24 bytes per loop -- the interface forces IEnumerator<T>
IReadOnlyList<Item> items = _items;
foreach (Item item in items) { }

// ✅ GOOD: zero allocation on List<T>, Dictionary<K,V>, HashSet<T> and arrays alike
foreach (Item item in _items) { }
```

Measured on `6000.4.6f1`, 2,000,000 iterations, against a known allocator that moved the counter by
54.7 MB: `List<T>` 24,576 bytes, a `for` indexer loop 24,576, `Dictionary<K,V>` 20,480, `int[]`
12,288 -- one noise band -- against **5,709,824** for the same list typed as `IEnumerable<T>`.

**Taking the enumerator by hand saves nothing**, because `foreach` over a concrete collection already
compiles to exactly that. Measured over 2,000,000 iterations: `list.GetEnumerator()` in a `while`
loop cost 20,480 bytes against `foreach`'s 24,576 -- the same noise band. Write the `foreach`.

The hand-rolled form is still correct where you genuinely need the enumerator as a value (resuming
it, passing it, interleaving two of them). If you write one, dispose it with `using` rather than an
explicit call:

```csharp
// A hand-held struct enumerator is disposed by `using`, never by an explicit Dispose().
using (Dictionary<K, V>.Enumerator enumerator = dict.GetEnumerator())
{
    while (enumerator.MoveNext())
    {
        KeyValuePair<K, V> entry = enumerator.Current;
    }
}
```

**IMPORTANT**: Always use `using` statements for struct enumerators, never explicit `Dispose()` calls. The `using` statement:

- Ensures proper disposal even if exceptions occur
- Is more readable and less error-prone
- Follows standard C# patterns for disposable resources

---

## Prefer AddRange Over foreach + Add

When populating a list from an `IEnumerable<T>`, **always prefer `AddRange`** over `foreach` + `Add`:

```csharp
// ❌ BAD: foreach allocates an enumerator, no capacity pre-allocation
using var lease = Buffers<T>.List.Get(out List<T> result);
foreach (T item in source)
{
    result.Add(item);  // May trigger multiple resizes
}

// ✅ GOOD: AddRange is optimized for performance
using var lease = Buffers<T>.List.Get(out List<T> result);
result.AddRange(source);
```

**Why AddRange is better:**

1. **Capacity pre-allocation**: If source is `ICollection<T>`, AddRange queries `Count` first and ensures capacity
2. **Bulk copy**: For arrays and `List<T>`, uses `Array.Copy` which is much faster than individual adds
3. **Potential zero-allocation**: If source already has the items in contiguous memory, no enumerator needed
4. **Fewer resizes**: Pre-allocated capacity means fewer or no list resizes during population

### When to Use for Loop Instead of AddRange

Use indexed `for` loops only when you must **transform** or **filter** each element:

```csharp
// ✅ TRANSFORMATION: Must use for loop - each element needs conversion
using var lease = Buffers<string>.GetList(guids.Length, out List<string> paths);
for (int i = 0; i < guids.Length; i++)
{
    paths.Add(ConvertGuidToPath(guids[i]));  // Can't use AddRange - transforming each element
}

// ✅ FILTERING: Must use for loop - conditionally adding elements
using var lease = Buffers<T>.GetList(items.Length, out List<T> filtered);
for (int i = 0; i < items.Length; i++)
{
    if (items[i].IsValid)
    {
        filtered.Add(items[i]);  // Can't use AddRange - filtering
    }
}

// ❌ BAD: Using for loop when AddRange would work
using var lease = Buffers<T>.GetList(source.Count, out List<T> result);
for (int i = 0; i < source.Count; i++)
{
    result.Add(source[i]);  // Should use AddRange!
}

// ✅ GOOD: Use AddRange for straight copies
using var lease = Buffers<T>.GetList(source.Count, out List<T> result);
result.AddRange(source);
```

**Decision guide:**

| Scenario                | Use                                             |
| ----------------------- | ----------------------------------------------- |
| Copy all elements as-is | `AddRange(source)`                              |
| Transform each element  | `for` loop + `Add(Transform(item))`             |
| Filter elements         | `for` loop + conditional `Add`                  |
| Transform AND filter    | `for` loop + conditional `Add(Transform(item))` |

---

## Implement IEquatable<T> to Avoid Boxing

Without `IEquatable<T>`, struct comparisons in collections cause boxing:

```csharp
// ❌ BAD: Allocates 4MB for 128K Contains calls!
public struct BadStruct
{
    public int X, Y;
}

var list = new List<BadStruct>();
list.Contains(someStruct);  // Boxes twice per call!

// ✅ GOOD: Zero allocation
public struct GoodStruct : IEquatable<GoodStruct>
{
    public int X, Y;

    public bool Equals(GoodStruct other) => X == other.X && Y == other.Y;

    public override bool Equals(object obj) =>
        obj is GoodStruct other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y);

    public static bool operator ==(GoodStruct a, GoodStruct b) => a.Equals(b);
    public static bool operator !=(GoodStruct a, GoodStruct b) => !a.Equals(b);
}
```

---

## Enum Dictionary Keys Cause Boxing

Enum keys box on every lookup unless you provide a custom comparer:

```csharp
// ❌ BAD: Allocates 4.5MB for 128K lookups!
Dictionary<MyEnum, string> dict = new Dictionary<MyEnum, string>();
var value = dict[MyEnum.SomeValue];  // Boxing per lookup!

// ✅ GOOD: Custom comparer (zero allocation)
public struct MyEnumComparer : IEqualityComparer<MyEnum>
{
    public bool Equals(MyEnum x, MyEnum y) => x == y;
    public int GetHashCode(MyEnum obj) => (int)obj;
}

var dict = new Dictionary<MyEnum, string>(new MyEnumComparer());

// ✅ ALTERNATIVE: Cast to int
Dictionary<int, string> dict = new Dictionary<int, string>();
dict[(int)MyEnum.SomeValue] = "value";
```

---

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

- [high-performance-csharp](./high-performance-csharp.md) - Core performance philosophy and patterns
- [use-pooling](./use-pooling.md) - Collection and buffer pooling patterns
- [use-array-pool](./use-array-pool.md) - Array pool selection guide
- [gc-architecture-unity](./gc-architecture-unity.md) - Unity GC architecture details
- [refactor-to-zero-alloc](./refactor-to-zero-alloc.md) - Migration guide for existing code
