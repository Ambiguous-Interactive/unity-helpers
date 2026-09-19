# integrate-odin-inspector - Part 3

## Split Content

## Odin Inspector Type Support

### SerializedMonoBehaviour/SerializedScriptableObject

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

## Odin Drawer to Unity Helpers Attribute Mapping

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

---

## Checklist for New Odin Drawer

1. [ ] Create drawer in `Editor/CustomDrawers/Odin/`
2. [ ] Place `#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR` **inside** namespace
3. [ ] Inherit from appropriate base class (`OdinAttributeDrawer<TAttribute>` or `OdinValueDrawer<TValue>`)
4. [ ] Extract shared logic to `Editor/CustomDrawers/Utils/` if Unity PropertyDrawer exists
5. [ ] Use type aliases for long namespace imports
6. [ ] Create test folder: `Tests/Editor/CustomDrawers/Odin/`
7. [ ] Create test types folder: `Tests/Editor/TestTypes/Odin/{Feature}/`
8. [ ] Generate `.meta` files for all new files

---

## Related Skills

- [integrate-optional-dependency](../skills/integrate-optional-dependency.md) - General optional dependency patterns
- [test-odin-drawers](../skills/test-odin-drawers.md) - Comprehensive Odin drawer testing patterns
- [add-inspector-attribute](../skills/add-inspector-attribute.md) - Available inspector attributes with Odin compatibility
- [create-property-drawer](../skills/create-property-drawer.md) - Unity PropertyDrawer creation patterns
- [editor-caching-patterns](../skills/editor-caching-patterns.md) - Centralized caching for editor code
