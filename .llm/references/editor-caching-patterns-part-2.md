# editor-caching-patterns - Part 2

## Split Content

## Cache Invalidation Patterns

### Target-Based Invalidation

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

### Time-Based Invalidation

```csharp
private static readonly Dictionary<string, (float timestamp, object value)> TimeCache = new();
private const float CacheLifetimeSeconds = 5f;

public static T GetCachedOrCompute<T>(string key, Func<T> compute)
{
    float now = (float)EditorApplication.timeSinceStartup;

    if (TimeCache.TryGetValue(key, out var entry))
    {
        if (now - entry.timestamp < CacheLifetimeSeconds)
        {
            return (T)entry.value;
        }
    }

    T result = compute();
    TimeCache[key] = (now, result);
    return result;
}
```

### Domain Reload Handling

Destroy owned native textures before assembly reload and on editor exit, then clear styles that
reference them. Clearing only after reload cannot reach the old domain's native resources. Route
shared caches through `EditorCacheManager`; make cleanup idempotent and preserve borrowed Unity
resources. Window-owned caches clear in `OnDisable`, which also runs when a window is destroyed.

Static caches persist across domain reloads in some configurations. Handle this:

```csharp
[InitializeOnLoadMethod]
private static void ClearCachesOnDomainReload()
{
    EditorApplication.quitting += ClearAllCaches;
    AssemblyReloadEvents.beforeAssemblyReload += ClearAllCaches;
}

private static void ClearAllCaches()
{
    HeightCache.Clear();
    TypeCache.Clear();
    // Clear other static caches
}
```

---

## USS/UXML Style Loading

Style loading should also use caching:

```csharp
namespace WallstopStudios.UnityHelpers.Editor.Styles
{
#if UNITY_EDITOR
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Core.Helper;

    public static class MyStyleLoader
    {
        private const string StylesRelativePath = "Editor/Styles/MyComponent/";
        private const string StylesFileName = "MyStyles.uss";

        private static StyleSheet _stylesStyleSheet;
        private static bool _initialized;

        public static StyleSheet Styles
        {
            get
            {
                EnsureInitialized();
                return _stylesStyleSheet;
            }
        }

        public static bool IsProSkin => EditorGUIUtility.isProSkin;

        public static void ApplyStyles(VisualElement element)
        {
            if (element == null)
            {
                return;
            }

            EnsureInitialized();

            if (_stylesStyleSheet != null)
            {
                element.styleSheets.Add(_stylesStyleSheet);
            }
        }

        private static void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;

            string stylesPath = DirectoryHelper.GetPackagePath(StylesRelativePath + StylesFileName);
            _stylesStyleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(stylesPath);
        }
    }
#endif
}
```

USS files go in `Editor/Styles/` with `.uss` extension.

---

## Common Editor Patterns

Progress bars, undo scopes, `AssetDatabase` refresh batching, drag-and-drop and suppressible
dialogs are editor interaction mechanics rather than caching, and they live in
[editor-interaction-patterns](../skills/editor-interaction-patterns.md).

---
