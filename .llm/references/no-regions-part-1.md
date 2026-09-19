# no-regions - Part 1

## Split Content

**Trigger**: When writing or reviewing any C# code in this repository.

---

## The Rule

**NEVER use `#region` or `#endregion` in any C# code.** This is an absolute, non-negotiable rule.

This rule is enforced by:

1. AI agent guidelines (this document and [LLM context](../context.md))
2. Pre-commit git hook (blocks commits containing regions)
3. Code review policy

---

## Why Regions Are Forbidden

### 1. Code Smell Indicator

Regions often indicate a class has grown too large and should be refactored:

- If you need regions to organize a file, the file is too big
- Extract classes, use composition, or split into multiple files
- Well-organized small classes do not need artificial grouping

### 2. Hidden Complexity

Regions hide code rather than organize it:

- Collapsed regions obscure what code exists in a file
- Developers skip reading collapsed sections, missing important details
- Code reviews become less thorough when regions are collapsed

### 3. IDE Navigation Interference

Modern IDEs provide superior navigation:

- Outline views show class structure without regions
- Go-to-definition, find references, and search work better without regions
- Regions add noise to these navigation tools

### 4. Inconsistent Usage

Regions lead to bikeshedding and inconsistency:

- Teams argue about what to group and how to name regions
- Different developers use different grouping strategies
- This wastes time and creates unnecessary diff noise

---

## What To Do Instead

### Organize Through File Structure

```text
Runtime/
  Core/
    DataStructure/
      Cache/
        Cache.cs           # Main Cache<TKey, TValue> implementation
        CacheEntry.cs      # Entry struct
        CachePresets.cs    # Factory methods for common configurations
        ICachePolicy.cs    # Eviction policy interface
```

### Use Partial Classes (Sparingly)

For genuinely large classes that cannot be split, use partial classes in separate files:

```csharp
// SpatialHash.cs - Core implementation
public sealed partial class SpatialHash<T>
{
    public void Add(T item) { /* ... */ }
    public void Remove(T item) { /* ... */ }
}

// SpatialHash.Queries.cs - Query methods
public sealed partial class SpatialHash<T>
{
    public void QueryRadius(Vector2 center, float radius, List<T> results) { /* ... */ }
    public void QueryRect(Rect bounds, List<T> results) { /* ... */ }
}
```

### Follow Consistent Member Ordering

Organize members in a consistent order without regions:

1. Constants and static readonly fields
2. Instance fields
3. Constructors
4. Public properties
5. Public methods
6. Private/internal methods
7. Nested types (if any)

This ordering is self-documenting and does not require regions.

### Extract Classes

If a class has many methods, extract related functionality:

```csharp
// Before: One large class with regions
public class PlayerController
{
    #region Movement
    // 200 lines of movement code
    #endregion

    #region Combat
    // 200 lines of combat code
    #endregion

    #region Inventory
    // 200 lines of inventory code
    #endregion
}

// After: Composition with focused classes
public sealed class PlayerController
{
    private readonly PlayerMovement _movement;
    private readonly PlayerCombat _combat;
    private readonly PlayerInventory _inventory;
}
```

---
