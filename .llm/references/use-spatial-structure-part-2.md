# use-spatial-structure - Part 2

## Split Content

### RTree2D for Bounding Box Queries

```csharp
public class AreaTriggerSystem : MonoBehaviour
{
    private RTree2D<TriggerZone> triggerTree;

    public List<TriggerZone> GetTriggersInArea(Bounds2D queryArea)
    {
        using var lease = Buffers<TriggerZone>.List.Get(out List<TriggerZone> result);
        foreach (TriggerZone zone in triggerTree.GetElementsInBounds(queryArea))
        {
            result.Add(zone);
        }
        return new List<TriggerZone>(result);
    }
}
```

---

## Performance Tips

### Batch Updates

```csharp
// ❌ Slow - many individual updates
foreach (Enemy enemy in enemies)
{
    tree.Remove(enemy);
    tree.Insert(enemy, enemy.Position);
}

// ✅ Fast - rebuild for many changes
tree.Clear();
foreach (Enemy enemy in enemies)
{
    tree.Insert(enemy, enemy.Position);
}
```

### Choose Appropriate Cell Size (SpatialHash)

```csharp
// Cell size should be ~2x your typical query radius
float queryRadius = 10f;
SpatialHash2D<T> hash = new SpatialHash2D<T>(cellSize: queryRadius * 2);
```

### Use Pooled Results

```csharp
// ✅ Use Buffers for temporary results
using var lease = Buffers<T>.List.Get(out List<T> results);
foreach (T item in tree.GetElementsInRange(center, radius))
{
    results.Add(item);
}
ProcessResults(results);
```

---

## Structure Comparison

| Feature      | QuadTree/OctTree | KDTree       | RTree        | SpatialHash |
| ------------ | ---------------- | ------------ | ------------ | ----------- |
| Insert       | O(log n)         | O(log n)     | O(log n)     | O(1)        |
| Remove       | O(log n)         | O(n) rebuild | O(log n)     | O(1)        |
| Range Query  | O(√n + k)        | O(√n + k)    | O(log n + k) | O(k)        |
| Nearest      | O(log n)         | O(log n)     | O(log n)     | O(n)        |
| Dynamic Data | Good             | Poor         | Good         | Excellent   |
| Memory       | Medium           | Low          | High         | Variable    |

Note: k = number of results
