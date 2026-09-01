# Core Data Structures: Concepts, Usage, and Trade-offs

## TL;DR: What Problem This Solves

- Pick the right container for performance and clarity: ring buffers, deques, heaps, tries, sparse sets, etc.
- Each section gives a plain‑language “Use for / Pros / Cons” and a tiny code snapshot to copy.
- When in doubt, prefer simple .NET types; use these when you hit performance or ergonomics limits.

This guide covers several foundational data structures used across the library and when to use them.

## Cyclic Buffer (Ring Buffer)

- What it is: Fixed-capacity circular array with head/tail indices that wrap.
- Use for: Streaming data, fixed-size queues, audio/network buffers.
- Operations: enqueue/dequeue in O(1); overwriting old data optional.
- Pros: Constant-time, cache-friendly, no reallocations at steady size.
- Cons: Fixed capacity unless resized; must handle wrap-around.

![Cyclic Buffer](../../images/utilities/data-structures/cyclic-buffer.svg)

When to use vs. DotNET queues

- Prefer `CyclicBuffer<T>` over `Queue<T>` when you want bounded memory with O(1) push/pop at both ends and predictable behavior under backpressure (drop/overwrite oldest, or pop proactively).
- Use `Queue<T>` when you need unbounded growth without wrap semantics.

API snapshot

<!-- doc-sample: compiles -->

```csharp
using WallstopStudios.UnityHelpers.Core.DataStructure;

var rb = new CyclicBuffer<int>(capacity: 4);
rb.Add(10); rb.Add(20); rb.Add(30);

// Pop from front/back
if (rb.TryPopFront(out var first)) { /* first == 10 */ }
if (rb.TryPopBack(out var last))  { /* last == 30  */ }

// Remove by value / predicate
rb.Add(40); rb.Add(50);
rb.Remove(20);                  // O(n) compacting via temp buffer
rb.RemoveAll(x => x % 2 == 0);  // remove evens

// Resize in place (may drop tail elements)
rb.Resize(8);
```

Tips and pitfalls

- Know your overflow policy. `Add` will wrap and overwrite the oldest only once capacity is reached; use `TryPopFront` periodically to keep buffer from evicting data you still need.
- Iteration enumerates logical order starting at the head, not the underlying storage order.
- `Remove`/`RemoveAll` are O(n); keep hot paths to `Add`/`TryPop*` when possible.

## Deque (Double-Ended Queue)

- What it is: Queue with efficient push/pop at both ends.
- Use for: Sliding windows, BFS frontiers, task schedulers.
- Operations: push_front/push_back/pop_front/pop_back in amortized O(1).
- Pros: Flexible ends; generalizes queue and stack behavior.
- Cons: Implementation complexity for block-based layouts.

![Deque](../../images/utilities/data-structures/deque.svg)

When to use vs `Queue<T>` / `Stack<T>`

- Prefer `Deque<T>` when you need both `push_front` and `push_back` in O(1) amortized.
- Use `Queue<T>` for simple FIFO; `Stack<T>` for LIFO only.

![Queue vs Deque](../../images/utilities/data-structures/deque-queue.svg)

API snapshot

<!-- doc-sample: compiles -->

```csharp
using WallstopStudios.UnityHelpers.Core.DataStructure;

var dq = new Deque<string>(capacity: 8);
dq.PushFront("start");
dq.PushBack("end");

if (dq.TryPopFront(out var a)) { /* a == "start" */ }
if (dq.TryPopBack(out var b))  { /* b == "end"   */ }

// Peeking without removal
dq.PushBack("x");
if (dq.TryPeekFront(out var f)) { /* f == "x" */ }
```

Tips

- Capacity grows geometrically as needed; call `TrimExcess()` after spikes to return memory.
- Indexer is in logical order (0 is front, Count-1 is back).

## Binary Heap (Priority Queue)

