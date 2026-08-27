# Unity Main Thread Dispatcher & Guard

`UnityMainThreadDispatcher` and `UnityMainThreadGuard` provide thread-safe access to Unity's main thread from background workers, ensuring callbacks execute correctly and preventing common threading errors.

## Overview

| Component                   | Purpose                               | Usage Pattern                             |
| --------------------------- | ------------------------------------- | ----------------------------------------- |
| `UnityMainThreadDispatcher` | **Dispatch** work to the main thread  | `dispatcher.RunOnMainThread(() => ...)`   |
| `UnityMainThreadGuard`      | **Assert** code is on the main thread | `UnityMainThreadGuard.EnsureMainThread()` |

## UnityMainThreadDispatcher

The `UnityMainThreadDispatcher` is the package-wide bridge for marshaling callbacks from worker threads back onto Unity's main thread. The dispatcher is implemented as a `RuntimeSingleton<UnityMainThreadDispatcher>`, is marked `[ExecuteAlways]`, and runs both in Edit Mode and Play Mode so background logging, importers, and build scripts can all enqueue work safely.

### Basic Usage

```csharp
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.Helper;

public class BackgroundProcessor
{
    public void ProcessOnWorkerThread()
    {
        // Queue work from any thread to execute on Unity's main thread
        UnityMainThreadDispatcher.Instance.RunOnMainThread(() =>
        {
            // This code executes on the main thread during the next Update
            Debug.Log("Safe to call Unity APIs here");
            Object.FindObjectOfType<Player>().UpdateState();
        });
    }
}
```

### API

```csharp
// Queue action to run on main thread (logs warning if queue is full)
UnityMainThreadDispatcher.Instance.RunOnMainThread(Action action);

// Queue action silently (returns false if queue is full, no warning)
bool queued = UnityMainThreadDispatcher.Instance.TryRunOnMainThread(Action action);

// Check current queue depth
int pending = UnityMainThreadDispatcher.Instance.PendingActionCount;

// Configure queue limit (0 = unlimited)
UnityMainThreadDispatcher.Instance.PendingActionLimit = 4096;
```

### Use Cases

```csharp
// Async/await with Unity API access
public async Task<GameObject> LoadAssetAsync(string path)
{
    GameObject asset = await LoadFromDiskAsync(path);

    // Marshal instantiation to main thread
    TaskCompletionSource<GameObject> tcs = new();
    UnityMainThreadDispatcher.Instance.RunOnMainThread(() =>
    {
        tcs.SetResult(Object.Instantiate(asset));
    });

    return await tcs.Task;
}

// Thread Pool work
ThreadPool.QueueUserWorkItem(_ =>
{
    ComputedData data = ProcessHeavyComputation();

    UnityMainThreadDispatcher.Instance.RunOnMainThread(() =>
    {
        ApplyToScene(data);  // Safe Unity API calls
    });
});

// Task continuation
Task.Run(() => ComputeData())
    .ContinueWith(task =>
    {
        UnityMainThreadDispatcher.Instance.RunOnMainThread(() =>
        {
            DisplayResults(task.Result);
        });
    });
```

## UnityMainThreadGuard

The `UnityMainThreadGuard` is a **guard/assertion** that throws an exception if called from a background thread. Use it to protect Unity API access points that must never be called off-thread.

> ⚠️ **Important:** `EnsureMainThread()` does NOT dispatch work to the main thread. It throws `InvalidOperationException` if called from a background thread. To dispatch work, use `UnityMainThreadDispatcher.Instance.RunOnMainThread()`.
>
> 📦 **Internal API:** `UnityMainThreadGuard` is an `internal` class, accessible only within the Unity Helpers assembly or via `[InternalsVisibleTo]`. For most use cases, prefer using `UnityMainThreadDispatcher` directly which is `public`.

### Basic Usage

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

public class PlayerManager
{
    private Player _cachedPlayer;

    public Player Instance
    {
        get
        {
            // Throws if accessed from background thread
            UnityMainThreadGuard.EnsureMainThread();
            return _cachedPlayer;
        }
    }

    public void RefreshUI()
    {
        // Optional context string for better error messages
        UnityMainThreadGuard.EnsureMainThread("Refreshing player UI");
        // Safe to interact with Unity objects here
    }
}
```

### Why It Exists

**Problem:** Unity APIs must be called from the main thread, but bugs can allow background thread access that causes silent corruption or crashes.

**Solution:** `UnityMainThreadGuard.EnsureMainThread()` fails fast with a clear error message, catching threading bugs during development.

### API

```csharp
// Throws InvalidOperationException if not on main thread
UnityMainThreadGuard.EnsureMainThread();
UnityMainThreadGuard.EnsureMainThread("optional context");

