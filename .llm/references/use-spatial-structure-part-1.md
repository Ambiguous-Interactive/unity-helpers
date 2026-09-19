# use-spatial-structure - Part 1

## Split Content

**Trigger**: When implementing spatial queries, collision detection, or proximity-based logic.

---

## Available Structures

### 2D Structures

| Structure          | Best For                              |
| ------------------ | ------------------------------------- |
| `QuadTree2D<T>`    | General-purpose, balanced performance |
| `KDTree2D<T>`      | Nearest neighbor queries              |
| `RTree2D<T>`       | Range queries, bounding box searches  |
| `SpatialHash2D<T>` | Uniform distribution, fast insertion  |

### 3D Structures

| Structure          | Best For                              |
| ------------------ | ------------------------------------- |
| `OctTree3D<T>`     | General-purpose, balanced performance |
| `KDTree3D<T>`      | Nearest neighbor queries              |
| `RTree3D<T>`       | Range queries, bounding box searches  |
| `SpatialHash3D<T>` | Uniform distribution, fast insertion  |

---

## Selection Guide

```text
What's your primary query type?
├─ Nearest neighbor → KDTree
├─ Range/radius queries → RTree or QuadTree/OctTree
├─ Bounding box queries → RTree
└─ Frequent updates (dynamic objects) → SpatialHash

Is your data distribution uniform?
├─ YES → SpatialHash (fastest)
└─ NO (clustered) → QuadTree/OctTree or RTree
```

---

## Common API

All spatial structures implement `ISpatialTree<T>` with these methods:

```csharp
// Insert element at position
void Insert(T element, Vector2 position);  // 2D
void Insert(T element, Vector3 position);  // 3D

// Remove element
bool Remove(T element);

// Clear all elements
void Clear();

// Query by radius
IEnumerable<T> GetElementsInRange(Vector2 center, float radius);
IEnumerable<T> GetElementsInRange(Vector3 center, float radius);

// Query by bounds
IEnumerable<T> GetElementsInBounds(Bounds2D bounds);
IEnumerable<T> GetElementsInBounds(Bounds bounds);

// Nearest neighbors
IEnumerable<T> GetApproximateNearestNeighbors(Vector2 point, int count);
IEnumerable<T> GetApproximateNearestNeighbors(Vector3 point, int count);
```

---

## Usage Examples

### QuadTree2D for Enemy Detection

```csharp
public class EnemyManager : MonoBehaviour
{
    private QuadTree2D<Enemy> enemyTree;

    private void Awake()
    {
        Bounds2D worldBounds = new Bounds2D(Vector2.zero, new Vector2(100, 100));
        enemyTree = new QuadTree2D<Enemy>(worldBounds);
    }

    public void RegisterEnemy(Enemy enemy)
    {
        enemyTree.Insert(enemy, enemy.Position);
    }

    public void UnregisterEnemy(Enemy enemy)
    {
        enemyTree.Remove(enemy);
    }

    public List<Enemy> GetEnemiesNearPlayer(Vector2 playerPos, float range)
    {
        using var lease = Buffers<Enemy>.List.Get(out List<Enemy> result);
        foreach (Enemy enemy in enemyTree.GetElementsInRange(playerPos, range))
        {
            result.Add(enemy);
        }
        return new List<Enemy>(result);  // Return copy, lease returns to pool
    }
}
```

### KDTree3D for Nearest Neighbor

```csharp
public class TargetingSystem : MonoBehaviour
{
    private KDTree3D<TargetableUnit> targetTree;

    public TargetableUnit GetNearestTarget(Vector3 origin, int maxCandidates = 5)
    {
        TargetableUnit nearest = null;
        float nearestDist = float.MaxValue;

        foreach (TargetableUnit unit in targetTree.GetApproximateNearestNeighbors(origin, maxCandidates))
        {
            float dist = Vector3.Distance(origin, unit.Position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = unit;
            }
        }

        return nearest;
    }
}
```

### SpatialHash2D for Projectiles

```csharp
public class ProjectileManager : MonoBehaviour
{
    private SpatialHash2D<Projectile> projectileHash;

    private void Awake()
    {
        // Cell size should match typical query radius
        projectileHash = new SpatialHash2D<Projectile>(cellSize: 5f);
    }

    private void FixedUpdate()
    {
        // Rebuild hash each frame for dynamic objects
        projectileHash.Clear();
        foreach (Projectile proj in activeProjectiles)
        {
            projectileHash.Insert(proj, proj.Position);
        }
    }

    public IEnumerable<Projectile> GetProjectilesNear(Vector2 point, float radius)
    {
        return projectileHash.GetElementsInRange(point, radius);
    }
}
```
