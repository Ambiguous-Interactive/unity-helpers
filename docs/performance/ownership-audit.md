# Use Pooled Resources Safely

Keep a pooled resource inside a `using` scope. Dispose its lease when the work ends, and stop
using the resource after disposal. If temporary array work fits inside one synchronous callback,
consider `SystemArrayPool<T>.TryWithBuffer` instead: it returns the buffer for you.

See the [pooling guide](../features/utilities/pooling-guide.md) for pool configuration,
retention limits and the complete array APIs.

<a id="coverage"></a>
<a id="current-contracts"></a>
<a id="site-dispositions"></a>

## Choose an ownership shape

| What you need                               | API                                                  | Your responsibility                                                                   |
| ------------------------------------------- | ---------------------------------------------------- | ------------------------------------------------------------------------------------- |
| A reusable object or collection             | `WallstopGenericPool<T>.Get` and `PooledResource<T>` | Dispose the lease; do not retain or use the object after return.                      |
| A temporary array across several statements | `SystemArrayPool<T>.Get` and `PooledArray<T>`        | Use only the logical length, initialize values before reading, and dispose the lease. |
| An array used by one synchronous operation  | `SystemArrayPool<T>.TryWithBuffer`                   | Keep the span inside the callback and check the success result and error.             |
| An action when a scope ends                 | `DisposableScope.Create`                             | Keep the concrete scope type and dispose it; cleanup runs at most once.               |

## Keep the lease and resource together

This example reuses a list to filter values. The list stays inside its lease, and the returned
array is an independent copy that the caller owns.

<!-- doc-sample: compiles -->

```csharp
using System.Collections.Generic;
using WallstopStudios.UnityHelpers.Utils;

public static class PositiveScores
{
    private static readonly WallstopGenericPool<List<int>> Scratch = new(
        producer: () => new List<int>(),
        onGet: list => list.Clear()
    );

    public static int[] Copy(int[] source)
    {
        using PooledResource<List<int>> lease = Scratch.Get(out List<int> values);
        if (source != null)
        {
            foreach (int value in source)
            {
                if (value > 0)
                {
                    values.Add(value);
                }
            }
        }
        return values.ToArray();
    }
}
```

Returning `values` itself would expose an object that is already back in the pool when the
method returns. Returning an iterator, callback or task that captures it has the same problem.
Make an owned copy, or keep the lease alive until every consumer has finished.

Pooling does not make the example allocation-free: the returned array allocates, and a cold pool
or a growing list may allocate too. Choose pooling when reuse reduces the cost of your actual
workload.

## Copies share cleanup

`PooledResource<T>`, `PooledArray<T>`, `DisposableScope` and `SemaphoreLease` use shared disposal
ownership. Copying a handle does not acquire another resource or extend its lifetime. The first
successful disposal claims cleanup; repeated disposal and stale copies cannot release a later
rental through that handle.

These checks protect the return operation. They do not revoke an array or object reference you
copied out of the lease. A reference can still be used incorrectly after return, when another
caller may own the same storage. Keep one clear owner even when copying the handle is safe.

Keep the lease's concrete type in `using` declarations. Assigning a disposable struct to
`IDisposable` or `object` boxes it. For reusable generic cleanup code, constrain the type parameter
to `IDisposable` rather than accepting an interface value. The
[WUH022 analyzer](./analyzers.md#wuh022-boxing-a-disposable-value-type) reports avoidable boxing.

## Array initialization and clearing

A pooled array's physical length can exceed the requested length. `PooledArray<T>.length` is the
logical length; do not pass the full array to an API that reads every physical element.
A zero-length `SystemArrayPool<T>.Get` returns an empty array without acquiring a lease.

Pooled values may contain old data. Write each element before reading it, or request
`clearArray: true` when the algorithm reads before writing, such as visited flags or counters.
Reference-bearing arrays are cleared on return by the package, but other users of the shared
BCL pool can return arrays with different clearing policies. Clearing on return is not a promise
that a newly rented array starts empty.

For an example and details, see [renting an array](../features/utilities/pooling-guide.md#renting-an-array).

## Borrow a buffer for one callback

`TryWithBuffer` exposes a span of exactly the requested length. Reference-bearing elements are
initialized before the callback, and the underlying reference-bearing array is cleared on return.
For numeric buffers, initialize before reading or request input clearing.

The method reports rent, callback and return failures through `false` and `error`. Result overloads
reset the result on failure. It attempts one return after a successful rent; it cannot guarantee
release if the pool's return operation itself fails. Changes your callback makes to external state
remain in place after failure.

The span cannot outlive the synchronous callback. Element objects, explicit copies and external
state can outlive it, so the API does not transfer deep ownership of those objects. See
[borrowing a buffer](../features/utilities/pooling-guide.md#borrowing-a-buffer-for-one-operation)
for a complete result-producing example.

<a id="adoption-requirements"></a>
<a id="release-player-channel-qualification"></a>

## Check the complete operation

Measure acquisition, initialization, useful work and cleanup together. Include a cold pool and
representative peak sizes when checking memory use. A warm rent that allocates nothing can still
leave a large array retained by the shared pool.

When comparing a lease with a callback, include callback creation and captured state in the cost.
A cached static callback can avoid a closure; a capturing lambda may allocate. Neither ownership
shape is a general speed guarantee. Validate behavior and profile your target player before
changing a working lifetime contract.