- What it is: Array-backed binary tree maintaining heap-order (min/max).
- Use for: Priority queues, Dijkstra/A\*, event simulation.
- Operations: push/pop in O(log n); peek O(1); build-heap O(n).
- Pros: Simple; great constant factors; contiguous memory.
- Cons: Not ideal for decrease-key unless augmented.

![Heap](../../images/utilities/data-structures/heap.svg)

When to use vs `SortedSet<T>`

- Prefer `Heap<T>`/`PriorityQueue<T>` for frequent push/pop top in O(log n) with low overhead.
- Use `SortedSet<T>` for ordered iteration and fast remove arbitrary item (by key), at higher constants.

API snapshot (Heap)

<!-- doc-sample: compiles -->

```csharp
using WallstopStudios.UnityHelpers.Core.DataStructure;

var minHeap = new Heap<int>();         // default comparer => min-heap
minHeap.Add(5); minHeap.Add(2); minHeap.Add(9);

if (minHeap.TryPop(out var top)) { /* top == 2 */ }
if (minHeap.TryPeek(out var peek)) { /* peek == 5 */ }
```

API snapshot (PriorityQueue)

<!-- doc-sample: compiles -->

```csharp
using WallstopStudios.UnityHelpers.Core.DataStructure;

var pq = PriorityQueue<(int priority, string job)>.CreateMin(
    capacity: 32
);
pq.Enqueue((1, "emergency"));
pq.Enqueue((5, "later"));
pq.TryDequeue(out var item); // (1, "emergency")
```

Tips

- Use `PriorityQueue<T>.CreateMax()` to flip ordering without writing a custom comparer.
- Heaps don’t support efficient decrease-key out of the box; reinsert updated items instead.

## Disjoint Set (Union-Find)

- What it is: Structure tracking partition of elements into sets.
- Use for: Connectivity, Kruskal’s MST, percolation, clustering.
- Operations: union/find in amortized near O(1) with path compression + union by rank.
- Pros: Extremely fast for bulk unions/finds; minimal memory.
- Cons: Not suited for deletions or enumerating members without extra indexes.

![Disjoint Set](../../images/utilities/data-structures/disjoint-set.svg)

When to use

- Batch connectivity queries where the graph mutates only via unions (no deletions): MST (Kruskal), island labeling, clustering, grouping by equivalence.

API snapshot (int-based)

<!-- doc-sample: compiles -->

```csharp
using WallstopStudios.UnityHelpers.Core.DataStructure;

var uf = new DisjointSet(n: 6);        // elements 0..5
uf.TryUnion(0, 1);
uf.TryUnion(2, 3);
uf.TryIsConnected(0, 3, out var conn); // false
uf.TryUnion(1, 3);
uf.TryIsConnected(0, 3, out conn);     // true
```

API snapshot (generic)

<!-- doc-sample: compiles -->

```csharp
using WallstopStudios.UnityHelpers.Core.DataStructure;

var people = new[] { "Ana", "Bo", "Cy" };
var uf = new DisjointSet<string>(people);
uf.TryUnion("Ana", "Bo");
uf.TryIsConnected("Ana", "Cy", out var conn); // false
```

Tips

- Use the generic variant to work with domain objects; internally it maps to indices.
- No deletions: rebuild if you need dynamic splits.

## Sparse Set

- What it is: Two arrays (sparse and dense) enabling O(1) membership checks and iteration over active items.
- Use for: ECS entity sets, fast presence checks with dense iteration.
- Operations: insert/remove/contains in O(1); iterate dense in O(n_active).
- Pros: Very fast, cache-friendly on dense array; stable indices optional.
- Cons: Requires ID space for indices; sparse array sized by max ID.

![Sparse Set](../../images/utilities/data-structures/sparse-set.svg)

When to use vs `HashSet<T>`

- Prefer `SparseSet` when your IDs are small integers (0..N) and you need O(1) contains with dense, cache-friendly iteration over active items.
- Use `HashSet<T>` for arbitrary keys, very large/unbounded key spaces, or when memory for `sparse` cannot scale to the max ID.

API snapshot (int IDs)

