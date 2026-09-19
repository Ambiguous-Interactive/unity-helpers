# test-odin-drawers - Part 3

## Split Content

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

- [create-test](../skills/create-test.md) — General test creation guidelines
- [test-unity-lifecycle](../skills/test-unity-lifecycle.md) — Track(), DestroyImmediate, object management
- [create-editor-tool](../skills/create-editor-tool.md) — Editor tool creation patterns
- [defensive-editor-programming](../skills/defensive-editor-programming.md) — Editor-specific defensive patterns
