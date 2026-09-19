# use-threading - Part 1

## Split Content

**Trigger**: When marshalling work between background threads/tasks and Unity's main thread, or when enforcing thread-safety for Unity API calls.

---

## UnityMainThreadDispatcher

Thread-safe singleton that enqueues work to run on Unity's main thread. Works in both Edit Mode and Play Mode.

### Basic Usage: RunOnMainThread

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

// From a background thread or Task
Task.Run(async () =>
{
    string data = await FetchDataAsync();

    // Marshal callback to main thread
    UnityMainThreadDispatcher.Instance.RunOnMainThread(() =>
    {
        // Safe to call Unity APIs here
        myText.text = data;
        Debug.Log("Data loaded!");
    });
});
```

### Async/Await Pattern: RunAsync

```csharp
UnityMainThreadDispatcher dispatcher = UnityMainThreadDispatcher.Instance;

// Fire-and-forget with await
await dispatcher.RunAsync(() =>
{
    player.Health = 0;
    playerAnimator.Play("Die");
});

// With cancellation support
using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
await dispatcher.RunAsync(async token =>
{
    await FadeCanvasGroupAsync(canvasGroup, 0f, token);
}, timeout.Token);
```

### TryRunOnMainThread (Silent Failures)

```csharp
// Use when overflow is expected and you want to silently drop work
bool queued = dispatcher.TryRunOnMainThread(() => UpdateTelemetryUI(status));
if (!queued)
{
    // Queue was full, action was dropped
    Debug.LogWarning("Telemetry update dropped");
}
```

---

## UnityMainThreadGuard

Enforces main thread access for Unity API calls. Throws `InvalidOperationException` when called from background threads.

### EnsureMainThread

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

public class MySingleton
{
    private static MySingleton _instance;

    public static MySingleton Instance
    {
        get
        {
            // Throws if not on main thread
            UnityMainThreadGuard.EnsureMainThread();
            return _instance;
        }
    }

    public void RefreshUI()
    {
        // With context for better error messages
        UnityMainThreadGuard.EnsureMainThread("Refreshing UI");

        // Safe to interact with Unity objects here
        canvas.enabled = true;
    }
}
```

### Check Without Throwing

```csharp
// Check thread without throwing
if (UnityMainThreadGuard.IsMainThread)
{
    // Direct Unity API call
    myTransform.position = newPos;
}
else
{
    // Marshal to main thread
    UnityMainThreadDispatcher.Instance.RunOnMainThread(() =>
    {
        myTransform.position = newPos;
    });
}
```

---

## SingleThreadedThreadPool

Dedicated single-threaded work queue for background processing. Useful for sequential async operations that shouldn't run on the thread pool.

```csharp
using WallstopStudios.UnityHelpers.Core.Threading;

// Create pool (runs in background by default)
SingleThreadedThreadPool pool = new SingleThreadedThreadPool();

// Enqueue synchronous work
pool.Enqueue(() => ProcessData(data));

// Enqueue async work
pool.Enqueue(async () => await SaveAsync(data));

// Check pending work count
int pending = pool.Count;

// Check for exceptions
while (pool.Exceptions.TryDequeue(out Exception ex))
{
    Debug.LogError($"Worker exception: {ex}");
}

// Cleanup
await pool.DisposeAsync();
```

---
