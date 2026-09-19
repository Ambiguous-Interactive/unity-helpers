# Forbidden Patterns Part 1

## LINQ Patterns

LINQ methods allocate iterator objects and delegate objects on every call.

| Forbidden                                     | Use Instead                                   | Reason                              |
| --------------------------------------------- | --------------------------------------------- | ----------------------------------- |
| `.Where()`                                    | Explicit `for` loop with condition            | Allocates WhereIterator + delegate  |
| `.Select()`                                   | Explicit `for` loop with transform            | Allocates SelectIterator + delegate |
| `.Any()`                                      | Explicit `for` loop with `break`              | Allocates delegate                  |
| `.First()` / `.FirstOrDefault()`              | Explicit `for` loop with `break`              | Allocates delegate                  |
| `.ToList()` / `.ToArray()`                    | Use pooled collection                         | Creates new collection              |
| `.OrderBy()` / `.OrderByDescending()`         | `List.Sort()` with cached comparer            | Allocates buffer + comparer         |
| `.Count()` on IEnumerable                     | Track count manually or use `.Count` property | May allocate enumerator             |
| `.Sum()` / `.Average()` / `.Min()` / `.Max()` | Explicit loop with accumulator                | Allocates delegate                  |
| Chained LINQ (`.Where().Select().ToList()`)   | Single explicit loop                          | Multiple allocations compound       |

### LINQ vs Native Collection Methods

**Critical distinction**: Some methods that look like LINQ are actually native collection methods and do NOT allocate.

| Method Call                                   | Is LINQ? | Allocates?           | Notes                                        |
| --------------------------------------------- | -------- | -------------------- | -------------------------------------------- |
| `List<T>.ToArray()`                           | No       | Yes (new array)      | Native method, not System.Linq               |
| `IEnumerable<T>.ToArray()` (System.Linq)      | Yes      | Yes (array + buffer) | LINQ extension, avoid in hot paths           |
| `Array.Empty<T>()`                            | No       | No                   | Cached singleton, always safe                |
| `List<T>.Contains()`                          | No       | No                   | Native method                                |
| `IEnumerable<T>.Contains()` (System.Linq)     | Yes      | Maybe                | LINQ extension, may allocate enumerator      |
| `string.Concat(IEnumerable<string>)`          | No       | Yes (new string)     | Native BCL method, not LINQ                  |
| `List<T>.Exists(Predicate<T>)`                | No       | No                   | Native method, delegate passed directly      |
| `IEnumerable<T>.Any(Func<T,bool>)`            | Yes      | Yes (enumerator)     | LINQ extension, allocates                    |
| `Dictionary<K,V>.TryGetValue()`               | No       | No                   | Native method                                |
| `IEnumerable<T>.ToDictionary()` (System.Linq) | Yes      | Yes                  | LINQ extension, creates new dict + allocates |

**Rule of thumb**: If calling on a concrete type (`List<T>`, `Dictionary<K,V>`, `T[]`), check if it is a native method first. If calling on `IEnumerable<T>`, assume it is LINQ.

---

## Collection Building Patterns

| Forbidden                        | Use Instead                     | Reason                                    |
| -------------------------------- | ------------------------------- | ----------------------------------------- |
| `foreach` + `.Add()` on unknown  | `.AddRange()` when available    | `AddRange` pre-allocates and uses memcopy |
| `for` loop + `.Add()` repeatedly | Pre-size with capacity + `.Add` | Avoids resize/copy on every add           |
| Building without known capacity  | Pass capacity to constructor    | Avoids multiple internal resizes          |

### AddRange vs Foreach+Add

```csharp
// Forbidden - O(n) individual Add calls, potential resizes
foreach (var item in source)
{
    destination.Add(item);
}

// Preferred - Single operation, pre-allocates, uses Array.Copy
destination.AddRange(source);

// If source is IEnumerable<T> (not ICollection<T>), AddRange may still enumerate
// In that case, prefer explicit capacity + Add pattern:
destination.Capacity = destination.Count + expectedCount;
for (int i = 0; i < source.Length; i++)
{
    destination.Add(source[i]);
}
```

---

## Collection Iteration

| Forbidden                             | Use Instead                        | Reason                      |
| ------------------------------------- | ---------------------------------- | --------------------------- |
| Forbidden                             | Use Instead                        | Reason                      |
| ------------------------------------- | ---------------------------------- | --------------------------- |
| `foreach` over `IEnumerable<T>`       | Iterate the concrete type          | Boxes enumerator (24 bytes) |
| `foreach` over `IList<T>` / `ISet<T>` | Iterate the concrete type          | Boxes enumerator (24 bytes) |
| A field or parameter typed `IList<T>` | Type it `List<T>` where you own it | The boxing is at the TYPE   |

