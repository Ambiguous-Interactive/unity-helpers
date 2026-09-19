# test-odin-drawers - Part 1

## Split Content

**Trigger**: When testing Odin `OdinAttributeDrawer` implementations in this repository.

---

## When to Use

Use this skill when:

- Creating tests for Odin Inspector drawer implementations
- Testing attributes that work with `SerializedMonoBehaviour` or `SerializedScriptableObject`
- Adding test coverage for Odin-specific functionality
- Verifying drawer behavior with Odin's property system

For general test creation, see [create-test](../skills/create-test.md).
For Unity object lifecycle management, see [test-unity-lifecycle](../skills/test-unity-lifecycle.md).

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
