# optimize-unity-physics - Part 2

## Split Content

## Collider Performance

### Performance Hierarchy (Best to Worst)

| Collider Type     | Performance     | Use Case                   |
| ----------------- | --------------- | -------------------------- |
| Sphere            | ★★★★★ (fastest) | Radial proximity detection |
| Capsule           | ★★★★☆           | Character controllers      |
| Box               | ★★★☆☆           | Rectangular objects        |
| Mesh (Convex)     | ★★☆☆☆           | Complex but limited shapes |
| Mesh (Non-Convex) | ★☆☆☆☆ (slowest) | AVOID - use compound       |

### Critical Rules

1. **Never use non-convex mesh colliders** — Replace with compound primitive colliders
2. **Prefer spheres** — A sphere approximation is often sufficient
3. **Limit vertices** — Convex mesh colliders should have < 255 vertices

### Compound Collider Pattern

```csharp
// Instead of one complex mesh collider, use multiple primitives:
// - One box for the main body
// - Spheres for rounded ends
// - Capsules for cylindrical parts

// This is configured in the Inspector hierarchy:
// Parent (Rigidbody)
// ├── Body (BoxCollider)
// ├── Head (SphereCollider)
// └── Arms (CapsuleCollider)
```

---

## Layer Collision Matrix

Configure which layers can collide in **Project Settings > Physics > Layer Collision Matrix**.

### Benefits

- Skips collision checks between non-interacting layers
- Reduces broad-phase overhead
- Critical for large scenes

### Example Configuration

```text
Layer Setup:
- Player (Layer 8)
- Enemy (Layer 9)
- PlayerBullet (Layer 10)
- EnemyBullet (Layer 11)
- Environment (Layer 12)

Collision Matrix:
- PlayerBullet collides with: Enemy, Environment
- EnemyBullet collides with: Player, Environment
- Player collides with: Enemy, Environment, EnemyBullet
- Enemy collides with: Player, Environment, PlayerBullet
```

### Scripted Layer Configuration

```csharp
// Ignore collision between two layers
Physics.IgnoreLayerCollision(playerBulletLayer, playerLayer);
Physics.IgnoreLayerCollision(enemyBulletLayer, enemyLayer);
```

---

## Rigidbody Best Practices

### Static Colliders

Objects that never move should have:

- Collider component
- **No Rigidbody** (Unity optimizes these as static)
- Do not move via Transform (causes physics recalculation)

### Kinematic Rigidbodies

For scripted movement (not physics-driven):

```csharp
// In Inspector or code:
_rigidbody.isKinematic = true;

// Move with MovePosition/MoveRotation for physics compatibility
void FixedUpdate()
{
    _rigidbody.MovePosition(targetPosition);
    _rigidbody.MoveRotation(targetRotation);
}
```

### Constraint Unused Axes

```csharp
// Freeze axes that aren't needed
// Reduces physics calculations

// 2D game - freeze Z position and X/Y rotation:
_rigidbody.constraints =
    RigidbodyConstraints.FreezePositionZ |
    RigidbodyConstraints.FreezeRotationX |
    RigidbodyConstraints.FreezeRotationY;
```

### Disable Gravity When Not Needed

```csharp
// Flying enemies, floating objects, etc.
_rigidbody.useGravity = false;
```

---

## Physics Settings Optimization

### Time Settings

```csharp
// Project Settings > Time
// Fixed Timestep: 0.02 (50 Hz) - default
// For mobile: Consider 0.0333 (30 Hz)

// In code:
Time.fixedDeltaTime = 0.0333f; // 30 Hz
```

### Solver Iterations

```csharp
// Reduce for better performance (less accuracy)
// Project Settings > Physics > Default Solver Iterations
// Default: 6, Mobile: 2-4

Physics.defaultSolverIterations = 4;
Physics.defaultSolverVelocityIterations = 1;
```

### Sleep Threshold

```csharp
// Increase to put objects to sleep faster
Physics.sleepThreshold = 0.01f; // Default 0.005
```

---
