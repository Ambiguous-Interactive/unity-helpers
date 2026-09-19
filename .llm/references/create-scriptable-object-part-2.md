# create-scriptable-object - Part 2

## Split Content

### Buttons

```csharp
// Add inspector button to invoke method
[WButton("Refresh Cache")]
private void RefreshCache()
{
    // Implementation
}

// Button with placement control
[WButton("Validate", WButtonGroupPlacement.Below)]
private void Validate()
{
    // Implementation
}
```

---

## OnValidate for Editor-Time Validation

Use `OnValidate()` to enforce constraints and update computed values when the asset is modified in the Editor:

```csharp
#if UNITY_EDITOR
private void OnValidate()
{
    // Clamp values
    _timeout = Mathf.Max(0f, _timeout);

    // Ensure list is initialized
    if (_items == null)
    {
        _items = new List<Item>();
    }

    // Update computed fields
    _cachedDescription = BuildDescription();

    // Mark dirty if changes were made programmatically
    UnityEditor.EditorUtility.SetDirty(this);
}
#endif
```

---

## Serialization Considerations

### JSON/Protobuf Compatibility

For ScriptableObjects that may be serialized to JSON or Protobuf:

```csharp
using System.Text.Json.Serialization;

public sealed class MySerializableData : ScriptableObject
{
    // Include in JSON serialization
    public string id;
    public float value;

    // Exclude Unity-specific references from JSON
    [JsonIgnore]
    public GameObject prefab;

    [JsonIgnore]
    public List<CosmeticEffectData> cosmetics = new();
}
```

### Unity Serialization

```csharp
// Use [SerializeField] for private fields that need serialization
[SerializeField]
private float _internalValue;

// Use [NonSerialized] for runtime-only cached data
[NonSerialized]
private readonly Lazy<ComputedData> _cached;

// Use [FormerlySerializedAs] when renaming fields to preserve data
[FormerlySerializedAs("oldFieldName")]
[SerializeField]
private float _newFieldName;
```

---

## CreateAssetMenu Organization

Follow the menu hierarchy pattern:

```csharp
// Top-level category for the package
[CreateAssetMenu(menuName = "Wallstop Studios/Unity Helpers/{Feature}/{Asset Type}")]

// Examples:
[CreateAssetMenu(menuName = "Wallstop Studios/Unity Helpers/Attribute Effect")]
[CreateAssetMenu(menuName = "Wallstop Studios/Unity Helpers/Effects/Burning Behaviour")]
[CreateAssetMenu(menuName = "Wallstop Studios/Unity Helpers/Settings/Audio Settings")]
```

Optional parameters:

```csharp
[CreateAssetMenu(
    menuName = "Wallstop Studios/Unity Helpers/My Asset",
    fileName = "NewMyAsset",     // Default filename when creating
    order = 100                   // Menu position
)]
```

---

## Odin Inspector Compatibility

Runtime ScriptableObjects in this package use Unity bases unless the package-owned
Odin define enables guarded Sirenix bases. Use the package's own
attributes in runtime assets, and put Odin-specific drawers, editors, and tests in
the dedicated Odin integration folders.

```csharp
public sealed class MyAsset : ScriptableObject
{
    [WShowIf(nameof(showAdvanced))]
    [WGroup("Advanced")]
    public float advancedValue;
}
```

If a test or editor-only integration must compile against Odin/Sirenix types, follow
[integrate-odin-inspector](../skills/integrate-odin-inspector.md) and
[test-odin-drawers](../skills/test-odin-drawers.md). Gate that source with
`WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR` and add only the directly used Sirenix DLLs
to the owning asmdef.

---

## Post-Creation Steps (MANDATORY)

1. **Generate meta file** (required — do not skip):

   ```bash
   ./scripts/generate-meta.sh <path-to-file.cs>
   ```

   > ⚠️ See [create-unity-meta](../skills/create-unity-meta.md) for full details. This step is **mandatory** — every `.cs` file MUST have a corresponding `.meta` file.

2. **Format code**:

   ```bash
   dotnet tool run csharpier format .
   ```

3. **Verify no errors**:
   - Check IDE for compilation errors
   - Ensure `.asmdef` references are correct if adding new namespaces

4. **Update documentation** (MANDATORY for user-facing ScriptableObjects):
   - Add CHANGELOG entry in `### Added` section
   - Document the asset type in `docs/features/`
   - Add XML documentation (`///`) on all public members
   - Include usage examples in documentation
   - See [update-documentation](../skills/update-documentation.md) for standards

---
