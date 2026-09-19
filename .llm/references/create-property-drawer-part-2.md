# create-property-drawer - Part 2

## Split Content

## Defensive Programming (MANDATORY)

PropertyDrawers are especially vulnerable to unexpected states. ALL drawer code MUST follow [defensive-programming](../skills/defensive-programming.md).

### Safe SerializedProperty Access

```csharp
// Safe OnGUI implementation
public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
{
    if (property == null)
    {
        return;
    }

    if (property.serializedObject == null || property.serializedObject.targetObject == null)
    {
        EditorGUI.LabelField(position, label, new GUIContent("(Missing Object)"));
        return;
    }

    EditorGUI.BeginProperty(position, label, property);
    try
    {
        // Draw property safely...
    }
    finally
    {
        EditorGUI.EndProperty();
    }
}
```

### Cache Invalidation on Target Change

```csharp
private Object _lastTarget;
private SerializedProperty _cachedProperty;

private SerializedProperty GetProperty(SerializedObject so)
{
    if (so == null || so.targetObject == null)
    {
        _cachedProperty = null;
        _lastTarget = null;
        return null;
    }

    if (_lastTarget != so.targetObject)
    {
        _cachedProperty = null;
        _lastTarget = so.targetObject;
    }

    if (_cachedProperty == null)
    {
        _cachedProperty = so.FindProperty("_fieldName");
    }

    return _cachedProperty;
}
```

### Never Throw From Drawer Code

- Return early for null/invalid inputs
- Use `TryXxx` patterns for failable operations
- Log warnings for debugging, don't crash the inspector
- Handle destroyed objects gracefully

---

## Related Skills

- [property-drawer-examples](../skills/property-drawer-examples.md) - Dropdown and foldout drawer examples
- [property-drawer-rules](../skills/property-drawer-rules.md) - Critical rules, multi-object editing, testing
- [create-editor-tool](../skills/create-editor-tool.md) - EditorWindows and Custom Inspectors
- [editor-caching-patterns](../skills/editor-caching-patterns.md) - Editor caching and common patterns
- [defensive-programming](../skills/defensive-programming.md) - General defensive coding practices
- [create-test](../skills/create-test.md) - Test creation guidelines
- [test-odin-drawers](../skills/test-odin-drawers.md) - Odin Inspector drawer testing
- [editor-multi-object-editing](../skills/editor-multi-object-editing.md) - Multi-object editing patterns and undo support
- [editor-api-rules](../skills/editor-api-rules.md) - Forbidden Editor APIs and value handling rules
