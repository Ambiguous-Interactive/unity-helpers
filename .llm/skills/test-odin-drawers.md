# Skill: Test Odin Drawers

<!-- trigger: odin, drawer, test, attribute, inspector | Odin Inspector drawer testing patterns | Core -->

**Trigger**: When testing Odin `OdinAttributeDrawer` implementations in this repository.

---

## When to Use

Use this skill when:

- Creating tests for Odin Inspector drawer implementations
- Testing attributes that work with `SerializedMonoBehaviour` or `SerializedScriptableObject`
- Adding test coverage for Odin-specific functionality
- Verifying drawer behavior with Odin's property system

For general test creation, see [create-test](./create-test.md).
For Unity object lifecycle management, see [test-unity-lifecycle](./test-unity-lifecycle.md).

---

## Test Target Structure

Test targets must be in separate files under `Tests/Editor/TestTypes/Odin/{Feature}/`:

```text
Tests/Editor/
├── TestTypes/
│   ├── SharedEnums/                        # Shared test enums
│   │   ├── SimpleTestEnum.cs
│   │   ├── TestFlagsEnum.cs
│   │   └── TestModeEnum.cs
│   └── Odin/
│       ├── EnumToggleButtons/              # Per-feature subfolders
│       │   ├── OdinEnumToggleButtonsRegularTarget.cs
│       │   ├── OdinEnumToggleButtonsFlagsTarget.cs
│       │   ├── OdinEnumToggleButtonsMonoBehaviour.cs
│       │   └── OdinEnumToggleButtonsPaginated.cs
│       ├── ShowIf/
│       │   ├── OdinShowIfBoolTarget.cs
│       │   └── OdinShowIfEnumTarget.cs
│       └── InLineEditor/
│           └── OdinInLineEditorTarget.cs
├── CustomDrawers/
│   └── Odin/
│       ├── WEnumToggleButtonsOdinDrawerTests.cs
│       └── WShowIfOdinDrawerTests.cs
```

---

## Test Target Template (SerializedScriptableObject)

```csharp
namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.MyFeature
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.SharedEnums;

    /// <summary>
    /// Test target for MyAttribute with SerializedScriptableObject.
    /// </summary>
    internal sealed class OdinMyFeatureTarget : SerializedScriptableObject
    {
        [MyAttribute]
        public SimpleTestEnum myField;
    }
#endif
}
```

---

## Test Target Template (SerializedMonoBehaviour)

```csharp
namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.MyFeature
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.SharedEnums;

    /// <summary>
    /// Test target for MyAttribute with SerializedMonoBehaviour.
    /// </summary>
    internal sealed class OdinMyFeatureMonoBehaviour : SerializedMonoBehaviour
    {
        [MyAttribute]
        public SimpleTestEnum myField;
    }
#endif
}
```

---

## Odin Drawer Test Class Template

```csharp
namespace WallstopStudios.UnityHelpers.Tests.Editor.CustomDrawers
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using Sirenix.OdinInspector;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.MyFeature;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.SharedEnums;

    /// <summary>
    /// Tests for MyOdinDrawer ensuring MyAttribute works correctly
    /// with Odin Inspector for SerializedMonoBehaviour/SerializedScriptableObject.
    /// </summary>
    [TestFixture]
    public sealed class MyOdinDrawerTests : CommonTestBase
    {
        [Test]
        public void DrawerRegistrationCreatesEditorForScriptableObject()
        {
            OdinMyFeatureTarget target = CreateScriptableObject<OdinMyFeatureTarget>();
            Editor editor = Editor.CreateEditor(target);
            Track(editor);

            Assert.IsTrue(editor != null, "Editor should be created");
        }

        [Test]
        public void DrawerRegistrationCreatesEditorForMonoBehaviour()
        {
            OdinMyFeatureMonoBehaviour target = NewGameObject("TestMB")
                .AddComponent<OdinMyFeatureMonoBehaviour>();
            Editor editor = Editor.CreateEditor(target);
            Track(editor);

            Assert.IsTrue(editor != null, "Editor should be created for MB");
        }

        [Test]
        public void OnInspectorGuiDoesNotThrowForScriptableObject()
        {
            OdinMyFeatureTarget target = CreateScriptableObject<OdinMyFeatureTarget>();
            Editor editor = Editor.CreateEditor(target);
            Track(editor);

            Assert.DoesNotThrow(() => editor.OnInspectorGUI());
        }

        [Test]
        public void OnInspectorGuiDoesNotThrowForMonoBehaviour()
        {
            OdinMyFeatureMonoBehaviour target = NewGameObject("TestMB")
                .AddComponent<OdinMyFeatureMonoBehaviour>();
            Editor editor = Editor.CreateEditor(target);
            Track(editor);

            Assert.DoesNotThrow(() => editor.OnInspectorGUI());
        }

        [Test]
        public void OnInspectorGuiHandlesMultipleCalls()
        {
            OdinMyFeatureTarget target = CreateScriptableObject<OdinMyFeatureTarget>();
            Editor editor = Editor.CreateEditor(target);
            Track(editor);

            Assert.DoesNotThrow(() =>
            {
                editor.OnInspectorGUI();
                editor.OnInspectorGUI();
                editor.OnInspectorGUI();
            });
        }

        [Test]
        [TestCaseSource(nameof(FieldValueTestCases))]
        public void OnInspectorGuiHandlesVariousFieldValues(SimpleTestEnum value)
        {
            OdinMyFeatureTarget target = CreateScriptableObject<OdinMyFeatureTarget>();
            target.myField = value;
            Editor editor = Editor.CreateEditor(target);
            Track(editor);

            Assert.DoesNotThrow(() => editor.OnInspectorGUI());
        }

        private static IEnumerable<TestCaseData> FieldValueTestCases()
        {
            yield return new TestCaseData(SimpleTestEnum.One)
                .SetName("Value.FirstEnumMember");
            yield return new TestCaseData(SimpleTestEnum.Two)
                .SetName("Value.SecondEnumMember");
            yield return new TestCaseData(SimpleTestEnum.Three)
                .SetName("Value.ThirdEnumMember");
            yield return new TestCaseData((SimpleTestEnum)999)
                .SetName("Value.InvalidEnumValue");
        }
    }
#endif
}
```

