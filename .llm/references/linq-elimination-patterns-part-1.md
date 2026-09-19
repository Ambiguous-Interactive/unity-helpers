# linq-elimination-patterns - Part 1

## Split Content

**Trigger**: When eliminating LINQ from code to achieve zero-allocation patterns. Use this skill when:

- Converting existing LINQ code to explicit loops
- Understanding why LINQ is forbidden in this codebase
- Implementing zero-allocation visitor patterns
- Refactoring code for hot path performance

---

## When to Use This Skill

Use this skill when you encounter LINQ methods in code that needs to be allocation-free. LINQ is forbidden in runtime code because every LINQ method allocates:

- **Iterator object** - The `IEnumerator<T>` state machine
- **Delegate allocation** - Every lambda/predicate passed
- **Closure objects** - When lambdas capture variables

Even "streaming" LINQ (`IEnumerable` without `ToList()`) still allocates the iterator and delegate on every call.

---

## LINQ is FORBIDDEN in Runtime Code

**LINQ is NEVER acceptable in runtime code.** All LINQ operations must be converted to explicit loops or visitor patterns.

### LINQ Allocation Breakdown

| Method       | Allocations                          |
| ------------ | ------------------------------------ |
| `.Where()`   | WhereIterator + delegate             |
| `.Select()`  | SelectIterator + delegate            |
| `.Any()`     | Delegate (no iterator if early exit) |
| `.First()`   | Delegate                             |
| `.ToList()`  | New `List<T>`                        |
| `.ToArray()` | New `T[]`                            |
| `.Count()`   | Enumeration (may allocate)           |
| `.Sum()`     | Delegate                             |
| `.OrderBy()` | Buffer + comparer                    |

---

## Zero-Allocation Visitor Pattern

Instead of LINQ, use explicit loops with the visitor pattern:

```csharp
// FORBIDDEN: LINQ streaming (still allocates iterator + delegate!)
IEnumerable<Event> events = GetEventsLazy();
foreach (Event e in events.Where(e => e.IsRecent))
{
    ProcessEvent(e);
}

// REQUIRED: Zero-allocation visitor pattern
public void VisitRecentEvents(IReadOnlyList<Event> events, Action<Event> visitor)
{
    for (int i = 0; i < events.Count; i++)
    {
        Event e = events[i];
        if (e.IsRecent)
        {
            visitor(e);
        }
    }
}

// BETTER: Inline visitor logic to avoid delegate allocation
for (int i = 0; i < events.Count; i++)
{
    Event e = events[i];
    if (e.IsRecent)
    {
        ProcessEvent(e);
    }
}

// BEST: Generic visitor with ref struct for complex state
public ref struct EventVisitor
{
    public int ProcessedCount;
    public int TotalDamage;

    public void Visit(Event e)
    {
        if (e.IsRecent)
        {
            ProcessedCount++;
            TotalDamage += e.Damage;
        }
    }
}

EventVisitor visitor = new EventVisitor();
for (int i = 0; i < events.Count; i++)
{
    visitor.Visit(events[i]);
}
```

---

## Pattern: Where + FirstOrDefault

```csharp
// BEFORE: Allocates iterator and delegate
Enemy target = enemies.Where(e => e.IsAlive && e.Team != myTeam)
                      .FirstOrDefault();

// AFTER: Zero allocation
Enemy target = null;
for (int i = 0; i < enemies.Count; i++)
{
    Enemy e = enemies[i];
    if (e.IsAlive && e.Team != myTeam)
    {
        target = e;
        break;
    }
}
```

---

## Pattern: Where + ToList

```csharp
// BEFORE: Allocates iterator, delegate, AND new list
List<Enemy> activeEnemies = enemies.Where(e => e.IsActive).ToList();

// AFTER: Zero allocation with pooling
using var lease = Buffers<Enemy>.List.Get(out List<Enemy> activeEnemies);
for (int i = 0; i < enemies.Count; i++)
{
    Enemy e = enemies[i];
    if (e.IsActive)
    {
        activeEnemies.Add(e);
    }
}
```

---

## Pattern: Select + ToArray

```csharp
// BEFORE: Allocates iterator, delegate, and array
string[] names = items.Select(x => x.Name).ToArray();

// AFTER: Zero allocation (if size known)
using PooledArray<string> pooled = SystemArrayPool<string>.Get(items.Count, out string[] names);
for (int i = 0; i < pooled.Length; i++)
{
    names[i] = items[i].Name;
}
```

---

## Pattern: Any

```csharp
// BEFORE: Allocates iterator and delegate
bool hasActive = enemies.Any(e => e.IsActive);

// AFTER: Zero allocation
bool hasActive = false;
for (int i = 0; i < enemies.Count; i++)
{
    if (enemies[i].IsActive)
    {
        hasActive = true;
        break;
    }
}
```

---
