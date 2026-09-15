# Odin Inspector to Unity Helpers Migration Guide

A practical guide for migrating from Odin Inspector to Unity Helpers. Examples are verified against the actual source code.

---

## Safe Source Migration Tool

Use **Tools > Wallstop Studios > Odin Migration** to preview or apply conservative source edits
under `Assets`. The selected-script commands accept selected `.cs` files and selected folders; the
Assets commands scan all scripts under `Assets`. Generated scripts are skipped.

The tool rewrites only parameterless `ReadOnly` and `EnumToggleButtons` attributes on public fields,
or non-public fields carrying the exact `global::UnityEngine.SerializeField` or
`global::UnityEngine.SerializeReference` attribute (with or without the `Attribute` suffix). This
textual check does not validate whether Unity supports the field's type. Properties, static fields,
constants, read-only fields, and
`NonSerialized` fields remain unchanged for review. Type, method, event, and ambiguous targets also
remain unchanged, including when several attribute lists precede a declaration.

Automatic rewrites require the exact global qualification
`global::Sirenix.OdinInspector.Attribute`. Non-global qualified names, namespace aliases, attribute
aliases, and unqualified attributes brought into scope by `using Sirenix.OdinInspector;` remain
review items: textual analysis cannot prove that any of those names was not shadowed in a nearer
scope. Explicit attribute targets are also checked; only no target or `field:` is eligible for an
automatic edit. Every globally qualified Odin inspector attribute appears in the
report, including attributes for which Unity Helpers has no automatic migration.
`ShowIf`, `HideIf`, `Button`, and `Required` also remain manual because their target lookup,
multiplicity, and constructor semantics are not identical enough to infer from source text alone.
Resolver strings such as `"enabled"` are never rewritten.

The quick-reference table below lists conceptual replacements. It does not promise that the tool
can rewrite every row safely. The report identifies unsupported attributes and options for manual
review. It reports serialized Odin bases,
`OdinSerialize`, and Odin-owned dictionary or set shapes as blockers and never rewrites them.
Always run Preview first and inspect the Console report. Cancelling a scan stops before reading the
next file. Cancellation or any scan failure disables Apply, so a partial scan cannot write source
files.

> [!CAUTION]
> Replacing `SerializedMonoBehaviour`, `SerializedScriptableObject`, `[OdinSerialize]`, or an
> Odin-serialized collection is a data migration, not a source rename. Existing scene, prefab, and
> asset data can remain in Odin's serialization payload and disappear when the source type changes.
> The automated tool deliberately leaves these constructs unchanged.

Apply performs a best-effort check that every source file still matches its preview, writes through
a staged durable-file replacement, and attempts to roll back completed writes if a later write
fails. It refuses that rollback when the live bytes show a newer external edit. These checks reduce
common editor races but cannot coordinate atomically with other processes. Timestamped byte-for-byte backups are retained under
`Library/WallstopOdinMigrationBackups`.

This is a **Tier C filesystem operation**: Unity Undo does not cover it. Commit or stash your work
before Apply, keep the backup until the project has compiled and assets have been validated, and use
version control for the authoritative rollback. The tool preserves each supported file's encoding,
byte-order mark, and newline style, then refreshes the Asset Database once after a successful batch.

## Quick Reference Table

| Odin Feature              | Unity Helpers Equivalent                               |
| ------------------------- | ------------------------------------------------------ |
| `[Button]`                | `[WButton]`                                            |
| `[ReadOnly]`              | `[WReadOnly]`                                          |
| `[ShowIf]` / `[HideIf]`   | `[WShowIf]`                                            |
| `[EnumToggleButtons]`     | `[WEnumToggleButtons]`                                 |
| `[ValueDropdown]`         | `[WValueDropDown]`, `[IntDropDown]`, `[StringInList]`  |
| `[BoxGroup]`              | `[WGroup]`                                             |
| `[FoldoutGroup]`          | `[WGroup(collapsible: true)]`                          |
| `[InlineEditor]`          | `[WInLineEditor]`                                      |
| `[Required]`              | `[WNotNull]`, `[ValidateAssignment]`                   |
| `SerializedMonoBehaviour` | Staged data migration to standard `MonoBehaviour`      |
| `SerializedDictionary`    | Staged data migration to `SerializableDictionary<K,V>` |
| N/A (paid feature)        | `SerializableHashSet<T>`                               |

