# Skill: Integrate Odin Inspector

<!-- trigger: odin, sirenix, property tree, attribute drawer | Odin Inspector integration patterns | Feature -->

**Trigger**: When creating or modifying Odin Inspector drawer implementations in this repository.

---

## When to Use This Skill

Use this skill when:

- Creating `OdinAttributeDrawer` implementations for custom attributes
- Navigating Odin's `InspectorProperty` tree for condition evaluation
- Accessing values through Odin's `ValueEntry` system
- Implementing shared logic between Unity PropertyDrawer and Odin AttributeDrawer
- Working with `SerializedMonoBehaviour` or `SerializedScriptableObject`

For general optional dependency patterns, see [integrate-optional-dependency](./integrate-optional-dependency.md).
For testing Odin drawers, see [test-odin-drawers](./test-odin-drawers.md).

---

## Odin Drawer Types

| Drawer Type            | Use Case                               | Base Class                                |
| ---------------------- | -------------------------------------- | ----------------------------------------- |
| Attribute Drawer       | Drawers triggered by custom attributes | `OdinAttributeDrawer<TAttribute>`         |
| Typed Attribute Drawer | Attribute + value type constraint      | `OdinAttributeDrawer<TAttribute, TValue>` |
| Value Drawer           | Drawers for specific types             | `OdinValueDrawer<TValue>`                 |

---

## Odin Drawer Lifecycle

```csharp
namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector.Editor;
    using UnityEngine;

    public sealed class MyOdinDrawer : OdinAttributeDrawer<MyAttribute>
    {
        /// <summary>
        /// Called once when the drawer is created. Use for one-time setup.
        /// </summary>
        protected override void Initialize()
        {
            // Cache expensive computations here
        }

        /// <summary>
        /// Main draw method. Called every frame while inspector is visible.
        /// </summary>
        protected override void DrawPropertyLayout(GUIContent label)
        {
            // Access property value: this.ValueEntry.SmartValue
            // Access property info: this.Property
            // Call next drawer: this.CallNextDrawer(label)
        }

        /// <summary>
        /// Optional: Override to filter which properties this drawer handles.
        /// </summary>
        protected override bool CanDrawProperty(InspectorProperty property)
        {
            return base.CanDrawProperty(property);
        }
    }
#endif
}
```

---

## Property Tree Navigation

### Basic Navigation

```csharp
// Access current property's value
object value = this.Property.ValueEntry.WeakSmartValue;
T typedValue = this.ValueEntry.SmartValue;

// Access parent property
InspectorProperty parent = this.Property.Parent;
object parentValue = parent?.ValueEntry?.WeakSmartValue;

// Access child properties
foreach (InspectorProperty child in this.Property.Children)
{
    child.Draw(child.Label);
}

// Find sibling by name
InspectorProperty sibling = this.Property.Parent?.Children[memberName];
```

### Advanced Navigation (for Condition Evaluation)

```csharp
// Navigate to parent's value (e.g., to access sibling members for conditions)
InspectorProperty parent = this.Property.Parent;
object parentValue = parent?.ValueEntry?.WeakSmartValue;

// Access sibling property by name (useful for WShowIf, conditions)
string conditionMember = this.Attribute.conditionMember;
InspectorProperty sibling = parent?.Children.Get(conditionMember);
object conditionValue = sibling?.ValueEntry?.WeakSmartValue;

// Resolve member via reflection fallback when property tree fails
Type parentType = parentValue?.GetType();
MemberInfo memberInfo = parentType?.GetField(conditionMember, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

// Draw children (for inline/nested editors)
foreach (InspectorProperty child in this.Property.Children)
{
    child.Draw(child.Label);
}

// Check if property has Odin drawer chain
bool hasNextDrawer = this.Property.GetNextDrawer() != null;
```

---

## Common Sirenix Namespace Imports

