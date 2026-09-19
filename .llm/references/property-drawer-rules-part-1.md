# property-drawer-rules - Part 1

## Split Content

**Trigger**: When reviewing or implementing PropertyDrawer requirements, multi-object editing, or testing.

---

## Multi-Object Editing (MANDATORY)

When modifying property values via callbacks (e.g., `GenericMenu` selection), always support multi-object editing:

1. **Use `Undo.RecordObjects(serializedObject.targetObjects, ...)`** to record undo for ALL selected objects
2. **Use typed `SerializedProperty` setters** (e.g., `property.vector2Value`, `property.colorValue`) instead of reflection — these automatically handle multi-object editing through `SerializedObject.ApplyModifiedProperties()`
3. **If reflection is unavoidable**, iterate over ALL `serializedObject.targetObjects` and apply the change to each one individually

### Typed Setters vs Reflection

```csharp
// CORRECT - Uses SerializedProperty typed setters (multi-object safe)
case SerializedPropertyType.Vector2:
    if (selectedOption is Vector2 v2) property.vector2Value = v2;
    break;
case SerializedPropertyType.Color:
    if (selectedOption is Color c) property.colorValue = c;
    break;

// WRONG - Reflection on single targetObject (breaks multi-object editing)
UnityEngine.Object target = property.serializedObject.targetObject;
SetFieldValue(target, property.propertyPath, selectedOption);

// CORRECT - Reflection iterating ALL targetObjects
UnityEngine.Object[] targets = property.serializedObject.targetObjects;
for (int i = 0; i < targets.Length; i++)
{
    SetFieldValue(targets[i], property.propertyPath, selectedOption);
    EditorUtility.SetDirty(targets[i]);
}
```

---

## Multi-Object Editing Pitfalls (CRITICAL)

Multi-object editing is notoriously bug-prone. These rules prevent common issues:

### 1. Never Modify Property During Render Phase

Only modify `SerializedProperty` values in callbacks (e.g., `GenericMenu` selection) or inside `BeginChangeCheck`/`EndChangeCheck` guards, NEVER unconditionally during `OnGUI` rendering:

```csharp
// WRONG - Modifying during render causes infinite repaint loops
public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
{
    int index = CalculateIndex(property);
    if (index < 0)
    {
        property.intValue = 0; // BUG: Writing during render!
    }
}

// WRONG - Direct assignment writes every frame even without user interaction
apply.boolValue = EditorGUI.ToggleLeft(r, label, apply.boolValue);
nameProp.stringValue = EditorGUI.TextField(r, "Name", nameProp.stringValue);

// CORRECT - Only modify in callbacks
public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
{
    if (EditorGUI.DropdownButton(fieldRect, content, FocusType.Keyboard))
    {
        GenericMenu menu = new();
        menu.AddItem(new GUIContent("Option"), false, () =>
        {
            property.intValue = 0; // Safe: callback context
            property.serializedObject.ApplyModifiedProperties();
        });
        menu.DropDown(fieldRect);
    }
}

// CORRECT - Guard with change detection
EditorGUI.BeginChangeCheck();
bool newValue = EditorGUI.ToggleLeft(r, label, apply.boolValue);
if (EditorGUI.EndChangeCheck())
{
    apply.boolValue = newValue;
}
```

**Note**: `EditorGUI.PropertyField()` handles change detection internally and does not need explicit guards.

### 2. Set showMixedValue BEFORE Index Calculations

Always check `hasMultipleDifferentValues` and set `EditorGUI.showMixedValue` BEFORE calculating display indices:

```csharp
// CORRECT - Check mixed state first
bool isMixed = property.hasMultipleDifferentValues;
EditorGUI.showMixedValue = isMixed;

// Now calculate index (may be invalid for some objects when mixed)
int currentIndex = GetCurrentIndex(property);
string displayText = isMixed ? "\u2014" : GetDisplayText(currentIndex);
```

### 3. Use Em Dash for Mixed Values

Display `"\u2014"` (em dash) when values differ across selected objects. This is the standard Unity convention:

```csharp
string GetDisplayValue(SerializedProperty property, string[] options)
{
    if (property.hasMultipleDifferentValues)
    {
        return "\u2014"; // Em dash for mixed values
    }
    int index = property.intValue;
    return IsValidIndex(index, options) ? options[index] : $"{index} (Invalid)";
}
```

### 4. Show "(Invalid)" for Out-of-Range Values

Never silently clamp invalid indices. Show the invalid state clearly:

```csharp
// WRONG - Silent clamping hides data corruption
int safeIndex = Mathf.Clamp(property.intValue, 0, options.Length - 1);

// CORRECT - Show invalid state
int index = property.intValue;
if (index < 0 || index >= options.Length)
{
    displayText = $"{index} (Invalid)";
}
```

### 5. Guard GenericMenu `isSelected` and Popup `SelectedIndex` for Mixed Values

When building `GenericMenu` items, guard the `isSelected` flag with `!property.hasMultipleDifferentValues` to prevent misleading checkmarks in multi-object editing mode:

```csharp
// CORRECT - No checkmark when values differ across selected objects
bool isSelected = i == currentIndex && !property.hasMultipleDifferentValues;
menu.AddItem(new GUIContent(label), isSelected, callback);

// WRONG - Shows a checkmark based on one object's value even when mixed
bool isSelected = i == currentIndex;
```

Similarly, popup window `SelectedIndex` should be set to `-1` when mixed:

```csharp
// CORRECT - Popup highlights nothing when mixed
SelectedIndex = property.hasMultipleDifferentValues ? -1 : currentIndex,

// WRONG - Highlights an index that only applies to one of the selected objects
SelectedIndex = currentIndex,
```

### 6. Undo.RecordObjects Pattern for Multi-Object

When modifying via reflection or direct field access, record undo for ALL targets:

```csharp
void ApplySelection(SerializedProperty property, object newValue)
{
    // Record undo for all selected objects
    Undo.RecordObjects(property.serializedObject.targetObjects, "Change Value");

    // Apply to all targets
    foreach (var target in property.serializedObject.targetObjects)
    {
        SetFieldValue(target, property.propertyPath, newValue);
        EditorUtility.SetDirty(target);
    }

    Undo.FlushUndoRecordObjects();
    property.serializedObject.Update();
}
```
