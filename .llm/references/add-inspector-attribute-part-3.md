# add-inspector-attribute - Part 3

## Split Content

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

See [update-documentation](../skills/update-documentation.md) for detailed standards.

---

## Complete Example

```csharp
public class EnemyController : MonoBehaviour
{
    [WGroup("Identity", autoIncludeCount: WGroupAttribute.InfiniteAutoInclude)]
    [WNotNull]
    [SerializeField] private string enemyId;

    [WReadOnly]
    [WGroupEnd("Identity")]
    [SerializeField] private string displayName;

    [WGroup("Stats", autoIncludeCount: WGroupAttribute.InfiniteAutoInclude)]
    [SerializeField] private int maxHealth = 100;

    [WReadOnly]
    [SerializeField] private int currentHealth;

    [WEnumToggleButtons]
    [WGroupEnd("Stats")]
    [SerializeField] private DamageTypes weaknesses;

    [WGroup("Behavior", autoIncludeCount: WGroupAttribute.InfiniteAutoInclude)]
    [SerializeField] private bool isAggressive;

    [WShowIf(nameof(isAggressive))]
    [SerializeField] private float aggroRange = 10f;

    [WShowIf(nameof(isAggressive))]
    [WInLineEditor]
    [WGroupEnd("Behavior")]
    [SerializeField] private AttackPattern attackPattern;

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
