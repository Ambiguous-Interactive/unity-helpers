# integrate-optional-dependency - Part 3

## Split Content

## Assembly Definition Configuration

When optional dependencies affect assembly definitions, use Version Defines:

```json
{
  "name": "WallstopStudios.UnityHelpers.Editor",
  "references": ["WallstopStudios.UnityHelpers"],
  "versionDefines": [
    {
      "name": "odininspector",
      "expression": "0.0.1",
      "define": "WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR"
    },
    {
      "name": "jp.hadashikick.vcontainer",
      "expression": "",
      "define": "VCONTAINER"
    },
    {
      "name": "com.svermeulen.extenject",
      "expression": "",
      "define": "ZENJECT"
    }
  ]
}
```

### Transitive Precompiled References (Critical)

When an assembly has `"overrideReferences": true`, it can ONLY see precompiled DLLs explicitly listed in its `precompiledReferences`. These references do NOT propagate transitively through assembly references.

**Example**: an assembly that directly compiles Odin test targets derived from `SerializedScriptableObject` must include `Sirenix.Serialization.dll` in its own `precompiledReferences` and gate those files with `WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR`. Runtime conditional Odin base aliases are the only runtime Sirenix use; do not add Sirenix DLLs merely because an assembly references `WallstopStudios.UnityHelpers`.

**When splitting assemblies**: Always audit the parent assembly's `precompiledReferences` and propagate required DLLs to each child. See [manage-assembly-definitions](../skills/manage-assembly-definitions.md) for the full checklist.

---

## Related Skills

- [integrate-odin-inspector](../skills/integrate-odin-inspector.md) - Detailed Odin Inspector drawer patterns
- [test-odin-drawers](../skills/test-odin-drawers.md) - Testing patterns for Odin drawers
- [add-inspector-attribute](../skills/add-inspector-attribute.md) - Inspector attributes with Odin compatibility
- [create-property-drawer](../skills/create-property-drawer.md) - Unity PropertyDrawer creation patterns
- [editor-caching-patterns](../skills/editor-caching-patterns.md) - Centralized caching for editor code
