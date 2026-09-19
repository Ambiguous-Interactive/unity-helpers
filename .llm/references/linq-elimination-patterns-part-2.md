# linq-elimination-patterns - Part 2

## Split Content

## Pattern: Count with Predicate

```csharp
// BEFORE: Allocates iterator and delegate
int activeCount = enemies.Count(e => e.IsActive);

// AFTER: Zero allocation
int activeCount = 0;
for (int i = 0; i < enemies.Count; i++)
{
    if (enemies[i].IsActive)
    {
        activeCount++;
    }
}
```

---

## Pattern: Sum

```csharp
// BEFORE: Allocates delegate
int totalDamage = attacks.Sum(a => a.Damage);

// AFTER: Zero allocation
int totalDamage = 0;
for (int i = 0; i < attacks.Count; i++)
{
    totalDamage += attacks[i].Damage;
}
```

---

## Pattern: OrderBy + ToList

```csharp
// BEFORE: Multiple allocations
List<Enemy> sorted = enemies.OrderBy(e => e.Distance).ToList();

// AFTER: Zero allocation with pooling and in-place sort
using var lease = Buffers<Enemy>.List.Get(out List<Enemy> sorted);
for (int i = 0; i < enemies.Count; i++)
{
    sorted.Add(enemies[i]);
}
sorted.Sort((a, b) => a.Distance.CompareTo(b.Distance));
```

---

## Pattern: GroupBy

```csharp
// BEFORE: Allocates grouping objects, iterator, delegate
var groups = items.GroupBy(x => x.Category);

// AFTER: Manual grouping with pooled dictionary
using var dictLease = Buffers<Category, List<Item>>.Dictionary.Get(
    out Dictionary<Category, List<Item>> groups);

for (int i = 0; i < items.Count; i++)
{
    Item item = items[i];
    if (!groups.TryGetValue(item.Category, out List<Item> group))
    {
        // Note: Inner lists need careful lifecycle management
        group = new List<Item>();
        groups[item.Category] = group;
    }
    group.Add(item);
}
```

---

## Pattern: Distinct

```csharp
// BEFORE: Allocates iterator and HashSet internally
var unique = items.Distinct().ToList();

// AFTER: Zero allocation with pooled HashSet
using var hashLease = Buffers<Item>.HashSet.Get(out HashSet<Item> seen);
using var listLease = Buffers<Item>.List.Get(out List<Item> unique);

for (int i = 0; i < items.Count; i++)
{
    if (seen.Add(items[i]))
    {
        unique.Add(items[i]);
    }
}
```

---

## Pattern: Aggregate/Reduce

```csharp
// BEFORE: Allocates delegate
int product = numbers.Aggregate(1, (acc, n) => acc * n);

// AFTER: Zero allocation
int product = 1;
for (int i = 0; i < numbers.Count; i++)
{
    product *= numbers[i];
}
```

---

## Pattern: All

```csharp
// BEFORE: Allocates delegate
bool allValid = items.All(x => x.IsValid);

// AFTER: Zero allocation
bool allValid = true;
for (int i = 0; i < items.Count; i++)
{
    if (!items[i].IsValid)
    {
        allValid = false;
        break;
    }
}
```

---

## Pattern: Take/Skip

```csharp
// BEFORE: Allocates iterators
var firstFive = items.Skip(10).Take(5).ToList();

// AFTER: Zero allocation with bounds checking
using var lease = Buffers<Item>.List.Get(out List<Item> result);
int start = Math.Min(10, items.Count);
int end = Math.Min(start + 5, items.Count);
for (int i = start; i < end; i++)
{
    result.Add(items[i]);
}
```

---

## When LINQ Might Be Acceptable (Extremely Rare)

LINQ is only acceptable when ALL of these conditions are met:

1. **Editor-only code** - Never called at runtime
2. **One-time initialization** - Called once during app startup, not per-frame
3. **Complexity significantly reduced** - The LINQ version is dramatically clearer
4. **Documented exception** - Comment explains why LINQ was chosen

```csharp
// Editor-only, one-time initialization - LINQ acceptable with documentation
// LINQ Exception: Editor tool initialization, called once on domain reload
private static readonly Dictionary<Type, MethodInfo[]> CachedMethods =
    AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(a => a.GetTypes())
        .ToDictionary(t => t, t => t.GetMethods());
```

**When in doubt: DO NOT use LINQ.** The explicit loop is always safe.

---
