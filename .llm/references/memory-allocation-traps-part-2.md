# memory-allocation-traps - Part 2

## Split Content

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