---

## 1. Serializable Collections

### Dictionary

**Odin:**

```csharp
using Sirenix.OdinInspector;
using Sirenix.Serialization;

public class Example : SerializedMonoBehaviour
{
    public Dictionary<string, int> scores;
}
```

**Unity Helpers:**

<!-- doc-sample: compiles -->

```csharp
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

public class Example : MonoBehaviour
{
    [SerializeField]
    private SerializableDictionary<string, int> scores = new SerializableDictionary<string, int>();
}
```

**Key differences:**

- No special base class required (use standard `MonoBehaviour`)
- Must use `[SerializeField]` or `public`
- Initialize with `new` to avoid null references (good practice, Unity will initialize this like it does List<T> and arrays)

### HashSet

**Odin:**

```csharp
using Sirenix.OdinInspector;
using Sirenix.Serialization;

public class Example : SerializedMonoBehaviour
{
    public HashSet<string> unlockedItems;
}
```

**Unity Helpers:**

<!-- doc-sample: compiles -->

```csharp
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

public class Example : MonoBehaviour
{
    [SerializeField]
    private SerializableHashSet<string> unlockedItems = new SerializableHashSet<string>();
}
```

---

## 2. Inspector Buttons

**Odin:**

```csharp
[Button("Regenerate")]
private void RegenerateLevel() { }

[Button, ButtonGroup("Actions")]
private void Save() { }
```

**Unity Helpers:**

<!-- doc-sample: compiles -->

```csharp
[WButton("Regenerate")]
private void RegenerateLevel() { }

[WButton(groupName: "Actions")]
private void Save() { }
```

**Additional options:**

<!-- doc-sample: compiles -->

```csharp
// Control button order within a group
[WButton(drawOrder: 1, groupName: "Debug")]
private void PrintDebugInfo() { }

// Control group placement (top or bottom of inspector)
[WButton(groupName: "Authoring", groupPlacement: WButtonGroupPlacement.Top)]
private void GenerateIds() { }
```

**Odin Inspector Support:**

WButton works with Odin's `SerializedMonoBehaviour` and `SerializedScriptableObject` without additional setup. Use `[WButton]` on your methods.

**Manual integration may be needed if:**

