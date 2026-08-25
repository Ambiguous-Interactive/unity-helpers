# Skill: Memory Allocation Traps

<!-- trigger: allocation, hidden, boxing, closure, trap | Finding hidden allocation sources | Performance -->

**Trigger**: When reviewing code for hidden allocations or when code unexpectedly causes GC pressure. This skill catalogs non-obvious allocation sources in Unity/C#.

---

## Quick Reference: Allocation Costs

| Trap                           | Bytes Per Occurrence | Risk Level |
| ------------------------------ | -------------------- | ---------- |
| `foreach` through an interface | 24 bytes             | 🔴 High    |
| LINQ `.Where()`                | 32+ bytes            | 🔴 High    |
| LINQ `.Select()`               | 32+ bytes            | 🔴 High    |
| Closure capturing local        | 32+ bytes            | 🔴 High    |
| `params` method call           | 24+ bytes            | 🟡 Medium  |
| Delegate in loop               | 52 bytes             | 🔴 High    |
| Enum dictionary lookup         | 24 bytes             | 🟡 Medium  |
| Struct without `IEquatable<T>` | 24+ bytes            | 🟡 Medium  |
| String concatenation           | Varies               | 🔴 High    |
| Boxing to `object`             | 12+ bytes            | 🟡 Medium  |

---

## Trap 1: foreach through an interface

The boxing is decided by the **static type you iterate**, not by `foreach`. `foreach` binds to
whatever `GetEnumerator()` the type exposes; on a concrete collection that is a struct and nothing is
allocated, and through an interface it is `IEnumerator<T>`, which is boxed once per loop.

```csharp
// ❌ BAD: 24 bytes per loop -- the interface forces IEnumerator<T>
IReadOnlyList<Item> items = _items;
foreach (Item item in items) { Process(item); }

// ✅ GOOD: zero allocation -- List<T>.Enumerator is a struct
foreach (Item item in _items) { Process(item); }
```

Measured on `6000.4.6f1`, 2,000,000 iterations over a 4-element list, against a known allocator that
moved the counter by 54.7 MB:

| iterated as                    | bytes         |
| ------------------------------ | ------------- |
| `int[]`                        | 12,288        |
| `List<T>`                      | 24,576        |
| `for` with indexer             | 24,576        |
| `list.GetEnumerator()` by hand | 20,480        |
| `Dictionary<K,V>`              | 20,480        |
| `Dictionary<K,V>.Values`       | 16,384        |
| **`IEnumerable<T>`**           | **5,709,824** |

Everything concrete is one noise band; only the interface allocates. Two consequences:

- **Do not rewrite a concrete `foreach` into a `for` loop to save allocation.** There is none to
  save, `for` does not work on `HashSet`/`Dictionary`, and the indexer form is harder to read.
- **Taking the enumerator by hand is a no-op.** It measured 20,480 bytes against `foreach`'s 24,576 --
  the same band, because `foreach` already does exactly that.

The rule to apply instead is about **types**: a field, parameter or local typed `IEnumerable<T>` /
`IList<T>` / `IReadOnlyList<T>` allocates on every iteration of it. Where you own the declaration and
the hot path iterates it, declare the concrete type.

---

## Trap 2: LINQ Methods

All LINQ methods allocate iterator objects and often delegate objects:

```csharp
// ❌ BAD: Each method allocates
var result = enemies
    .Where(e => e.IsAlive)     // Iterator + delegate allocation
    .Select(e => e.Position)   // Another iterator + delegate
    .ToList();                 // New List allocation

// ✅ GOOD: Explicit loop with pooling
using var lease = Buffers<Vector3>.List.Get(out List<Vector3> result);
for (int i = 0; i < enemies.Count; i++)
{
    if (enemies[i].IsAlive)
    {
        result.Add(enemies[i].Position);
    }
}
```

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

## Trap 3: Closures Capturing Variables

When a lambda references a local variable or `this`, it creates a closure object:

