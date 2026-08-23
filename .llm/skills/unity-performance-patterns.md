# Skill: Unity Performance Patterns

<!-- trigger: unity, api, component, cache, pool | Unity-specific optimizations (APIs, pooling) | Performance -->

**Trigger**: When writing Unity-specific code, accessing Unity APIs, or working with MonoBehaviours, GameObjects, or other Unity systems. This skill complements [high-performance-csharp](./high-performance-csharp.md) with Unity-specific patterns.

---

## Unity's Garbage Collector

Unity's Boehm GC differs significantly from .NET's generational collector. Key points:

- **Non-generational** — scans entire heap on every collection
- **No compaction** — memory fragments over time
- **Stop-the-world** — game freezes during collection

**Target**: 0 bytes allocated per frame. At 60 FPS with 1KB/frame = **3.6 MB/minute** of garbage.

See [gc-architecture-unity](./gc-architecture-unity.md) for detailed architecture, incremental GC, and when to manually trigger collection.

---

## Component & Reference Caching

### Cache Component References

`GetComponent<T>()` involves internal lookups and should **never** be called in `Update()`.

```csharp
// ❌ NEVER: Expensive lookup every frame
void Update()
{
    Rigidbody rb = GetComponent<Rigidbody>();
    rb.AddForce(Vector3.up);
}

// ✅ ALWAYS: Cache in Awake()
private Rigidbody _rigidbody;
private Transform _transform;
private Camera _mainCamera;

void Awake()
{
    _rigidbody = GetComponent<Rigidbody>();
    _transform = transform;  // Cache transform property too
    _mainCamera = Camera.main;
}

void Update()
{
    _rigidbody.AddForce(Vector3.up);
    _transform.position = _mainCamera.transform.position;
}
```

### Cache Expensive Properties

Many Unity properties perform work each access:

```csharp
// ❌ BAD: Camera.main performs FindGameObjectWithTag internally
void Update()
{
    Vector3 camPos = Camera.main.transform.position;  // Lookup + property access
}

// ✅ GOOD: Cached reference
private Camera _mainCamera;
void Awake() { _mainCamera = Camera.main; }
void Update()
{
    Vector3 camPos = _mainCamera.transform.position;
}
```

---

## Never Use SendMessage

`SendMessage()` and `BroadcastMessage()` are **up to 1000x slower** than direct function calls due to reflection-based method lookup:

```csharp
// ❌ NEVER: Extremely slow, no compile-time safety
gameObject.SendMessage("OnDamage", damage);
gameObject.BroadcastMessage("OnHit");

// ✅ ALWAYS: Direct interface calls
var damageable = gameObject.GetComponent<IDamageable>();
if (damageable != null)
{
    damageable.OnDamage(damage);
}

// ✅ Or use events/delegates
public event Action<float> OnDamage;
OnDamage?.Invoke(damage);
```

---

## Unity API Allocation Traps

### Array-Valued Properties Create Copies

Many Unity properties return **new array copies** on each access:

```csharp
// ❌ TERRIBLE: Creates 4 array copies per iteration!
void Update()
{
    for (int i = 0; i < mesh.vertices.Length; i++)
    {
        float x = mesh.vertices[i].x;  // New array!
        float y = mesh.vertices[i].y;  // New array!
        float z = mesh.vertices[i].z;  // New array!
    }
}

// ✅ BEST: Use non-allocating API
private List<Vector3> _vertices = new List<Vector3>();

void Update()
{
    mesh.GetVertices(_vertices);  // No allocation!
    for (int i = 0; i < _vertices.Count; i++)
    {
        DoSomething(_vertices[i].x, _vertices[i].y, _vertices[i].z);
    }
}
```

### Non-Allocating Unity API Alternatives

| Allocating API             | Non-Allocating Alternative                             |
| -------------------------- | ------------------------------------------------------ |
| `mesh.vertices`            | `mesh.GetVertices(list)`                               |
| `mesh.normals`             | `mesh.GetNormals(list)`                                |
| `mesh.uv`                  | `mesh.GetUVs(channel, list)`                           |
| `mesh.triangles`           | `mesh.GetTriangles(list, submesh)`                     |
| `Input.touches`            | `Input.touchCount` + `Input.GetTouch(i)`               |
| `Animator.parameters`      | `Animator.parameterCount` + `Animator.GetParameter(i)` |
| `Renderer.sharedMaterials` | `Renderer.GetSharedMaterials(list)`                    |
| `gameObject.tag`           | `gameObject.CompareTag("Tag")`                         |
| `gameObject.name`          | Cache in Awake if needed repeatedly                    |

For physics-specific non-alloc APIs, see [optimize-unity-physics](./optimize-unity-physics.md).

---

## Tag & Layer Comparisons

### Avoid String Allocation

```csharp
// ❌ BAD: .tag allocates a new string
if (gameObject.tag == "Player") { }

// ❌ BAD: .name also allocates
if (gameObject.name == "Enemy") { }

// ✅ GOOD: CompareTag is allocation-free
if (gameObject.CompareTag("Player")) { }

// ✅ GOOD: Cache name if needed repeatedly
private string _cachedName;
void Awake() { _cachedName = gameObject.name; }
```

---

## Update Methods and Coroutines

Choosing between `Update`/`FixedUpdate`/`LateUpdate`, removing empty callbacks, replacing hundreds
of `Update` calls with one manager, and caching `WaitForSeconds` are in
[unity-frame-loop](./unity-frame-loop.md).

---

## GameObject Pooling

