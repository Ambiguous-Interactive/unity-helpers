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

Prefer `AddRange` when the source implements `ICollection<T>` for the destination's exact element
type. That interface gives the bulk-copy path. A source without it uses enumeration instead.

```csharp
// With an ICollection<T> source, AddRange can reserve capacity and copy directly.
using var lease = Buffers<T>.List.Get(out List<T> result);
foreach (T item in source)
{
    result.Add(item);  // May trigger multiple resizes
}

// Preferred when source implements ICollection<T> for this result's T.
using var lease = Buffers<T>.List.Get(out List<T> result);
result.AddRange(source);
```

**Why the `ICollection<T>` path helps:**

1. **Capacity pre-allocation**: If source is `ICollection<T>`, AddRange queries `Count` first and ensures capacity
2. **Bulk copy**: For arrays and `List<T>`, copies directly instead of calling `Add` for each element
3. **Potential zero-allocation**: If source already has the items in contiguous memory, no enumerator needed
4. **Fewer resizes**: Pre-allocated capacity means fewer or no list resizes during population

`List<Derived>` does not implement `ICollection<Base>`. Passing it to `List<Base>.AddRange` uses
the covariant enumerable fallback and boxes its struct enumerator for nonempty transfers. Keep `foreach` over the
concrete typed source for that transfer. Reserve destination capacity when its required size is known.

### When to Use for Loop Instead of AddRange

Use a per-element loop when you must **transform** or **filter** elements. Prefer typed `foreach` unless the body needs the index:

```csharp
// ✅ TRANSFORMATION: Process each element to transform it
using var lease = Buffers<string>.GetList(guids.Length, out List<string> paths);
foreach (string guid in guids)
{
    paths.Add(ConvertGuidToPath(guid));  // Can't use AddRange - transforming each element
}

// ✅ FILTERING: Test each element before adding it
using var lease = Buffers<T>.GetList(items.Length, out List<T> filtered);
foreach (T item in items)
{
    if (item.IsValid)
    {
        filtered.Add(item);  // Can't use AddRange - filtering
    }
}

// Avoid this indexed copy when source implements ICollection<T>.
using var lease = Buffers<T>.GetList(source.Count, out List<T> result);
for (int i = 0; i < source.Count; i++)
{
    result.Add(source[i]);  // Should use AddRange!
}

// Use AddRange for a source implementing ICollection<T> for this result's T.
using var lease = Buffers<T>.GetList(source.Count, out List<T> result);
result.AddRange(source);
```

**Decision guide:**

| Scenario                | Use                                                                               |
| ----------------------- | --------------------------------------------------------------------------------- |
| Copy all elements as-is | `AddRange` for `ICollection<T>`; typed `foreach` for differing list element types |
| Transform each element  | `foreach` + `Add(Transform(item))`                                                |
| Filter elements         | `foreach` + conditional `Add`                                                     |
| Transform AND filter    | `foreach` + conditional `Add(Transform(item))`                                    |

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
