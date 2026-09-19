# use-priority-structures - Part 2

## Split Content

## PriorityQueue\<T\>

Wrapper around `Heap<T>` with queue-like semantics. Clearer API for task scheduling, event systems, and AI decision making.

### API

```csharp
// Min-priority queue
PriorityQueue<T> queue = PriorityQueue<T>.CreateMin();

// Max-priority queue
PriorityQueue<T> queue = PriorityQueue<T>.CreateMax();

// Custom comparer
PriorityQueue<T> queue = new PriorityQueue<T>(comparer);

queue.Enqueue(item);           // Add item
queue.Dequeue();               // Remove and return highest priority
queue.Peek();                  // View highest priority without removing
queue.TryDequeue(out T item);  // Safe removal
queue.TryPeek(out T item);     // Safe peek
queue.IsEmpty;                 // Check if empty
queue.Count;                   // Number of items
queue.Clear();                 // Remove all items
```

### Example: Event Scheduler

```csharp
public class EventScheduler : MonoBehaviour
{
    private readonly PriorityQueue<ScheduledEvent> _events;

    public EventScheduler()
    {
        // Events ordered by execution time (earliest first)
        _events = new PriorityQueue<ScheduledEvent>(
            Comparer<ScheduledEvent>.Create((a, b) => a.ExecuteTime.CompareTo(b.ExecuteTime))
        );
    }

    public void Schedule(Action action, float delay)
    {
        _events.Enqueue(new ScheduledEvent(action, Time.time + delay));
    }

    private void Update()
    {
        while (!_events.IsEmpty && _events.TryPeek(out var evt) && evt.ExecuteTime <= Time.time)
        {
            _events.Dequeue();
            evt.Action?.Invoke();
        }
    }
}

public readonly struct ScheduledEvent
{
    public readonly Action Action;
    public readonly float ExecuteTime;

    public ScheduledEvent(Action action, float executeTime)
    {
        Action = action;
        ExecuteTime = executeTime;
    }
}
```

### Example: AI Priority System

```csharp
public class AIPrioritySystem : MonoBehaviour
{
    private readonly PriorityQueue<AITask> _taskQueue;

    public AIPrioritySystem()
    {
        // Higher priority value = processed first
        _taskQueue = PriorityQueue<AITask>.CreateMax();
    }

    public void AddTask(AITask task)
    {
        _taskQueue.Enqueue(task);
    }

    public AITask GetNextTask()
    {
        return _taskQueue.TryDequeue(out AITask task) ? task : null;
    }

    public void ProcessTasks(int maxTasks)
    {
        int processed = 0;
        while (processed < maxTasks && _taskQueue.TryDequeue(out AITask task))
        {
            task.Execute();
            processed++;
        }
    }
}
```

---

## Performance Tips

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

### Pre-size Collections

```csharp
// Avoid resizing by specifying initial capacity
Heap<PathNode> openSet = new Heap<PathNode>(comparer, capacity: 256);
```

### Reuse Instances

```csharp
// Clear and reuse instead of creating new
_openSet.Clear();
// ... use the heap
```

### Use Pooled Results with Queries

```csharp
// Combine with Buffers for temporary processing
using var lease = Buffers<PathNode>.List.Get(out List<PathNode> results);
while (!_openSet.IsEmpty)
{
    results.Add(_openSet.Pop());
}
ProcessResults(results);
```

---

## Complexity

| Operation | Heap     | PriorityQueue |
| --------- | -------- | ------------- |
| Insert    | O(log n) | O(log n)      |
| Remove    | O(log n) | O(log n)      |
| Peek      | O(1)     | O(1)          |
| Search    | O(n)     | O(n)          |
| Heapify   | O(n)     | O(n)          |

Memory: O(n) for both structures

---

## Related Skills

- [use-data-structures](../skills/use-data-structures.md) - Overview of all data structures
- [use-queue-structures](../skills/use-queue-structures.md) - CyclicBuffer and Deque
- [use-algorithmic-structures](../skills/use-algorithmic-structures.md) - DisjointSet, Trie, BitSet
- [use-spatial-structure](../skills/use-spatial-structure.md) - Spatial trees for proximity queries