```csharp
using WallstopStudios.UnityHelpers.Core.DataStructure;

var set = new SparseSet(capacity: 1000); // supports IDs in [0,1000)
set.TryAdd(42);                   // returns bool indicating success
bool has42 = set.Contains(42);    // true
set.TryRemove(42);                // returns bool indicating success

// Dense iteration order over active IDs
foreach (int id in set) { /* ... */ }
```

API snapshot (generic values)

<!-- doc-sample: compiles -->

```csharp
public sealed class MyComponent { }

var set = new SparseSet<MyComponent>(capacity: 1024);
set.TryAdd(new MyComponent()); // the element IS the key, returns bool
var comp = set[0];             // dense index 0 value
```

Tips

- Capacity equals the universe size for keys; do not set capacity larger than your maximum possible ID.
- Deletions swap-with-last in dense array; dense order is not stable.

## Trie (Prefix Tree)

- What it is: Tree keyed by characters for efficient prefix-based lookup.
- Use for: Autocomplete, spell-checking, dictionary prefix queries.
- Operations: insert/search O(m) where m is key length.
- Pros: Predictable per-character traversal; supports prefix enumeration.
- Cons: Memory overhead vs hash tables; compact with radix/compressed tries.

![Trie](../../images/utilities/data-structures/trie.svg)

When to use vs dictionaries

- Prefer `Trie` for lots of prefix queries and auto-complete where per-character traversal beats repeated hashing.
- Use `Dictionary<string, T>` when you rarely do prefix scans and primarily need exact lookup.

API snapshot

<!-- doc-sample: compiles -->

```csharp
using WallstopStudios.UnityHelpers.Core.DataStructure;

// Words only
var words = new Trie(new[] { "cat", "car", "dog" });
bool hasDog = words.Contains("dog");          // true
List<string> outWords = new();
words.GetWordsWithPrefix("ca", outWords);     // outWords = ["cat","car"]

// Key -> Value
var items = new Dictionary<string, int> { ["apple"] = 1, ["apricot"] = 2 };
var trie = new Trie<int>(items);
if (trie.TryGetValue("apricot", out var v)) { /* 2 */ }
List<int> values = new();
trie.GetValuesWithPrefix("ap", values);       // values = [1,2]
```

Tips

- Build once with full vocabulary; Tries here are immutable post-construction (no public insert) to stay compact.
- Memory scales with total characters; very large alphabets or long keys benefit from compressed/radix tries (not included here).

## Bitset

- What it is: Packed array of bits for boolean sets and flags.
- Use for: Fast membership bitmaps, masks, filters, small Bloom filters.
- Operations: set/clear/test O(1); bitwise ops on words are vectorizable.
- Pros: Extremely compact; very fast bitwise operations.
- Cons: Fixed maximum size unless dynamically extended; needs index mapping.

![Bitset](../../images/utilities/data-structures/bitset.svg)

When to use vs `bool[]` / `HashSet<int>`

- Prefer `BitSet` for dense boolean sets with fast bitwise ops (masks, layers, filters) and compact storage.
- Use `bool[]` for tiny, fixed schemas you manipulate rarely; use `HashSet<int>` for sparse, very large universes.

API snapshot

<!-- doc-sample: compiles -->

```csharp
using WallstopStudios.UnityHelpers.Core.DataStructure;

var bits = new BitSet(initialCapacity: 128);
bits[3] = true;                 // indexer calls TrySet/TryClear
bits.TrySet(64);
bool any = bits.Any();          // any bit set?
int count = bits.CountSetBits();

// Bitwise ops
bits.Not();        // invert
bits.LeftShift(2); // multiply-by-4 mask window
bits.RightShift(1);
```

Tips

- Capacity grows automatically when setting beyond bounds; prefer sizing appropriately upfront for fewer resizes.
- Left/Right shift drop/zero-fill at the edges; use with care if capacity is small.

## IntMap (Int-Keyed Open-Addressing Map)

