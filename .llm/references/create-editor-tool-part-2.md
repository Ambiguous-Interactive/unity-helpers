# create-editor-tool - Part 2

## Split Content

## Custom Inspector (Editor) Template

```csharp
namespace WallstopStudios.UnityHelpers.Editor.CustomEditors
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Utils;

    [CustomEditor(typeof(MyComponent))]
    [CanEditMultipleObjects]
    public sealed class MyComponentEditor : Editor
    {
        private static readonly WallstopGenericPool<
            Dictionary<string, SerializedProperty>
        > PropertyLookupPool = new(
            () => new Dictionary<string, SerializedProperty>(16, StringComparer.Ordinal),
            onRelease: d => d.Clear()
        );

        public override void OnInspectorGUI()
        {
            serializedObject.UpdateIfRequiredOrScript();

            // Draw default script field (disabled)
            SerializedProperty scriptProperty = serializedObject.FindProperty("m_Script");
            if (scriptProperty != null)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.PropertyField(scriptProperty, true);
                }
                EditorGUILayout.Space();
            }

            // Draw custom properties
            SerializedProperty myProperty = serializedObject.FindProperty("_myField");
            if (myProperty != null)
            {
                EditorGUILayout.PropertyField(myProperty);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}
```

### Key Custom Inspector Patterns

1. **Multi-Object Editing**: Include `[CanEditMultipleObjects]` for selection support
2. **Script Field**: Draw `m_Script` property disabled at top
3. **Property Pooling**: Use `WallstopGenericPool` for dictionary caches in performance-sensitive inspectors
4. **Update/Apply Cycle**: Always call `UpdateIfRequiredOrScript()` and `ApplyModifiedProperties()`

---

## Caching Requirements (CRITICAL)

Inspectors run **every frame**. See [editor-caching-patterns](../skills/editor-caching-patterns.md) for complete guidance.

Quick reference:

```csharp
// CORRECT - Static caches
private static readonly Dictionary<string, float> HeightCache = new(StringComparer.Ordinal);
private static readonly GUIContent ReusableContent = new();

// CORRECT - Use shared cache helper
using WallstopStudios.UnityHelpers.Editor.Core.Helper;
string display = EditorCacheHelper.GetCachedIntString(index);

// INCORRECT - Allocates every frame
public override void OnInspectorGUI()
{
    GUIContent content = new GUIContent("Label"); // Allocation!
}
```

---

## Critical Rules

### 1. `#if UNITY_EDITOR` Wrapping

Wrap all editor code after namespace declaration:

```csharp
namespace WallstopStudios.UnityHelpers.Editor.Tools
{
#if UNITY_EDITOR
    // All code here
#endif
}
```

### 2. `using` Directives INSIDE Namespace

```csharp
namespace WallstopStudios.UnityHelpers.Editor.Tools
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

Editor tools should be `sealed` unless designed for inheritance:

```csharp
public sealed class MyToolWindow : EditorWindow { }
```

---