`foreach` over a **concrete** `List<T>`, `Dictionary<K,V>`, `HashSet<T>` or array does **not**
allocate: the C# compiler binds to the type's own struct enumerator by duck typing. Measured on `6000.4.6f1`, 2,000,000 iterations, against a known allocator that moved the counter by 54.7 MB: `foreach` over a concrete `List<T>` allocates **24,576 bytes** -- the same as a `for` indexer loop, and the same as doing nothing. The identical loop over the same list typed as `IEnumerable<T>` allocates **5,709,824 bytes**.
Do not rewrite a concrete `foreach` into a `for` loop for allocation reasons -- there is nothing to
save, and the indexer form does not work on the non-indexable collections.

### Struct Enumerator Pattern

```csharp
// Instead of foreach on non-array collections:
var enumerator = collection.GetEnumerator();
while (enumerator.MoveNext())
{
    var element = enumerator.Current;
    // Process element
}
```

---

## Memory Allocation Traps

| Forbidden                               | Use Instead                         | Reason                   |
| --------------------------------------- | ----------------------------------- | ------------------------ |
| `new List<T>()` in hot path             | Use `Buffers<T>.List.Get()`         | Pool avoids allocation   |
| `new Dictionary<K,V>()` in hot path     | Use `Buffers<K,V>.Dictionary.Get()` | Pool avoids allocation   |
| `new StringBuilder()` in hot path       | Use `Buffers.StringBuilder.Get()`   | Pool avoids allocation   |
| String concatenation in loops           | Use pooled `StringBuilder`          | O(n²) allocations        |
| `$"interpolated {string}"` in hot paths | Use `StringBuilder.Append()` chain  | Hidden allocations       |
| `params` method calls                   | Chain 2-argument overloads          | Array allocated per call |
| Delegate assignment in loops            | Assign delegate once outside loop   | 52 bytes per iteration   |
| Closure capturing local variable        | Use explicit loop or static lambda  | Allocates closure class  |

---

## Boxing Traps

| Forbidden                        | Use Instead                       | Reason                |
| -------------------------------- | --------------------------------- | --------------------- |
| Struct in `Dictionary<TEnum, V>` | Custom `IEqualityComparer<TEnum>` | Boxing per lookup     |
| Struct without `IEquatable<T>`   | Implement `IEquatable<T>`         | Boxing per comparison |
| Value type to `object` parameter | Use generic method                | Boxing (12+ bytes)    |
| Interface boxing (non-generic)   | Use generic constraint            | Boxing (12+ bytes)    |

---

## Hash Code Patterns

**CRITICAL**: Hash code implementations must be deterministic across processes and Unity versions. The project uses `Objects.HashCode()` for all hash code generation.

| Forbidden                          | Use Instead          | Reason                                       |
| ---------------------------------- | -------------------- | -------------------------------------------- |
| `System.HashCode.Combine()`        | `Objects.HashCode()` | Non-deterministic between processes/restarts |
| `obj.GetHashCode()` for custom     | `Objects.HashCode()` | May be non-deterministic for Unity types     |
| `hash * 31 + field.GetHashCode()`  | `Objects.HashCode()` | Hand-rolled patterns are error-prone         |
| `hash ^ field.GetHashCode()`       | `Objects.HashCode()` | XOR patterns have poor distribution          |
| `hash * 397 ^ field.GetHashCode()` | `Objects.HashCode()` | ReSharper pattern, still non-deterministic   |
| `HashCode.Add()` builder pattern   | `Objects.HashCode()` | System.HashCode is non-deterministic         |

### Why System.HashCode is Forbidden

`System.HashCode.Combine()` uses per-process random seed initialization:

```csharp
// This is FORBIDDEN - hash value changes between process restarts
int hash = HashCode.Combine(name, value, type);
```

This causes problems for:

- Save files (hash stored, then different on reload)
- Network synchronization (different hash on different machines)
- Reproducible testing (tests may pass/fail non-deterministically)
- Caching (cache keys invalid after restart)

### Correct Pattern

Use `Objects.HashCode()` from this project, which provides deterministic hashing:

```csharp
// Correct - deterministic across processes and platforms
public override int GetHashCode()
{
    return Objects.HashCode(_name, _value, _type);
}

// For structs with IEquatable<T>
public readonly struct MyStruct : IEquatable<MyStruct>
{
    private readonly string _name;
    private readonly int _value;

    public override int GetHashCode() => Objects.HashCode(_name, _value);
    public bool Equals(MyStruct other) => _name == other._name && _value == other._value;
    public override bool Equals(object obj) => obj is MyStruct other && Equals(other);
}
```

See [ObjectsHashCodePattern.cs](../code-samples/patterns/ObjectsHashCodePattern.cs) for complete examples.

---