// Check without throwing (internal API)
bool isMainThread = UnityMainThreadGuard.IsMainThread;
```

## Default Bootstrapping Flow

- A `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]` (`EnsureDispatcherBootstrap`) spins up the dispatcher before user assemblies receive their own `RuntimeInitializeOnLoadMethod` callbacks. This guarantees that worker threads can enqueue diagnostics as soon as your game loads.
- Inside the editor, `[InitializeOnLoadMethod]` (`EnsureDispatcherBootstrapInEditor`) mirrors the same behavior for Edit Mode tools. The bootstrap politely skips when play mode is already running, so it does not duplicate the instance scene-side.
- Both entry points run through `UnityMainThreadGuard.EnsureMainThread(...)` before touching Unity APIs, so the dispatcher is always created on the real main thread even when background code tries to force instantiation.

With no configuration, the dispatcher therefore always exists, enforces the queue bound exposed via `PendingActionLimit`, and is hidden from the hierarchy during Edit Mode by `HideFlags.HideInHierarchy | HideFlags.HideInInspector | HideFlags.NotEditable`.

## Opting In/Out of Auto-Creation

Occasionally (especially in tests) you want to control exactly when the dispatcher is created so that scenes unload cleanly and Unity does not warn about hidden objects surviving domain reloads. Use the internal switch:

```csharp
UnityMainThreadDispatcher.SetAutoCreationEnabled(false);
// ... destroy any dispatcher instance if you want a completely clean slate ...
UnityMainThreadDispatcher.SetAutoCreationEnabled(true);
```

- The toggle is global to the current domain; disabling it prevents the runtime/editor bootstrap hooks from creating fresh GameObjects.
- When you re-enable auto-creation the very next call to `UnityMainThreadDispatcher.Instance` will recreate the singleton on the main thread (and Play Mode bootstrap will recreate it on the following domain reload).
- Always destroy the existing dispatcher GameObject (`UnityMainThreadDispatcher.DestroyExistingDispatcher` or the test helper) before turning the feature back on so that stale instances are removed from `Resources.FindObjectsOfTypeAll`.

### AutoCreationScope (scoped toggles)

`UnityMainThreadDispatcher.AutoCreationScope` wraps the toggle/cleanup pattern above in an `IDisposable` so you cannot forget to restore the previous state:

```csharp
using UnityMainThreadDispatcher.AutoCreationScope scope =
    UnityMainThreadDispatcher.AutoCreationScope.Disabled(
        destroyExistingInstanceOnEnter: true,
        destroyInstancesOnDispose: true,
        destroyImmediate: true   // prefer true in tests, false in runtime code
    );

// Auto-creation is disabled inside the scope; manually enable or create instances as needed.
```

- On enter, the scope captures the previous `AutoCreationEnabled` value, switches to the desired state, and optionally destroys any existing dispatcher instances.
- On dispose, the scope restores the original toggle and (when requested) destroys any dispatcher that may have been created during the scoped work, ensuring follow-up tests inherit a clean slate.

## Test Bootstrap Workflow

Two dedicated bootstraps live under `Tests/*/TestUtils` and keep dispatcher lifetimes predictable during automated suites:

### Runtime / PlayMode (`Tests/Runtime/TestUtils/UnityMainThreadDispatcherTestBootstrap.cs`)

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
private static void DisableAutoCreation()
{
    UnityMainThreadDispatcher.SetAutoCreationEnabled(false);
    UnityMainThreadDispatcherTestHelper.DestroyDispatcherIfExists(immediate: true);
}
```

- Runs before every play-mode domain reload.
- Forces any hidden dispatcher left over from a prior scene to be `DestroyImmediate`'d so play-mode tests can opt into dispatcher usage explicitly.
- Guarantees a clean slate for suites that create and destroy scenes repeatedly (see `CommonTestBase.BaseSetUp`).

### Editor / EditMode (`Tests/Editor/TestUtils/UnityMainThreadDispatcherEditorTestBootstrap.cs`)

```csharp
[InitializeOnLoad]
internal static class UnityMainThreadDispatcherEditorTestBootstrap
{
    static UnityMainThreadDispatcherEditorTestBootstrap()
    {
        UnityMainThreadDispatcher.SetAutoCreationEnabled(false);
        // DestroyImmediate the hidden dispatcher object, if any.
    }
}
```

- Executes once per editor domain reload (before EditMode tests run).
- Ensures the hidden dispatcher object is removed from the hierarchy so Unity's test runner does not flag leaked objects between assemblies.

<a id="testing-patterns"></a>

### Re-enabling per Test

The runtime and editor `CommonTestBase` fixtures demonstrate the intended per-test lifecycle via `UnityMainThreadDispatcher.AutoCreationScope`:

1. At `[SetUp]` it grabs `UnityMainThreadDispatcher.CreateTestScope(destroyImmediate: true)` which internally disables auto-creation, destroys stragglers, and then re-enables auto-creation so the test can access `Instance` normally.
2. Production code can create/destroy the dispatcher freely; the scope tracks everything automatically.
3. During every teardown stage it disposes the scope, restoring the previous auto-creation flag and destroying any dispatcher created while the test runs; no manual try/finally blocks required.

Downstream packages can copy the exact pattern:

1. Add a `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` that disables auto-creation and destroys lingering instances.
2. Add an `[InitializeOnLoad]` editor bootstrap that mirrors the same behavior for Edit Mode.
3. When a single test needs to temporarily disable the dispatcher, wrap the custom logic in `using var scope = UnityMainThreadDispatcher.AutoCreationScope.Disabled(...)` so cleanup always runs.

Following this workflow keeps Unity from warning about hidden GameObjects during test teardown, prevents worker threads from instantiating the dispatcher off-thread, and documents exactly when auto-creation is in effect for your package.