- You create a custom `OdinEditor` for a specific type
- See [Inspector Buttons - Custom Editors](../features/inspector/inspector-button.md#integration-with-custom-odin-editors) for details

---

## 3. Conditional Display

### Basic Boolean Condition

**Odin:**

```csharp
public bool showAdvanced;

[ShowIf("showAdvanced")]
public float advancedSetting;
```

**Unity Helpers:**

<!-- doc-sample: compiles -->

```csharp
public bool showAdvanced;

[WShowIf(nameof(showAdvanced))]
public float advancedSetting;
```

### Hide If (Inverse)

**Odin:**

```csharp
[HideIf("isDisabled")]
public float value;
```

**Unity Helpers:**

```csharp
[WShowIf(nameof(isDisabled), inverse: true)]
public float value;
```

### Enum Value Comparison

**Odin:**

```csharp
public AttackType attackType;

[ShowIf("attackType", AttackType.Ranged)]
public float range;
```

**Unity Helpers:**

```csharp
public AttackType attackType;

[WShowIf(nameof(attackType), AttackType.Ranged)]
public float range;
```

### Numeric Comparisons

**Odin:**

```csharp
[ShowIf("@level >= 5")]
public Ability ultimateAbility;
```

**Unity Helpers:**

```csharp
[WShowIf(nameof(level), WShowIfComparison.GreaterThanOrEqual, 5)]
public Ability ultimateAbility;
```

**Available comparisons:** `Equal`, `NotEqual`, `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual`, `IsNull`, `IsNotNull`, `IsNullOrEmpty`, `IsNotNullOrEmpty`

---

## 4. Enum Toggle Buttons

**Odin:**

```csharp
[EnumToggleButtons]
public Direction direction;

[EnumToggleButtons]
public MovementFlags flags; // [Flags] enum
```

**Unity Helpers:**

<!-- doc-sample: compiles -->

```csharp
[System.Flags]
public enum MovementFlags { None = 0, Walk = 1, Run = 2, Jump = 4 }

[WEnumToggleButtons]
public Direction direction;

[WEnumToggleButtons(showSelectAll: true, showSelectNone: true)]
public MovementFlags flags; // [Flags] enum
```

**Control buttons per row:**

<!-- doc-sample: compiles -->

```csharp
[System.Flags]
public enum DamageType { None = 0, Physical = 1, Fire = 2, Ice = 4 }

[WEnumToggleButtons(buttonsPerRow: 4)]
public DamageType damageTypes;
```

---

## 5. Value Dropdowns

### Integer Dropdown

**Odin:**

```csharp
[ValueDropdown("GetFrameRates")]
public int targetFrameRate;

private int[] GetFrameRates() => new[] { 30, 60, 120 };
```

**Unity Helpers:**

```csharp
// Inline values (simplest)
[IntDropDown(30, 60, 120)]
public int targetFrameRate;

// Or with provider method
[IntDropDown(nameof(GetFrameRates))]
public int targetFrameRate;

private IEnumerable<int> GetFrameRates() => new[] { 30, 60, 120 };
```

### String Dropdown

**Odin:**

```csharp
[ValueDropdown("GetDifficulties")]
public string difficulty;
```

**Unity Helpers:**

```csharp
// Inline values
[StringInList("Easy", "Normal", "Hard")]
public string difficulty;

// Or with provider
[StringInList(nameof(GetDifficulties))]
public string difficulty;
```

### Generic Value Dropdown

**Unity Helpers:**

```csharp
// Static provider from another class
[WValueDropDown(typeof(AudioManager), nameof(AudioManager.GetSoundNames))]
public string soundEffect;

// Instance provider (method on same class)
[WValueDropDown(nameof(GetAvailableWeapons), typeof(WeaponData))]
public WeaponData selectedWeapon;
```

---

## 6. Grouping Fields

**Odin:**

```csharp
[BoxGroup("Movement")]
public float speed;

[BoxGroup("Movement")]
public float jumpHeight;

[FoldoutGroup("Advanced")]
public float acceleration;
```

**Unity Helpers:**

```csharp
// Auto-include next N fields
[WGroup("Movement", autoIncludeCount: 2)]
public float speed;        // Field 1: in group
public float jumpHeight;   // Field 2: in group (auto-included, last by count)

// Or explicit end marker
[WGroup("Movement")]
public float speed;        // In group
public float jumpHeight;   // In group (auto-included)
[WGroupEnd]                // friction IS included, then group closes
public float friction;     // In group (last field)

// Collapsible (foldout)
[WGroup("Advanced", collapsible: true, startCollapsed: true)]
public float acceleration;
```

### Nested Groups

**Unity Helpers:**

<!-- doc-sample: compiles -->

```csharp
[WGroup("Character", displayName: "Character Settings")]
public string characterName;

[WGroup("Stats", parentGroup: "Character")]
public int health;
public int mana;
```

---

## 7. Inline Editors

**Odin:**

```csharp
[InlineEditor]
public EnemyConfig config;

[InlineEditor(InlineEditorModes.GUIOnly)]
public ItemData item;
```

**Unity Helpers:**

<!-- doc-sample: compiles -->

```csharp
public sealed class EnemyConfig : ScriptableObject { }

public sealed class ItemData : ScriptableObject { }

[WInLineEditor]
public EnemyConfig config;

[WInLineEditor(WInLineEditorMode.FoldoutExpanded, inspectorHeight: 200f)]
public ItemData item;
```

**Available modes:** `AlwaysExpanded`, `FoldoutExpanded`, `FoldoutCollapsed`

---

## 8. Required/NotNull Validation

**Odin:**

```csharp
[Required]
public GameObject prefab;

[Required("Player reference is required!")]
public Transform player;
```

**Unity Helpers:**

<!-- doc-sample: compiles -->

```csharp
[WNotNull]
public GameObject prefab;

[WNotNull(WNotNullMessageType.Error, "Player reference is required!")]
public Transform player;

// Runtime validation in Awake/Start
private void Awake()
{
    this.CheckForNulls(); // Extension method
}
```

### Collection Validation

For validating that collections aren't empty:

<!-- doc-sample: compiles -->

```csharp
public sealed class EnemyData : ScriptableObject { }

[ValidateAssignment]
public List<Transform> spawnPoints; // Warns if null or empty

[ValidateAssignment(ValidateAssignmentMessageType.Error, "Need at least one enemy type")]
public List<EnemyData> enemyTypes;

// Runtime check
private void Start()
{
    this.ValidateAssignments();
}
```

---

## 9. Read-Only Fields

**Odin:**

```csharp
[ReadOnly]
public string generatedId;
```

**Unity Helpers:**

<!-- doc-sample: compiles -->

```csharp
[WReadOnly]
public string generatedId;
```

---

## 10. Complete Migration Example

**Before (Odin):**

```csharp
using Sirenix.OdinInspector;
using Sirenix.Serialization;
using UnityEngine;
using System.Collections.Generic;

public class EnemySpawner : SerializedMonoBehaviour
{
    [BoxGroup("Settings")]
    [Required]
    public GameObject enemyPrefab;

    [BoxGroup("Settings")]
    [ShowIf("useWaves")]
    public int wavesCount = 3;

    public bool useWaves;

    [EnumToggleButtons]
    public SpawnPattern pattern;

    [ValueDropdown("GetSpawnRates")]
    public float spawnRate;

    public Dictionary<string, int> enemyWeights;

    [Button("Spawn Wave")]
    private void SpawnWave() { }

    private float[] GetSpawnRates() => new[] { 0.5f, 1f, 2f };
}
```

**After (Unity Helpers):**

<!-- doc-sample: compiles -->

```csharp
using UnityEngine;
using System.Collections.Generic;
using WallstopStudios.UnityHelpers.Core.Attributes;
using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

public enum SpawnPattern { Burst, Trickle, Ring }

public class EnemySpawner : MonoBehaviour
{
    [WGroup("Settings", autoIncludeCount: 2)]
    [WNotNull]
    [SerializeField]
    private GameObject enemyPrefab;

    [WShowIf(nameof(useWaves))]
    [SerializeField]
    private int wavesCount = 3;

    [SerializeField]
    private bool useWaves;

    [WEnumToggleButtons]
    [SerializeField]
    private SpawnPattern pattern;

    [WValueDropDown(0.5f, 1f, 2f)]
    [SerializeField]
    private float spawnRate;

    [SerializeField]
    private SerializableDictionary<string, int> enemyWeights =
        new SerializableDictionary<string, int>();

    [WButton("Spawn Wave")]
    private void SpawnWave() { }
}
```

---

## Namespace Reference

<!-- doc-sample: compiles -->

```csharp
// Attributes
using WallstopStudios.UnityHelpers.Core.Attributes;

// Serializable collections
using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
```

---

## Key Differences Summary

1. **No special base class** - Use standard `MonoBehaviour` / `ScriptableObject`
2. **Use `nameof()`** - Unity Helpers uses `nameof()` for condition fields (type-safe)
3. **Initialize collections** - Initialize `new SerializableDictionary<K,V>()` etc. to avoid null references
4. **[HideIf] becomes inverse** - Use `[WShowIf(..., inverse: true)]` instead of `[HideIf]`
5. **Numeric conditions** - Use `WShowIfComparison` enum instead of expression strings
6. **Groups auto-include** - `[WGroup]` can auto-include subsequent fields with `autoIncludeCount`

---

## See Also

- **[Inspector Overview](../features/inspector/inspector-overview.md)** - Complete inspector features guide
- **[Serialization Types](../features/serialization/serialization-types.md)** - All serializable types
- **[Inspector Buttons](../features/inspector/inspector-button.md)** - WButton detailed guide
- **[Inspector Conditional Display](../features/inspector/inspector-conditional-display.md)** - WShowIf detailed guide
- **[Inspector Selection Attributes](../features/inspector/inspector-selection-attributes.md)** - Dropdowns and toggles