---

## Required Test Categories for Odin Drawers

| Category                 | Test Scenarios                                          |
| ------------------------ | ------------------------------------------------------- |
| Editor creation          | `Editor.CreateEditor(target)` returns non-null          |
| ScriptableObject targets | `SerializedScriptableObject` base class works correctly |
| MonoBehaviour targets    | `SerializedMonoBehaviour` base class works correctly    |
| No-throw on GUI          | `OnInspectorGUI()` doesn't throw for valid targets      |
| Multiple GUI calls       | Repeated `OnInspectorGUI()` calls don't cause issues    |
| Various field values     | Different enum values, null references, edge cases      |
| Multiple fields          | Multiple attributes on same target work together        |
| Attribute configurations | Different attribute constructor parameters              |
| Caching behavior         | Multiple instances share caches correctly               |
| Editor cleanup           | `DestroyImmediate(editor)` in finally blocks            |

---

## Shared Test Enums

Extract common test enums to `Tests/Editor/TestTypes/SharedEnums/`:

```csharp
// Tests/Editor/TestTypes/SharedEnums/SimpleTestEnum.cs
namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.SharedEnums
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    public enum SimpleTestEnum { One, Two, Three }
#endif
}

// Tests/Editor/TestTypes/SharedEnums/TestFlagsEnum.cs
namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.SharedEnums
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System;

    [Flags]
    public enum TestFlagsEnum
    {
        None = 0,
        Flag1 = 1,
        Flag2 = 2,
        Flag3 = 4,
        All = Flag1 | Flag2 | Flag3
    }
#endif
}
```

---

## CommonTestBase Usage for Odin Tests

Always inherit from `CommonTestBase` for automatic cleanup:

```csharp
public sealed class MyOdinDrawerTests : CommonTestBase
{
    // CreateScriptableObject<T>() - Creates and tracks SO for cleanup
    // NewGameObject(name) - Creates and tracks GO for cleanup
    // Track(obj) - Manually track any Unity object for cleanup
}
```

---

## Conditional Compilation

All Odin-specific code must be wrapped in conditional compilation:

```csharp
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    // Odin-specific code here
#endif
```

This ensures:

- Code only compiles when Odin Inspector is installed
- Tests are skipped in environments without Odin
- No compile errors in builds without optional dependencies

---

## Inspector/Drawer-Specific Test Categories

When testing property drawers, custom inspectors, or editor tools:

| Category             | Test Scenarios                                     |
| -------------------- | -------------------------------------------------- |
| Property types       | All supported `SerializedPropertyType` values      |
| Null targets         | Null `SerializedProperty`, null `SerializedObject` |
| Missing attributes   | Fields without the target attribute                |
| Multi-object editing | Multiple selected objects with different values    |
| Nested properties    | Properties inside arrays, lists, nested classes    |
| Undo/Redo            | State preservation across undo operations          |
| Layout calculations  | Height calculations for varying content            |

---

## Related Skills

- [create-test](./create-test.md) — General test creation guidelines
- [test-unity-lifecycle](./test-unity-lifecycle.md) — Track(), DestroyImmediate, object management
- [create-editor-tool](./create-editor-tool.md) — Editor tool creation patterns
- [defensive-editor-programming](./defensive-editor-programming.md) — Editor-specific defensive patterns
