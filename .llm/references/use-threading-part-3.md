# use-threading - Part 3

## Split Content

## Common Mistakes

### ❌ Deadlock: Blocking on Main Thread

```csharp
// ❌ DEADLOCK - main thread blocks waiting for itself
void Update()
{
    Task task = UnityMainThreadDispatcher.Instance.RunAsync(() => DoWork());
    task.Wait(); // Blocks main thread forever!
}

// ✅ Use async/await instead
async void Update()
{
    await UnityMainThreadDispatcher.Instance.RunAsync(() => DoWork());
}
```

### ❌ Using Destroyed Dispatcher

```csharp
// ❌ Dispatcher may be destroyed on scene change
UnityMainThreadDispatcher dispatcher = UnityMainThreadDispatcher.Instance;
// ... later, after scene change ...
dispatcher.RunOnMainThread(() => DoWork()); // May throw!

// ✅ Always get fresh reference or check
if (UnityMainThreadDispatcher.TryGetInstance(out var dispatcher))
{
    dispatcher.RunOnMainThread(() => DoWork());
}

// Or simply get Instance each time (auto-creates if needed)
UnityMainThreadDispatcher.Instance.RunOnMainThread(() => DoWork());
```

### ❌ Capturing Unity Objects in Background Tasks

```csharp
// ❌ Accessing Transform from background thread
Transform myTransform = transform;
Task.Run(() =>
{
    Vector3 pos = myTransform.position; // CRASH! Unity API on background thread
});

// ✅ Capture values, not Unity objects
Vector3 currentPos = transform.position;
Task.Run(() =>
{
    Vector3 newPos = CalculateNewPosition(currentPos);
    UnityMainThreadDispatcher.Instance.RunOnMainThread(() =>
    {
        transform.position = newPos;
    });
});
```

### ❌ Ignoring Queue Overflow

```csharp
// ❌ Flooding the dispatcher queue
for (int i = 0; i < 100000; i++)
{
    dispatcher.RunOnMainThread(() => UpdateSomething(i));
}

// ✅ Batch work or check queue status
dispatcher.PendingActionLimit = 1000; // Set reasonable limit

// Or batch updates
List<int> batch = new List<int>();
for (int i = 0; i < 100000; i++)
{
    batch.Add(i);
    if (batch.Count >= 100)
    {
        List<int> captured = new List<int>(batch);
        dispatcher.RunOnMainThread(() => UpdateBatch(captured));
        batch.Clear();
    }
}
```

### ❌ Edit Mode Assumptions

```csharp
// ❌ Assuming Play Mode behavior in Edit Mode
void OnValidate()
{
    // RunOnMainThread works, but Update timing differs in Edit Mode
    UnityMainThreadDispatcher.Instance.RunOnMainThread(() =>
    {
        // May execute later than expected in Edit Mode
        RefreshEditor();
    });
}

// ✅ Use EditorApplication.delayCall for Edit Mode
#if UNITY_EDITOR
void OnValidate()
{
    if (!Application.isPlaying)
    {
        UnityEditor.EditorApplication.delayCall += RefreshEditor;
        return;
    }

    UnityMainThreadDispatcher.Instance.RunOnMainThread(RefreshEditor);
}
#endif
```

---

## API Reference

| Class                       | Method                                                       | Description                                  |
| --------------------------- | ------------------------------------------------------------ | -------------------------------------------- |
| `UnityMainThreadDispatcher` | `Instance`                                                   | Singleton accessor (auto-creates if enabled) |
|                             | `RunOnMainThread(Action)`                                    | Enqueue action for main thread execution     |
|                             | `TryRunOnMainThread(Action)`                                 | Try enqueue without logging overflow         |
|                             | `RunAsync(Action)`                                           | Enqueue and return awaitable Task            |
|                             | `RunAsync(Func<CancellationToken, Task>, CancellationToken)` | Enqueue async delegate with cancellation     |
|                             | `PendingActionCount`                                         | Number of queued actions                     |
|                             | `PendingActionLimit`                                         | Max queue size (0 = unlimited)               |
| `UnityMainThreadGuard`      | `EnsureMainThread(string)`                                   | Throw if not on main thread                  |
|                             | `IsMainThread`                                               | Check without throwing                       |
| `SingleThreadedThreadPool`  | `Enqueue(Action)`                                            | Queue synchronous work                       |
|                             | `Enqueue(Func<Task>)`                                        | Queue async work                             |
|                             | `Count`                                                      | Pending work items                           |
|                             | `Exceptions`                                                 | Queue of caught exceptions                   |