### Unity's ObjectPool (Unity 2021+)

```csharp
using UnityEngine.Pool;

public class BulletManager : MonoBehaviour
{
    [SerializeField] private GameObject _bulletPrefab;

    private ObjectPool<GameObject> _bulletPool;

    void Awake()
    {
        _bulletPool = new ObjectPool<GameObject>(
            createFunc: () => Instantiate(_bulletPrefab),
            actionOnGet: bullet => bullet.SetActive(true),
            actionOnRelease: bullet => bullet.SetActive(false),
            actionOnDestroy: bullet => Destroy(bullet),
            defaultCapacity: 50,
            maxSize: 200
        );
    }

    public GameObject SpawnBullet(Vector3 position)
    {
        GameObject bullet = _bulletPool.Get();
        bullet.transform.position = position;
        return bullet;
    }

    public void ReturnBullet(GameObject bullet)
    {
        _bulletPool.Release(bullet);
    }
}
```

### What to Pool

- Projectiles (bullets, missiles)
- Particle effects
- Enemies that spawn/despawn frequently
- UI elements that appear/disappear
- Audio sources for sound effects
- Any frequently Instantiated/Destroyed object

See [use-pooling](./use-pooling.md) for detailed pooling patterns.

---

## Debug.Log Performance

### Remove in Production

```csharp
// ❌ BAD: Debug.Log still executes in builds (string allocation)
Debug.Log($"Player position: {transform.position}");

// ✅ GOOD: Conditional compilation
#if UNITY_EDITOR
Debug.Log($"Player position: {transform.position}");
#endif

// ✅ GOOD: Use [Conditional] attribute for debug methods
[System.Diagnostics.Conditional("UNITY_EDITOR")]
private void LogDebug(string message)
{
    Debug.Log(message);
}
```

---

## String Operations in Unity

### Update Text Efficiently

```csharp
// ❌ BAD: Creates strings every frame
void Update()
{
    scoreText.text = "Score: " + score.ToString();  // 3 allocations!
}

// ✅ BETTER: Only update when changed
private int _lastScore = -1;

void Update()
{
    if (score != _lastScore)
    {
        scoreText.text = "Score: " + score.ToString();
        _lastScore = score;
    }
}

// ✅ BEST: Separate label from value
public TMP_Text scoreLabelText;  // "Score: "
public TMP_Text scoreValueText;  // Just the number

void Start()
{
    scoreLabelText.text = "Score: ";
}

void UpdateScore()
{
    scoreValueText.text = score.ToString();
}
```

---

## Async Operations

### Use Unity's Awaitable (Unity 2023+)

```csharp
// ✅ GOOD: Unity's Awaitable uses pooling internally
async Awaitable LoadDataAsync()
{
    await Awaitable.WaitForSecondsAsync(1f);
    await Awaitable.NextFrameAsync();
}

// ✅ GOOD: Load scenes asynchronously
public async Awaitable LoadSceneAsync(string sceneName)
{
    AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);
    asyncLoad.allowSceneActivation = false;

    while (asyncLoad.progress < 0.9f)
    {
        await Awaitable.NextFrameAsync();
    }
    asyncLoad.allowSceneActivation = true;
}
```

---

## Memory Cleanup

### Scene Transitions

```csharp
// Call during scene transitions to clean up
public void CleanupMemory()
{
    Resources.UnloadUnusedAssets();
    System.GC.Collect();
}
```

### Addressables Cleanup

```csharp
// When using Addressables
Addressables.Release(handle);
Addressables.ReleaseInstance(gameObject);
```

---

## Quick Reference: Unity Anti-Patterns

| ❌ Anti-Pattern                  | ✅ Solution                 |
| -------------------------------- | --------------------------- |
| `GetComponent<T>()` in Update    | Cache in Awake/Start        |
| `Camera.main` in Update          | Cache the reference         |
| `mesh.vertices` repeatedly       | Use `GetVertices(list)`     |
| `gameObject.tag == "Tag"`        | Use `CompareTag("Tag")`     |
| `new WaitForSeconds()` in loop   | Cache yield instructions    |
| `Debug.Log` in builds            | Use conditional compilation |
| Empty Update/FixedUpdate         | Remove unused callbacks     |
| `Instantiate`/`Destroy` spam     | Use object pooling          |
| `SendMessage`/`BroadcastMessage` | Direct interface calls      |
| Many MonoBehaviours with Update  | Centralized update manager  |

See also: [optimize-unity-physics](./optimize-unity-physics.md), [optimize-unity-rendering](./optimize-unity-rendering.md)

---

## Related Skills

- [high-performance-csharp](./high-performance-csharp.md) — Core performance patterns (MANDATORY)
- [unity-frame-loop](./unity-frame-loop.md) — Per-frame callbacks, update managers, coroutines
- [optimize-unity-physics](./optimize-unity-physics.md) — Physics, colliders, raycasts
- [optimize-unity-rendering](./optimize-unity-rendering.md) — Materials, shaders, batching
- [use-pooling](./use-pooling.md) — Collection pooling patterns
- [refactor-to-zero-alloc](./refactor-to-zero-alloc.md) — Migration guide
- [performance-audit](./performance-audit.md) — Performance review checklist
- [gc-architecture-unity](./gc-architecture-unity.md) — Unity GC architecture details
- [memory-allocation-traps](./memory-allocation-traps.md) — Hidden allocation sources
- [mobile-xr-optimization](./mobile-xr-optimization.md) — Mobile and XR patterns
