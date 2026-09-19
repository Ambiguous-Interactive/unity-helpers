# use-algorithmic-structures - Part 3

## Split Content

## BitSet / ImmutableBitSet

Compact bit storage using a single bit per boolean flag. Ideal for entity state masks, collision layers, and dense flag arrays.

### API

```csharp
BitSet bits = new BitSet(initialCapacity);

bits.TrySet(index);                 // Set bit to 1
bits.TryClear(index);               // Set bit to 0
bits.TryGet(index, out bool value); // Read bit
bits[index];                        // Indexer (get/set)
bits.Capacity;                      // Current capacity
bits.SetAll();                      // Set all bits to 1
bits.ClearAll();                    // Set all bits to 0
bits.And(other);                    // Bitwise AND
bits.Or(other);                     // Bitwise OR
bits.Xor(other);                    // Bitwise XOR
bits.Not();                         // Bitwise NOT

// ImmutableBitSet for read-only scenarios
ImmutableBitSet immutable = new ImmutableBitSet(bits);
ImmutableBitSet immutable = new ImmutableBitSet(trueIndices);
```

### Example: Entity State Flags

```csharp
public class EntityStateManager
{
    private enum StateFlag { Active = 0, Visible = 1, Damaged = 2, Invincible = 3 }

    private readonly BitSet _entityStates;

    public EntityStateManager(int maxEntities)
    {
        // 4 flags per entity
        _entityStates = new BitSet(maxEntities * 4);
    }

    private int GetFlagIndex(int entityId, StateFlag flag) => entityId * 4 + (int)flag;

    public void SetFlag(int entityId, StateFlag flag)
    {
        _entityStates.TrySet(GetFlagIndex(entityId, flag));
    }

    public void ClearFlag(int entityId, StateFlag flag)
    {
        _entityStates.TryClear(GetFlagIndex(entityId, flag));
    }

    public bool HasFlag(int entityId, StateFlag flag)
    {
        return _entityStates.TryGet(GetFlagIndex(entityId, flag), out bool value) && value;
    }
}
```

### Example: Layer Mask Operations

```csharp
public class LayerMaskHelper
{
    public static BitSet FromUnityLayerMask(LayerMask mask)
    {
        BitSet bits = new BitSet(32);
        int maskValue = mask.value;
        for (int i = 0; i < 32; i++)
        {
            if ((maskValue & (1 << i)) != 0)
            {
                bits.TrySet(i);
            }
        }
        return bits;
    }

    public static BitSet CombineMasks(BitSet a, BitSet b)
    {
        BitSet result = new BitSet(a.Capacity);
        result.Or(a);
        result.Or(b);
        return result;
    }

    public static BitSet IntersectMasks(BitSet a, BitSet b)
    {
        BitSet result = new BitSet(a.Capacity);
        result.Or(a);
        result.And(b);
        return result;
    }
}
```

### Example: Visibility Culling

```csharp
public class VisibilityCuller
{
    private readonly BitSet _visibleObjects;

    public VisibilityCuller(int maxObjects)
    {
        _visibleObjects = new BitSet(maxObjects);
    }

    public void SetVisible(int objectId) => _visibleObjects.TrySet(objectId);
    public void SetHidden(int objectId) => _visibleObjects.TryClear(objectId);
    public bool IsVisible(int objectId) =>
        _visibleObjects.TryGet(objectId, out bool v) && v;

    public void ClearAll() => _visibleObjects.ClearAll();
}
```

---

## Complexity Comparison

| Structure   | Insert | Remove | Search      | Memory         |
| ----------- | ------ | ------ | ----------- | -------------- |
| DisjointSet | -      | -      | O(alpha(n)) | O(n)           |
| Trie        | O(k)   | -      | O(k)        | O(total chars) |
| TimedCache  | -      | -      | O(1)\*      | O(1)           |
| BitSet      | O(1)   | O(1)   | O(1)        | O(n/64)        |

k = string length, alpha = inverse Ackermann function (effectively constant) \* May trigger recomputation if TTL expired

---

## Serialization Support

These structures support ProtoBuf and Unity serialization:

```csharp
[ProtoContract]
public class SaveData
{
    [ProtoMember(1)]
    public DisjointSet Connectivity { get; set; }

    [ProtoMember(2)]
    public BitSet UnlockedFeatures { get; set; }
}
```

---

## Related Skills

- [use-data-structures](../skills/use-data-structures.md) - Overview of all data structures
- [use-priority-structures](../skills/use-priority-structures.md) - Heap and PriorityQueue
- [use-queue-structures](../skills/use-queue-structures.md) - CyclicBuffer and Deque
- [use-spatial-structure](../skills/use-spatial-structure.md) - Spatial trees for proximity queries