```csharp
// Core attributes (runtime)
using Sirenix.OdinInspector;                // [Button], [ShowIf], [Required], etc.

// Drawer development (editor only)
using Sirenix.OdinInspector.Editor;         // OdinAttributeDrawer, InspectorProperty
using Sirenix.OdinInspector.Editor.ValueResolvers;  // ValueResolver for member evaluation
using Sirenix.Utilities;                    // TypeExtensions, MemberFinder
using Sirenix.Utilities.Editor;             // GUIHelper, SirenixEditorGUI

// Serialization (for SerializedMonoBehaviour/SerializedScriptableObject)
using Sirenix.Serialization;                // OdinSerializeAttribute, ISerializationCallbackReceiver
```

---

## Odin vs Unity Property Access Comparison

| Operation      | Unity PropertyDrawer                | Odin AttributeDrawer              |
| -------------- | ----------------------------------- | --------------------------------- |
| Get value      | `property.objectReferenceValue`     | `this.ValueEntry.SmartValue`      |
| Set value      | `property.objectReferenceValue = x` | `this.ValueEntry.SmartValue = x`  |
| Get parent     | Reflection required                 | `this.Property.Parent`            |
| Get field info | `fieldInfo` parameter               | `this.Property.Info.MemberInfo`   |
| Disable GUI    | `EditorGUI.BeginDisabledGroup()`    | `GUIHelper.PushGUIEnabled(false)` |
| Draw property  | `EditorGUI.PropertyField()`         | `this.CallNextDrawer(label)`      |
| Get attribute  | `attribute` field                   | `this.Attribute`                  |

---

## Consolidating Odin and Non-Odin Drawer Logic

When both implementations share significant logic, create utility classes:

### Correct (Shared Utility)

```csharp
// Editor/CustomDrawers/Utils/ShowIfConditionEvaluator.cs
namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils
{
    /// <summary>
    /// Shared condition evaluation logic for WShowIf drawers.
    /// Used by both Unity PropertyDrawer and Odin AttributeDrawer.
    /// </summary>
    public static class ShowIfConditionEvaluator
    {
        public static bool EvaluateCondition(
            object conditionValue,
            object[] expectedValues,
            WShowIfOperator op,
            bool inverse)
        {
            // Shared implementation
        }
    }
}

// Editor/CustomDrawers/Odin/WShowIfOdinDrawer.cs
protected override void DrawPropertyLayout(GUIContent label)
{
    object conditionValue = GetConditionValue(); // Odin-specific
    bool show = ShowIfConditionEvaluator.EvaluateCondition(
        conditionValue, Attribute.expectedValues, Attribute.op, Attribute.inverse);
    if (show) CallNextDrawer(label);
}

// Editor/CustomDrawers/WShowIfPropertyDrawer.cs
public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
{
    object conditionValue = GetConditionValue(property); // Unity-specific
    bool show = ShowIfConditionEvaluator.EvaluateCondition(
        conditionValue, attribute.expectedValues, attribute.op, attribute.inverse);
    if (show) EditorGUI.PropertyField(position, property, label);
}
```

### Incorrect (Duplicated Logic)

```csharp
// DON'T copy 200 lines of condition evaluation into both drawers!
```

---

## Shared Utility Folder Structure

Based on the completed Odin consolidation (Sessions 22-26), the actual structure is:

```text
Editor/CustomDrawers/
├── Utils/                               # Shared utilities (ALWAYS #if UNITY_EDITOR)
│   ├── EnumToggleButtonsShared.cs       # Enum button logic (952 lines)
│   ├── ShowIfConditionEvaluator.cs      # Condition evaluation (586 lines)
│   ├── InLineEditorShared.cs            # Inline editor state (587 lines)
│   ├── DropDownShared.cs                # Dropdown rendering (643 lines)
│   └── ValidationShared.cs              # WNotNull/ValidateAssignment helpers (542 lines)
Editor/Core/Helper/
│   └── EditorCacheHelper.cs             # Centralized style/string/texture caching
├── Odin/                                # Odin-specific drawers (#if WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR)
│   ├── WEnumToggleButtonsOdinDrawer.cs
│   ├── WShowIfOdinDrawer.cs
│   ├── WInLineEditorOdinDrawer.cs
│   ├── WValueDropDownOdinDrawer.cs
│   ├── IntDropDownOdinDrawer.cs
│   ├── StringInListOdinDrawer.cs
│   ├── WNotNullOdinDrawer.cs
│   ├── ValidateAssignmentOdinDrawer.cs
│   ├── WReadOnlyOdinDrawer.cs
│   └── ...
├── WEnumToggleButtonsPropertyDrawer.cs  # Standard Unity drawers
├── WShowIfPropertyDrawer.cs
└── ...
```