```csharp
// ❌ BAD: Captures 'searchId' - allocates closure class
int searchId = GetTargetId();
Item found = list.Find(item => item.Id == searchId);

// ❌ BAD: Captures 'this' implicitly
items.RemoveAll(x => x.Owner == this);

// ✅ GOOD: Explicit loop, no closure
int searchId = GetTargetId();
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

### Static Lambdas (C# 9+)

```csharp
// ✅ Static lambda - compiler error if it tries to capture
items.Sort(static (a, b) => a.Priority.CompareTo(b.Priority));

// ✅ Cached delegate - single allocation at class load
private static readonly Comparison<Item> PriorityComparison =
    static (a, b) => a.Priority.CompareTo(b.Priority);

items.Sort(PriorityComparison);
```

---

## Trap 4: params Methods

Methods with `params` allocate an array for every call:

```csharp
// Method signature
public static T Max<T>(params T[] values) { }

// ❌ BAD: Allocates array (36 bytes for 3 ints)
int max = Mathf.Max(a, b, c);

// ✅ GOOD: Chain 2-argument overloads
int max = Mathf.Max(Mathf.Max(a, b), c);
```

### Common params Traps

| Method                         | Allocation               |
| ------------------------------ | ------------------------ |
| `string.Format(fmt, params)`   | Array + formatted string |
| `Debug.LogFormat(fmt, params)` | Array + formatted string |
| `Mathf.Max(params)`            | Array                    |
| `Path.Combine(params)`         | Array + result string    |

---

## Trap 5: Delegate Assignment in Loops

Assigning a method to a delegate variable boxes each iteration:

```csharp
// ❌ BAD: 52 bytes per iteration!
for (int i = 0; i < count; i++)
{
    Func<int> fn = MyFunction;  // Boxing each iteration
    result += fn();
}

// ✅ GOOD: Assign once outside loop
Func<int> fn = MyFunction;
for (int i = 0; i < count; i++)
{
    result += fn();
}
```

---

## Trap 6: Enum Dictionary Keys

Enum keys cause boxing on every dictionary operation unless you provide a custom comparer:

```csharp
// ❌ BAD: Boxing per lookup (4.5MB for 128K lookups!)
Dictionary<MyEnum, string> dict = new Dictionary<MyEnum, string>();
var value = dict[MyEnum.SomeValue];

// ✅ GOOD: Custom comparer (zero allocation)
public struct MyEnumComparer : IEqualityComparer<MyEnum>
{
    public bool Equals(MyEnum x, MyEnum y) => x == y;
    public int GetHashCode(MyEnum obj) => (int)obj;
}

var dict = new Dictionary<MyEnum, string>(new MyEnumComparer());

// ✅ ALTERNATIVE: Use int keys
Dictionary<int, string> dict = new Dictionary<int, string>();
dict[(int)MyEnum.SomeValue] = "value";
```

---

## Trap 7: Structs Without IEquatable<T>

Structs used in collections without `IEquatable<T>` cause boxing:

```csharp
// ❌ BAD: Boxing per comparison (4MB for 128K Contains calls!)
public struct BadStruct
{
    public int X, Y;
}

list.Contains(someStruct);  // Boxes each comparison!

// ✅ GOOD: Implement IEquatable<T>
public struct GoodStruct : IEquatable<GoodStruct>
{
    public int X, Y;

    public bool Equals(GoodStruct other) => X == other.X && Y == other.Y;
    public override bool Equals(object obj) => obj is GoodStruct s && Equals(s);
    public override int GetHashCode() => HashCode.Combine(X, Y);
}
```

---

## Trap 8: String Operations

Strings are immutable; every modification creates a new string:

```csharp
// ❌ BAD: O(n²) allocations
string result = "";
for (int i = 0; i < items.Count; i++)
{
    result += items[i].Name;  // New string each iteration!
}

// ❌ BAD: Hidden allocation in interpolation
string msg = $"Player {name} at {position}";  // Multiple allocations

