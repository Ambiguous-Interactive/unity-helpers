# Asset Change Detection

**Automatically respond to asset creation and deletion events.**

The `[DetectAssetChanged]` attribute allows you to annotate methods that should execute automatically when specific asset types are created or deleted in the Unity Editor. Perfect for cache invalidation, autoconfiguration, validation, and maintaining derived data.

---

## Table of Contents

- [Basic Usage](#basic-usage)
- [Attribute Parameters](#attribute-parameters)
- [Method Signatures](#method-signatures)
- [Inheritance Support](#inheritance-support)
- [Asset Change Context](#asset-change-context)
- [Best Practices](#best-practices)
- [Examples](#examples)

---

## Basic Usage

```csharp
using System.Collections.Generic;
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.Attributes;
using WallstopStudios.UnityHelpers.Core.Extension;

public class SpriteCache : ScriptableObject
{
    private static readonly HashSet<string> TrackedSpritePaths = new();

    [DetectAssetChanged(
        typeof(Sprite),
        AssetChangeFlags.Created | AssetChangeFlags.Deleted
    )]
    private static void OnSpriteChanged(AssetChangeContext context)
    {
        foreach (string path in context.CreatedAssetPaths)
        {
            TrackedSpritePaths.Add(path);
            Debug.Log($"New sprite added: {path}");
        }

        foreach (string path in context.DeletedAssetPaths)
        {
            TrackedSpritePaths.Remove(path);
            Debug.Log($"Sprite removed: {path}");
        }
    }
}
```

> **Visual Reference**
>
> ![Asset change detection workflow diagram](../../images/editor-tools/asset-change-detection-flow.gif)
> _Automatic method invocation when assets are created or deleted_

---

## Attribute Parameters

```csharp
[DetectAssetChanged(
    Type assetType,                          // Type of asset to monitor (required)
    AssetChangeFlags flags,                  // Created, Deleted, or both (required)
    DetectAssetChangedOptions options = None // IncludeAssignableTypes for inheritance
)]
```

### AssetChangeFlags

```csharp
[Flags]
public enum AssetChangeFlags
{
    None = 0,
    Created = 1 << 0,     // Trigger on asset creation
    Deleted = 1 << 1,     // Trigger on asset deletion
}
```

### DetectAssetChangedOptions

```csharp
[Flags]
public enum DetectAssetChangedOptions
{
    None = 0,
    IncludeAssignableTypes = 1 << 0,  // Also trigger for derived types
    SearchPrefabs = 1 << 1,           // Search prefabs for MonoBehaviour handlers
    SearchSceneObjects = 1 << 2,      // Search open scenes for MonoBehaviour handlers
}
```

> **Important:** `SearchPrefabs` and `SearchSceneObjects` are only applicable to **instance methods** on **MonoBehaviour** classes. Static methods work without these options.

---

## Method Signatures

The attribute supports three method signatures:

### 1. No Parameters (Fire-and-Forget)

```csharp
[DetectAssetChanged(typeof(ScriptableObject), AssetChangeFlags.Created)]
private static void OnScriptableObjectCreated()
{
    Debug.Log("A ScriptableObject was created - invalidate cache");
}
```

**When to use:** Simple cache invalidation that doesn't need asset details

---

### 2. Full Context (Recommended)

```csharp
[DetectAssetChanged(typeof(AudioClip), AssetChangeFlags.Created | AssetChangeFlags.Deleted)]
private static void OnAudioClipChanged(AssetChangeContext context)
{
    Debug.Log($"AudioClip change: {context.Flags}");

    foreach (string path in context.CreatedAssetPaths)
    {
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        ProcessAudioClip(clip);
    }

    foreach (string path in context.DeletedAssetPaths)
    {
        Debug.Log($"AudioClip deleted: {path}");
    }
}
```

**When to use:** Need to handle both creation and deletion, or need access to all changed paths

---

### 3. Typed Arrays (Advanced)

```csharp
[DetectAssetChanged(typeof(Material), AssetChangeFlags.Created | AssetChangeFlags.Deleted)]
private static void OnMaterialChanged(Material[] createdMaterials, string[] deletedPaths)
{
    foreach (Material mat in createdMaterials)
    {
        Debug.Log($"Material created: {mat.name}");
        ValidateMaterial(mat);
    }

    foreach (string path in deletedPaths)
    {
        Debug.Log($"Material deleted: {path}");
    }
}
```

**When to use:** Need strongly-typed access to created assets; deleted assets are always paths since the asset no longer exists

---

## Inheritance Support

By default, the attribute only triggers for exact type matches. Use `IncludeAssignableTypes` to include derived types:

```csharp
// Triggers for ScriptableObject and ALL derived types
[DetectAssetChanged(
    typeof(ScriptableObject),
    AssetChangeFlags.Created,
    DetectAssetChangedOptions.IncludeAssignableTypes
)]
private static void OnAnyScriptableObjectCreated(ScriptableObject obj)
{
    Debug.Log($"ScriptableObject created: {obj.GetType().Name}");
}

// Only triggers for exact Material type (not derived classes)
[DetectAssetChanged(typeof(Material), AssetChangeFlags.Created)]
private static void OnExactMaterialCreated(Material mat)
{
    Debug.Log("Material (exact type) created");
}
```

---

## Asset Change Context

The `AssetChangeContext` class provides complete information about the change:

```csharp
public sealed class AssetChangeContext
{
    public Type AssetType { get; }                      // The type being watched
    public AssetChangeFlags Flags { get; }              // Created, Deleted, or both
    public IReadOnlyList<string> CreatedAssetPaths { get; }  // Paths of created assets
    public IReadOnlyList<string> DeletedAssetPaths { get; }  // Paths of deleted assets
    public bool HasCreatedAssets { get; }               // True if any created
    public bool HasDeletedAssets { get; }               // True if any deleted
}
```

---

## Best Practices

### Performance Considerations

1. **Keep methods fast** - They run synchronously during asset import
2. **Avoid heavy operations** - Consider deferring work with `EditorApplication.delayCall`
3. **Use static methods when possible** - Faster invocation, no instance required

### Design Patterns

```csharp
// ✅ GOOD: Static method for global cache
[DetectAssetChanged(typeof(Sprite), AssetChangeFlags.Created | AssetChangeFlags.Deleted)]
private static void OnSpriteChanged()
{
    SpriteManager.InvalidateCache();
}

// ✅ GOOD: Instance method for component-specific logic
[DetectAssetChanged(typeof(AudioClip), AssetChangeFlags.Created)]
private void OnAudioClipCreated(AudioClip clip)
{
    if (clip.name.StartsWith(audioPrefix))
    {
        RegisterClip(clip);
    }
}

// ⚠️ CAUTION: Expensive operation during import
[DetectAssetChanged(typeof(Texture2D), AssetChangeFlags.Created)]
private static void OnTextureCreated(Texture2D texture)
{
    // Heavy processing - consider deferring
    ProcessTexture(texture);
}
```

### Avoiding Reentrant Issues

```csharp
[DetectAssetChanged(typeof(Material), AssetChangeFlags.Created)]
private static void OnMaterialCreated(string assetPath)
{
    // ❌ BAD: Creating assets during asset processing can cause loops
    // AssetDatabase.CreateAsset(newMaterial, "Assets/Generated.mat");

    // ✅ GOOD: Defer asset creation
    EditorApplication.delayCall += () =>
    {
        AssetDatabase.CreateAsset(newMaterial, "Assets/Generated.mat");
    };
}
```

---

## Examples

### Cache Invalidation

```csharp
public class TextureAtlas : ScriptableObject
{
    private static List<Texture2D> _cachedTextures;

    [DetectAssetChanged(typeof(Texture2D), AssetChangeFlags.Created | AssetChangeFlags.Deleted)]
    private static void OnTextureChanged()
    {
        _cachedTextures = null; // Invalidate cache
    }
}
```

### Auto-Configuration

```csharp
public class MaterialValidator : ScriptableObject
{
    [DetectAssetChanged(typeof(Material), AssetChangeFlags.Created)]
    private static void ValidateNewMaterials(Material[] createdMaterials, string[] deletedPaths)
    {
        foreach (Material material in createdMaterials)
        {
            if (material.shader.name == "Standard")
            {
                // Apply project-wide defaults
                material.SetFloat("_Metallic", 0.0f);
                material.SetFloat("_Glossiness", 0.5f);
                EditorUtility.SetDirty(material);
            }
        }
    }
}
```

### Derived Type Monitoring

```csharp
public abstract class GameData : ScriptableObject { }

public class DataRegistry : ScriptableObject
{
    private static readonly HashSet<string> RegisteredPaths = new();

    [DetectAssetChanged(
        typeof(GameData),
        AssetChangeFlags.Created | AssetChangeFlags.Deleted,
        DetectAssetChangedOptions.IncludeAssignableTypes
    )]
    private static void OnGameDataChanged(GameData[] created, string[] deletedPaths)
    {
        foreach (GameData data in created)
        {
            string path = AssetDatabase.GetAssetPath(data);
            RegisteredPaths.Add(path);
            Debug.Log($"Registered: {data.GetType().Name} at {path}");
        }

        foreach (string path in deletedPaths)
        {
            RegisteredPaths.Remove(path);
            Debug.Log($"Unregistered: {path}");
        }
    }
}
```

### Prefab-Based Instance Methods

Use `SearchPrefabs` to invoke instance methods on MonoBehaviours attached to prefabs:

```csharp
public class SpriteCache : MonoBehaviour
{
    [SerializeField] private List<Sprite> _cachedSprites = new();

    [DetectAssetChanged(
        typeof(Sprite),
        AssetChangeFlags.Created | AssetChangeFlags.Deleted,
        DetectAssetChangedOptions.SearchPrefabs
    )]
    private void OnSpriteChanged(AssetChangeContext context)
    {
        // This instance method is called on the prefab asset
        Debug.Log($"SpriteCache on prefab received sprite change: {context.Flags}");
        RefreshCache();
    }

    private void RefreshCache()
    {
        _cachedSprites.Clear();
        // Rebuild cache...
    }
}
```

**When to use:** When your MonoBehaviour needs instance-specific state or serialized fields

### Scene Object Instance Methods

Use `SearchSceneObjects` to invoke instance methods on MonoBehaviours in open scenes:

```csharp
public class LiveAssetWatcher : MonoBehaviour
{
    [SerializeField] private string _watchedFolder;

    [DetectAssetChanged(
        typeof(Texture2D),
        AssetChangeFlags.Created,
        DetectAssetChangedOptions.SearchSceneObjects
    )]
    private void OnTextureCreated(AssetChangeContext context)
    {
        // Called on every LiveAssetWatcher instance in all open scenes
        foreach (string path in context.CreatedAssetPaths)
        {
            if (path.StartsWith(_watchedFolder))
            {
                Debug.Log($"{name} detected new texture: {path}");
                HandleNewTexture(path);
            }
        }
    }

    private void HandleNewTexture(string path) { /* ... */ }
}
```

**When to use:** For editor tools that need to react to changes based on scene-specific configuration

### Combined Prefab and Scene Search

Use both options together to find handlers in both prefabs and open scenes:

```csharp
public class UniversalAssetHandler : MonoBehaviour
{
    [DetectAssetChanged(
        typeof(AudioClip),
        AssetChangeFlags.Created | AssetChangeFlags.Deleted,
        DetectAssetChangedOptions.SearchPrefabs | DetectAssetChangedOptions.SearchSceneObjects
    )]
    private void OnAudioClipChanged(AssetChangeContext context)
    {
        // Called on instances in both prefabs AND scene objects
        Debug.Log($"{name} (on {gameObject.name}) received audio change");
    }
}
```

**Performance Note:** Searching prefabs and scenes has overhead. Use these options only when you need instance-specific behavior. For simple notifications, prefer static methods.

---

## Implementation Details

The `DetectAssetChangeProcessor` (Editor assembly) automatically:

1. Scans for methods decorated with `[DetectAssetChanged]`
2. Registers callbacks with Unity's `AssetPostprocessor`
3. Invokes methods when matching assets change
4. Handles null checks and error cases
5. Supports both Edit Mode and Play Mode

**Threading:** All callbacks execute on the main thread during asset processing

**Timing:** Methods are called after Unity completes asset import/deletion

---

## Troubleshooting

### Method Is Not Called

- Ensure the method is in a type that Unity can discover (not in a generic class)
- Check that the asset type matches exactly (unless using `IncludeAssignableTypes`)
- Verify the asset change flags match the operation (Created vs. Deleted)
- **For MonoBehaviour instance methods:**
  - Use `SearchPrefabs` if the handler is on a prefab asset
  - Use `SearchSceneObjects` if the handler is on a GameObject in a scene
  - Instance methods without these options only work for ScriptableObjects saved as assets

### MonoBehaviour Instance Methods Not Working

If your instance method on a MonoBehaviour isn't being called:

1. **On a prefab?** Add `DetectAssetChangedOptions.SearchPrefabs`
2. **In a scene?** Add `DetectAssetChangedOptions.SearchSceneObjects`
3. **Need both?** Combine: `SearchPrefabs | SearchSceneObjects`
4. **Don't need instance state?** Use a `static` method instead (most efficient)

### Performance Issues

- Profile with Unity Profiler during asset import
- Consider deferring work with `EditorApplication.delayCall`
- Use `static` methods to avoid unnecessary instance lookups
- **Avoid `SearchPrefabs` in large projects** - it loads all prefabs to check for components
- **Avoid `SearchSceneObjects` with many open scenes** - searches all loaded scenes

### Resetting Loop Protection

If a callback repeatedly creates more matching asset changes, the watcher enters loop protection and skips additional batches until it is reset. After fixing the callback or clearing the bad state, editor tools can resume dispatch without a domain reload:

```csharp
using WallstopStudios.UnityHelpers.Editor.AssetProcessors;

AssetChangeDetectionUtility.ResetLoopProtection();
```

The reset clears only loop-protection state and queued changes. It preserves discovered watchers and subscriptions.

### Null Reference Exceptions

- Remember: asset parameter is `null` for deletion events
- Always null-check when handling `AssetChangeFlags.Deleted`

---

## Related Features

- [Attribute Metadata Cache Generator](./editor-tools-guide.md#attribute-metadata-cache-generator) - Caches attribute metadata for fast lookup
- [ScriptableObject Singleton Creator](./editor-tools-guide.md#scriptableobject-singleton-creator) - Auto-creates singleton assets
- [Inspector Attributes](../inspector/inspector-overview.md) - Other custom inspector features
