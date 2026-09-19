# use-priority-structures - Part 1

## Split Content

**Trigger**: When implementing priority-based element access, A\* pathfinding open sets, task scheduling, or event systems.

---

## When to Use This Skill

- Implementing A\* or Dijkstra pathfinding algorithms
- Building event schedulers that process events by time
- Creating AI decision systems with weighted priorities
- Managing task queues where order matters
- Any scenario requiring efficient access to min/max elements

---

## Available Structures

| Structure          | Best For                         |
| ------------------ | -------------------------------- |
| `Heap<T>`          | Priority ordering, A\* open sets |
| `PriorityQueue<T>` | Task scheduling, event systems   |

### When to Use Each

- **Heap<T>**: Lower-level control, direct heap operations, custom bulk initialization
- **PriorityQueue<T>**: Queue-like semantics, clearer API for scheduling scenarios

---

## Heap\<T\>

Array-backed binary heap with min-heap or max-heap ordering. Optimized for priority-based element access with O(log n) push/pop.

### API

```csharp
// Min-heap (default)
Heap<T> heap = new Heap<T>();

// Custom ordering
Heap<T> heap = new Heap<T>(Comparer<T>.Create((a, b) => a.Priority.CompareTo(b.Priority)));

// From existing collection (O(n) heapify)
Heap<T> heap = new Heap<T>(items, comparer);

// With initial capacity
Heap<T> heap = new Heap<T>(comparer, capacity: 256);

heap.Push(item);            // Add item
heap.Pop();                 // Remove and return top item
heap.Peek();                // View top item without removing
heap.TryPop(out T item);    // Safe removal
heap.TryPeek(out T item);   // Safe peek
heap.IsEmpty;               // Check if empty
heap.Count;                 // Number of items
heap.Clear();               // Remove all items
```

### Example: A\* Pathfinding Open Set

```csharp
public class PathFinder
{
    private readonly Heap<PathNode> _openSet;

    public PathFinder()
    {
        // Min-heap ordered by F cost
        _openSet = new Heap<PathNode>(
            Comparer<PathNode>.Create((a, b) => a.FCost.CompareTo(b.FCost))
        );
    }

    public List<Vector2Int> FindPath(Vector2Int start, Vector2Int goal)
    {
        _openSet.Clear();
        _openSet.Push(new PathNode(start, 0, Heuristic(start, goal)));

        while (!_openSet.IsEmpty && _openSet.TryPop(out PathNode current))
        {
            if (current.Position == goal)
                return ReconstructPath(current);

            foreach (PathNode neighbor in GetNeighbors(current))
            {
                _openSet.Push(neighbor);
            }
        }
        return null;
    }
}
```

### Example: Top-K Selection

```csharp
public class TopKSelector<T>
{
    private readonly Heap<T> _maxHeap;
    private readonly int _k;

    public TopKSelector(int k, IComparer<T> comparer)
    {
        _k = k;
        // Use max-heap to efficiently maintain top-k smallest
        _maxHeap = new Heap<T>(Comparer<T>.Create((a, b) => comparer.Compare(b, a)));
    }

    public void Add(T item)
    {
        if (_maxHeap.Count < _k)
        {
            _maxHeap.Push(item);
        }
        else if (_maxHeap.TryPeek(out T max) && Comparer<T>.Default.Compare(item, max) < 0)
        {
            _maxHeap.Pop();
            _maxHeap.Push(item);
        }
    }

    public IEnumerable<T> GetTopK()
    {
        List<T> results = new List<T>(_maxHeap.Count);
        while (!_maxHeap.IsEmpty)
        {
            results.Add(_maxHeap.Pop());
        }
        results.Reverse();
        return results;
    }
}
```

---