// ✅ GOOD: StringBuilder pooling
using var lease = Buffers.StringBuilder.Get(out StringBuilder sb);
for (int i = 0; i < items.Count; i++)
{
    sb.Append(items[i].Name);
}
string result = sb.ToString();
```

### String Comparison Trap

```csharp
// ❌ BAD: gameObject.tag allocates a new string!
if (gameObject.tag == "Player") { }

// ✅ GOOD: CompareTag is allocation-free
if (gameObject.CompareTag("Player")) { }

// ❌ BAD: gameObject.name also allocates
if (gameObject.name == "Enemy") { }

// ✅ GOOD: Cache in Awake if needed
private string _cachedName;
void Awake() { _cachedName = gameObject.name; }
```

---

## Trap 9: Boxing Value Types

Passing structs to `object` parameters causes boxing:

```csharp
// ❌ BAD: Boxing
int x = 42;
object boxed = x;           // Boxes int
ArrayList list = new ArrayList();
list.Add(x);                // Boxes int

// ❌ BAD: Interface boxing (without generics)
IComparable comp = x;       // Boxes int

// ✅ GOOD: Use generic collections
List<int> list = new List<int>();
list.Add(x);                // No boxing

// ✅ GOOD: Generic interface constraint
void Compare<T>(T a, T b) where T : IComparable<T>
{
    a.CompareTo(b);         // No boxing
}
```

---

## Trap 10: Unity API Array Properties

Many Unity properties return new arrays on each access:

```csharp
// ❌ TERRIBLE: 4 array copies per iteration!
for (int i = 0; i < mesh.vertices.Length; i++)
{
    float x = mesh.vertices[i].x;  // New array!
    float y = mesh.vertices[i].y;  // New array!
    float z = mesh.vertices[i].z;  // New array!
}

// ✅ BETTER: Cache array
var vertices = mesh.vertices;  // Single copy
for (int i = 0; i < vertices.Length; i++)
{
    Process(vertices[i]);
}

// ✅ BEST: Non-allocating API
private List<Vector3> _vertices = new List<Vector3>();
mesh.GetVertices(_vertices);  // No allocation!
```

### Array-Returning Properties to Avoid

| Property                   | Non-Allocating Alternative                             |
| -------------------------- | ------------------------------------------------------ |
| `mesh.vertices`            | `mesh.GetVertices(list)`                               |
| `mesh.normals`             | `mesh.GetNormals(list)`                                |
| `mesh.uv`                  | `mesh.GetUVs(channel, list)`                           |
| `mesh.triangles`           | `mesh.GetTriangles(list, submesh)`                     |
| `Input.touches`            | `Input.touchCount` + `Input.GetTouch(i)`               |
| `Animator.parameters`      | `Animator.parameterCount` + `Animator.GetParameter(i)` |
| `Renderer.sharedMaterials` | `Renderer.GetSharedMaterials(list)`                    |

---

## Detection: Finding Hidden Allocations

### Unity Profiler

1. Enable **Deep Profile** for detailed call stacks
2. Sort by **GC Alloc** column
3. Look for allocations in `Update`, `FixedUpdate`, `LateUpdate`

### Search Patterns (Regex)

```regex
# LINQ usage
\.Where\(|\.Select\(|\.Any\(|\.First\(|\.ToList\(|\.ToArray\(

# Collection creation
new List<|new Dictionary<|new HashSet<|new Queue<|new Stack<

# String operations in loops
\+\s*"|\+\s*\w+\.ToString\(\)

# foreach on collections
foreach.*List<|foreach.*Dictionary<|foreach.*HashSet<

# Potential closures (lambdas with external references)
=>\s*[^;]*[a-z_][a-zA-Z0-9_]*[^(]
```

---

## Related Skills

- [high-performance-csharp](./high-performance-csharp.md) — Zero-allocation patterns
- [gc-architecture-unity](./gc-architecture-unity.md) — Why allocations matter
- [refactor-to-zero-alloc](./refactor-to-zero-alloc.md) — Migration patterns
- [unity-performance-patterns](./unity-performance-patterns.md) — Unity-specific patterns
- [performance-audit](./performance-audit.md) — Audit checklist
