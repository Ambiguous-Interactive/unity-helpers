# add-inspector-attribute - Part 1

## Split Content

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
[WGroup("Movement Settings", autoIncludeCount: WGroupAttribute.InfiniteAutoInclude)]
[SerializeField] private float moveSpeed = 5f;
[SerializeField] private float jumpHeight = 2f;

[WGroupEnd("Movement Settings")] // gravity IS included, then the group closes
[SerializeField] private float gravity = -9.81f;

[WGroup("Combat Settings", autoIncludeCount: WGroupAttribute.InfiniteAutoInclude)]
[SerializeField] private int damage = 10;

[WGroupEnd("Combat Settings")] // attackRange IS included, then the group closes
[SerializeField] private float attackRange = 2f;
```

**`[WGroupEnd]` attaches to the member below it, not the one above.** An attribute always binds to
the next declaration, so an end written on its own line after the last field of a group actually
binds to the first field of the **next** one and closes that group instead. Put it on the last field
you want included.

**Name the group when a type declares more than one.** A bare `[WGroupEnd]` closes _every_ open
group, which is right for the last group in a type and wrong in the middle of one.

**There is no color parameter.** `WGroupAttribute` takes `groupName`, `displayName`,
`autoIncludeCount`, `collapsible`, `startCollapsed`, `hideHeader` and `parentGroup`. Group colors
come from the palettes under `Project Settings > Wallstop Studios > Unity Helpers > Color Palettes`,
not from the attribute.

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
