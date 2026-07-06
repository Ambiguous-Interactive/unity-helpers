# Skill: Add Inspector Attribute

<!-- trigger: attribute, inspector, wgroup, wbutton, wshowif | Improving editor UX with attributes | Feature -->

**Trigger**: When adding or configuring Unity Helpers inspector attributes to improve editor UX.

---

## Available Attributes

| Attribute                          | Purpose                                 |
| ---------------------------------- | --------------------------------------- |
| `[WGroup]` / `[WGroupEnd]`         | Boxed sections with collapsible headers |
| `[WButton]`                        | Method buttons with async support       |
| `[WShowIf]`                        | Conditional visibility                  |
| `[WEnumToggleButtons]`             | Flag enums as toggle grids              |
| `[WInLineEditor]`                  | Inline nested editor                    |
| `[WValueDropDown]`                 | Value dropdown selection                |
| `[WSerializableCollectionFoldout]` | Foldout for collections                 |
| `[WReadOnly]`                      | Read-only display                       |
| `[WNotNull]`                       | Required field validation               |
| `[ValidateAssignment]`             | Runtime assignment validation           |
| `[StringInList]`                   | Dropdown from string list               |
| `[IntDropDown]`                    | Integer dropdown                        |
| `[EnumDisplayName]`                | Custom enum display names               |

---

## Grouping: `[WGroup]` / `[WGroupEnd]`

Create boxed, collapsible sections:

```csharp
[WGroup("Movement Settings", WGroupColor.Blue)]
[SerializeField] private float moveSpeed = 5f;
[SerializeField] private float jumpHeight = 2f;
[SerializeField] private float gravity = -9.81f;
[WGroupEnd]

[WGroup("Combat Settings", WGroupColor.Red)]
[SerializeField] private int damage = 10;
[SerializeField] private float attackRange = 2f;
[WGroupEnd]
```

### Group Colors

`WGroupColor.Blue`, `WGroupColor.Red`, `WGroupColor.Green`, `WGroupColor.Yellow`, `WGroupColor.Purple`, `WGroupColor.Orange`, `WGroupColor.Gray`

---

## Buttons: `[WButton]`

Add clickable method buttons:

```csharp
[WButton("Reset to Defaults")]
private void ResetDefaults()
{
    moveSpeed = 5f;
    jumpHeight = 2f;
}

[WButton("Spawn Enemy")]
private void SpawnEnemy()
{
    // Spawning logic
}

// Async support with cancellation
[WButton("Long Operation")]
private async Task LongOperation(CancellationToken token)
{
    await Task.Delay(1000, token);
}
```

---

## Conditional Display: `[WShowIf]`

Show/hide fields based on conditions:

```csharp
[SerializeField] private bool useCustomSettings;

[WShowIf(nameof(useCustomSettings))]
[SerializeField] private float customValue;

// Comparison operators
[WShowIf(nameof(healthPercent), WShowIfOperator.LessThan, 0.5f)]
[SerializeField] private GameObject lowHealthWarning;

// Multiple conditions
[WShowIf(nameof(isEnabled))]
[WShowIf(nameof(level), WShowIfOperator.GreaterOrEqual, 5)]
[SerializeField] private string advancedOption;
```

### Operators

`Equal`, `NotEqual`, `GreaterThan`, `LessThan`, `GreaterOrEqual`, `LessOrEqual`, `And`, `Or`, `Not`

---

## Enum Toggle Buttons: `[WEnumToggleButtons]`

Display flag enums as visual toggle grids:

```csharp
[Flags]
public enum DamageTypes
{
    None = 0,
    Physical = 1,
    Fire = 2,
    Ice = 4,
    Lightning = 8,
}

[WEnumToggleButtons]
[SerializeField] private DamageTypes resistances;
```

---

## Inline Editor: `[WInLineEditor]`

Edit referenced ScriptableObjects inline:

```csharp
[WInLineEditor]
[SerializeField] private WeaponData weaponData;

[WInLineEditor]
[SerializeField] private List<BuffEffect> buffs;
```

---

## Dropdowns: `[WValueDropDown]` / `[StringInList]` / `[IntDropDown]`

### Value Dropdown

```csharp
[WValueDropDown(nameof(GetAvailableOptions))]
[SerializeField] private string selectedOption;

private IEnumerable<string> GetAvailableOptions()
{
    return new[] { "Option A", "Option B", "Option C" };
}
```

### String in List

```csharp
[StringInList("Easy", "Medium", "Hard")]
[SerializeField] private string difficulty;

// Or from method
[StringInList(nameof(GetDifficultyOptions))]
[SerializeField] private string difficulty;
```

