# create-editor-tool - Part 1

## Split Content

**Trigger**: When creating Unity Editor GUI tools, windows, or custom inspectors in this repository.

> For PropertyDrawers, see [create-property-drawer](../skills/create-property-drawer.md).
> For caching patterns, see [editor-caching-patterns](../skills/editor-caching-patterns.md).

---

## File Location Rules

All editor tools MUST go in the `Editor/` folder tree:

- Editor windows: `Editor/` or `Editor/Tools/`
- Property drawers: `Editor/CustomDrawers/`
- Custom inspectors: `Editor/CustomEditors/`
- Style loaders: `Editor/Styles/`

> Editor code cannot reference Runtime code unless via assembly definition references.

---

## EditorWindow Template

```csharp
namespace WallstopStudios.UnityHelpers.Editor.Tools
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using Object = UnityEngine.Object;

    public sealed class MyToolWindow : EditorWindow
    {
        private static bool SuppressUserPrompts { get; set; }

        static MyToolWindow()
        {
            try
            {
                if (Application.isBatchMode || IsInvokedByTestRunner())
                {
                    SuppressUserPrompts = true;
                }
            }
            catch { }
        }

        private static bool IsInvokedByTestRunner()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; ++i)
            {
                string a = args[i];
                if (
                    a.IndexOf("runTests", StringComparison.OrdinalIgnoreCase) >= 0
                    || a.IndexOf("testResults", StringComparison.OrdinalIgnoreCase) >= 0
                    || a.IndexOf("testPlatform", StringComparison.OrdinalIgnoreCase) >= 0
                )
                {
                    return true;
                }
            }
            return false;
        }

        [SerializeField]
        internal List<Object> _targetPaths = new();

        private Vector2 _scrollPosition;
        private SerializedObject _serializedObject;
        private SerializedProperty _targetPathsProperty;

        [MenuItem("Tools/Wallstop Studios/Unity Helpers/My Tool", priority = -1)]
        public static void ShowWindow()
        {
            GetWindow<MyToolWindow>("My Tool");
        }

        private void OnEnable()
        {
            _serializedObject = new SerializedObject(this);
            _targetPathsProperty = _serializedObject.FindProperty(nameof(_targetPaths));
        }

        private void OnGUI()
        {
            _serializedObject.Update();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            try
            {
                EditorGUILayout.LabelField("My Tool Settings", EditorStyles.boldLabel);
                EditorGUILayout.Space();

                EditorGUILayout.PropertyField(_targetPathsProperty, new GUIContent("Target Paths"));

                EditorGUILayout.Space();
                if (GUILayout.Button("Run", GUILayout.Height(30)))
                {
                    RunTool();
                }
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }

            _serializedObject.ApplyModifiedProperties();
        }

        private void RunTool()
        {
            // Implementation
        }
    }
#endif
}
```

### Key EditorWindow Patterns

1. **Test Runner Detection**: Include `SuppressUserPrompts` and `IsInvokedByTestRunner()` for batch mode compatibility
2. **SerializedObject for Window State**: Use `SerializedObject(this)` and `SerializedProperty` for persistent window state
3. **Scroll View**: Wrap content in `EditorGUILayout.BeginScrollView` / `EndScrollView`
4. **Menu Registration**: Use `[MenuItem("Tools/Wallstop Studios/Unity Helpers/...")]` with `priority = -1`

---