---

## Shared Utility Design Principles

1. **Pure Logic Only**: Shared utilities contain only data structures, constants, and pure helper methods
2. **No GUI Drawing**: Actual drawing stays in drawer classes (different APIs between Unity/Odin)
3. **State Management**: State dictionaries (foldouts, scroll positions, pagination) are centralized
4. **Type Aliases**: Odin drawers use type aliases for cleaner imports:

```csharp
using EnumShared = WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils.EnumToggleButtonsShared;
using CacheHelper = WallstopStudios.UnityHelpers.Editor.Core.Helper.EditorCacheHelper;
```

---

## Example: EnumToggleButtonsShared Usage

```csharp
// In Odin drawer (WEnumToggleButtonsOdinDrawer.cs)
protected override void DrawPropertyLayout(GUIContent label)
{
    EnumShared.ToggleOption[] options = EnumShared.BuildToggleOptions(valueType);
    ulong currentMask = EnumShared.ConvertToUInt64(this.ValueEntry.SmartValue);
    EnumShared.SelectionSummary summary = EnumShared.BuildSelectionSummary(
        options, currentMask, isFlags, startIndex, visibleCount, usePagination
    );
    // Odin-specific rendering...
}

// In Unity drawer (WEnumToggleButtonsPropertyDrawer.cs)
public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
{
    EnumShared.ToggleOption[] options = EnumShared.BuildToggleOptions(enumType);
    ulong currentMask = EnumShared.ConvertToUInt64(property.enumValueFlag);
    EnumShared.SelectionSummary summary = EnumShared.BuildSelectionSummary(
        options, currentMask, isFlags, startIndex, visibleCount, usePagination
    );
    // Unity-specific rendering...
}
```

---

## Testing Odin Drawers

For comprehensive testing patterns, see [test-odin-drawers](./test-odin-drawers.md).

### Basic Test Structure

```csharp
namespace WallstopStudios.UnityHelpers.Tests.Editor.CustomDrawers
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using NUnit.Framework;
    using Sirenix.OdinInspector;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.MyFeature;

    [TestFixture]
    public sealed class MyOdinDrawerTests : CommonTestBase
    {
        [Test]
        public void DrawerRegistrationCreatesEditorForScriptableObject()
        {
            MyOdinTestTarget target = CreateScriptableObject<MyOdinTestTarget>();
            Editor editor = Editor.CreateEditor(target);
            Track(editor);

            Assert.IsTrue(editor != null);
        }

        [Test]
        public void OnInspectorGuiDoesNotThrowForValidTarget()
        {
            MyOdinTestTarget target = CreateScriptableObject<MyOdinTestTarget>();
            Editor editor = Editor.CreateEditor(target);
            Track(editor);

            Assert.DoesNotThrow(() => editor.OnInspectorGUI());
        }
    }
#endif
}
```

---

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

- [integrate-optional-dependency](./integrate-optional-dependency.md) - General optional dependency patterns
- [test-odin-drawers](./test-odin-drawers.md) - Comprehensive Odin drawer testing patterns
- [add-inspector-attribute](./add-inspector-attribute.md) - Available inspector attributes with Odin compatibility
- [create-property-drawer](./create-property-drawer.md) - Unity PropertyDrawer creation patterns
- [editor-caching-patterns](./editor-caching-patterns.md) - Centralized caching for editor code
