# property-drawer-rules - Part 2

## Split Content

### 7. Odin Drawer Mixed Value Detection

Odin drawers do NOT have `hasMultipleDifferentValues`. Manually check:

```csharp
// In OdinAttributeDrawer
bool HasMixedValues<T>(IPropertyValueEntry<T> valueEntry)
{
    if (valueEntry.ValueCount <= 1)
        return false;

    T firstValue = valueEntry.WeakValues[0] as T;
    for (int i = 1; i < valueEntry.ValueCount; i++)
    {
        T currentValue = valueEntry.WeakValues[i] as T;
        if (!EqualityComparer<T>.Default.Equals(firstValue, currentValue))
            return true;
    }
    return false;
}

// Usage
bool isMixed = HasMixedValues(Property.ValueEntry);
EditorGUI.showMixedValue = isMixed;
```

### 8. UI Toolkit Elements Need Same Handling

UI Toolkit `VisualElement`-based drawers require the same patterns:

```csharp
public override VisualElement CreatePropertyGUI(SerializedProperty property)
{
    var dropdown = new DropdownField();

    // Bind with mixed value support
    dropdown.RegisterCallback<AttachToPanelEvent>(evt =>
    {
        UpdateDropdownDisplay(dropdown, property);
    });

    return dropdown;
}

void UpdateDropdownDisplay(DropdownField dropdown, SerializedProperty property)
{
    if (property.hasMultipleDifferentValues)
    {
        dropdown.SetValueWithoutNotify("\u2014");
        return;
    }
    // Normal display logic
}
```

### 9. Default Field Values Must Not Collide with Sentinel Values

When a dropdown drawer uses a sentinel value (e.g., empty string for "Custom" mode), the data class field must NOT default to that sentinel. Otherwise, new entries will appear in the sentinel state (e.g., "Custom") instead of a sensible known option, and APIs that skip sentinel-valued entries will silently ignore new entries:

```csharp
// WRONG - Default collides with Custom sentinel
public string platformName = string.Empty; // Custom sentinel is also empty string
// New entries render as "Custom" and are skipped by the API

// CORRECT - Default is a known valid value; sentinel is distinct
public string platformName = TexturePlatformNameHelper.DefaultPlatformName;
// New entries render as "DefaultTexturePlatform" and are processed by the API
// Custom sentinel (empty string) is only set when user explicitly selects "Custom"
```

**Rule**: Define constants for sentinel values and ensure default field values use a known valid option. When the drawer maps `string.Empty` to "Custom", the field must default to a concrete platform or option name.

### 10. Reuse GUIContent in OnGUI — Never Allocate Per Frame

`OnGUI` runs every frame. Allocating `new GUIContent(...)` inside `OnGUI` or `DrawPropertyLayout` creates avoidable GC pressure. Reuse a static `GUIContent` instance and update its `.text`/`.tooltip` before each use:

```csharp
// WRONG - Allocates GUIContent every frame
public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
{
    GUIContent buttonContent = new(displayValue); // Allocation!
    EditorGUI.DropdownButton(fieldRect, buttonContent, FocusType.Keyboard);
}

// CORRECT - Reuse static instance
private static readonly GUIContent ReusableDropDownButtonContent = new();

public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
{
    ReusableDropDownButtonContent.text = displayValue;
    ReusableDropDownButtonContent.tooltip = string.Empty;
    EditorGUI.DropdownButton(fieldRect, ReusableDropDownButtonContent, FocusType.Keyboard);
}
```

**Exception**: `GenericMenu.AddItem(new GUIContent(...), ...)` allocations are acceptable because they only execute on user click (not every frame) and `GenericMenu` stores references internally, so a single instance cannot be reused across multiple `AddItem` calls.

---

## Standard and Odin Drawer Consistency (MANDATORY)

When this package has BOTH a standard `PropertyDrawer` AND an Odin `OdinAttributeDrawer` for the same attribute:

1. **Both drawers MUST have the same rendering behavior** — if one uses `GenericMenu`, the other must too
2. **Both drawers MUST use the same dropdown implementation** — never mix `EditorGUI.Popup` in one and `GenericMenu` in another
3. **CHANGELOG entries MUST accurately reflect which drawers were changed** — never claim all variants were updated if only some were
4. **When fixing a rendering bug in one variant, fix ALL variants** — standard drawer, Odin drawer, and their popup/inline code paths

---

## Critical Rules

### 1. `#if UNITY_EDITOR` Wrapping

Wrap all editor code after namespace declaration:

```csharp
namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
#if UNITY_EDITOR
    // All code here
#endif
}
```

### 2. `using` Directives INSIDE Namespace

```csharp
namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
#if UNITY_EDITOR
    using System;
    using UnityEditor;
    using UnityEngine;
    // ...
#endif
}
```

### 3. Qualify `Object` References

```csharp
using Object = UnityEngine.Object;
```

### 4. Unity Object Null Checks

```csharp
// CORRECT
if (targetObject != null)
{
    // Use targetObject
}

// INCORRECT
if (targetObject?.name != null) // Bypasses Unity null check
```

### 5. Sealed Classes

PropertyDrawers should be `sealed` unless designed for inheritance:

```csharp
public sealed class MyAttributePropertyDrawer : PropertyDrawer { }
```

---
