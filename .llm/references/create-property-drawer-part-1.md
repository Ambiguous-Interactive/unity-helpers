# create-property-drawer - Part 1

## Split Content

**Trigger**: When creating Unity PropertyDrawers for custom attributes or field types in this repository.

---

## File Location

All PropertyDrawers MUST go in `Editor/CustomDrawers/`.

> Editor code cannot reference Runtime code unless via assembly definition references.

---

## PropertyDrawer Template

See full template: [code-samples/property-drawer-template.cs](../skills/code-samples/property-drawer-template.cs)

> **Note**: The inline template below shows IMGUI-only implementation. The full template file adds:
>
> - `UnityEditor.UIElements` and `UnityEngine.UIElements` imports
> - `CreatePropertyGUI(SerializedProperty)` method for UI Toolkit support

```csharp
namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    [CustomPropertyDrawer(typeof(MyAttribute))]
    public sealed class MyAttributePropertyDrawer : PropertyDrawer
    {
        private static readonly Dictionary<string, float> HeightCache = new(StringComparer.Ordinal);
        private static readonly GUIContent ReusableContent = new();

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float baseHeight = EditorGUI.GetPropertyHeight(property, label, true);
            return baseHeight;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            try
            {
                EditorGUI.PropertyField(position, property, label, true);
            }
            finally
            {
                EditorGUI.EndProperty();
            }
        }
    }
#endif
}
```

---

## Key PropertyDrawer Patterns

1. **Caching**: Use static `Dictionary` caches for height calculations - drawers run every frame
2. **Reusable GUIContent**: Create static `GUIContent` instances to reduce allocations
3. **BeginProperty/EndProperty**: Always wrap drawing in `EditorGUI.BeginProperty` / `EndProperty`
4. **Height Calculation**: Override `GetPropertyHeight` if adding custom elements

---

## Caching Requirements (CRITICAL)

PropertyDrawers run **every frame**. Minimize allocations:

```csharp
// CORRECT - Static caches
private static readonly Dictionary<string, float> HeightCache = new(StringComparer.Ordinal);
private static readonly GUIContent ReusableContent = new();

// CORRECT - Pool expensive objects
private static readonly WallstopGenericPool<List<string>> ListPool = new(
    () => new List<string>(),
    onRelease: list => list.Clear()
);

// INCORRECT - Allocates every frame
public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
{
    GUIContent content = new GUIContent("Label"); // Allocation!
    List<string> items = new List<string>();       // Allocation!
}
```

### Shared Caches (DRY Principle)

When multiple drawers need the same cache, use `EditorCacheHelper`:

```csharp
// CORRECT - Use shared cache helper
using WallstopStudios.UnityHelpers.Editor.Core.Helper;

// In any drawer:
string display = EditorCacheHelper.GetCachedIntString(index);
string pageLabel = EditorCacheHelper.GetPaginationLabel(page, total);
Texture2D solidTex = EditorCacheHelper.GetSolidTexture(color);

// INCORRECT - Duplicating caches across multiple files
// Drawer1.cs
private static readonly Dictionary<int, string> IntToStringCache = new();

// Drawer2.cs (duplicate!)
private static readonly Dictionary<int, string> IntToStringCache = new();
```

See [editor-caching-patterns](../skills/editor-caching-patterns.md) for complete caching guidance.

---

## SerializedProperty Patterns

### Common Property Operations

```csharp
// Get property
SerializedProperty property = serializedObject.FindProperty("_fieldName");

// Array operations
SerializedProperty arrayProp = serializedObject.FindProperty("_items");
int arraySize = arrayProp.arraySize;
SerializedProperty element = arrayProp.GetArrayElementAtIndex(0);
arrayProp.InsertArrayElementAtIndex(arraySize);
arrayProp.DeleteArrayElementAtIndex(0);

// Nested properties
SerializedProperty nested = property.FindPropertyRelative("nestedField");

// Iterate children
SerializedProperty iterator = property.Copy();
SerializedProperty endProperty = iterator.GetEndProperty();
while (iterator.NextVisible(true) && !SerializedProperty.EqualContents(iterator, endProperty))
{
    // Process each visible property
}

// Apply changes
serializedObject.ApplyModifiedProperties();
```

### Property Types

```csharp
switch (property.propertyType)
{
    case SerializedPropertyType.Integer:
        int intValue = property.intValue;
        break;
    case SerializedPropertyType.Float:
        float floatValue = property.floatValue;
        break;
    case SerializedPropertyType.String:
        string stringValue = property.stringValue;
        break;
    case SerializedPropertyType.Boolean:
        bool boolValue = property.boolValue;
        break;
    case SerializedPropertyType.ObjectReference:
        Object objValue = property.objectReferenceValue;
        break;
    case SerializedPropertyType.Enum:
        int enumIndex = property.enumValueIndex;
        break;
}
```

---
