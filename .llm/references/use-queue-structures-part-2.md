# use-queue-structures - Part 2

## Split Content

## Deque\<T\>

Double-ended queue with O(1) insertion and removal from both front and back. Implemented as a circular array.

### API

```csharp
Deque<T> deque = new Deque<T>();
Deque<T> deque = new Deque<T>(initialCapacity);

deque.PushFront(item);      // Add to front
deque.PushBack(item);       // Add to back
deque.PopFront();           // Remove from front
deque.PopBack();            // Remove from back
deque.PeekFront();          // View front without removing
deque.PeekBack();           // View back without removing
deque.TryPopFront(out T);   // Safe front removal
deque.TryPopBack(out T);    // Safe back removal
deque[index];               // Random access
deque.Count;                // Number of items
deque.IsEmpty;              // Check if empty
deque.Clear();              // Remove all items
```

### Example: Undo/Redo System

```csharp
public class UndoRedoManager<T>
{
    private readonly Deque<T> _undoStack = new Deque<T>();
    private readonly Deque<T> _redoStack = new Deque<T>();
    private readonly int _maxHistory;

    public UndoRedoManager(int maxHistory = 50)
    {
        _maxHistory = maxHistory;
    }

    public void RecordState(T state)
    {
        _undoStack.PushBack(state);
        _redoStack.Clear();

        // Limit history size
        while (_undoStack.Count > _maxHistory)
        {
            _undoStack.PopFront();
        }
    }

    public bool TryUndo(out T previousState)
    {
        if (_undoStack.TryPopBack(out previousState))
        {
            _redoStack.PushBack(previousState);
            return true;
        }
        return false;
    }

    public bool TryRedo(out T nextState)
    {
        if (_redoStack.TryPopBack(out nextState))
        {
            _undoStack.PushBack(nextState);
            return true;
        }
        return false;
    }

    public bool CanUndo => !_undoStack.IsEmpty;
    public bool CanRedo => !_redoStack.IsEmpty;
}
```

### Example: BFS Traversal

```csharp
public static IEnumerable<T> BreadthFirstSearch<T>(T start, Func<T, IEnumerable<T>> getNeighbors)
{
    HashSet<T> visited = new HashSet<T>();
    Deque<T> queue = new Deque<T>();

    queue.PushBack(start);
    visited.Add(start);

    while (!queue.IsEmpty && queue.TryPopFront(out T current))
    {
        yield return current;

        foreach (T neighbor in getNeighbors(current))
        {
            if (visited.Add(neighbor))
            {
                queue.PushBack(neighbor);
            }
        }
    }
}
```

### Example: Sliding Window Maximum

```csharp
public class SlidingWindowMax
{
    private readonly Deque<(int index, int value)> _deque = new Deque<(int, int)>();
    private readonly int _windowSize;

    public SlidingWindowMax(int windowSize)
    {
        _windowSize = windowSize;
    }

    public int ProcessNext(int index, int value)
    {
        // Remove elements outside the window
        while (!_deque.IsEmpty && _deque.PeekFront().index <= index - _windowSize)
        {
            _deque.PopFront();
        }

        // Remove smaller elements from back
        while (!_deque.IsEmpty && _deque.PeekBack().value <= value)
        {
            _deque.PopBack();
        }

        _deque.PushBack((index, value));

        return _deque.PeekFront().value;
    }
}
```
