# integrate-odin-inspector - Part 2

## Split Content

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

For comprehensive testing patterns, see [test-odin-drawers](../skills/test-odin-drawers.md).

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
