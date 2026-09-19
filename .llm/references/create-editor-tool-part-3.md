# create-editor-tool - Part 3

## Split Content

## Defensive Programming (MANDATORY)

Editor code is especially vulnerable to unexpected states. ALL editor code MUST follow [defensive-programming](../skills/defensive-programming.md).

### Editor-Specific Defensive Patterns

```csharp
// Safe SerializedProperty access
public override void OnInspectorGUI()
{
    if (serializedObject == null || serializedObject.targetObject == null)
    {
        EditorGUILayout.HelpBox("Target object is missing.", MessageType.Warning);
        return;
    }

    serializedObject.UpdateIfRequiredOrScript();

    SerializedProperty prop = serializedObject.FindProperty("_myField");
    if (prop != null)
    {
        EditorGUILayout.PropertyField(prop);
    }

    serializedObject.ApplyModifiedProperties();
}

// Safe asset operations
public static T LoadAssetSafe<T>(string path) where T : Object
{
    if (string.IsNullOrEmpty(path))
    {
        return null;
    }
    return AssetDatabase.LoadAssetAtPath<T>(path);
}
```

### Never Throw From Editor Code

- Return early for null/invalid inputs
- Use `TryXxx` patterns for failable operations
- Log warnings for debugging, don't crash the inspector
- Handle destroyed objects gracefully

---

## Post-Creation Steps (MANDATORY)

1. **Generate meta file** (required - do not skip):

   ```bash
   ./scripts/generate-meta.sh <path-to-file.cs>
   ```

   > See [create-unity-meta](../skills/create-unity-meta.md) for full details.

2. **Format code**:

   ```bash
   dotnet tool run csharpier format .
   ```

3. **Verify no errors**:
   - Check IDE for compilation errors
   - Ensure `WallstopStudios.UnityHelpers.Editor.asmdef` reference is correct

4. **Update documentation** (MANDATORY for user-facing tools):
   - Add CHANGELOG entry in `### Added` section
   - Document the tool in `docs/features/editor-tools/`
   - Add XML documentation (`///`) on public API
   - Include screenshots for UI-based tools
   - See [update-documentation](../skills/update-documentation.md) for standards

---

## File Naming Conventions

| Type           | Naming                      | Example                          |
| -------------- | --------------------------- | -------------------------------- |
| EditorWindow   | `{Name}Window.cs`           | `FitTextureSizeWindow.cs`        |
| Tool Window    | `{Name}Tool.cs`             | `ImageBlurTool.cs`               |
| PropertyDrawer | `{Attribute}Drawer.cs`      | `WNotNullPropertyDrawer.cs`      |
| Custom Editor  | `{Component}Editor.cs`      | `MatchColliderToSpriteEditor.cs` |
| Style Loader   | `{Component}StyleLoader.cs` | `WDropDownStyleLoader.cs`        |

---

## Testing Editor Tools (MANDATORY)

**All editor tools and custom inspectors MUST have exhaustive tests.** Create tests in `Tests/Editor/` mirroring the source structure.

See [create-test](../skills/create-test.md) for full testing guidelines.

### Required Test Coverage

| Category           | Test Scenarios                                      |
| ------------------ | --------------------------------------------------- |
| **Normal Cases**   | Typical usage, valid inputs, expected workflows     |
| **Negative Cases** | Invalid inputs, null values, missing dependencies   |
| **Edge Cases**     | Empty data, boundary values, unusual configurations |
| **Null Targets**   | Null `SerializedProperty`, null `SerializedObject`  |
| **Multi-Object**   | Multiple selected objects with different values     |

### Example: Data-Driven Editor Tests

```csharp
namespace WallstopStudios.UnityHelpers.Tests.Editor.Tools
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Editor.Tools;

    [TestFixture]
    public sealed class MyToolWindowTests
    {
        [Test]
        public void ShowWindowCreatesWindowInstance()
        {
            MyToolWindow window = EditorWindow.GetWindow<MyToolWindow>();

            Assert.IsTrue(window != null);

            window.Close();
        }

        private static IEnumerable<TestCaseData> InvalidInputTestCases()
        {
            yield return new TestCaseData(null).SetName("Input.Null.Handled");
            yield return new TestCaseData("").SetName("Input.Empty.Handled");
            yield return new TestCaseData("   ").SetName("Input.Whitespace.Handled");
        }

        [Test]
        [TestCaseSource(nameof(InvalidInputTestCases))]
        public void ProcessInputHandlesInvalidValues(string input)
        {
            MyToolWindow window = EditorWindow.GetWindow<MyToolWindow>();

            bool result = window.ProcessInput(input);

            Assert.IsFalse(result);
            window.Close();
        }
    }
}
```

---

## Related Skills

- [update-documentation](../skills/update-documentation.md) — **MANDATORY** after creating user-facing tools
- [create-property-drawer](../skills/create-property-drawer.md) — PropertyDrawer creation guide
- [editor-caching-patterns](../skills/editor-caching-patterns.md) — Caching and common editor patterns
- [defensive-programming](../skills/defensive-programming.md) — General defensive coding practices
- [create-test](../skills/create-test.md) — Test creation guidelines
- [test-odin-drawers](../skills/test-odin-drawers.md) — Odin Inspector drawer testing
- [editor-api-rules](../skills/editor-api-rules.md) — Forbidden Editor APIs and value handling rules
- [editor-singleton-patterns](../skills/editor-singleton-patterns.md) — Singleton asset management patterns for Editor code
