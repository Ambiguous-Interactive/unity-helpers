# use-queue-structures - Part 1

## Split Content

**Trigger**: When implementing rolling history, position trails, undo/redo systems, BFS traversal, or any double-ended queue operations.

---

## When to Use This Skill

- Recording recent player inputs or positions
- Implementing undo/redo functionality
- Creating fixed-size rolling logs or telemetry
- BFS graph traversal algorithms
- Any scenario requiring efficient front/back insertion/removal

---

## Available Structures

| Structure         | Best For                                 |
| ----------------- | ---------------------------------------- |
| `CyclicBuffer<T>` | Fixed-size rolling history, ring buffers |
| `Deque<T>`        | Double-ended queue, BFS, undo/redo       |

### When to Use Each

- **CyclicBuffer<T>**: Fixed capacity that automatically overwrites oldest entries
- **Deque<T>**: Dynamic capacity with efficient operations at both ends

---

## CyclicBuffer\<T\>

Fixed-capacity ring buffer that overwrites old entries when full. Ideal for rolling logs, recent inputs, telemetry windows, and position trails.

### API

```csharp
CyclicBuffer<T> buffer = new CyclicBuffer<T>(capacity);

buffer.Add(item);           // Add item, overwrites oldest if full
buffer[index];              // Access by index (0 = oldest)
buffer.Count;               // Current number of items
buffer.Capacity;            // Maximum capacity
buffer.Remove(item);        // Remove specific item
buffer.Clear();             // Clear all items

// Allocation-free iteration
foreach (T item in buffer) { }
```

### Example: Position Trail

```csharp
public class TrailRenderer : MonoBehaviour
{
    private CyclicBuffer<Vector3> _positionHistory;

    private void Awake()
    {
        _positionHistory = new CyclicBuffer<Vector3>(32);
    }

    private void FixedUpdate()
    {
        _positionHistory.Add(transform.position);
    }

    private void OnDrawGizmos()
    {
        if (_positionHistory == null) return;

        Gizmos.color = Color.yellow;
        foreach (Vector3 pos in _positionHistory)
        {
            Gizmos.DrawSphere(pos, 0.1f);
        }
    }
}
```

### Example: Input Buffer for Combo System

```csharp
public class ComboInputBuffer : MonoBehaviour
{
    private CyclicBuffer<InputAction> _inputBuffer;
    private const int BufferSize = 10;

    private void Awake()
    {
        _inputBuffer = new CyclicBuffer<InputAction>(BufferSize);
    }

    public void RecordInput(InputAction action)
    {
        _inputBuffer.Add(action);
    }

    public bool MatchesCombo(InputAction[] combo)
    {
        if (_inputBuffer.Count < combo.Length)
            return false;

        int bufferStart = _inputBuffer.Count - combo.Length;
        for (int i = 0; i < combo.Length; i++)
        {
            if (!_inputBuffer[bufferStart + i].Equals(combo[i]))
                return false;
        }
        return true;
    }

    public void ClearBuffer()
    {
        _inputBuffer.Clear();
    }
}
```

### Example: Rolling Average Calculator

```csharp
public class RollingAverage
{
    private readonly CyclicBuffer<float> _samples;

    public RollingAverage(int windowSize)
    {
        _samples = new CyclicBuffer<float>(windowSize);
    }

    public void AddSample(float value)
    {
        _samples.Add(value);
    }

    public float GetAverage()
    {
        if (_samples.Count == 0)
            return 0f;

        float sum = 0f;
        foreach (float sample in _samples)
        {
            sum += sample;
        }
        return sum / _samples.Count;
    }
}
```

---
