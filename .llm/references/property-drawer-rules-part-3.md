# property-drawer-rules - Part 3

## Split Content

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

---

## File Naming Conventions

| Type           | Naming                 | Example                     |
| -------------- | ---------------------- | --------------------------- |
| PropertyDrawer | `{Attribute}Drawer.cs` | `WNotNullPropertyDrawer.cs` |

---

## Testing PropertyDrawers (MANDATORY)

**All PropertyDrawers MUST have exhaustive tests.** Create tests in `Tests/Editor/` mirroring the source structure.

See [create-test](../skills/create-test.md) for full testing guidelines.

### Required Test Coverage

| Category           | Test Scenarios                                      |
| ------------------ | --------------------------------------------------- |
| **Normal Cases**   | Typical usage, valid inputs, expected workflows     |
| **Negative Cases** | Invalid inputs, null values, missing dependencies   |
| **Edge Cases**     | Empty data, boundary values, unusual configurations |
| **Property Types** | All supported `SerializedPropertyType` values       |
| **Null Targets**   | Null `SerializedProperty`, null `SerializedObject`  |
| **Multi-Object**   | Multiple selected objects with different values     |

---

## Related Skills

- [create-property-drawer](../skills/create-property-drawer.md) - Main PropertyDrawer creation guide
- [property-drawer-examples](../skills/property-drawer-examples.md) - Dropdown and foldout examples
- [create-test](../skills/create-test.md) - Test creation guidelines
- [test-odin-drawers](../skills/test-odin-drawers.md) - Odin Inspector drawer testing
- [defensive-editor-programming](../skills/defensive-editor-programming.md) - Editor defensive coding patterns
- [defensive-programming](../skills/defensive-programming.md) - General defensive coding practices
- [editor-multi-object-editing](../skills/editor-multi-object-editing.md) - Multi-object editing patterns and undo support
