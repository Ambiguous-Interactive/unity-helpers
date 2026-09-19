# use-data-structures - Part 1

## Split Content

**Trigger**: When implementing collections, caches, priority scheduling, connectivity checks, string prefix operations, or bit manipulation.

---

## When to Use This Skill

This is an overview skill for selecting the right data structure. Use this when:

- You need to choose between multiple data structure options
- You want a quick reference for available structures
- You need to compare complexity/performance characteristics

For detailed API documentation and examples, see the specialized skills linked below.

---

## Available Structures Overview

| Structure          | Best For                                      | Skill Reference                                               |
| ------------------ | --------------------------------------------- | ------------------------------------------------------------- |
| `CyclicBuffer<T>`  | Fixed-size rolling history, ring buffers      | [use-queue-structures](../skills/use-queue-structures.md)             |
| `Heap<T>`          | Priority ordering, A\* open sets              | [use-priority-structures](../skills/use-priority-structures.md)       |
| `PriorityQueue<T>` | Task scheduling, event systems                | [use-priority-structures](../skills/use-priority-structures.md)       |
| `Deque<T>`         | Double-ended queue, BFS, undo/redo            | [use-queue-structures](../skills/use-queue-structures.md)             |
| `DisjointSet`      | Union-find, connectivity, clustering          | [use-algorithmic-structures](../skills/use-algorithmic-structures.md) |
| `Trie`             | Prefix search, autocomplete, command matching | [use-algorithmic-structures](../skills/use-algorithmic-structures.md) |
| `TimedCache<T>`    | Expiring cached computations                  | [use-algorithmic-structures](../skills/use-algorithmic-structures.md) |
| `BitSet`           | Dense boolean flags, state masks, layer flags | [use-algorithmic-structures](../skills/use-algorithmic-structures.md) |
| `QuadTree2D<T>`    | 2D spatial queries, collision detection       | [use-spatial-structure](../skills/use-spatial-structure.md)           |
| `OctTree3D<T>`     | 3D spatial queries, collision detection       | [use-spatial-structure](../skills/use-spatial-structure.md)           |
| `KDTree<T>`        | Nearest neighbor queries                      | [use-spatial-structure](../skills/use-spatial-structure.md)           |
| `SpatialHash<T>`   | Uniform distribution, fast insertion          | [use-spatial-structure](../skills/use-spatial-structure.md)           |

---

## Selection Guide

```text
What's your use case?
├─ Fixed-size history/trail → CyclicBuffer<T>
├─ Priority-based processing
│  ├─ Simple heap operations → Heap<T>
│  └─ Queue-like semantics → PriorityQueue<T>
├─ Insert/remove both ends → Deque<T>
├─ Connectivity/grouping → DisjointSet
├─ String prefix matching → Trie
├─ Expensive computation caching → TimedCache<T>
├─ Dense boolean flags → BitSet
└─ Spatial queries → See use-spatial-structure skill

Need serialization?
├─ YES → CyclicBuffer, Deque, DisjointSet, BitSet (all support ProtoBuf + Unity)
└─ NO → Any structure works
```

---

## Quick API Reference

### CyclicBuffer\<T\> - Rolling History

```csharp
CyclicBuffer<T> buffer = new CyclicBuffer<T>(capacity);
buffer.Add(item);           // Add item, overwrites oldest if full
buffer[index];              // Access by index (0 = oldest)
buffer.Count;               // Current number of items
```

See [use-queue-structures](../skills/use-queue-structures.md) for full API and examples.

### Heap\<T\> - Priority Access

```csharp
Heap<T> heap = new Heap<T>(comparer);
heap.Push(item);            // Add item
heap.Pop();                 // Remove and return top item
heap.TryPeek(out T item);   // Safe peek
```

See [use-priority-structures](../skills/use-priority-structures.md) for full API and examples.

### PriorityQueue\<T\> - Task Scheduling

```csharp
PriorityQueue<T> queue = PriorityQueue<T>.CreateMin();
queue.Enqueue(item);        // Add item
queue.Dequeue();            // Remove highest priority
queue.TryPeek(out T item);  // Safe peek
```

See [use-priority-structures](../skills/use-priority-structures.md) for full API and examples.

### Deque\<T\> - Double-Ended Queue

```csharp
Deque<T> deque = new Deque<T>();
deque.PushFront(item);      // Add to front
deque.PushBack(item);       // Add to back
deque.PopFront();           // Remove from front
deque.PopBack();            // Remove from back
```

See [use-queue-structures](../skills/use-queue-structures.md) for full API and examples.

### DisjointSet - Connectivity

```csharp
DisjointSet set = new DisjointSet(elementCount);
set.TryUnion(x, y);                     // Merge two sets
set.TryIsConnected(x, y, out bool c);   // Check if same set
set.SetCount;                           // Number of distinct sets
```

See [use-algorithmic-structures](../skills/use-algorithmic-structures.md) for full API and examples.

### Trie - Prefix Search

```csharp
Trie trie = new Trie(wordCollection);
trie.Contains(word);                              // Exact match
trie.GetWordsWithPrefix(prefix, results, max);    // Prefix search
```

See [use-algorithmic-structures](../skills/use-algorithmic-structures.md) for full API and examples.

### TimedCache\<T\> - Expiring Cache

```csharp
TimedCache<T> cache = new TimedCache<T>(valueProducer, cacheTtl);
cache.Value;                // Get cached value, recomputes if expired
cache.Reset();              // Force recomputation
```

See [use-algorithmic-structures](../skills/use-algorithmic-structures.md) for full API and examples.

### BitSet - Dense Flags

```csharp
BitSet bits = new BitSet(capacity);
bits.TrySet(index);                 // Set bit to 1
bits.TryClear(index);               // Set bit to 0
bits.TryGet(index, out bool value); // Read bit
bits.And(other);                    // Bitwise AND
bits.Or(other);                     // Bitwise OR
```

See [use-algorithmic-structures](../skills/use-algorithmic-structures.md) for full API and examples.

---