### Int Dropdown

```csharp
[IntDropDown(1, 5, 10, 25, 50, 100)]
[SerializeField] private int spawnCount;
```

---

## Validation: `[WNotNull]` / `[ValidateAssignment]`

### Required Fields

```csharp
[WNotNull]
[SerializeField] private Transform spawnPoint;  // Shows error if null

[WNotNull("Player reference is required!")]
[SerializeField] private PlayerController player;
```

### Runtime Validation

```csharp
[ValidateAssignment]
[SerializeField] private GameObject prefab;  // Logs warning if invalid assignment
```

---

## Read-Only Display: `[WReadOnly]`

```csharp
[WReadOnly]
[SerializeField] private int currentHealth;

[WReadOnly]
[SerializeField] private string generatedId;
```

---

## Collection Foldout: `[WSerializableCollectionFoldout]`

```csharp
[WSerializableCollectionFoldout]
[SerializeField] private List<Item> inventory;

[WSerializableCollectionFoldout("Equipped Items")]
[SerializeField] private SerializableDictionary<EquipSlot, Item> equipped;
```

---

## Custom Enum Names: `[EnumDisplayName]`

```csharp
public enum ItemRarity
{
    [EnumDisplayName("Common (Gray)")]
    Common = 1,

    [EnumDisplayName("Uncommon (Green)")]
    Uncommon = 2,

    [EnumDisplayName("Rare (Blue)")]
    Rare = 3,

    [EnumDisplayName("Legendary (Orange)")]
    Legendary = 4,
}
```

---

## Odin Inspector Compatibility

All Unity Helpers inspector attributes are designed to work seamlessly with Odin Inspector. When Odin is installed as the `odininspector` package, the package-owned `WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR` symbol activates dedicated Odin drawers.

### Attribute Comparison

| Unity Helpers              | Odin Equivalent       | Unity Helpers Advantages                           |
| -------------------------- | --------------------- | -------------------------------------------------- |
| `[WGroup]` / `[WGroupEnd]` | `[FoldoutGroup]`      | No Odin dependency; colored themes                 |
| `[WButton]`                | `[Button]`            | `async Task`, `CancellationToken` support          |
| `[WShowIf]`                | `[ShowIf]`            | More comparison operators (`GreaterOrEqual`, etc.) |
| `[WEnumToggleButtons]`     | `[EnumToggleButtons]` | Identical appearance; no dependency                |
| `[WInLineEditor]`          | `[InlineEditor]`      | Configurable height and scrolling                  |
| `[WValueDropDown]`         | `[ValueDropdown]`     | Same functionality                                 |
| `[WReadOnly]`              | `[ReadOnly]`          | Identical behavior                                 |
| `[WNotNull]`               | `[Required]`          | Runtime validation via extension method            |
| `[ValidateAssignment]`     | `[ValidateInput]`     | Validation during assignment                       |

### When Odin Inspector is Installed

1. **SerializedMonoBehaviour Support**: Full compatibility with Odin's enhanced serialization
2. **SerializedScriptableObject Support**: Complex types (dictionaries, interfaces) serialize properly
3. **Automatic Drawer Selection**: Odin drawers handle `[W*]` attributes on Odin types
4. **Mixed Usage**: Can use both Unity Helpers and Odin attributes on same class

### Behavior Parity

All attributes maintain **identical behavior** whether Odin is installed or not:

```csharp
// Works identically with or without Odin
public class MyBehaviour : MonoBehaviour
{
    [WShowIf(nameof(useCustomSettings))]
    [SerializeField] private float customValue;

    [WButton("Reset")]
    private void Reset() { }
}

// Also works with Odin base classes
public class MyOdinBehaviour : SerializedMonoBehaviour
{
    [WShowIf(nameof(useCustomSettings))]  // Unity Helpers attribute works on Odin types
    [SerializeField] private float customValue;

    [WButton("Reset")]  // Unity Helpers button on Odin type
    private void Reset() { }
}
```

### Choosing Between Unity Helpers and Odin Attributes

| Scenario                                      | Recommendation                                     |
| --------------------------------------------- | -------------------------------------------------- |
| Project without Odin                          | Use Unity Helpers attributes                       |
| Project with Odin                             | Either works; Unity Helpers for async buttons      |
| Shared code/packages                          | Use Unity Helpers (no external dependency)         |
| Need Odin's `[InlineProperty]`, `[TableList]` | Use Odin (features not available in Unity Helpers) |
| Need async/cancellable buttons                | Use Unity Helpers `[WButton]`                      |

### Technical Implementation Details

