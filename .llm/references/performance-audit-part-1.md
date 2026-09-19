# performance-audit - Part 1

## Split Content

**Trigger**: When reviewing or writing performance-sensitive code, or when asked to optimize.

---

## Allocation Checklist

### ❌ LINQ in Hot Paths

LINQ methods allocate iterators and delegates:

```csharp
// ❌ Allocates: iterator, delegate, list
List<Enemy> active = enemies.Where(e => e.Health > 0).ToList();

// ❌ Allocates delegate
bool hasTarget = items.Any(x => x.Id == targetId);

// ❌ Allocates iterator and delegate
string name = items.Select(x => x.Name).FirstOrDefault();
```

**Fix**: Use explicit loops:

```csharp
// ✅ Zero allocations with pooled buffer
using var lease = Buffers<Enemy>.List.Get(out List<Enemy> active);
for (int i = 0; i < enemies.Count; i++)
{
    Enemy enemy = enemies[i];
    if (enemy.Health > 0)
    {
        active.Add(enemy);
    }
}
```

### ❌ Closures That Capture Variables

Closures allocate heap objects:

```csharp
// ❌ Captures searchId, allocates closure
Item found = list.Find(item => item.Id == searchId);

// ❌ Captures this, allocates closure
items.RemoveAll(x => x.Owner == this);
```

**Fix**: Use explicit loops or pass state via parameters:

```csharp
// ✅ No allocation
Item found = null;
for (int i = 0; i < list.Count; i++)
{
    if (list[i].Id == searchId)
    {
        found = list[i];
        break;
    }
}
```

### ❌ String Operations in Loops

```csharp
// ❌ Creates new string each iteration
string result = "";
for (int i = 0; i < items.Count; i++)
{
    result += items[i].Name;
}

// ❌ String.Format allocates
string msg = string.Format("Value: {0}", value);
```

**Fix**: Use StringBuilder or cache strings:

```csharp
// ✅ Single allocation
StringBuilder sb = new StringBuilder();
for (int i = 0; i < items.Count; i++)
{
    sb.Append(items[i].Name);
}
string result = sb.ToString();
```

### ❌ Frequent Collection Allocations

```csharp
// ❌ New allocation every call
void ProcessItems()
{
    List<Item> temp = new List<Item>();
    // ...
}
```

**Fix**: Use pooled collections:

```csharp
// ✅ Zero allocation (pooled)
void ProcessItems()
{
    using var lease = Buffers<Item>.List.Get(out List<Item> temp);
    // ...
}
```

---

## Pooling Patterns

### Collection Pooling

```csharp
// List pooling
using var listLease = Buffers<T>.List.Get(out List<T> buffer);

// HashSet pooling
using var setLease = Buffers<T>.HashSet.Get(out HashSet<T> buffer);
```

### Array Pooling

See the [Array Pooling Guide](../skills/use-array-pool.md) for detailed guidance.

Quick reference:

- **Fixed sizes** → `WallstopArrayPool<T>`
- **Variable sizes** → `SystemArrayPool<T>`

---

## Struct vs Class

### Use Structs When

- Data container under ~16 bytes
- No inheritance needed
- Short-lived, frequently created
- Value semantics desired

```csharp
// ✅ Good struct candidate
public readonly struct Vector2Int
{
    public readonly int X;
    public readonly int Y;
}
```

### Avoid Boxing

```csharp
// ❌ Boxing occurs
object boxed = myStruct;
IComparable comparable = myStruct;

// ❌ Non-generic collections box
ArrayList list = new ArrayList();
list.Add(myStruct);  // Boxing

// ✅ Use generic collections
List<MyStruct> list = new List<MyStruct>();
```

---
