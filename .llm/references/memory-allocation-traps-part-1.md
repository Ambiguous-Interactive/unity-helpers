# memory-allocation-traps - Part 1

## Split Content

**Trigger**: When reviewing code for hidden allocations or when code unexpectedly causes GC pressure. This skill catalogs non-obvious allocation sources in Unity/C#.

---

## Quick Reference: Allocation Costs

| Trap                           | Bytes Per Occurrence | Risk Level |
| ------------------------------ | -------------------- | ---------- |
| `foreach` through an interface | 24 bytes             | 🔴 High    |
| LINQ `.Where()`                | 32+ bytes            | 🔴 High    |
| LINQ `.Select()`               | 32+ bytes            | 🔴 High    |
| Closure capturing local        | 32+ bytes            | 🔴 High    |
| `params` method call           | 24+ bytes            | 🟡 Medium  |
| Delegate in loop               | 52 bytes             | 🔴 High    |
| Enum dictionary lookup         | 24 bytes             | 🟡 Medium  |
| Struct without `IEquatable<T>` | 24+ bytes            | 🟡 Medium  |
| String concatenation           | Varies               | 🔴 High    |
| Boxing to `object`             | 12+ bytes            | 🟡 Medium  |

---

## Trap 1: foreach through an interface

The boxing is decided by the **static type you iterate**, not by `foreach`. `foreach` binds to
whatever `GetEnumerator()` the type exposes; on a concrete collection that is a struct and nothing is
allocated, and through an interface it is `IEnumerator<T>`, which is boxed once per loop.

```csharp
// ❌ BAD: 24 bytes per loop -- the interface forces IEnumerator<T>
IReadOnlyList<Item> items = _items;
foreach (Item item in items) { Process(item); }

// ✅ GOOD: zero allocation -- List<T>.Enumerator is a struct
foreach (Item item in _items) { Process(item); }
```

Measured on `6000.4.6f1`, 2,000,000 iterations over a 4-element list, against a known allocator that
moved the counter by 54.7 MB:

| iterated as                    | bytes         |
| ------------------------------ | ------------- |
| `int[]`                        | 12,288        |
| `List<T>`                      | 24,576        |
| `for` with indexer             | 24,576        |
| `list.GetEnumerator()` by hand | 20,480        |
| `Dictionary<K,V>`              | 20,480        |
| `Dictionary<K,V>.Values`       | 16,384        |
| **`IEnumerable<T>`**           | **5,709,824** |

Everything concrete is one noise band; only the interface allocates. Two consequences:

- **Do not rewrite a concrete `foreach` into a `for` loop to save allocation.** There is none to
  save, `for` does not work on `HashSet`/`Dictionary`, and the indexer form is harder to read.
- **Taking the enumerator by hand is a no-op.** It measured 20,480 bytes against `foreach`'s 24,576 --
  the same band, because `foreach` already does exactly that.

The rule to apply instead is about **types**: a field, parameter or local typed `IEnumerable<T>` /
`IList<T>` / `IReadOnlyList<T>` allocates on every iteration of it. Where you own the declaration and
the hot path iterates it, declare the concrete type.

---

## Trap 2: LINQ Methods

All LINQ methods allocate iterator objects and often delegate objects:

```csharp
// ❌ BAD: Each method allocates
var result = enemies
    .Where(e => e.IsAlive)     // Iterator + delegate allocation
    .Select(e => e.Position)   // Another iterator + delegate
    .ToList();                 // New List allocation

// ✅ GOOD: Explicit loop with pooling
using var lease = Buffers<Vector3>.List.Get(out List<Vector3> result);
for (int i = 0; i < enemies.Count; i++)
{
    if (enemies[i].IsAlive)
    {
        result.Add(enemies[i].Position);
    }
}
```

### LINQ Allocation Breakdown

| Method       | Allocations                          |
| ------------ | ------------------------------------ |
| `.Where()`   | WhereIterator + delegate             |
| `.Select()`  | SelectIterator + delegate            |
| `.Any()`     | Delegate (no iterator if early exit) |
| `.First()`   | Delegate                             |
| `.ToList()`  | New `List<T>`                        |
| `.ToArray()` | New `T[]`                            |
| `.Count()`   | Enumeration (may allocate)           |
| `.Sum()`     | Delegate                             |
| `.OrderBy()` | Buffer + comparer                    |

---

## Trap 3: Closures Capturing Variables

When a lambda references a local variable or `this`, it creates a closure object:

```csharp
// ❌ BAD: Captures 'searchId' - allocates closure class
int searchId = GetTargetId();
Item found = list.Find(item => item.Id == searchId);

// ❌ BAD: Captures 'this' implicitly
items.RemoveAll(x => x.Owner == this);

// ✅ GOOD: Explicit loop, no closure
int searchId = GetTargetId();
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

### Static Lambdas (C# 9+)

```csharp
// ✅ Static lambda - compiler error if it tries to capture
items.Sort(static (a, b) => a.Priority.CompareTo(b.Priority));

// ✅ Cached delegate - single allocation at class load
private static readonly Comparison<Item> PriorityComparison =
    static (a, b) => a.Priority.CompareTo(b.Priority);

items.Sort(PriorityComparison);
```

---

## Trap 4: params Methods

Methods with `params` allocate an array for every call:

```csharp
// Method signature
public static T Max<T>(params T[] values) { }

// ❌ BAD: Allocates array (36 bytes for 3 ints)
int max = Mathf.Max(a, b, c);

// ✅ GOOD: Chain 2-argument overloads
int max = Mathf.Max(Mathf.Max(a, b), c);
```

### Common params Traps

| Method                         | Allocation               |
| ------------------------------ | ------------------------ |
| `string.Format(fmt, params)`   | Array + formatted string |
| `Debug.LogFormat(fmt, params)` | Array + formatted string |
| `Mathf.Max(params)`            | Array                    |
| `Path.Combine(params)`         | Array + result string    |

---
