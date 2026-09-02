# Helper Utilities Guide

## TL;DR: Why Use These

Static helper classes and utilities that solve common programming problems without needing components on GameObjects. Use these for predictive aiming, path utilities, threading, hashing, formatting, and more.

---

## Contents

- [Gameplay Helpers](#gameplay-helpers): Predictive aiming, spatial sampling, rotation
- [GameObject & Component Helpers](#gameobject--component-helpers): Component discovery, hierarchy manipulation
- [Transform Helpers](#transform-helpers): Hierarchy traversal
- [Coroutine Wait Pools](#coroutine-wait-pools): Configure `Buffers.GetWaitForSeconds*` caching
- [Pooling Unity Objects That Outlive Their Scope](#pooling-unity-objects-that-outlive-their-scope): `TrackedObjectPool<T>`
- [Threading](#threading): Main thread dispatcher, single-threaded pool teardown
- [Path & File Helpers](#path--file-helpers): Path resolution, file operations
- [Scene Helpers](#scene-helpers): Scene queries and loading
- [Advanced Utilities](#advanced-utilities): `RestorableGlobal<T>`, null checks, hashing, formatting
- [Environment Detection](#environment-detection): CI, batch mode, and runtime environment

---

## Coroutine Wait Pools

Unity allocates a new `WaitForSeconds`/`WaitForSecondsRealtime` every time you yield with a literal. `Buffers.GetWaitForSeconds(...)` and `Buffers.GetWaitForSecondsRealTime(...)` pool those instructions to reduce coroutine allocations, but each distinct duration used to stick around forever. Large ranges (randomized cooldowns, tweens, etc.) could leak thousands of instances.

**New pooling policy knobs (Runtime 2.2.1+):**

| Setting                                                                                    | Default   | Purpose                                                                                                                                                                                         |
| ------------------------------------------------------------------------------------------ | --------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Buffers.WaitInstructionMaxDistinctEntries`                                                | `512`     | Upper bound on distinct cached durations. Set to `0` to disable the cap, or tighten it for editor/dev builds. When the limit is reached the cache stops growing (or evicts, if LRU is enabled). |
| `Buffers.WaitInstructionQuantizationStepSeconds`                                           | `0` (off) | Rounds requested durations to the nearest step before caching. Useful when you can tolerate millisecond snapping (e.g., `.005f` → `.01f`).                                                      |
| `Buffers.WaitInstructionUseLruEviction`                                                    | `false`   | When true, the cache becomes an LRU: it evicts the least recently used duration whenever it hits the max entry count instead of rejecting new ones. Diagnostics expose the eviction count.      |
| `Buffers.TryGetWaitForSecondsPooled(float seconds)` / `TryGetWaitForSecondsRealtimePooled` | n/a       | Returns the cached instruction or `null` if the request would exceed the cap. Use this when you want to detect “unsafe” usages and allocate manually instead.                                   |
| `Buffers.WaitForSecondsCacheDiagnostics` / `.WaitForSecondsRealtimeCacheDiagnostics`       | snapshot  | Exposes `DistinctEntries`, `MaxDistinctEntries`, `LimitRefusals`, and whether quantization is active so you can surface metrics in your own tooling.                                            |

> ⚙️ **Project-wide defaults:** Open the **Coroutine Wait Instruction Buffers** foldout under **Project Settings ▸ Wallstop Studios ▸ Unity Helpers** to edit these knobs. The settings asset lives at `Resources/Wallstop Studios/Unity Helpers/UnityHelpersBufferSettings.asset`, ships with your build, and automatically applies on script/domain reload or when a player starts (unless your code overrides the values at runtime). Use **Apply Defaults Now** to push the current sliders into the active domain or **Capture Current Values** to snapshot whatever `Buffers` is using in play mode.
>
> 🔒 **Persistence Behavior:** When you click **Apply Defaults Now**, the settings are immediately:
>
> 1. **Saved to disk**: The asset is marked dirty and saved via `AssetDatabase.SaveAssets()`
> 2. **Applied to the runtime**: `Buffers.WaitInstruction*` properties are updated immediately
>
> This ensures settings persist across:
>
> - **Domain reloads** (script recompilation, entering/exiting play mode): Via `[InitializeOnLoadMethod]`
> - **Editor restarts**: The asset is saved to disk and reloads automatically
> - **Standalone builds**: The asset ships under `Resources/` and auto-applies via `[RuntimeInitializeOnLoadMethod]`
>
> Toggle **Apply On Load** to control whether the saved defaults auto-apply when the domain loads. If disabled, the asset serves as a reference and you must call `asset.ApplyToBuffers()` manually.

```csharp
// Clamp the cache to 128 distinct waits, quantize to milliseconds, and reuse LRU entries.
Buffers.WaitInstructionMaxDistinctEntries = 128;
Buffers.WaitInstructionQuantizationStepSeconds = 0.001f;
Buffers.WaitInstructionUseLruEviction = true;

IEnumerator WeaponCooldown(Func<float> cooldownSeconds)
{
    float waitSeconds = cooldownSeconds();

    // Prefer pooled waits, but fall back to a fresh instance if the cache refuses it.
    WaitForSeconds pooled = Buffers.TryGetWaitForSecondsPooled(waitSeconds)
        ?? new WaitForSeconds(waitSeconds);

    yield return pooled;
}

void OnGUI()
{
    WaitInstructionCacheDiagnostics stats = Buffers.WaitForSecondsCacheDiagnostics;
    GUILayout.Label(
        $"Wait cache: {stats.DistinctEntries}/{stats.MaxDistinctEntries} (refusals={stats.LimitRefusals}, evictions={stats.Evictions})"
    );
}
```

> ⚠️ **Limit warnings:** In Editor and Development builds the first limit hit (and every 25th after) emits a warning so you can spot misuses quickly. Production builds skip the log to avoid noise.
>
> ✅ **Deterministic fallback:** When the cache refuses a duration, `Buffers.GetWaitForSeconds*` still returns a valid instruction; it just isn’t cached, so highly variable waits no longer lead to unbounded memory growth.

---

<a id="gameplay-helpers"></a>

## Pooling Unity Objects That Outlive Their Scope

`TrackedObjectPool<T>` pools `UnityEngine.Object` instances whose lifetime ends in a **callback**
rather than at the end of a scope (a tween's `OnComplete`, an animation event, a coroutine).

That is the one shape `WallstopGenericPool<T>` cannot serve. It hands out a `PooledResource<T>` whose
disposal returns the item, which is a lexical scope and therefore cannot strand anything; it also
cannot refuse a return, and refusing one is the point here. Use it for scratch buffers, and this for
pooled effects.

```csharp
TrackedObjectPool<GameObject> puffs = new(
    producer: () => Instantiate(_puffPrefab),
    onTake: puff => puff.SetActive(true),
    onRelease: puff => puff.SetActive(false),
    onDestroy: puff => Destroy(puff));

if (puffs.TryTake(out GameObject puff))
{
    puff.transform.position = point;
    tween.OnComplete(() => puffs.Release(puff));
}

// Destroys the puff still in flight too, rather than leaving it in the scene.
puffs.Dispose();
```

| Member           | What it answers                                                                                                                                                                                                   |
| ---------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `TryTake(out T)` | `false` when the pool is disposed or nothing could be produced. An item destroyed while pooled is discarded, never handed out.                                                                                    |
| `Release(T)`     | `false` for a double release, for something this pool never handed out, and for a release arriving after `Dispose`. It never throws: the caller is usually a completion callback, where a throw surfaces nowhere. |
| `InFlightCount`  | How many are checked out, and would be destroyed by a teardown right now.                                                                                                                                         |
| `Dispose()`      | Applies `onDestroy` to everything, in flight included, draining the tracking list first so a `Release` from a destroyed item's own ending is refused rather than counted twice.                                   |

An item destroyed while checked out is still removed from tracking when released: `ReferenceEquals(item, null)`
asks whether anything was handed in, `item == null` asks whether it is gone, and skipping the removal
on the second question leaks one dead reference per use. The pool never calls `Object.Destroy` on its
own initiative: `onDestroy` is where destruction lives, and a `null` one means something else owns it.

---

## Gameplay Helpers

### Predictive Aiming

**What it does:** Calculates where to aim when shooting at a moving target, accounting for projectile travel time.

**Problem it solves:** Shooting a bullet at where an enemy _is_ misses if they're moving. You need to aim at where they _will be_.

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

Vector2 enemyPos = enemy.transform.position;
Vector2 enemyVelocity = enemy.GetComponent<Rigidbody2D>().velocity;
Vector2 turretPos = turret.transform.position;
float bulletSpeed = 20f;

Vector2? aimPosition = Helpers.PredictCurrentTarget(
    enemyPos,
    enemyVelocity,
    turretPos,
    bulletSpeed
);

if (aimPosition.HasValue)
{
    // Aim at aimPosition to hit the moving target
    Vector2 aimDirection = (aimPosition.Value - turretPos).normalized;
    FireProjectile(aimDirection, bulletSpeed);
}
else
{
    // Target is too fast, can't hit
}
```

**When to use:**

- Turrets shooting at moving enemies
- AI aiming at moving players
- Predictive targeting systems
- Guided missiles

**When NOT to use:**

- Homing projectiles (use steering behaviors)
- Instant-hit weapons (use raycasts)
- Slow-moving or stationary targets (just aim directly)

---

### Spatial Sampling

**Get random points in circles/spheres:**

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

// Random point inside circle (uniform distribution)
Vector2 spawnPoint = Helpers.GetRandomPointInCircle(center, radius);

// Random point inside sphere (uniform distribution)
Vector3 explosionPoint = Helpers.GetRandomPointInSphere(center, radius);
```

**Use for:**

- Spawn points (enemies, pickups, particles)
- Explosion damage distribution
- Random movement destinations
- Scatter patterns

---

### Smooth Rotation Helpers

**Get rotation speed for smooth turning:**

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

// Calculate how much to rotate this frame toward target
float currentAngle = transform.eulerAngles.z;
float targetAngle = GetTargetAngle();
float maxDegreesPerSecond = 180f;

float newAngle = Helpers.GetAngleWithSpeed(
    currentAngle,
    targetAngle,
    maxDegreesPerSecond,
    Time.deltaTime
);

transform.eulerAngles = new Vector3(0, 0, newAngle);
```

**Handles:**

- Frame-rate independence
- Shortest rotation path (doesn't spin 270° when 90° is shorter)
- Angle wrapping (0-360°)

---

### Delayed Execution

**Execute code after delay or next frame:**

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

// Execute after 2 seconds
Helpers.ExecuteFunctionAfterDelay(
    monoBehaviour,
    () => Debug.Log("Delayed!"),
    delayInSeconds: 2f
);

// Execute next frame
Helpers.ExecuteFunctionNextFrame(
    monoBehaviour,
    () => Debug.Log("Next frame!")
);
```

Uses coroutines under the hood.

---

### Repeating Execution with Jitter

**Run function repeatedly with random timing variance:**

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

// Spawn enemy every 5-8 seconds
Helpers.StartFunctionAsCoroutine(
    gameManager,
    SpawnEnemy,
    baseInterval: 5f,
    intervalJitter: 3f  // Random ±3 seconds
);

void SpawnEnemy()
{
    Instantiate(enemyPrefab, spawnPoint.position, Quaternion.identity);
}
```

**Use for:**

- Enemy spawning with variability
- Random event triggers
- Staggered updates to spread CPU load
- Natural-feeling timing

---

### Layer & Label Queries

<!-- doc-sample: compiles -->

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

// Get all layer names (cached after first call)
string[] allLayers = Helpers.GetAllLayerNames();

// Get all sprite label names (editor only, cached)
string[] labels = Helpers.GetAllSpriteLabelNames();
```

**Use for:**

- Populating dropdowns in editor tools
- Runtime layer/label validation
- Configuration systems

---

### Collider Syncing

**Update PolygonCollider2D to match sprite:**

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

SpriteRenderer renderer = GetComponent<SpriteRenderer>();
PolygonCollider2D collider = GetComponent<PolygonCollider2D>();

Helpers.UpdateShapeToSprite(renderer, collider);
// Collider now matches sprite's physics shape
```

---

<a id="gameobject--component-helpers"></a>

## GameObject & Component Helpers

### Cached Component Lookup

**Tag-based component finding with caching:**

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

// First call searches scene, subsequent calls use cache
Player player = Helpers.Find<Player>("Player");

// Clear cache manually if needed
Helpers.ClearInstance<Player>();

// Set cache manually (for dependency injection scenarios)
Helpers.SetInstance(playerInstance);
```

**Performance:** First call searches the scene using GameObject.FindWithTag; subsequent calls use a cached O(1) dictionary lookup. The cache persists until manually cleared.

---

### Component Existence Checks

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

// Check if component exists without allocating
bool hasRigidbody = Helpers.HasComponent<Rigidbody2D>(gameObject);

// Better than:
bool hasRigidbody = GetComponent<Rigidbody2D>() != null; // Allocates
```

---

### Get-or-Add Pattern

<!-- doc-sample: compiles -->

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

// Get existing component or add if missing
Rigidbody2D rb = Helpers.GetOrAddComponent<Rigidbody2D>(gameObject);
```

---

### Hierarchical Enable/Disable

**Recursively enable/disable components:**

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

// Enable all Collider2D components in children
Helpers.EnableRecursively<Collider2D>(rootObject, enable: true);

// Disable all renderers in hierarchy
Helpers.EnableRendererRecursively<SpriteRenderer>(rootObject, enable: false);
```

**Use for:**

- Toggling collision for entire character rigs
- Hiding/showing complex prefabs
- Debug visualization toggles

---

### Bulk Child Destruction

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

// Destroy all children (useful for clearing containers)
Helpers.DestroyAllChildrenGameObjects(parentTransform);
```

**Use for:**

- Clearing inventory UI
- Resetting spawn containers
- Cleanup before repopulating

---

### Smart Destruction

**Editor/runtime aware destruction:**

<!-- doc-sample: compiles -->

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

// Uses DestroyImmediate in editor, Destroy in play mode
Helpers.SmartDestroy(gameObject);

// Also handles assets correctly (won't destroy project assets)
```

**Use in editor tools** to avoid "Destroying assets is not permitted" errors.

---

### Prefab Utilities

<!-- doc-sample: compiles -->

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

// Check if GameObject is a prefab asset or instance
bool isPrefab = Helpers.IsPrefab(gameObject);

// Safely modify prefab (editor only)
#if UNITY_EDITOR
Helpers.ModifyAndSavePrefab(prefabAssetPath, prefab =>
{
    // Modify prefab here
    var component = prefab.AddComponent<MyComponent>();
    component.value = 42;
    // Changes saved automatically
});
#endif
```

---

<a id="transform-helpers"></a>

## Transform Helpers

### Hierarchy Traversal (Depth-First)

**Visit all children recursively:**

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

// Depth-first traversal (visits deepest children first)
Helpers.IterateOverAllChildrenRecursively<SpriteRenderer>(rootTransform, renderer =>
{
    renderer.color = Color.red;
});

// Buffered version (reduces allocations)
using (var buffer = Buffers<Transform>.List.Get())
{
    Helpers.IterateOverAllChildrenRecursively(rootTransform, buffer.Value);
    foreach (Transform child in buffer.Value)
    {
        // Process children
    }
}
```

---

### Hierarchy Traversal (Breadth-First)

**Visit by depth level:**

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

// Breadth-first traversal with depth limit
Helpers.IterateOverAllChildrenRecursivelyBreadthFirst(
    rootTransform,
    transform => Debug.Log(transform.name),
    maxDepth: 3  // Only visit 3 levels deep
);
```

**Use for:**

- Finding immediate area (not entire tree)
- Level-based operations
- Performance-sensitive searches

---

### Parent Traversal

**Walk up the hierarchy:**

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

// Find component in parents
Helpers.IterateOverAllParentComponentsRecursively<Canvas>(transform, canvas =>
{
    Debug.Log($"Found canvas: {canvas.name}");
});

// Get all parents (no component filter)
using (var buffer = Buffers<Transform>.List.Get())
{
    Helpers.IterateOverAllParents(transform, buffer.Value);
    // buffer contains all parent transforms up to root
}
```

**Use for:**

- Finding UI Canvas parents
- Inheritance checking (is this under X?)
- Walking to root of hierarchy

---

### Direct Children/Parents

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

// Get immediate children (non-recursive)
using (var buffer = Buffers<Transform>.List.Get())
{
    Helpers.IterateOverAllChildren(transform, buffer.Value);
    // Only direct children, no grandchildren
}
```

---

<a id="threading"></a>

## Threading

### UnityMainThreadDispatcher

**Execute code on Unity's main thread from background threads:**

**Problem it solves:** Unity APIs can only be called from the main thread. Background Tasks/threads can't directly manipulate GameObjects. This marshals callbacks back to the main thread.

See the dedicated [Unity Main Thread Dispatcher guide](../logging/unity-main-thread-dispatcher.md) for details about auto-creation, queue limits, the `AutoCreationScope` helper, and the `CreateTestScope(...)` convenience method that packages can use in their own test fixtures.

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;
using System.Threading.Tasks;

async Task LoadDataInBackground()
{
    // Background thread work
    await Task.Run(() =>
    {
        // Expensive computation
        var data = LoadFromDatabase();

        // Need to update UI - marshal back to main thread
        UnityMainThreadDispatcher.Instance.RunOnMainThread(() =>
        {
            // Safe to call Unity APIs here
            uiText.text = data.ToString();
        });
    });
}
```

**Async version with result:**

```csharp
async Task<string> GetTextFromMainThread()
{
    // Called from background thread, executes on main thread
    string text = await UnityMainThreadDispatcher.Instance.Post(() =>
    {
        return uiText.text; // Safe to access Unity objects
    });

    return text;
}
```

### SingleThreadedThreadPool

**Run background work one item at a time, in enqueue order:**

**Disposal discards queued work.** `Dispose()` and `DisposeAsync()` cancel the worker rather than
draining it, so anything enqueued but not yet started is dropped; the `await` inside
`DisposeAsync()` waits only for the item already in flight. That is what you want for work that can
simply be redone, such as a generation pass, and not what you want for durable work such as a
persistence write.

The window is narrow enough to hide in testing: work enqueued a millisecond or more before disposal
almost always completes, work enqueued immediately before it almost never does.

Call `DrainAsync()` when queued items must run. It closes the pool to new work permanently, then
returns once the queue is empty and nothing is executing:

```csharp
using WallstopStudios.UnityHelpers.Core.Threading;

// Work that can be redone: drop whatever is still queued.
_generationPool.Dispose();

// Durable work: let the queue run down first.
if (!await _persistencePool.DrainAsync())
{
    this.Log()?.Warn("Pool did not drain; writing final state on this thread.");
}

await _persistencePool.DisposeAsync();
```

`DrainAsync()` returns `false` when the wait was abandoned via its `CancellationToken`, the pool was
already disposed, or the worker had already stopped with items still queued, so a caller can fall
back to writing the final state itself. `IsAcceptingWork` reports whether `Enqueue` still does
anything.

The guarantee covers every item the calling thread enqueued before the call. A producer racing the
drain from another thread is not covered, so stop those producers first.

**One caveat on the synchronous `Dispose()`:** it blocks the calling thread until the in-flight item
finishes. The pool posts nothing back to Unity's main thread, so this is safe for ordinary work
items. A work item whose own continuations capture the main thread's synchronization context would
deadlock, because `OnDestroy` runs on that thread; prefer `DisposeAsync()` there.

### Semaphore Leases

`SemaphoreSlim` makes you pair every wait with a `finally`. `Acquire()` returns a `SemaphoreLease`
instead, so the critical section is a `using` block:

```csharp
using WallstopStudios.UnityHelpers.Core.Threading;

private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

public void Append(string line)
{
    using (_gate.Acquire())
    {
        _lines.Add(line);
    }
}

public async Task AppendAsync(string line, CancellationToken cancellationToken)
{
    using (await _gate.AcquireAsync(cancellationToken))
    {
        _lines.Add(line);
    }
}
```

When you would rather not block, `TryAcquire` reports failure instead:

```csharp
if (_gate.TryAcquire(TimeSpan.FromMilliseconds(50), out SemaphoreLease lease))
{
    using (lease)
    {
        // Took a permit within the timeout.
    }
}
```

`SemaphoreLease` is a struct, so an uncontended acquire allocates nothing.

**Disposal is tracked and idempotent.** Disposing a lease twice returns one permit, not two. That
matters more than it sounds: an extra `Release()` raises the permit count above the semaphore's
maximum and quietly lets two callers into a section built for one. `IsHeld` reports whether a lease
still owns a permit, and a lease from a failed `TryAcquire` is not held, so disposing it is a no-op.

**Copying a lease is safe.** Assigning it to another variable, capturing it, or passing it by value
produces copies that all point at the same permit, and exactly one of them releases it — whichever is
disposed first. The claim is held outside the struct, where every copy reads the same state, so
`IsHeld` reports false on every copy once any of them has released.

**Construct semaphores with an explicit maximum.** It does not make copying safe, but it decides
whether the mistake is loud-free or silent:

| Constructor               | A copied lease disposed twice                                                                                                                |
| ------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------- |
| `new SemaphoreSlim(1, 1)` | The extra release throws and `Dispose` swallows it. Count survives.                                                                          |
| `new SemaphoreSlim(1)`    | Maximum defaults to `int.MaxValue`, so the extra release **succeeds**; the count silently rises to 2 and a second caller enters the section. |

Acquire directly into a `using` and let the lease die there, and neither case can arise.

`Acquire()` and `AcquireAsync()` throw `ArgumentNullException` on a null semaphore rather than
handing back a lease that is not held: a silently unlocked critical section surfaces far from its
cause. `TryAcquire` reports `false` instead.

---

## Logging

Use the [Logging Extensions guide](../logging/logging-extensions.md) for:

- Rich text tags applied directly inside interpolated strings (`$"{value:b,color=red}"`)
- Thread-aware logging helpers (`this.Log`, `this.LogWarn`, `this.LogError`, `this.LogDebug`)
- Tips for registering custom decorations and gating logs per-object or globally

These helpers rely on the same dispatcher utilities above, so logging from jobs/background threads stays safe.

**Fire-and-forget on main thread:**

```csharp
// From background thread
UnityMainThreadDispatcher.Instance.RunOnMainThread(() =>
{
    Instantiate(prefab, position, rotation);
});
```

**When to use:**

- Async file loading callbacks
- Network request callbacks
- Database query results
- Background computation results that update UI

**Important:**

- Works in both edit mode and play mode
- Actions queued during edit mode execute in next editor update
- Don't block the main thread with long operations

---

<a id="path--file-helpers"></a>

## Path & File Helpers

### Path Sanitization

**Normalize path separators:**

<!-- doc-sample: compiles -->

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

string windowsPath = @"Assets\Sprites\Player.png";
string unityPath = PathHelper.Sanitize(windowsPath);
// Result: "Assets/Sprites/Player.png"
```

Unity prefers forward slashes. Use this for cross-platform paths.

---

### Directory Utilities

**Create directories safely:**

<!-- doc-sample: compiles -->

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

#if UNITY_EDITOR
// Creates directory and updates AssetDatabase
DirectoryHelper.EnsureDirectoryExists("Assets/Generated/Data");
#endif
```

**Find package root:**

```csharp
// Walk hierarchy to find package.json
string packageRoot = DirectoryHelper.FindPackageRootPath();
// Returns path to package containing calling script
```

**Use for:**

- Editor tools generating assets
- Finding package-relative paths
- Build scripts creating folders

---

### Path Conversion

**Convert between absolute and Unity-relative paths:**

<!-- doc-sample: compiles -->

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

string absolute = "C:/Projects/MyGame/Assets/Textures/player.png";
string relative = DirectoryHelper.AbsoluteToUnityRelativePath(absolute);
// Result: "Assets/Textures/player.png"
```

**Get calling script's directory:**

<!-- doc-sample: compiles -->

```csharp
// Uses [CallerFilePath] magic
string scriptDir = DirectoryHelper.GetCallerScriptDirectory();
// Returns directory containing the calling .cs file
```

---

### File Operations

**Initialize file if missing:**

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

// Create config.json with default contents if it doesn't exist
FileHelper.InitializePath(
    "Assets/config.json",
    "{ \"version\": 1 }"
);
```

**Async file copy:**

<!-- doc-sample: compiles -->

```csharp
using System.Threading;

CancellationTokenSource cts = new CancellationTokenSource();

await FileHelper.CopyFileAsync(
    "source.txt",
    "destination.txt",
    bufferSize: 81920,  // 80KB buffer
    cts.Token
);
```

**Use for:**

- Large file operations without blocking
- Cancellable copy operations
- Streaming file operations

### Durable Writes for Player Data

`File.WriteAllText` empties the destination before it writes a single byte. If the game is killed, the
device loses power, or the disk fills in between, the player's save is gone and a truncated one is in its
place. `DurableFile` writes the new contents to a sibling file, forces them to disk, and only then swaps
them over the destination.

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

if (!DurableFile.TryWriteAllText(savePath, json, out Exception error))
{
    Debug.LogError($"Could not save: {error}");
    // The previous save is still on disk and still readable.
}
```

Every method reports failure instead of throwing, and the async ones return the exception (null on
success):

```csharp
Exception error = await DurableFile.WriteAllTextAsync(savePath, json, cancellationToken);

// Append is stronger still: it never rewrites bytes that are already on disk.
DurableFile.TryAppendAllText(ledgerPath, $"{score}\n", out Exception appendError);

// Replace one file with another under the same staging discipline.
DurableFile.TryCopy(savePath, backupPath, out Exception copyError);
```

`Serializer.WriteToJsonFile` and `WriteToJsonFileAsync` already write through this, so JSON saves get the
guarantee without changing any code.

**What it promises:**

- A reader sees either the complete previous contents or the complete new ones, never a partial file.
- The data is forced out of the page cache before the swap makes it live.
- Concurrent writes to the same path from your game are serialized.

**What it does not promise:**

- It is **not** full crash safety. .NET cannot flush a directory, so a filesystem may still reorder the
  rename behind the data write.
- It does not coordinate with other processes. A second process writing the same file at the same time is
  reported as a failure rather than allowed to corrupt the document.

A leftover `.tmp` sibling (`DurableFile.TemporarySuffix`) is what an interrupted write leaves behind; it is
safe to ignore or delete.

---

<a id="scene-helpers"></a>

## Scene Helpers

### Scene Queries

**Check if scene is loaded:**

<!-- doc-sample: compiles -->

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

bool loaded = SceneHelper.IsSceneLoaded("GameLevel");
// Checks by scene name or path
```

**Get all scene paths (editor):**

<!-- doc-sample: compiles -->

```csharp
#if UNITY_EDITOR
string[] allScenes = SceneHelper.GetAllScenePaths();
// Returns all .unity files in project

string[] buildScenes = SceneHelper.GetScenesInBuild();
// Returns only scenes in Build Settings
#endif
```

---

### Temporary Scene Loading

**Load scene, extract data, auto-unload:**

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

// RAII pattern - scene unloaded when disposed
using (var scope = SceneHelper.GetObjectOfTypeInScene<LevelConfig>("Scenes/LevelData"))
{
    if (scope.HasObject)
    {
        LevelConfig config = scope.Object;
        // Use config data
    }
    // Scene automatically unloaded here
}
```

**Use for:**

- Extracting data from data-only scenes
- Editor tools reading scene contents
- Validation scripts
- Testing scene contents

---

<a id="advanced-utilities"></a>

## Advanced Utilities

### `RestorableGlobal<T>`

**The problem:** the obvious way to borrow a global for the length of a block captures the previous
value in the scope's own field and restores from that field. Making the scope `readonly` fixes the
per-copy "have I been disposed?" flag and fixes nothing here: every copy agrees about _what_ to put
back and none of them about _whether it already has_, so a second `Dispose` re-imposes a value the
world has moved past. `WUH014` reports the shape; this type is the answer.

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

private static readonly RestorableGlobal<RenderTexture> ActiveTexture =
    new RestorableGlobal<RenderTexture>(
        () => RenderTexture.active,
        value => RenderTexture.active = value
    );

public static void BlitInto(RenderTexture target)
{
    using (ActiveTexture.Borrow(target))
    {
        GL.Clear(true, true, Color.clear);
    }
}
```

The scope holds an identifier rather than a value, and gives it back with a **call** to the owner.
Identifiers are never reused, so a stale copy's release is a no-op instead of a re-imposition.

**Nesting and out-of-order disposal.** Nesting one borrow inside another is the ordinary case, so the
rule is stated for any depth and any order:

- The global always holds the value the **newest live** borrow asked for. Releasing an older borrow
  writes nothing — the newer borrow is still running and is still entitled to what it asked for.
- The released borrow's restore value is inherited by the borrow above it, so the last release
  returns the global to the value it held before the outermost borrow, whatever order the releases
  happened in.
- That inheritance is conditional: the borrow above only takes it over when what it captured is still
  the value the released borrow applied. Where something else wrote to the global in between, that
  write is what comes back.

Unwinding every newer borrow along with an out-of-order older one was rejected: it takes a value away
from a scope that is still running.

**Nothing throws.** Disposing a `default` scope, disposing twice, disposing after `ReleaseAll()`, and
a getter or setter that throws are all handled — the failure is logged and the bookkeeping stays
consistent. `TryBorrow` reports whether the value actually took; `IsHeld` answers for every copy at
once; `Depth` is how many borrows are live.

**Cost.** A borrow allocates nothing once the slot table has grown to the deepest nesting reached:
the scope is a `readonly struct`, so `using` calls `Dispose` directly rather than through a boxed
interface. Construction allocates the owner, its table and the two delegates, once.

**Threading.** A per-instance monitor guards the table, so concurrent borrows cannot corrupt it — and
the getter and setter run under that monitor, which makes a borrow atomic against another thread's. A
process-wide cell still has one value, so two threads borrowing at once last-writer-wins on the thing
itself, and most globals worth borrowing (Unity's among them) are main-thread only regardless. Under
`SINGLE_THREADED` the monitor is compiled out.

---

### Unity-Aware Null Checks

**The problem:** Unity's `==` operator overload can be slow, and destroyed UnityEngine.Objects return `true` for `== null` but `false` for `is null`.

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

GameObject obj = GetMaybeDestroyedObject();

// Proper Unity null check
bool isNull = Objects.Null(obj);
bool notNull = Objects.NotNull(obj);
```

Handles:

- Destroyed UnityEngine.Objects
- Actual null references
- Optimized checks for non-Unity types

---

### Hash Code Composition

**Combine hash codes correctly:**

<!-- doc-sample: compiles -->

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

public class CompositeKey
{
    public string Name;
    public int Level;
    public Vector2 Position;

    public override int GetHashCode()
    {
        // FNV-1a mixing over each member's own hash code
        return Objects.HashCode(Name, Level, Position);
    }
}
```

Supports up to 20 parameters. The mixing step is FNV-1a, for good distribution.

**Hash entire collections:**

<!-- doc-sample: compiles -->

```csharp
List<int> numbers = new List<int> { 1, 2, 3, 4, 5 };
int hash = Objects.EnumerableHashCode(numbers);
```

**Use for:**

- Custom GetHashCode implementations
- Dictionary keys with multiple fields

**Not for anything that outlives the process.** The mixing is fixed, but each argument contributes
its ordinary `GetHashCode()` value, and those are not portable: .NET randomizes `string` hash codes
per process, a `UnityEngine.Object` hashes to a session-local instance id, and any other type
answers with whatever its author wrote. Two runs of the same build on the same machine can disagree.
Persisting one of these values, sending it over a network, or comparing it against a stored copy will
appear to work and then fail.

---

### Stable Hashing for Saves and Networking

`Objects.StableHash32V1` hashes bytes and nothing else, so its answer depends only on its arguments:
the same bytes and the same seed produce the same value in every process, on every platform, and in
every later version of this package. The algorithm is frozen -- that is what the `V1` names -- so a
future change arrives under a different name rather than as a new answer here.

```csharp
using System.Text;
using WallstopStudios.UnityHelpers.Core.Helper;

byte[] payload = Encoding.UTF8.GetBytes(saveSlotName);
uint digest = Objects.StableHash32V1(payload, Objects.Fnv32OffsetBasis);
```

An empty span returns the seed unchanged, so chunks can be folded together by passing the previous
result as the next seed. Encode text yourself, so the encoding is part of your format rather than an
assumption of this one. Being a 32-bit non-cryptographic hash, it is for identity and change
detection, never for security.

---

### Formatting

**Human-readable byte counts:**

<!-- doc-sample: compiles -->

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

long bytes = 1536000;
string formatted = FormattingHelpers.FormatBytes(bytes);
// Result: "1.46 MB"
```

Auto-scales to B, KB, MB, GB, TB.

**Use for:**

- File size displays
- Memory usage UI
- Profiling output
- Download progress

---

### Multi-Dimensional Array Iteration

**Enumerate 2D/3D array indices:**

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

int[,] grid = new int[10, 10];

// Get all indices as tuples
foreach (var (x, y) in IterationHelpers.IndexOver(grid))
{
    grid[x, y] = x + y;
}

// Buffered (reduces allocations)
using (var buffer = Buffers<(int, int)>.List.Get())
{
    IterationHelpers.IndexOver(grid, buffer.Value);
    foreach (var (x, y) in buffer.Value)
    {
        // Process
    }
}
```

Also supports 3D arrays with `(int, int, int)` tuples.

---

### Binary Array Conversion

**Marshalling between int[] and byte[]:**

<!-- doc-sample: compiles -->

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

int[] ints = { 1, 2, 3, 4, 5 };

// Convert to bytes (uses Buffer.BlockCopy)
byte[] bytes = ArrayConverter.IntArrayToByteArrayBlockCopy(ints);

// Convert back
int[] restored = ArrayConverter.ByteArrayToIntArrayBlockCopy(bytes);
```

**Use for:**

- Network serialization
- Binary file formats
- Save game data
- High-performance data conversion

**Performance:** Uses native memory copy (Buffer.BlockCopy) which is faster than element-by-element loops due to optimized native implementation, though both are O(n).

---

### Custom Comparers

**Create IComparer from lambda:**

<!-- doc-sample: compiles -->

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

public sealed class Enemy : MonoBehaviour { public int health; }

var enemies = new List<Enemy>();

// Sort by health descending
enemies.Sort(new FuncBasedComparer<Enemy>((a, b) =>
    b.health.CompareTo(a.health) // Descending
));
```

**Reverse any comparer:**

```csharp
var comparer = Comparer<int>.Default;
var reversed = new ReverseComparer<int>(comparer);

// Now sorts descending
list.Sort(reversed);
```

---

<a id="environment-detection"></a>

## Environment Detection

### CI/CD Detection

**Detect if running in a CI environment:**

<!-- doc-sample: compiles -->

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

if (Helpers.IsRunningInContinuousIntegration)
{
    // Skip interactive dialogs, use defaults
}

if (Helpers.IsRunningInBatchMode)
{
    // Running headless (no graphics device)
}
```

**Supported CI systems (checked via environment variables):**

| CI System           | Environment Variable     |
| ------------------- | ------------------------ |
| Generic CI          | `CI`                     |
| GitHub Actions      | `GITHUB_ACTIONS`         |
| GitLab CI           | `GITLAB_CI`              |
| Jenkins             | `JENKINS_URL`            |
| Travis CI           | `TRAVIS`                 |
| CircleCI            | `CIRCLECI`               |
| Azure Pipelines     | `TF_BUILD`               |
| TeamCity            | `TEAMCITY_VERSION`       |
| Buildkite           | `BUILDKITE`              |
| AWS CodeBuild       | `CODEBUILD_BUILD_ID`     |
| Bitbucket Pipelines | `BITBUCKET_BUILD_NUMBER` |
| AppVeyor            | `APPVEYOR`               |
| Drone CI            | `DRONE`                  |
| Unity CI            | `UNITY_CI`               |
| Unity Tests         | `UNITY_TESTS`            |

**Check specific environment variables:**

<!-- doc-sample: compiles -->

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

// Check if a specific environment variable is set (non-empty, non-whitespace)
bool onGitHub = Helpers.IsEnvironmentVariableSet(
    Helpers.CiEnvironmentVariables.GitHubActions
);

bool onJenkins = Helpers.IsEnvironmentVariableSet(
    Helpers.CiEnvironmentVariables.JenkinsUrl
);

// Access all known CI variable names
foreach (string varName in Helpers.CiEnvironmentVariables.All)
{
    if (Helpers.IsEnvironmentVariableSet(varName))
    {
        Debug.Log($"CI detected via: {varName}");
    }
}
```

**Use for:**

- Skipping interactive dialogs in CI
- Disabling expensive editor visualizations
- Conditional test behavior
- Build automation scripts
- Asset processors that shouldn't run headless

---

## Best Practices

### Performance

- **Cache lookups**: `Helpers.Find<T>()` caches, but don't call every frame anyway
- **Use buffered variants**: `IterateOverAllChildrenRecursively` with buffers for hot paths
- **Main thread dispatch**: Don't send hundreds of tiny tasks, batch work
- **Hierarchy traversal**: Use breadth-first with depth limits for large hierarchies

### Threading

- **Main thread rule**: Only Unity APIs need main thread, pure C# can stay on background threads
- **Avoid blocking**: Don't wait for main thread results in tight loops
- **CancellationToken**: Support cancellation for long operations

### Architecture

- **Component vs Helper**: Components (MonoBehaviours) for per-object state, Helpers for stateless operations
- **Static method smell**: If you need instance state, use a component instead
- **Editor/Runtime split**: Use `#if UNITY_EDITOR` guards for editor-only helpers

### Code Organization

- **Namespace imports**: Use `using WallstopStudios.UnityHelpers.Core.Helper;` at top of file
- **Don't extend helpers**: These are sealed utility classes, not inheritance hierarchies
- **Prefer composition**: Use helpers from components, don't try to combine them

---

## Related Documentation

- [Intelligent Pooling System](./pooling-guide.md) - Advanced object pooling with auto-purging
- [Math & Extensions](./math-and-extensions.md) - Extension methods on built-in types
- [Utility Components](../inspector/utility-components.md) - MonoBehaviour-based utilities
- [Reflection Helpers](./reflection-helpers.md) - High-performance reflection utilities
- [Singletons](./singletons.md) - RuntimeSingleton and ScriptableObjectSingleton
- [Data Structures](./data-structures.md) - Cache, spatial trees, and other collections
