# Avoid Allocations: Value Types And Closures

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