When Odin is installed, dedicated `OdinAttributeDrawer` implementations handle Unity Helpers attributes:

| Unity Helpers Attribute | Odin Drawer Class              | Shared Utility                |
| ----------------------- | ------------------------------ | ----------------------------- |
| `[WEnumToggleButtons]`  | `WEnumToggleButtonsOdinDrawer` | `EnumToggleButtonsShared.cs`  |
| `[WShowIf]`             | `WShowIfOdinDrawer`            | `ShowIfConditionEvaluator.cs` |
| `[WInLineEditor]`       | `WInLineEditorOdinDrawer`      | `InLineEditorShared.cs`       |
| `[WValueDropDown]`      | `WValueDropDownOdinDrawer`     | `DropDownShared.cs`           |
| `[IntDropDown]`         | `IntDropDownOdinDrawer`        | `DropDownShared.cs`           |
| `[StringInList]`        | `StringInListOdinDrawer`       | `DropDownShared.cs`           |
| `[WNotNull]`            | `WNotNullOdinDrawer`           | `ValidationShared.cs`         |
| `[ValidateAssignment]`  | `ValidateAssignmentOdinDrawer` | `ValidationShared.cs`         |
| `[WReadOnly]`           | `WReadOnlyOdinDrawer`          | -                             |

### Odin Property Tree Access

Odin drawers use `InspectorProperty` instead of `SerializedProperty`, enabling:

```csharp
// Attribute access
WShowIfAttribute showIf = this.Attribute;  // Strongly typed

// Value access (works with interfaces, dictionaries, etc.)
TValue currentValue = this.ValueEntry.SmartValue;

// Parent navigation (for condition evaluation)
InspectorProperty parent = this.Property.Parent;
InspectorProperty sibling = parent?.Children.Get(conditionMemberName);
object conditionValue = sibling?.ValueEntry?.WeakSmartValue;
```

### SerializedMonoBehaviour/SerializedScriptableObject Support

Unity Helpers attributes work on Odin's enhanced base classes which support:

- **Interface serialization**: `[SerializeField] private IMyInterface myField;`
- **Dictionary serialization**: `[SerializeField] private Dictionary<string, MyClass> lookup;`
- **Polymorphic lists**: `[SerializeField] private List<BaseClass> items;`

```csharp
// All Unity Helpers attributes work on Odin base classes
public class MyOdinComponent : SerializedMonoBehaviour
{
    [WEnumToggleButtons]
    [SerializeField] private MyFlags flags;

    [WShowIf(nameof(useCustomConfig))]
    [SerializeField] private IConfigProvider configProvider;  // Interface serialization

    [WInLineEditor]
    [SerializeField] private Dictionary<string, ScriptableObject> assets;  // Dictionary support

    [WButton("Process All")]
    private async Task ProcessAllAsync(CancellationToken token) { }
}
```

---

## Completion Requirements (MANDATORY)

After adding or modifying inspector attributes, you MUST complete these steps:

1. **Update CHANGELOG** — Add entry to `### Added` or `### Changed` section
2. **Update feature documentation** — Document the attribute in `docs/features/inspector/`
3. **Add XML documentation** — All public attribute members need `<summary>` tags
4. **Include code samples** — Working examples in docs and XML comments
5. **Run validation** — `npm run lint:docs` and `dotnet tool run csharpier format .`

See [update-documentation](./update-documentation.md) for detailed standards.

---

## Complete Example

```csharp
public class EnemyController : MonoBehaviour
{
    [WGroup("Identity", WGroupColor.Blue)]
    [WNotNull]
    [SerializeField] private string enemyId;

    [WReadOnly]
    [SerializeField] private string displayName;
    [WGroupEnd]

    [WGroup("Stats", WGroupColor.Green)]
    [SerializeField] private int maxHealth = 100;

    [WReadOnly]
    [SerializeField] private int currentHealth;

    [WEnumToggleButtons]
    [SerializeField] private DamageTypes weaknesses;
    [WGroupEnd]

    [WGroup("Behavior", WGroupColor.Purple)]
    [SerializeField] private bool isAggressive;

    [WShowIf(nameof(isAggressive))]
    [SerializeField] private float aggroRange = 10f;

    [WShowIf(nameof(isAggressive))]
    [WInLineEditor]
    [SerializeField] private AttackPattern attackPattern;
    [WGroupEnd]

    [WButton("Reset Health")]
    private void ResetHealth()
    {
        currentHealth = maxHealth;
    }

    [WButton("Test Attack")]
    private void TestAttack()
    {
        Debug.Log($"{displayName} attacks!");
    }
}
```
