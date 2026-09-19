# High-Performance C# Collection Paths

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

**Rule of thumb:** for a concrete `List<T>`, `T[]` or `Dictionary<K,V>`, check whether the method is native to that type. If the source is `IEnumerable<T>` or a LINQ result, it is a LINQ extension.

### Forbidden Hot Path Patterns

| Pattern                              | Problem                         | Alternative                         |
| ------------------------------------ | ------------------------------- | ----------------------------------- |
| LINQ (`.Where`, `.Select`, `.Any`)   | Iterator + delegate allocation  | `foreach` over the concrete type    |
| `string.Format()` / interpolation    | String allocation               | `StringBuilder` or cache            |
| `new List<T>()`                      | Heap allocation                 | `Buffers<T>.List.Get()`             |
| Lambda capturing locals              | Closure allocation              | Static lambda or explicit loop      |
| Boxing (`object x = struct`)         | Heap allocation                 | Generic methods                     |
| `foreach` through an INTERFACE       | Enumerator boxing (24 bytes)    | Iterate the concrete type           |
| `params` methods                     | Array allocation per call       | Chain 2-arg overloads               |
| Reflection                           | Slow, fragile, uncached         | Direct access, interfaces, generics |
| Hand-rolled hash codes (`* 31`, XOR) | Inconsistent, non-deterministic | `Objects.HashCode()`                |

---

## `foreach` Over a Concrete Collection, Not a Counting Loop

Owner policy, raised in review on PR #685: **prefer `foreach` on anything with a value-typed
enumerator.** An array, `List<T>`, `Dictionary<K, V>` or `HashSet<T>` gives a struct enumerator, so
the walk allocates nothing and the loop says what it does instead of restating index arithmetic.

```csharp
// ✅ CORRECT - List<T>'s enumerator is a struct; nothing allocates
foreach (AttributesComponent attributesComponent in attributes)
{
    attributesComponent.Apply(handle);
}

// ❌ WRONG - the index is used for nothing but xs[i]
for (int index = 0; index < attributes.Count; ++index)
{
    attributes[index].Apply(handle);
}
```

A counting loop is the right shape in exactly four cases:

1. **The body needs the index** — a message, arithmetic, indexing a second collection alongside.
2. **The collection is an interface type** (`IReadOnlyList<T>`, `IList<T>`), whose enumerator boxes
   — the row above in the forbidden-patterns table.
3. **The walk is not a simple forward pass** — reverse, a stride, or a bound that is not the
   collection's own count.
4. **The body mutates the collection or writes back through the indexer.** `foreach` throws on the
   first and cannot do the second for a struct element.

`WUH013` remains **off by default for consumers**; all five package check projects opt in through
`Generator~/CheckProjects.ruleset` ([#671](https://github.com/Ambiguous-Interactive/unity-helpers/issues/671)).
Direct writes, replacement and list mutation are excluded. Indirect callback mutation needs a
scoped suppression explaining why enumeration is unsafe.

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

## Bulk Copy Loses When the Destination Is Covariant

`Array.Copy` and `List<T>.CopyTo` beat a per-element loop **only when the source and destination
element types match exactly**. Store into an array through a covariant view -- a `BoxCollider[]`
handed around as `Component[]` -- and the runtime re-checks the element type on **every** element,
which costs more than the loop it replaced.

Measured on Unity 6000.4.6f1 Mono, building a `BoxCollider[]` from a `List<Component>`, best of three
trials (us):

|   n | `for` loop with `as` | `List.CopyTo` | `Array.Copy` |
| --: | -------------------: | ------------: | -----------: |
|   5 |           **0.0878** |        0.3439 |       0.3692 |
|  50 |           **0.6721** |        2.4317 |       2.2156 |
| 500 |           **6.4056** |       23.3469 |      20.8070 |

The bulk calls are 3.6-3.9x slower, and **the ratio is flat across sizes** -- the tell that this is
per-element cost, not fixed overhead that amortizes. A copy that gets relatively worse as `n` grows
is not a bulk win however it is spelled.

It is also not always legal: casting `TElement[]` to `TSource[]` throws `InvalidCastException` when
`TElement` is an **interface**, because an interface array is not a covariant view of a class array.

```csharp
// Wrong: every store re-checks the element type, and this throws for an interface TElement.
TElement[] result = new TElement[count];
source.CopyTo(0, (TSource[])(object)result, 0, count);

// Right: the loop stores into the array's own exact element type.
TElement[] result = new TElement[count];
for (int i = 0; i < count; ++i)
{
    result[i] = source[i] as TElement;
}
```

**When you want the bulk win, change what fills the buffer, not the copy.** A source already in a
`List<TElement>` makes `CopyTo` an exact-type memmove -- measured 1.15x at five elements rising to
1.41x at five hundred. Reaching that for Unity component queries needs a run-time-closed generic,
which IL2CPP has refused here before.
