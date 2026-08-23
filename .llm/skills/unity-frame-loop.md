# Skill: Unity Frame Loop

<!-- trigger: Update, FixedUpdate, LateUpdate, coroutine, WaitForSeconds | Per-frame callbacks, update managers, coroutine cost | Performance -->

**Trigger**: When writing or reviewing anything that runs every frame -- `Update`, `FixedUpdate`,
`LateUpdate`, or a coroutine. The remaining Unity-specific optimizations are in
[unity-performance-patterns](./unity-performance-patterns.md).

---

## Update Methods

### Remove Empty Callbacks

Even empty Unity callbacks have overhead due to managed/unmanaged code boundary crossing:

```csharp
// ❌ BAD: Remove these if not used
void Update() { }
void FixedUpdate() { }
void LateUpdate() { }
void OnGUI() { }
```

### Update Method Selection

| Method          | When Called                    | Use For                                    |
| --------------- | ------------------------------ | ------------------------------------------ |
| `Update()`      | Every frame (variable rate)    | Input, game logic, non-physics movement    |
| `FixedUpdate()` | Fixed timestep (default 50 Hz) | Physics operations, Rigidbody manipulation |
| `LateUpdate()`  | After all Update() calls       | Camera follow, post-processing positions   |

### Anti-Patterns

- **Physics in Update()**: Inconsistent behavior at different framerates
- **Input in FixedUpdate()**: May miss input events between fixed steps
- **Heavy logic every frame**: Consider spreading work across frames

---

## Centralized Update Manager Pattern

When you have many MonoBehaviours with `Update()`, the managed/native code boundary crossing adds significant overhead. Use a centralized manager instead:

```csharp
// ❌ BAD: 1000 MonoBehaviours each with Update = significant overhead
public class Enemy : MonoBehaviour
{
    void Update()
    {
        UpdateAI();
    }
}

// ✅ GOOD: Single Update call manages all entities
public class EnemyManager : MonoBehaviour
{
    private readonly List<Enemy> _enemies = new List<Enemy>(256);

    public void Register(Enemy enemy) => _enemies.Add(enemy);
    public void Unregister(Enemy enemy) => _enemies.Remove(enemy);

    void Update()
    {
        // Single native->managed boundary crossing
        for (int i = 0; i < _enemies.Count; i++)
        {
            _enemies[i].UpdateAI();
        }
    }
}

// Enemy becomes:
public class Enemy : MonoBehaviour
{
    void OnEnable() => EnemyManager.Instance.Register(this);
    void OnDisable() => EnemyManager.Instance.Unregister(this);

    public void UpdateAI()
    {
        // AI logic here
    }
}
```

**Benefits:**

- Reduces managed/native boundary crossings from N to 1
- Easier to profile (single entry point)
- Can implement prioritization, spatial partitioning, etc.

---

## Coroutine Optimization

### Cache WaitForSeconds

```csharp
// ❌ BAD: Allocates every iteration
IEnumerator BadCoroutine()
{
    while (true)
    {
        yield return new WaitForSeconds(1f);  // Allocation!
        DoWork();
    }
}

// ✅ GOOD: Cache and reuse
private readonly WaitForSeconds _waitOneSecond = new WaitForSeconds(1f);
private readonly WaitForEndOfFrame _waitEndOfFrame = new WaitForEndOfFrame();
private readonly WaitForFixedUpdate _waitFixedUpdate = new WaitForFixedUpdate();

IEnumerator GoodCoroutine()
{
    while (true)
    {
        yield return _waitOneSecond;  // No allocation!
        DoWork();
    }
}
```

### Yield Null Is Free

```csharp
// ✅ yield return null has no allocation
IEnumerator FrameByFrameCoroutine()
{
    while (condition)
    {
        yield return null;  // Free!
        ProcessNextStep();
    }
}
```

---

## Related Skills

- [unity-performance-patterns](./unity-performance-patterns.md) - The rest of the Unity-specific set
- [high-performance-csharp](./high-performance-csharp.md) - General allocation-free patterns
- [gc-architecture-unity](./gc-architecture-unity.md) - Why per-frame allocation costs what it does
- [use-pooling](./use-pooling.md) - Object pooling strategies
