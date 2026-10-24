# Avoid Enumerator Boxing

Use `AsValueEnumerable()` when a hot path receives an `IEnumerable<T>` backed by an array,
`List<T>` or `HashSet<T>`. Use `AsValueDictionaryEnumerable()` for a dictionary received through
an enumerable interface. These views avoid boxing the supported collections' enumerators.
They keep the source collection and read it when the loop runs; they do not make a snapshot.

If you already hold a concrete array, list, hash set or dictionary, start with ordinary `foreach`.
Keeping the concrete type usually gives the compiler the value enumerator directly.

<a id="coverage"></a>

## Choose a traversal

| Source held by your code                                                | Recommended traversal                        | Limit                                                                              |
| ----------------------------------------------------------------------- | -------------------------------------------- | ---------------------------------------------------------------------------------- |
| `T[]`, `List<T>`, `HashSet<T>` or `Dictionary<TKey, TValue>`            | Ordinary `foreach`                           | Element conversions and the loop body can still allocate.                          |
| `IEnumerable<T>` backed by an array, exact list or exact hash set       | `source.AsValueEnumerable()`                 | Derived lists and hash sets use their original enumeration protocol.               |
| `IEnumerable<KeyValuePair<TKey, TValue>>` backed by an exact dictionary | `source.AsValueDictionaryEnumerable()`       | Other sources, including derived dictionaries, use their original protocol.        |
| Lazy query, iterator, custom collection or sorted collection            | Keep its protocol; profile the complete loop | A view cannot remove allocation inside the source iterator or its backing storage. |

An _exact_ list, hash set or dictionary means an instance of that type itself, rather than a
subclass that might define different enumeration behavior.

<a id="results"></a>

## Traverse an interface-held collection

This method accepts any integer sequence. Arrays, exact lists and exact hash sets take the
optimized path; other sources keep their own enumerator and cleanup behavior.

<!-- doc-sample: compiles -->

```csharp
using System.Collections.Generic;
using WallstopStudios.UnityHelpers.Core.Extension;

public static class ScoreTotals
{
    public static long Sum(IEnumerable<int> scores)
    {
        long total = 0;
        foreach (int score in scores.AsValueEnumerable())
        {
            total += score;
        }
        return total;
    }
}
```

`AsValueEnumerable()` treats a null source as empty, so `ScoreTotals.Sum(null)` returns zero.
A default `ValueEnumerable<T>` is also empty. If a missing source is an error in your API,
validate it before the loop, or use `ValueEnumerable<T>.RequireSource(source)` from
`WallstopStudios.UnityHelpers.Core.DataStructure`. That factory preserves a null-source failure
when enumeration starts. Neither form acquires an enumerator when the view is created.

## Traverse dictionary entries

Use the dictionary-specific view when the source exposes key/value pairs. The general
`AsValueEnumerable()` view does not specialize dictionary enumeration.

<!-- doc-sample: compiles -->

```csharp
using System.Collections.Generic;
using WallstopStudios.UnityHelpers.Core.Extension;

public static class InventoryTotals
{
    public static long CountItems(IEnumerable<KeyValuePair<string, int>> inventory)
    {
        long total = 0;
        foreach (KeyValuePair<string, int> entry in inventory.AsValueDictionaryEnumerable())
        {
            total += entry.Value;
        }
        return total;
    }
}
```

The dictionary view also treats null and default views as empty.
`ValueDictionaryEnumerable<TKey, TValue>.RequireSource(source)` preserves null-source failures
when that behavior is required.

## Mutation and lifetime

The views are lazy and keep a reference to the source. Changes made before enumeration are
visible when the loop starts. During enumeration, each collection retains its own mutation
policy: an array reads live elements, while list, hash-set and dictionary enumerators follow
the running framework's rules. The view adds no locking or thread safety.

Prefer `foreach` so cleanup runs when the loop finishes, throws or exits early. If you call
`GetEnumerator()` directly, dispose the result yourself. Do not copy an active enumerator to
create an independent traversal: fallback copies can share the same underlying iterator and its
lifetime. Create another view enumeration when you need another traversal.

<a id="retained-allocation"></a>
<a id="generated-code"></a>
<a id="validation-limits"></a>

## Allocation limits

Avoiding enumerator boxing removes one possible allocation. It does not remove:

- Allocations made by a lazy iterator, LINQ query, custom source or loop body.
- Boxing caused by converting value elements to `object` or another interface.
- Backing storage used by a struct enumerator, such as a sorted tree's traversal stack.
- Cold pool growth, returned strings or collection snapshots created elsewhere in the operation.

Profile the complete call in your target player, with representative source types and sizes.
Editor and host results do not predict every Mono, IL2CPP or WebGL workload. See
[performance measurements](./baseline-tests-performance.md) for how to interpret the published tables,
and [collection extensions](../features/utilities/math-and-extensions.md#value-enumeration)
for additional view examples.
