# Skill: Editor API Rules

<!-- trigger: forbidden api, EditorGUI.Popup, IMGUI, UI Toolkit, string serialization, display label | Forbidden Editor APIs and value handling rules | Core -->

**Trigger**: When using Unity Editor APIs for rendering, value handling, or string serialization in editor code.

---

## When to Use

- Choosing between IMGUI and UI Toolkit rendering approaches
- Working with `EditorGUI` APIs for custom rendering
- Handling display labels and fallback values
- Reading or writing string values through `SerializedProperty`

## When NOT to Use

- Runtime code that doesn't use Editor APIs
- Code that only uses `EditorGUI.PropertyField()` (already safe)

---

## Forbidden Editor APIs

### EditorGUI.Popup - NEVER USE

`EditorGUI.Popup` renders phantom empty rows on Linux when the selected index is -1. Always use `EditorGUI.DropdownButton` + `GenericMenu` instead. This applies to ALL drawers - standard PropertyDrawers and Odin OdinAttributeDrawers.

```csharp
// FORBIDDEN - Phantom rows on Linux with index -1
int newIndex = EditorGUI.Popup(position, label.text, currentIndex, displayOptions);

// CORRECT - GenericMenu-based dropdown
Rect fieldRect = EditorGUI.PrefixLabel(position, label);
if (EditorGUI.DropdownButton(fieldRect, buttonContent, FocusType.Keyboard))
{
    GenericMenu menu = new();
    for (int i = 0; i < options.Length; i++)
    {
        int captured = i;
        menu.AddItem(new GUIContent(options[i]), i == currentIndex,
            () => ApplySelection(captured));
    }
    menu.DropDown(fieldRect);
}
```

---

## Consistent Display Label Normalization

When editor UI renders a fallback label (e.g., `(Option N)` for empty strings), **every** code path that references that label must apply the same normalization - not just rendering, but also search, filter, suggestion, and selection. Centralize the fallback in a single helper (e.g., `GetNormalizedDisplayLabel`) and never use the raw label in comparison paths.

**Rule:** If any code path transforms a value for display, audit all other paths that read the same value and apply the identical transform. This covers rendering, search/filter, autocomplete/suggestion, selection/resolution, and keyboard navigation labels.

---

## IMGUI vs UI Toolkit Value Handling

PropertyDrawers behave differently depending on which rendering path is used. Understanding these differences prevents unexpected value modifications:

### OnGUI (IMGUI Path)

- **Display-only**: Invalid values are shown as "(Invalid)" but are NOT modified
- **No clamping**: Out-of-range values remain unchanged during render
- **Explicit user action required**: Values only change when user makes a selection
- **Rationale**: IMGUI renders every frame; modifying values during render causes infinite repaint loops

```csharp
// OnGUI renders but does NOT modify invalid values
public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
{
    int currentValue = property.intValue;
    int index = Array.IndexOf(options, currentValue);

    // Display "(Invalid)" for out-of-range, but DO NOT clamp
    string display = index >= 0 ? options[index].ToString() : $"{currentValue} (Invalid)";

    // Value only changes via user selection callback, never during render
}
```

### CreatePropertyGUI (UI Toolkit Path)

- **Clamps on initialization**: Invalid values are clamped to the first valid option
- **Binding-driven**: UI Toolkit uses bindings that sync values immediately
- **One-time setup**: CreatePropertyGUI runs once, not every frame
- **Rationale**: UI Toolkit binding model expects valid values for proper field state

```csharp
// CreatePropertyGUI clamps invalid values during initialization
public override VisualElement CreatePropertyGUI(SerializedProperty property)
{
    int currentValue = property.intValue;
    int index = Array.IndexOf(options, currentValue);

    // Clamp to first option if invalid
    if (index < 0 && options.Length > 0)
    {
        property.intValue = options[0];
        property.serializedObject.ApplyModifiedProperties();
    }

    return CreateDropdownField(property);
}
```

### Summary Table

| Aspect              | OnGUI (IMGUI)            | CreatePropertyGUI (UI Toolkit) |
| ------------------- | ------------------------ | ------------------------------ |
| Invalid value       | Display "(Invalid)"      | Clamp to first option          |
| Modification timing | Only on user selection   | On initialization              |
| Render frequency    | Every frame              | Once (binding-driven updates)  |
| Value preservation  | Preserves invalid values | Normalizes to valid values     |

---

## Forbidden during AssetPostprocessor callbacks

`AssetPostprocessor` callbacks (`OnPostprocessAllAssets`, `OnPreprocessTexture`, `OnPostprocessPrefab`, etc.) run inside Unity's asset-import phase. While the import is active, `SendMessage` is forbidden — but Unity's own sprite/renderer lifecycle relays (`OnSpriteRendererBoundsChanged`, `OnSpriteTilingPropertyChange`, `OnValidate`, etc.) use `SendMessage` when we force deserialization or component inspection.

Any of the following called synchronously from an AssetPostprocessor callback will produce

> `SendMessage cannot be called during Awake, CheckConsistency, or OnValidate`

warnings in the Unity console (see [#234](https://github.com/wallstop/unity-helpers/pull/234)):

- `AssetDatabase.LoadAssetAtPath` / `LoadAllAssetsAtPath` / `LoadMainAssetAtPath`
- `GetComponentsInChildren` / `GetComponents<T>` on loaded prefabs or scene roots
- `AddComponent` / `Instantiate` / `DestroyImmediate`
- `MethodInfo.Invoke` dispatching user callbacks

**Rule:** AssetPostprocessor callbacks must only enqueue work. Route the actual work through [AssetPostprocessorDeferral.Schedule](../../Editor/AssetProcessors/AssetPostprocessorDeferral.cs) so it runs one editor tick later, after the import phase exits. See [asset-postprocessor-safety](./asset-postprocessor-safety.md) for the full pattern and hygiene test recipe.

---

## Unity String Serialization: Null-to-Empty Conversion

**CRITICAL**: `SerializedProperty.stringValue` **always converts null strings to `""`**. This is a fundamental Unity serialization behavior that affects all property drawers and editor code.

| Anti-Pattern                                            | Correct Pattern                                                            |
| ------------------------------------------------------- | -------------------------------------------------------------------------- |
| `if (prop.stringValue == null)`                         | Never true — `stringValue` returns `""` for null                           |
| `prop.stringValue ?? string.Empty`                      | Redundant — `stringValue` already returns `""`                             |
| Separate null vs empty logic in GetPropertyHeight/OnGUI | Treat null and empty as equivalent when reading through SerializedProperty |
| Tests asserting null survives serialization             | Null becomes `""` through `SerializedObject.Update()`                      |

**Impact on property drawers**: Any code path that checks `stringValue == null` is dead code. When writing null-check logic for strings read from SerializedProperty, you must treat null and `""` as equivalent. If your code distinguishes between null and `""` for string fields, the null branch is unreachable through property drawers.

**Impact on tests**: When a test sets a backing field to `null` and calls `_serializedObject.Update()`, the serialized value becomes `""`. Tests must account for this — null is NOT distinguishable from empty through SerializedProperty.

---

## Related Skills

- [asset-postprocessor-safety](./asset-postprocessor-safety.md) - AssetPostprocessor import-phase rules (avoiding SendMessage warnings)
- [defensive-editor-programming](./defensive-editor-programming.md) - Overview of all defensive editor patterns
- [editor-multi-object-editing](./editor-multi-object-editing.md) - Multi-object editing and undo patterns
- [create-property-drawer](./create-property-drawer.md) - PropertyDrawer creation patterns
- [editor-singleton-patterns](./editor-singleton-patterns.md) - Singleton asset management patterns
