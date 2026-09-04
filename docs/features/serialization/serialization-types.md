# Serialization Types

**Unity-friendly wrappers for complex data.**

Unity Helpers provides serializable wrappers for types that Unity can't serialize natively: GUIDs, dictionaries, sets, type references, and nullable values. All types include custom property drawers for a consistent inspector experience and support JSON/Protobuf serialization.

---

## Table of Contents

- [WGuid](#wguid)
- [SerializableDictionary](#serializabledictionary)
- [SerializableHashSet & SerializableSortedSet](#serializablehashset--serializablesortedset)
- [SerializableType](#serializabletype)
- [SerializableNullable](#serializablenullable)
- [SerializableValueTuple](#serializablevaluetuple)
- [Best Practices](#best-practices)
- [Examples](#examples)

---

## WGuid

Immutable version-4 GUID wrapper using two longs for efficient Unity serialization.

### Why WGuid?

- **Problem:** Unity doesn't serialize `System.Guid` directly
- **Solution:** `WGuid` stores as two `long` fields (`_low` and `_high`) for fast Unity serialization

**Performance:**

- 2x faster serialization than string-based GUID storage
- Smaller memory footprint (16 bytes vs. 36 bytes for string)
- Immutable design prevents accidental modification

---

### Basic Usage

<!-- doc-sample: compiles -->

```csharp
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

public class Entity : MonoBehaviour
{
    public WGuid entityId = WGuid.NewGuid();
}
```

> **Visual Reference**
>
> ![WGuid property drawer showing GUID field with Generate button](../../images/serialization/wguid-inspector.png)

---

### Creating GUIDs

```csharp
// Generate new GUID
WGuid id1 = WGuid.NewGuid();

// From System.Guid
System.Guid sysGuid = System.Guid.NewGuid();
WGuid id2 = (WGuid)sysGuid;

// Parse from string
WGuid id3 = WGuid.Parse("12345678-1234-1234-1234-123456789abc");

// Try parse (safe)
if (WGuid.TryParse("...", out WGuid id4))
{
    Debug.Log($"Parsed: {id4}");
}

// Empty GUID
WGuid empty = WGuid.EmptyGuid;
```

---

### Inspector Features

**Custom Drawer:**

- Text field displays GUID in standard format
- "Generate" button creates new GUID
- Validation warns if GUID is not version-4
- Undo/redo support

![WGuid drawer with validation warning for GUID input](../../images/serialization/wguid-inspector-obviously-invalid.png)

![WGuid drawer with validation warning for non-v4 GUID](../../images/serialization/wguid-inspector-not-v4.png)

![WGuid generate button showing that it generates new v4 GUIDs](../../images/serialization/wguid-inspector-generate.gif)

---

### Conversions

```csharp
// WGuid <-> System.Guid
WGuid wguid = WGuid.NewGuid();
System.Guid sysGuid = wguid.ToGuid();
WGuid back = (WGuid)sysGuid;

// ToString() formats
string standard = wguid.ToString();  // "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
string formatted = wguid.ToString("N");  // Without hyphens
```

---

### Equality & Comparison

<!-- doc-sample: compiles -->

```csharp
WGuid id1 = WGuid.NewGuid();
WGuid id2 = id1;

// Implements IEquatable<WGuid>
bool equal = id1.Equals(id2);  // true
bool opEqual = id1 == id2;     // true

// Implements IComparable<WGuid>
int comparison = id1.CompareTo(id2);  // 0
```

---

### Serialization Support

- **Unity:** Serialized as two `long` fields
- **JSON:** Serialized as GUID string
- **Protobuf:** Serialized as two `long` fields

<!-- doc-sample: compiles -->

```csharp
using ProtoBuf;

[ProtoContract]
public class SaveData
{
    [ProtoMember(1)] public WGuid playerId;
    [ProtoMember(2)] public WGuid sessionId;
}
```

---

## SerializableDictionary

Unity-friendly dictionary with synchronized key/value arrays and custom drawer.

### Why SerializableDictionary?

- **Problem:** Unity doesn't serialize `Dictionary<TKey, TValue>`
- **Solution:** `SerializableDictionary<TKey, TValue>` maintains synchronized arrays for Unity serialization and a runtime dictionary for fast lookups

---

### Basic Usage

<!-- doc-sample: compiles -->

```csharp
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

public class PrefabRegistry : MonoBehaviour
{
    public GameObject enemyPrefab;
    public GameObject playerPrefab;

    public SerializableDictionary<string, GameObject> prefabs;

    private void Start()
    {
        // Access entries
        if (prefabs.TryGetValue("Enemy", out GameObject prefab))
        {
            Instantiate(prefab);
        }

        // Count
        Debug.Log($"Prefab count: {prefabs.Count}");

        // Iteration
        foreach (var kvp in prefabs)
        {
            Debug.Log($"{kvp.Key}: {kvp.Value}");
        }

        /*
            Add entries (or overwrite)
            WARNING: This is just for demo purposes! SerializableDictionary is meant for editor-mode persistence.
            Nothing stops you from changing this at runtime, but it will be lost on next playthrough.
        */
        prefabs["Player"] = playerPrefab;
        prefabs["Enemy"] = enemyPrefab;
    }
}
```

---

### Inspector Features

> **Visual Reference**
>
> ![SerializableDictionary property drawer with add/remove controls](../../images/serialization/serializable-dictionary-inspector.png)
>
> _Dictionary inspector showing key-value pairs with pagination and inline editing_

**Custom Drawer:**

- Key/value pair editing
- Add/Remove buttons
- Reorderable list
- Duplicate key detection (visual warning)
- Null value highlighting
- Pagination for large dictionaries

![Dictionary pagination controls](../../images/serialization/serialized-dictionary-pagination.gif)

---

### Dictionary Operations

<!-- doc-sample: compiles -->

```csharp
// Implements IDictionary<TKey, TValue> and IReadOnlyDictionary<TKey, TValue>
SerializableDictionary<int, string> dict = new();

// Add
dict.Add(1, "One");
dict[2] = "Two";

// Read
string value = dict[1];
bool exists = dict.TryGetValue(2, out string val);
bool contains = dict.ContainsKey(1);

// Update
dict[1] = "First";

// Remove
dict.Remove(1);
dict.Clear();

// Iteration
foreach (KeyValuePair<int, string> kvp in dict)
{
    Debug.Log($"{kvp.Key} = {kvp.Value}");
}

// Keys/Values collections
ICollection<int> keys = dict.Keys;
ICollection<string> values = dict.Values;
```

---

### Specialized Dictionaries

<!-- doc-sample: compiles -->

```csharp
// Sorted dictionary (maintains key order)
public SerializableSortedDictionary<int, string> sortedDict;
```

**Note:** `SerializableSortedDictionary` uses `SortedDictionary<TKey, TValue>` internally for ordered keys.

---

### Collection Values

A dictionary whose value type is itself a collection just works. No wrapper type, no cache subclass,
no consumer change:

<!-- doc-sample: compiles -->

```csharp
public sealed class WeaponConfig : MonoBehaviour
{
    [SerializeField]
    private SerializableDictionary<string, List<float>> _curves = new();

    public void AddPoint(string weapon, float damage)
    {
        if (!_curves.TryGetValue(weapon, out List<float> curve))
        {
            curve = new List<float>();
            _curves[weapon] = curve;
        }

        curve.Add(damage);
    }
}
```

This holds for `List<T>` and `T[]` values, on both `SerializableDictionary` and
`SerializableSortedDictionary`.

#### How it works, and why your existing assets are unaffected

Unity does not serialize a nested collection: the serialized values array would be a `List<float>[]`,
which Unity drops entirely, while the parallel keys array survives because it is a plain `string[]`.
Rather than asking you to change the value type, the dictionary writes those values to a second
serialized array whose elements are one-field boxes (the indirection Unity wants) and unpacks them
on load.

That second array is **populated** only for value types Unity would otherwise drop. Every other
dictionary keeps storing its values exactly where it always did, so no existing data is rewritten
and assets written by this version stay readable by older package versions. Because a serialized
field cannot be declared conditionally, dictionaries do gain one empty array in their serialized
form; the first save after upgrading adds that line to affected assets and nothing else.

#### Nesting these inside each other

A serializable collection whose value or element is **another serializable collection type** has never
needed any of this, and still does not:

<!-- doc-sample: compiles -->

```csharp
// All fine, no wrapper and no box involved.
SerializableDictionary<string, SerializableDictionary<int, float>> byRegion;
SerializableHashSet<SerializableDictionary<int, float>> variants;
```

The reason is the same one the boxing exploits: `SerializableDictionary<int, float>` is a `[Serializable]`
**class**, and Unity has always accepted a class as an array element. Only a _raw_ `List<T>` or `T[]`
in that position is refused, and that is exactly the case the boxing now covers, so the two mechanisms
compose:

<!-- doc-sample: compiles -->

```csharp
// Boxed because the value is a raw List<>, and its elements nest normally inside the box.
SerializableDictionary<string, List<SerializableDictionary<int, float>>> curvesByRegion;
```

**The depth limit is Unity's, not this package's.** Unity stops descending after a fixed number of
nesting levels and warns rather than saving the remainder, and each dictionary in a chain costs
roughly two of those levels. Three dictionaries deep is covered by tests; arbitrarily deep recursion
is not something a wrapper can rescue, so if you find yourself approaching it, flatten the data:
a composite key is usually the answer:

<!-- doc-sample: compiles -->

```csharp
public enum RegionTierKey { NorthBronze, NorthSilver, SouthGold }

// Instead of Dictionary<A, Dictionary<B, Dictionary<C, V>>>
SerializableDictionary<RegionTierKey, float> byRegionAndTier;
```

#### Sets are different

`SerializableHashSet<List<T>>` still reports the shape as unsupported. That is deliberate rather than
pending: `List<T>` has reference equality, so a set of lists treats two lists with identical contents
as two distinct elements, and deserializing one never reproduces the set you saved. Use
`SerializableHashSet<SerializableList<T>>` only if you genuinely want identity semantics: the
wrapper serializes, but it declares no value equality either, so `Contains` on a restored set is
still false for an equal-content list. Otherwise the element type is the thing to reconsider.

`SerializableSortedSet<List<T>>` does not arise at all: `SerializableSortedSet<T>` constrains
`T : IComparable<T>`, and `List<T>` does not implement it, so the declaration is a compile error
rather than something the Inspector has to report.

`SerializableList<T>` remains available and is still the right choice when you want a list that draws
and serializes on its own, outside a dictionary. It implements `IList<T>`, converts implicitly to and
from `List<T>`, and exposes `AsList()` for the `List<T>` members it does not surface (`Sort`,
`BinarySearch`). It draws in the Inspector as the list it wraps, with no extra foldout, and
serializes to a plain JSON array.

#### Value types that are still unsupported

Interfaces, abstract types, `Dictionary<,>`, and classes without `[Serializable]` cannot be
serialized by Unity in any container, so no wrapper repairs them. The Inspector reports those as an
error rather than drawing a value column that persists nothing, so it is visible while authoring
instead of at runtime.

The three-argument cache form is still supported for a value type you want to route explicitly:

<!-- doc-sample: compiles -->

```csharp
[Serializable]
public sealed class FloatListCache : SerializableDictionary.Cache<List<float>> { }

[Serializable]
public sealed class DamageCurves
    : SerializableDictionary<string, List<float>, FloatListCache> { }
```

---

### Serialization Support

- **Unity:** Synchronized `_keys` and `_values` arrays
- **JSON:** Standard dictionary format
- **Protobuf:** Supported via surrogates

```csharp
// JSON example
{
    "prefabs": {
        "Enemy": { "instanceId": 12345 },
        "Player": { "instanceId": 67890 }
    }
}
```

---

<a id="serializablehashset--serializablesortedset"></a>
<a id="serializablehashset-serializablesortedset"></a>

## SerializableHashSet & SerializableSortedSet

Unity-friendly set collections with duplicate detection and custom drawers.

### Why Serializable Sets?

- **Problem:** Unity doesn't serialize `HashSet<T>` or `SortedSet<T>`
- **Solution:** `SerializableHashSet<T>` and `SerializableSortedSet<T>` maintain a serialized array and runtime set for fast lookups

---

### Basic Usage

<!-- doc-sample: compiles -->

```csharp
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

public class UniqueItemTracker : MonoBehaviour
{
    public SerializableHashSet<string> collectedItems;

    private void Start()
    {
        foreach (var item in collectedItems)
        {
            Debug.Log($"Found item: {item}");
        }
    }
}
```

> **Visual Reference**
>
> ![SerializableHashSet property drawer with duplicate detection](../../images/serialization/serializable-hashset-inspector.png)
>
> _Set inspector with add/remove controls, duplicate highlighting, and pagination_

---

### Inspector Features

**Custom Drawer:**

- Reorderable list
- Add/Remove/Clear/Sort buttons
- **Duplicate detection** with visual highlighting (shake animation + color)
- **Null entry highlighting** (red background)
- Pagination for large sets
- Move Up/Down buttons
- Current selection badge for items on other pages
- **New Entry foldout** to stage values before adding them to the runtime set (tune its animation via **Project Settings ▸ Wallstop Studios ▸ Unity Helpers ▸ Set Foldouts**)

> **Visual Reference**
>
> ![SerializableHashSet duplicate and null highlighting](../../images/serialization/serializable-hashset-validation.png)
>
> _Visual feedback for duplicate entries (yellow shake) and null values (red background)_
> ![Adding duplicate and seeing shake animation](../../images/serialization/set-duplicates.gif)

---

### Set Operations

<!-- doc-sample: compiles -->

```csharp
// Implements ISet<T> and IReadOnlyCollection<T>
SerializableHashSet<int> set = new();

// Add (returns true if new)
bool added = set.Add(42);

// Read
bool contains = set.Contains(42);
int count = set.Count;

// Remove
bool removed = set.Remove(42);
set.Clear();

// Set operations
HashSet<int> other = new HashSet<int> { 1, 2, 3 };
set.UnionWith(other);        // Add all from other
set.IntersectWith(other);    // Keep only common elements
set.ExceptWith(other);       // Remove elements in other
bool overlaps = set.Overlaps(other);

// Iteration
foreach (int item in set)
{
    Debug.Log(item);
}
```

### New Entry Foldout

Expandable "New Entry" controls let you configure the exact value that will be inserted, which is especially helpful for complex structs, managed references, or ScriptableObjects. The foldout supports the same field variety as the inline list and respects your duplicate/null validation. Animation for the New Entry foldout is governed by the **Serializable Set Foldouts** settings; adjust tweening and speed independently for `SerializableHashSet<T>` and `SerializableSortedSet<T>`.

---

### Foldout Defaults & Overrides

By default, SerializableSet inspectors start collapsed until you open them. This baseline comes from **Project Settings ▸ Wallstop Studios ▸ Unity Helpers** via the **Serializable Set Start Collapsed** toggle (and the equivalent **Serializable Dictionary Start Collapsed** toggle for dictionaries). You can override the default per-field with `[WSerializableCollectionFoldout]`:

<!-- doc-sample: compiles -->

```csharp
using WallstopStudios.UnityHelpers.Core.Attributes;

[WSerializableCollectionFoldout(WSerializableCollectionFoldoutBehavior.StartExpanded)]
public SerializableHashSet<string> unlockedBadges = new();
```

- **Project setting** establishes the initial state only.
- **`[WSerializableCollectionFoldout]`** can request expanded or collapsed behavior for specific collections.
- **Explicit changes** to `SerializedProperty.isExpanded` (scripts, custom inspectors, or tests) take ultimate precedence. The drawer now respects those manual decisions, so opting-in via code no longer gets undone by the attribute or the global default.

The attribute applies to both `SerializableHashSet<T>`/`SerializableSortedSet<T>` and the dictionary equivalents, making it straightforward to mix project-wide defaults with per-field intentions.

---

### Sorted Sets

<!-- doc-sample: compiles -->

```csharp
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.Attributes;
using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

public class ThresholdLogger : MonoBehaviour
{
    public SerializableSortedSet<int> scoreThresholds = new();

    [WButton]
    private string LogThresholds()
    {
        foreach (int threshold in scoreThresholds)
        {
            Debug.Log(threshold);
        }
        return $"Logged {scoreThresholds.Count} thresholds";
    }
}
```

![SerializableSortedSet showing sorted numeric values](../../images/serialization/serialized-sorted-set-inspector.gif)

---

### Serialization Support

- **Unity:** Serialized `_items` array
- **JSON:** Array format
- **Protobuf:** Supported via collection surrogates

```csharp
// JSON example
{
    "collectedItems": ["item_001", "item_042", "item_137"]
}
```

---

## SerializableType

Unity-friendly type reference that survives refactoring and namespace changes.

<!-- doc-sample: compiles -->

```csharp
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

public class SerializableTypeExample : MonoBehaviour
{
    public SerializableType type;
}
```

> **Visual Reference**
>
> ![SerializableType property drawer with searchable type dropdown](../../images/serialization/serializable-type-inspector.gif)
>
> _Type selection with searchable dropdown, namespace filtering, and validation_

### Why SerializableType?

- **Problem:** Unity doesn't serialize `System.Type`, and type names break when refactoring
- **Solution:** `SerializableType` stores assembly-qualified names with fallback resolution on rename/namespace changes

---

### Basic Usage

<!-- doc-sample: compiles -->

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.Attributes;
using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
using WallstopStudios.UnityHelpers.Core.Helper;

public class BehaviorSpawner : MonoBehaviour
{
    [WValueDropDown(typeof(BehaviorSpawner), nameof(GetAllMonoBehaviourNames))]
    public SerializableType behaviorType;

    private void SpawnBehavior()
    {
        if (behaviorType.IsEmpty)
        {
            Debug.LogWarning("No behavior type assigned!");
            return;
        }

        Type type = behaviorType.Value;
        if (type != null)
        {
            GameObject go = new GameObject(type.Name);
            go.AddComponent(type);
        }
    }

    private static IEnumerable<Type> GetAllMonoBehaviourNames()
    {
        return ReflectionHelpers
            .GetAllLoadedTypes()
            .Where(type => typeof(MonoBehaviour).IsAssignableFrom(type) && !type.IsAbstract);
    }
}
```

![SerializableType inspector with type browser and search](../../images/serialization/custom-type-filtering.gif)

---

### Inspector Features

**Custom Drawer (Two-Line):**

- **Search row:** Text field for filtering types
- **Popup row:** Dropdown showing matched types
- Clear button to unset the type
- Pagination for large type catalogs
- Auto-complete suggestions

**Search result caching.** `SerializableTypeCatalog.GetFilteredDescriptors` caches its answer per
search term, and reuses a shorter term's result as the starting set for a longer one, so typing a
name does not rescan the whole catalog on every keystroke. The cache holds at most
`SerializableTypeCatalog.MaxCachedFilterResults` terms (default 64) and evicts the least recently
used one past that -- each entry is a filtered slice of every type in the project, and the shortest
terms are the largest, so an unbounded cache retained one array per prefix of everything ever typed
([#694](https://github.com/Ambiguous-Interactive/unity-helpers/issues/694)). Eviction only costs a
wider rescan; the results are identical. Set it to 0 or less to remove the bound.

---

### Type Operations

<!-- doc-sample: compiles -->

```csharp
public sealed class PlayerController : MonoBehaviour { }

// Create
SerializableType typeRef = new SerializableType(typeof(PlayerController));

// Resolve
Type resolvedType = typeRef.Value;
if (resolvedType != null)
{
    object instance = Activator.CreateInstance(resolvedType);
}

// Check
bool isEmpty = typeRef.IsEmpty;
string displayName = typeRef.DisplayName;  // User-friendly name

// Equality
bool equal = typeRef.Equals(new SerializableType(typeof(PlayerController)));
```

---

### Refactoring Resilience

**Scenario:** You rename `PlayerController` to `PlayerBehavior` or move it to a new namespace.

- **Standard Approach:** Type reference breaks, data loss
- **SerializableType:** Automatically resolves via assembly scanning and fallback matching

**How it works:**

1. Stores assembly-qualified name (e.g., `Namespace.PlayerController, Assembly-CSharp`)
2. On deserialization, tries exact match first
3. If the exact match fails, it scans assemblies for the best partial match
4. Updates internal name if resolved to the new type

---

### Serialization Support

- **Unity:** Stores assembly-qualified name string
- **JSON:** Type name string with custom converter
- **Protobuf:** Supported via string surrogates

```csharp
// JSON example
{
    "behaviorType": "MyNamespace.PlayerController, Assembly-CSharp"
}
```

---

## SerializableNullable

Unity-friendly nullable value type wrapper.

### Why SerializableNullable?

- **Problem:** Unity doesn't serialize `Nullable<T>` (e.g., `int?`, `float?`)
- **Solution:** `SerializableNullable<T>` wraps any value type with `HasValue` and `Value` properties

---

### Basic Usage

<!-- doc-sample: compiles -->

```csharp
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

public class BonusConfig : MonoBehaviour
{
    public SerializableNullable<float> criticalHitMultiplier;

    private float GetDamage(float baseDamage, bool isCritical)
    {
        if (isCritical && criticalHitMultiplier.HasValue)
        {
            return baseDamage * criticalHitMultiplier.Value;
        }
        return baseDamage;
    }
}
```

![SerializableNullable inspector with toggle and value field](../../images/serialization/serializable-nullable-type-and-clear.gif)

---

### Inspector Features

**Custom Drawer:**

- Checkbox for `HasValue` state
- Inline value field (enabled when `HasValue == true`)
- Height adapts based on the nullable state

---

### Nullable Operations

<!-- doc-sample: compiles -->

```csharp
// Create with value
SerializableNullable<int> nullableInt = new SerializableNullable<int>(42);

// Create without value (null)
SerializableNullable<int> nullInt = new SerializableNullable<int>();

// Check
if (nullableInt.HasValue)
{
    int value = nullableInt.Value;
    Debug.Log($"Value: {value}");
}

// Implicit conversion from T
SerializableNullable<float> nullableFloat = 3.14f;

// Conversion to Nullable<T>
int? systemNullable = nullableInt.HasValue ? nullableInt.Value : null;
```

---

### Use Cases

**Optional Configuration:**

<!-- doc-sample: compiles -->

```csharp
public SerializableNullable<float> overrideSpeed;  // null = use default
```

**Conditional Bonuses:**

<!-- doc-sample: compiles -->

```csharp
public SerializableNullable<int> bonusGold;  // null = no bonus
```

**Dynamic Properties:**

<!-- doc-sample: compiles -->

```csharp
public SerializableNullable<Color> customColor;  // null = use preset
```

---

### Serialization Support

- **Unity:** Stores `_hasValue` bool and `_value` T fields
- **JSON:** Standard nullable format
- **Protobuf:** Supported via nullable surrogates

```csharp
// JSON example (has value)
{
    "criticalHitMultiplier": 2.5
}

// JSON example (null)
{
    "criticalHitMultiplier": null
}
```

---

---

## SerializableValueTuple

Unity-friendly stand-in for `ValueTuple`, in two- and three-component forms.

### Why SerializableValueTuple?

- **Problem:** Unity does not serialize `(int, float)`, and it fails _silently_. There is no
  `SerializedProperty` for the field at all, so a tuple inside a `SerializableDictionary` or a
  `List<T>` loses whatever you authored with nothing to report it. `[Serializable]` on the type is
  not the obstacle (`ValueTuple<,>` already carries it); Unity declines every type out of the
  framework assemblies.
- **And it is worse in a player.** `Serializer.ProtoSerialize((7, 1.5f))` and
  `Serializer.JsonStringify((7, 1.5f))` both work in the editor and both throw
  `ExecutionEngineException` on an IL2CPP standalone build: protobuf-net's
  `StructValueChecker<ValueTuple<int, float>>` and System.Text.Json's
  `ObjectDefaultConverter<ValueTuple<int, float>>` are instantiated reflectively, so no AOT code is
  generated for them. Measured on Unity 2021.3. A tuple therefore looks serializable right up until
  you ship.
- **Solution:** `SerializableValueTuple<T1, T2>` and `SerializableValueTuple<T1, T2, T3>`: the same
  components under a name Unity will serialize, with implicit conversions in both directions so
  `(T1, T2)` stays the spelling everywhere else.

---

### Basic Usage

<!-- doc-sample: compiles -->

```csharp
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

public class LootTable : MonoBehaviour
{
    // Authored in the Inspector, and it survives a domain reload.
    public SerializableDictionary<string, SerializableValueTuple<int, float>> drops = new();

    public void Grant(string id)
    {
        (int count, float weight) = drops[id];
        Debug.Log($"{count} at {weight}");
    }
}
```

---

### Interchangeable with `ValueTuple`

The field names and numbers are `ValueTuple`'s own, so payloads written with either read back through
the other: an existing save migrates without a rewrite:

<!-- doc-sample: compiles -->

```csharp
byte[] written = Serializer.ProtoSerialize((7, 1.5f));
SerializableValueTuple<int, float> read =
    Serializer.ProtoDeserialize<SerializableValueTuple<int, float>>(written);   // (7, 1.5)
```

Verified byte-identical for protobuf (`0807150000C03F` either way) and character-identical for JSON
(`{"Item1":7,"Item2":1.5}`).

Both directions work in a player for protobuf, so an existing save migrates either way.

---

### Tuples serialize on IL2CPP

You do not have to adopt the stand-in to fix protobuf. The package ships

```csharp
[assembly: WProtoRootMarshal(typeof(ValueTuple<,>), typeof(ValueTupleMarshalFormatter<,>))]
[assembly: WProtoRootMarshal(typeof(ValueTuple<, ,>), typeof(ValueTupleMarshalFormatter<, ,>))]
```

so the generator emits an ahead-of-time formatter for every closed `ValueTuple` your build actually
uses, and `Serializer.ProtoSerialize((7, 1.5f))` goes through it instead of protobuf-net's
reflection. The bytes are `SerializableValueTuple`'s by construction, so the tuple and the stand-in
cannot drift apart. **Protobuf only**; see the JSON caveat above.

The stand-in is still what you need for a **serialized field**, because that is Unity's own
serializer rather than ours.

#### Turning it off

Define **`WALLSTOP_DISABLE_VALUE_TUPLE_SERIALIZATION`** in _Player Settings → Scripting Define
Symbols_ to remove both. It is on by default because a tuple that throws only in a player is the
worst failure this package has to offer, but it is not free: the generator emits one formatter per
closed `ValueTuple` your build uses, and a tuple is a common local aggregate rather than a
deliberate container. On this package alone that is 41 registrations, 11 of them closing over types
that can never serialize (`Type`, `ConstructorInfo`, …). Those decline at run time, but under IL2CPP
each closure is still compiled code.

It also silences a second cost. Because the registration is automatic, a tuple that closes over a
type the generated registrar cannot name (a `private` nested type, say) produces a `WPROTO028`
warning asking you to widen it, for a formatter you never asked for. Two such warnings exist in this
package's own tests.

Turning it off does **not** affect `SerializableValueTuple`; the stand-in keeps its own converter
and its generated formatter either way. Only the automatic support for the raw framework tuple goes.

---

### Conversions

```csharp
SerializableValueTuple<int, float> pair = (7, 1.5f);   // implicit, from the framework tuple
(int, float) back = pair;                              // implicit, to it
(int count, float weight) = pair;                      // Deconstruct

pair == new SerializableValueTuple<int, float>(7, 1.5f);   // true
pair.Equals((7, 1.5f));                                    // true
pair.ToString();                                           // "(7, 1.5)"
```

`Equals` and `GetHashCode` use `EqualityComparer<T>.Default`, so a `null` component is safe rather
than a throw.

---

### Higher arities

Only two and three components ship. They cover the gameplay cases (`(item, count)`, `(min, max)`,
`(x, y, z)`), and each additional arity is public API to maintain forever. If you need more, a
`[Serializable]` struct with named fields is clearer at that size anyway.

## Best Practices

### 1. Choose the Right Type

```csharp
// ✅ GOOD: WGuid for entity IDs
public WGuid entityId = WGuid.NewGuid();

// ✅ GOOD: SerializableDictionary for key/value mappings
public SerializableDictionary<string, GameObject> prefabRegistry;

// ✅ GOOD: SerializableHashSet for unique collections
public SerializableHashSet<string> uniqueItemIds;

// ✅ GOOD: SerializableSortedSet for ordered unique values
public SerializableSortedSet<int> scoreThresholds;

// ✅ GOOD: SerializableType for type references
public SerializableType behaviorType;

// ✅ GOOD: SerializableNullable for optional values
public SerializableNullable<float> overrideSpeed;

// ❌ BAD: String-based GUID
public string entityId = System.Guid.NewGuid().ToString();  // Use WGuid!

// ❌ BAD: Parallel arrays instead of dictionary
public string[] keys;
public GameObject[] values;  // Use SerializableDictionary!

// ❌ BAD: List with manual duplicate checking
public List<string> uniqueItems;  // Use SerializableHashSet!
```

---

### 2. Initialize Collections

```csharp
// ✅ GOOD: Initialize in field declaration
public SerializableDictionary<string, int> scores = new();
public SerializableHashSet<string> tags = new();

// ❌ BAD: Null collections (NullReferenceException!)
public SerializableDictionary<string, int> scores;  // null!

private void Start()
{
    scores.Add("player", 100);  // Crash!
}
```

---

### 3. Use Sorted Variants for Ordered Data

```csharp
// ✅ GOOD: SortedSet for ordered priorities
public SerializableSortedSet<int> unlockLevels;

// ✅ GOOD: SortedDictionary for ordered display
public SerializableSortedDictionary<string, string> alphabeticalNames;

// ❌ BAD: HashSet for ordered data (no guaranteed order!)
public SerializableHashSet<int> unlockLevels;  // Order is random!
```

---

### 4. Handle WGuid Generation Carefully

<!-- doc-sample: compiles -->

```csharp
// ✅ GOOD: Generate once, then immutable
public WGuid entityId = WGuid.NewGuid();

// ✅ GOOD: Generate in Awake if needed
private void Awake()
{
    if (entityId == WGuid.EmptyGuid)
    {
        entityId = WGuid.NewGuid();
    }
}

// ❌ BAD: Regenerating on every access
public WGuid EntityId => WGuid.NewGuid();  // New GUID every time!
```

---

## Examples

### Example 1: Item Database with Dictionary

<!-- doc-sample: compiles -->

```csharp
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

[System.Serializable]
public class ItemData
{
    public string name;
    public Sprite icon;
    public int value;
}

public class ItemDatabase : MonoBehaviour
{
    public SerializableDictionary<string, ItemData> items;

    public bool TryGetItem(string itemId, out ItemData data)
    {
        return items.TryGetValue(itemId, out data);
    }

    public void AddItem(string itemId, ItemData data)
    {
        items[itemId] = data;
    }
}
```

---

### Example 2: Player Achievement Tracking

<!-- doc-sample: compiles -->

```csharp
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

public class PlayerProfile : MonoBehaviour
{
    public WGuid playerId = WGuid.NewGuid();
    public SerializableHashSet<string> unlockedAchievements;
    public SerializableSortedSet<int> highScores;

    public void UnlockAchievement(string achievementId)
    {
        if (unlockedAchievements.Add(achievementId))
        {
            Debug.Log($"Unlocked: {achievementId}");
            // Trigger UI notification, etc.
        }
    }

    public void RecordScore(int score)
    {
        highScores.Add(score);

        // Keep only top 10
        while (highScores.Count > 10)
        {
            highScores.Remove(highScores.Min);
        }
    }
}
```

---

### Example 3: Dynamic Behavior Spawning

<!-- doc-sample: compiles -->

```csharp
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
using System;

public class BehaviorFactory : MonoBehaviour
{
    [StringInList(typeof(TypeHelper), nameof(TypeHelper.GetAllMonoBehaviours))]
    public SerializableType defaultBehavior;

    public SerializableDictionary<string, SerializableType> namedBehaviors;

    public GameObject SpawnWithBehavior(string behaviorName = null)
    {
        SerializableType typeToSpawn = defaultBehavior;

        if (!string.IsNullOrEmpty(behaviorName) &&
            namedBehaviors.TryGetValue(behaviorName, out SerializableType namedType))
        {
            typeToSpawn = namedType;
        }

        if (typeToSpawn.IsEmpty)
        {
            Debug.LogWarning("No behavior type specified!");
            return null;
        }

        Type type = typeToSpawn.Value;
        if (type == null || !typeof(MonoBehaviour).IsAssignableFrom(type))
        {
            Debug.LogError($"Invalid behavior type: {typeToSpawn.DisplayName}");
            return null;
        }

        GameObject go = new GameObject(type.Name);
        go.AddComponent(type);
        return go;
    }
}

public static class TypeHelper
{
    public static IEnumerable<Type> GetAllMonoBehaviours()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(MonoBehaviour).IsAssignableFrom(t) && !t.IsAbstract);
    }
}
```

---

### Example 4: Optional Configuration with Nullable

<!-- doc-sample: compiles -->

```csharp
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

public class CharacterConfig : MonoBehaviour
{
    public float baseSpeed = 5f;
    public SerializableNullable<float> speedOverride;  // null = use baseSpeed

    public Color defaultColor = Color.white;
    public SerializableNullable<Color> colorOverride;  // null = use defaultColor

    private void Start()
    {
        float actualSpeed = speedOverride.HasValue ? speedOverride.Value : baseSpeed;
        Color actualColor = colorOverride.HasValue ? colorOverride.Value : defaultColor;

        Debug.Log($"Speed: {actualSpeed}, Color: {actualColor}");
    }
}
```

---

## See Also

- **[Inspector Overview](../inspector/inspector-overview.md)** - Complete inspector features overview
- **[Serialization Guide](./serialization.md)** - JSON/Protobuf serialization
- **[Data Structures](../utilities/data-structures.md)** - Other data structures
- **[Editor Tools Guide](../editor-tools/editor-tools-guide.md)** - Editor utilities

---

**Next Steps:**

- Replace string/int-based IDs with `WGuid`
- Use `SerializableDictionary` instead of parallel arrays
- Track unique collections with `SerializableHashSet`
- Store type references with `SerializableType`
- Add optional configuration with `SerializableNullable`
