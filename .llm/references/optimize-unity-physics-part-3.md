# optimize-unity-physics - Part 3

## Split Content

## Physics in Update vs FixedUpdate

### The Rule

- **Physics operations** → `FixedUpdate()`
- **Physics queries** → Either (but be consistent)
- **Input-driven physics** → Read input in `Update()`, apply in `FixedUpdate()`

### Correct Pattern

```csharp
private Vector3 _inputForce;

void Update()
{
    // Read input (happens every frame)
    float h = Input.GetAxis("Horizontal");
    float v = Input.GetAxis("Vertical");
    _inputForce = new Vector3(h, 0, v) * moveSpeed;
}

void FixedUpdate()
{
    // Apply physics (happens at fixed rate)
    _rigidbody.AddForce(_inputForce);
}
```

---

## Service Pattern for Physics Queries

When multiple systems need the same physics query results, compute once per frame:

```csharp
public class PhysicsQueryService : MonoBehaviour
{
    public static PhysicsQueryService Instance { get; private set; }

    private readonly RaycastHit[] _groundBuffer = new RaycastHit[8];
    private readonly Collider[] _nearbyBuffer = new Collider[32];

    // Cached results
    private bool _isGrounded;
    private RaycastHit _groundHit;
    private int _nearbyCount;

    void Awake() => Instance = this;

    void FixedUpdate()
    {
        // Ground check - computed once
        Ray groundRay = new Ray(transform.position, Vector3.down);
        int groundHits = Physics.RaycastNonAlloc(groundRay, _groundBuffer, 1.1f);
        _isGrounded = groundHits > 0;
        if (_isGrounded) _groundHit = _groundBuffer[0];

        // Nearby enemies - computed once
        _nearbyCount = Physics.OverlapSphereNonAlloc(
            transform.position, 10f, _nearbyBuffer, enemyLayer);
    }

    public bool IsGrounded => _isGrounded;
    public RaycastHit GroundHit => _groundHit;
    public ReadOnlySpan<Collider> NearbyEnemies => new ReadOnlySpan<Collider>(_nearbyBuffer, 0, _nearbyCount);
}
```

---

## Quick Reference: Physics Anti-Patterns

| ❌ Anti-Pattern                     | ✅ Solution                      |
| ----------------------------------- | -------------------------------- |
| `Physics.OverlapSphere` in loop     | `OverlapSphereNonAlloc`          |
| `Physics.RaycastAll` every frame    | `RaycastNonAlloc` + cache        |
| Non-convex mesh colliders           | Compound primitive colliders     |
| Moving static colliders             | Use kinematic Rigidbody          |
| Physics in `Update()`               | Use `FixedUpdate()`              |
| All layers colliding                | Configure Layer Collision Matrix |
| High solver iterations on mobile    | Reduce to 2-4                    |
| Physics queries in multiple scripts | Centralized query service        |

---

## Related Skills

- [unity-performance-patterns](../skills/unity-performance-patterns.md) — General Unity optimization
- [high-performance-csharp](../skills/high-performance-csharp.md) — Zero-allocation patterns
- [mobile-xr-optimization](../skills/mobile-xr-optimization.md) — Mobile and XR physics tuning
- [refactor-to-zero-alloc](../skills/refactor-to-zero-alloc.md) — Migration guide
