# editor-caching-patterns - Part 3

## Split Content

## Performance Profiling Tips

### Measuring Cache Effectiveness

```csharp
#if UNITY_EDITOR && DEVELOPMENT_BUILD
private static int _cacheHits;
private static int _cacheMisses;

public static void LogCacheStats()
{
    float hitRate = _cacheHits / (float)(_cacheHits + _cacheMisses) * 100;
    Debug.Log($"Cache hit rate: {hitRate:F1}% ({_cacheHits} hits, {_cacheMisses} misses)");
}
#endif
```

### Using Unity Profiler

```csharp
using Unity.Profiling;

private static readonly ProfilerMarker DrawerMarker = new("MyDrawer.OnGUI");

public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
{
    using (DrawerMarker.Auto())
    {
        // Drawing code
    }
}
```

---

## Related Skills

- [editor-interaction-patterns](../skills/editor-interaction-patterns.md) - Progress, undo, dialogs, drag-drop
- [create-editor-tool](../skills/create-editor-tool.md) - EditorWindows and Custom Inspectors
- [create-property-drawer](../skills/create-property-drawer.md) - PropertyDrawer creation
- [high-performance-csharp](../skills/high-performance-csharp.md) - General performance patterns
- [use-pooling](../skills/use-pooling.md) - Object pooling strategies
- [memory-allocation-traps](../skills/memory-allocation-traps.md) - Common allocation pitfalls