- What it is: `IntMap<TValue>`, a hash table with linear probing, a power-of-two table and keys compared as raw integers, with no `IEqualityComparer` indirection anywhere on the lookup path.
- Use for: read-mostly lookups keyed by entity ids, network ids, slot indices or any dense/sparse int id.
- Operations: `TryGet`, `TrySet`, indexer, `Remove`, `Clear`, enumeration; growth at a 0.5 load factor.
- Pros: measured 1.97x–2.19x faster than `Dictionary<int,int>` hit-heavy lookups on Unity 6000.4 editor Mono; 1.26x–1.65x at a 50% miss rate.
- Cons: misses are where probing loses; a miss-dominated workload should stay on `Dictionary<int,int>`; the two lowest key values (`int.MinValue` and one above) are internal slot markers, refused by `TrySet` without throwing.

```csharp
using WallstopStudios.UnityHelpers.Core.DataStructure;

var map = new IntMap<HealthState>();
map.TrySet(entityId, HealthState.Poisoned);
if (map.TryGet(entityId, out HealthState state)) { /* ... */ }
map.TrySet(entityId, HealthState.Healthy);   // overwrite in place
map.Remove(entityId);                        // leaves a tombstone, compacted at next resize
```

Tips and pitfalls

- The published margins come from `IntMapPerformanceTests` in `Tests/Runtime/Performance`: counterbalanced orderings with a settled heap per slot, so the numbers describe the code rather than the machine. Run it yourself before shipping a claim of your own; a run whose spread exceeds 3% is ignored rather than reported.
- **Those numbers are editor Mono only.** IL2CPP has not been measured, and part of the margin is Mono not devirtualizing `EqualityComparer<int>.Default` — which an AOT compiler may do. See [#578](https://github.com/Ambiguous-Interactive/unity-helpers/issues/578).
- Removed entries become tombstones that still occupy slots until growth compacts them away. Churning removals pays for its own churn.
- Keys below `IntMap<TValue>.MinimumAllowedKey` name slot states and are refused by `TrySet`; the indexer throws for them.

## Quick Selection Guide

- Need O(1) membership and dense iteration: Sparse Set
- Need priority scheduling: Binary Heap
- Need two-ended queueing: Deque
- Need circular fixed-capacity buffer: Cyclic Buffer
- Need prefix search: Trie
- Need compact boolean set: Bitset
- Need dynamic connectivity: Disjoint Set
- Need auto-evicting key-value store: Cache
- Need the fastest int-keyed lookups for a hit-heavy workload: IntMap

Common pitfalls

- Sparse Set capacity equals the max key + 1; allocating for huge key spaces is memory-heavy.
- Heaps don’t give you sorted iteration; popping yields order, but enumerating the heap array is not sorted.
- Cyclic Buffer `Remove`/`RemoveAll` are O(n); keep hot paths to TryPop/Add.

## Complexity Summary

- Cyclic Buffer: enqueue/dequeue O(1)
- Deque: push/pop ends amortized O(1)
- Heap: push/pop O(log n), peek O(1)
- Disjoint Set: union/find ~O(1) amortized (with heuristics)
- Sparse Set: insert/remove/contains O(1), iterate dense O(n_active)
- Trie: insert/search O(m)
- Bitset: set/test O(1), bitwise ops O(n/word_size)
- Cache: get/set/remove O(1), expiration scan O(n)
- IntMap: TryGet/TrySet/Remove amortized O(1), O(1) worst-case memory per slot; speed depends on hit rate

Notes on constants

- All structures are allocation-aware (enumerators avoid boxing; internal buffers reuse pools where applicable). Real-world throughput is often more important than asymptotic notation; these implementations are tuned for Unity/IL2CPP.

---

## Cache (LRU/LFU/SLRU/FIFO/Random)

- What it is: High-performance key-value cache with configurable eviction policies and time-based expiration.
- Use for: Memoization, asset lookups, network response caching, session data.
- Operations: get/set/remove in O(1); supports weighted entries, auto-loading, and statistics.
- Pros: Multiple eviction strategies; fluent builder API; jitter for thundering herd prevention.
- Cons: Memory overhead for tracking access patterns; requires configuration for optimal performance.

```mermaid
flowchart LR
    subgraph Cache Operations
        Get[Get] --> Hit{Hit?}
        Hit -->|Yes| Return[Return Value]
        Hit -->|No| Load[Load/Miss]
        Set[Set] --> Evict{At Capacity?}
        Evict -->|Yes| Policy[Apply Eviction Policy]
        Evict -->|No| Store[Store Entry]
        Policy --> Store
    end
```

### When to use

- Use `Cache<TKey, TValue>` when you need automatic eviction, expiration, or access tracking.
- Use `Dictionary<TKey, TValue>` for simple lookups without size limits or expiration.
- Use `ConcurrentDictionary<TKey, TValue>` for thread-safe access without cache semantics.

### Eviction Policies

| Policy     | Description                                     | Best For                             |
| ---------- | ----------------------------------------------- | ------------------------------------ |
| **LRU**    | Evicts least recently used entry                | General purpose, most common         |
| **SLRU**   | Segmented LRU with probation/protected segments | High-throughput with scan resistance |
| **LFU**    | Evicts least frequently used entry              | Stable access patterns               |
| **FIFO**   | First in, first out eviction                    | Render caches, predictable eviction  |
| **Random** | Random eviction                                 | Low overhead, uniform access         |

### API snapshot (Basic usage)

```csharp
using WallstopStudios.UnityHelpers.Core.DataStructure;

// Simple LRU cache with fluent builder
Cache<string, UserData> cache = CacheBuilder<string, UserData>.NewBuilder()
    .MaximumSize(1000)
    .ExpireAfterWrite(TimeSpan.FromMinutes(5))
    .EvictionPolicy(EvictionPolicy.Lru)
    .Build();

// Basic operations
cache.Set("user1", userData);
if (cache.TryGet("user1", out UserData data))
{
    // Use cached data
}

cache.TryRemove("user1");
cache.Clear();
```

### API snapshot (Loading cache with auto-compute)

```csharp
using WallstopStudios.UnityHelpers.Core.DataStructure;

// Loading cache - auto-computes missing values
Cache<int, ExpensiveResult> loadingCache = CacheBuilder<int, ExpensiveResult>.NewBuilder()
    .MaximumSize(100)
    .Build(key => ComputeExpensiveResult(key));

// GetOrAdd uses the loader when key is missing
ExpensiveResult result = loadingCache.GetOrAdd(42, null);
```

### API snapshot (Advanced configuration)

<!-- doc-sample: compiles -->

```csharp
using WallstopStudios.UnityHelpers.Core.DataStructure;

public sealed class PathResult { }

Cache<string, PathResult> pathCache = CacheBuilder<string, PathResult>.NewBuilder()
    .MaximumSize(2000)
    .InitialCapacity(64)                    // Start small, grow as needed
    .ExpireAfterWrite(300f)                 // 5 minutes TTL
    .WithJitter(12f)                        // Prevent thundering herd
    .EvictionPolicy(EvictionPolicy.Slru)    // Scan-resistant eviction
    .ProtectedRatio(0.8f)                   // 80% protected segment for SLRU
    .AllowGrowth(1.5f, 4000)                // Grow 1.5x up to 4000 when thrashing
    .RecordStatistics()                     // Enable hit/miss tracking
    .OnEviction((key, value, reason) => Debug.Log($"Evicted {key}: {reason}"))
    .OnGet((key, value) => Debug.Log($"Cache hit: {key}"))
    .OnSet((key, value) => Debug.Log($"Cache set: {key}"))
    .Build();

// Access statistics
CacheStatistics stats = pathCache.GetStatistics();
Debug.Log($"Hit rate: {stats.HitRate:P1}, Evictions: {stats.EvictionCount}");
```

### API snapshot (Weighted caching)

<!-- doc-sample: compiles -->

```csharp
using WallstopStudios.UnityHelpers.Core.DataStructure;

// Weight-based eviction (e.g., by byte size)
Cache<string, Texture2D> textureCache = CacheBuilder<string, Texture2D>.NewBuilder()
    .MaximumWeight(100_000_000)  // 100 MB total
    .Weigher((key, tex) => tex.width * tex.height * 4)  // Bytes per texture
    .ExpireAfterAccess(TimeSpan.FromMinutes(10))        // Sliding window
    .Build();
```

### CachePresets (Ready-to-use configurations)

Use `CachePresets` for common scenarios:

```csharp
using WallstopStudios.UnityHelpers.Core.DataStructure;

// Short-lived cache: 100 entries, 60s TTL, LRU
Cache<int, Vector3> tempCache = CachePresets.ShortLived<int, Vector3>().Build();

// Long-lived cache: 1000 entries, no TTL, LRU
Cache<string, GameObject> prefabCache = CachePresets.LongLived<string, GameObject>()
    .Build(key => Resources.Load<GameObject>($"Prefabs/{key}"));

// Session cache: 500 entries, 30 min sliding window, LRU
Cache<string, InventoryData> sessionCache = CachePresets.SessionCache<string, InventoryData>().Build();

// High-throughput: 2000 entries, 5 min TTL, SLRU, growth enabled
Cache<(Vector3, Vector3), NavMeshPath> pathCache = CachePresets.HighThroughput<(Vector3, Vector3), NavMeshPath>().Build();

// Render cache: 200 entries, 30s TTL, FIFO
Cache<int, MaterialPropertyBlock> renderCache = CachePresets.RenderCache<int, MaterialPropertyBlock>().Build();

// Network cache: 100 entries, 2 min TTL with jitter, LRU
Cache<string, JsonResponse> apiCache = CachePresets.NetworkCache<string, JsonResponse>().Build();
```

### Cache Properties and Methods

| Member                      | Description                                                                   |
| --------------------------- | ----------------------------------------------------------------------------- |
| `Count`                     | Current number of entries                                                     |
| `Size`                      | Total weight (weighted caching) or count                                      |
| `MaximumSize`               | Configured maximum entries                                                    |
| `Capacity`                  | Current internal capacity (may be < MaximumSize)                              |
| `Keys`                      | Enumerable of all keys (allocates state machine)                              |
| `TryGet(key, out value)`    | Returns true if key exists and not expired                                    |
| `Set(key, value)`           | Adds or updates an entry                                                      |
| `GetOrAdd(key, factory)`    | Gets existing or computes and caches new value (factory optional with loader) |
| `TryRemove(key)`            | Removes entry if present, returns bool                                        |
| `TryRemove(key, out value)` | Removes entry and returns the removed value                                   |
| `ContainsKey(key)`          | Checks if key exists                                                          |
| `Clear()`                   | Removes all entries                                                           |
| `CleanUp()`                 | Forces expiration scan                                                        |
| `Compact(ratio)`            | Evicts percentage of entries                                                  |
| `Resize(newSize)`           | Changes maximum size                                                          |
| `GetStatistics()`           | Returns hit/miss/eviction stats (if enabled)                                  |
| `GetAll(keys, dict)`        | Batch get into provided dictionary                                            |
| `SetAll(items)`             | Batch set from collection                                                     |
| `GetKeys(list)`             | Zero-allocation key enumeration into provided list                            |

### Tips and pitfalls

- **Choose the right preset**: `CachePresets` provides optimized defaults for common scenarios.
- **Enable statistics sparingly**: Recording stats adds overhead; enable only when debugging.
- **Use jitter for network caches**: Prevents thundering herd when many entries expire together.
- **Consider SLRU for high-throughput**: Better scan resistance than plain LRU.
- **Watch InitialCapacity**: The cache clamps initial capacity to prevent OutOfMemoryException. Don't set it larger than needed.
- **Weighted caches**: Use `MaximumWeight` + `Weigher` for size-based eviction (e.g., texture bytes).
- **Callbacks are synchronous**: `OnEviction`, `OnGet`, `OnSet` run on the calling thread.
