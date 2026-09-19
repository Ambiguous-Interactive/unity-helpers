# Avoid Allocations: Collections And Equality

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
