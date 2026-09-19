# unity-performance-patterns - Part 1

## Split Content

**Trigger**: When writing Unity-specific code, accessing Unity APIs, or working with MonoBehaviours, GameObjects, or other Unity systems. This skill complements [high-performance-csharp](../skills/high-performance-csharp.md) with Unity-specific patterns.

---

## Unity's Garbage Collector

Unity's Boehm GC differs significantly from .NET's generational collector. Key points:

- **Non-generational** — scans entire heap on every collection
- **No compaction** — memory fragments over time
- **Stop-the-world** — game freezes during collection

**Target**: 0 bytes allocated per frame. At 60 FPS with 1KB/frame = **3.6 MB/minute** of garbage.

See [gc-architecture-unity](../skills/gc-architecture-unity.md) for detailed architecture, incremental GC, and when to manually trigger collection.

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

For physics-specific non-alloc APIs, see [optimize-unity-physics](../skills/optimize-unity-physics.md).

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
[unity-frame-loop](../skills/unity-frame-loop.md).

---
