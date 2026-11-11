# use-data-structures - Part 2

## Split Content

## Performance Tips

### Key-Only Storage

For a specialized set, use key-only storage without placeholder value arrays. Share hashing and probing logic when that preserves the storage contract. Measure retained allocation against the corresponding map, and cover wrapped collisions, moved-entry removal, clear/reuse, growth, and allocation-failure recovery. Document any additional lookup cost introduced by reducing storage.

### Use Pooled Collections with Data Structures

```csharp
// Combine with Buffers for temporary results
using var lease = Buffers<string>.List.Get(out List<string> suggestions);
trie.GetWordsWithPrefix("sp", suggestions, maxResults: 10);
ProcessSuggestions(suggestions);
```

### Pre-size Collections

```csharp
// Avoid resizing by specifying initial capacity
Heap<PathNode> openSet = new Heap<PathNode>(comparer, capacity: 256);
Deque<Command> commandQueue = new Deque<Command>(initialCapacity: 64);
BitSet flags = new BitSet(initialCapacity: 1024);
```

### Heapify for Bulk Insert

```csharp
// Slow - O(n log n)
Heap<int> heap = new Heap<int>();
foreach (int item in items)
{
    heap.Push(item);
}

// Fast - O(n) heapify
Heap<int> heap = new Heap<int>(items);
```

---

## Complexity Comparison

| Structure     | Insert   | Remove   | Peek     | Search  | Memory         |
| ------------- | -------- | -------- | -------- | ------- | -------------- |
| CyclicBuffer  | O(1)     | O(n)     | O(1)     | O(n)    | O(capacity)    |
| Heap          | O(log n) | O(log n) | O(1)     | O(n)    | O(n)           |
| PriorityQueue | O(log n) | O(log n) | O(1)     | O(n)    | O(n)           |
| Deque         | O(1)\*   | O(1)\*   | O(1)     | O(n)    | O(n)           |
| DisjointSet   | -        | -        | -        | O(a(n)) | O(n)           |
| Trie          | O(k)     | -        | -        | O(k)    | O(total chars) |
| TimedCache    | -        | -        | O(1)\*\* | -       | O(1)           |
| BitSet        | O(1)     | O(1)     | O(1)     | O(1)    | O(n/64)        |

\* Amortized, front/back only
\*\* May trigger recomputation if TTL expired
k = string length, a(n) = inverse Ackermann function (effectively constant)

---

## Related Skills

- [use-priority-structures](../skills/use-priority-structures.md) - Heap and PriorityQueue details
- [use-queue-structures](../skills/use-queue-structures.md) - CyclicBuffer and Deque details
- [use-algorithmic-structures](../skills/use-algorithmic-structures.md) - DisjointSet, Trie, TimedCache, BitSet details
- [use-spatial-structure](../skills/use-spatial-structure.md) - QuadTree, OctTree, KDTree, SpatialHash
- [use-pooling](../skills/use-pooling.md) - Object pooling and Buffers
