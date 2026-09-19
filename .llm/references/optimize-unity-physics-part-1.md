# optimize-unity-physics - Part 1

## Split Content

**Trigger**: When working with Unity physics systems including colliders, raycasts, rigidbodies, or any Physics/Physics2D API calls. This skill focuses on eliminating allocations and optimizing physics performance.

---

## When to Use This Skill

- Implementing collision detection or physics-based gameplay
- Using raycasts, overlap checks, or other physics queries
- Configuring colliders and rigidbodies
- Optimizing physics-heavy scenes
- Targeting mobile or XR platforms where physics cost is critical

---

## Physics Query Allocations

### The Problem

Unity's physics APIs often allocate arrays for results. In hot paths, this creates garbage:

```csharp
// ❌ BAD: Allocates new array every call
void FindTargets()
{
    Collider[] hits = Physics.OverlapSphere(transform.position, radius);
    foreach (Collider hit in hits)
    {
        ProcessTarget(hit);
    }
}
```

### The Solution: NonAlloc APIs

Unity provides non-allocating versions that write to pre-allocated buffers:

```csharp
// ✅ GOOD: Pre-allocated buffer, zero allocations
private readonly Collider[] _hitBuffer = new Collider[32];

void FindTargets()
{
    int count = Physics.OverlapSphereNonAlloc(
        transform.position, radius, _hitBuffer);

    for (int i = 0; i < count; i++)
    {
        ProcessTarget(_hitBuffer[i]);
    }
}
```

---

## Non-Allocating Physics API Reference

### Physics (3D)

| Allocating API           | Non-Allocating Alternative       |
| ------------------------ | -------------------------------- |
| `Physics.RaycastAll`     | `Physics.RaycastNonAlloc`        |
| `Physics.OverlapSphere`  | `Physics.OverlapSphereNonAlloc`  |
| `Physics.OverlapBox`     | `Physics.OverlapBoxNonAlloc`     |
| `Physics.OverlapCapsule` | `Physics.OverlapCapsuleNonAlloc` |
| `Physics.SphereCastAll`  | `Physics.SphereCastNonAlloc`     |
| `Physics.BoxCastAll`     | `Physics.BoxCastNonAlloc`        |
| `Physics.CapsuleCastAll` | `Physics.CapsuleCastNonAlloc`    |

### Physics2D

| Allocating API                 | Non-Allocating Alternative             |
| ------------------------------ | -------------------------------------- |
| `Physics2D.RaycastAll`         | `Physics2D.RaycastNonAlloc`            |
| `Physics2D.OverlapCircleAll`   | `Physics2D.OverlapCircleNonAlloc`      |
| `Physics2D.OverlapBoxAll`      | `Physics2D.OverlapBoxNonAlloc`         |
| `Physics2D.OverlapCapsuleAll`  | `Physics2D.OverlapCapsuleNonAlloc`     |
| `Physics2D.OverlapAreaAll`     | `Physics2D.OverlapAreaNonAlloc`        |
| `Physics2D.CircleCastAll`      | `Physics2D.CircleCastNonAlloc`         |
| `Physics2D.BoxCastAll`         | `Physics2D.BoxCastNonAlloc`            |
| `Physics2D.GetRayIntersection` | `Physics2D.GetRayIntersectionNonAlloc` |

---

## Raycast Patterns

### Single Raycast (No Allocation by Default)

```csharp
// ✅ Single raycast doesn't allocate
if (Physics.Raycast(origin, direction, out RaycastHit hit, maxDistance))
{
    ProcessHit(hit);
}
```

### Multiple Results (Use NonAlloc)

```csharp
private readonly RaycastHit[] _raycastBuffer = new RaycastHit[16];

void FindAllHits(Ray ray)
{
    int count = Physics.RaycastNonAlloc(ray, _raycastBuffer, 100f);

    for (int i = 0; i < count; i++)
    {
        ProcessHit(_raycastBuffer[i]);
    }
}
```

### Sorting Results by Distance

```csharp
private readonly RaycastHit[] _raycastBuffer = new RaycastHit[16];

void FindClosestHit(Ray ray)
{
    int count = Physics.RaycastNonAlloc(ray, _raycastBuffer, 100f);

    if (count == 0) return;

    // Sort by distance (NonAlloc doesn't guarantee order)
    System.Array.Sort(_raycastBuffer, 0, count,
        Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance)));

    ProcessHit(_raycastBuffer[0]);
}
```

---

## Overlap Check Patterns

### Sphere Check

```csharp
private readonly Collider[] _overlapBuffer = new Collider[32];

void FindEnemiesInRange(Vector3 position, float radius, LayerMask enemyLayer)
{
    int count = Physics.OverlapSphereNonAlloc(
        position, radius, _overlapBuffer, enemyLayer);

    for (int i = 0; i < count; i++)
    {
        if (_overlapBuffer[i].TryGetComponent<Enemy>(out var enemy))
        {
            enemy.Alert();
        }
    }
}
```

### Box Check with Rotation

```csharp
private readonly Collider[] _boxBuffer = new Collider[16];

void CheckZone(Vector3 center, Vector3 halfExtents, Quaternion rotation)
{
    int count = Physics.OverlapBoxNonAlloc(
        center, halfExtents, _boxBuffer, rotation);

    for (int i = 0; i < count; i++)
    {
        ProcessCollider(_boxBuffer[i]);
    }
}
```

---
