# add-inspector-attribute - Part 2

## Split Content

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
