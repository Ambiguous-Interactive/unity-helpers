# use-queue-structures - Part 3

## Split Content

### Example: Work Stealing Queue

```csharp
public class WorkStealingQueue<T>
{
    private readonly Deque<T> _tasks = new Deque<T>();
    private readonly object _lock = new object();

    // Owner pushes and pops from back
    public void Push(T task)
    {
        lock (_lock)
        {
            _tasks.PushBack(task);
        }
    }

    public bool TryPopOwn(out T task)
    {
        lock (_lock)
        {
            return _tasks.TryPopBack(out task);
        }
    }

    // Thieves steal from front
    public bool TrySteal(out T task)
    {
        lock (_lock)
        {
            return _tasks.TryPopFront(out task);
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _tasks.Count;
            }
        }
    }
}
```

---

## Performance Tips

### Pre-size Collections

```csharp
// Avoid resizing by specifying initial capacity
Deque<Command> commandQueue = new Deque<Command>(initialCapacity: 64);
CyclicBuffer<Vector3> trail = new CyclicBuffer<Vector3>(32);
```

### Use Appropriate Capacity for CyclicBuffer

```csharp
// Capacity should match your actual needs
// Too small = losing important data
// Too large = wasted memory

// Position trail: ~1-2 seconds of positions
int trailCapacity = (int)(1.5f / Time.fixedDeltaTime);  // ~75 at 50fps
CyclicBuffer<Vector3> trail = new CyclicBuffer<Vector3>(trailCapacity);
```

### Allocation-Free Iteration

```csharp
// Both structures support allocation-free foreach
foreach (T item in buffer) { }
foreach (T item in deque) { }
```

---

## Complexity

| Operation     | CyclicBuffer | Deque  |
| ------------- | ------------ | ------ |
| Add/Push Back | O(1)         | O(1)\* |
| Push Front    | N/A          | O(1)\* |
| Pop Back      | N/A          | O(1)   |
| Pop Front     | N/A          | O(1)   |
| Remove        | O(n)         | O(n)   |
| Index Access  | O(1)         | O(1)   |
| Search        | O(n)         | O(n)   |

\* Amortized - occasional resize may occur

Memory:

- CyclicBuffer: O(capacity) - fixed
- Deque: O(n) - grows as needed

---

## Serialization Support

Both structures support ProtoBuf and Unity serialization:

```csharp
[Serializable]
public class SerializableTrail
{
    [SerializeField]
    private CyclicBuffer<Vector3> _positions;
}

[ProtoContract]
public class NetworkState
{
    [ProtoMember(1)]
    public Deque<InputFrame> InputHistory { get; set; }
}
```

---

## Related Skills

- [use-data-structures](../skills/use-data-structures.md) - Overview of all data structures
- [use-priority-structures](../skills/use-priority-structures.md) - Heap and PriorityQueue
- [use-algorithmic-structures](../skills/use-algorithmic-structures.md) - DisjointSet, Trie, BitSet
- [use-pooling](../skills/use-pooling.md) - Object pooling for reusable instances
