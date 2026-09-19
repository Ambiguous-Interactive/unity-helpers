# create-csharp-file - Part 3

## Split Content

```csharp
// ✅ CORRECT - Odin directive inside namespace
namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector.Editor;

    public sealed class MyOdinDrawer : OdinAttributeDrawer<MyAttribute>
    {
        // Implementation
    }
#endif
}

// ❌ INCORRECT - Odin directive outside namespace
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
    public sealed class MyOdinDrawer : OdinAttributeDrawer<MyAttribute>
    {
        // Implementation
    }
}
#endif
```

See [integrate-optional-dependency](../skills/integrate-optional-dependency.md) for complete patterns.

### 11. Default Construction of Configuration Objects

Public reference-type options, limits, or configuration objects intended to support default
construction need a real public parameterless constructor. When a configured constructor defines
the defaults, delegate the parameterless constructor to it with explicit intended defaults.
An all-optional constructor permits `new Options()` but does not
satisfy `where T : new()`; verify that contract through a generic factory test asserting the defaults.
Apply this rule to default configuration contracts, not unrelated attributes, required-input types,
Unity objects, structs, or resource-owning services. For structs, define what zero-initialized
`default` means separately; it bypasses constructor logic.

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

3. **Spell-check** (cspell lints C# comments, XML docs, and log strings):

   ```bash
   npm run lint:spelling
   ```

   See [Rule 4: Spell-Check Every Change cspell Covers](../skills/validate-before-commit.md#rule-4-spell-check-every-change-cspell-covers) for the failure-recovery decision tree.

4. **Add XML documentation** for all public types and members:

   ```csharp
   /// <summary>
   /// Brief description of the type or member.
   /// </summary>
   /// <param name="paramName">Description of parameter.</param>
   /// <returns>Description of return value.</returns>
   public int MyMethod(string paramName) { }
   ```

   > See [update-documentation](../skills/update-documentation.md) for XML doc standards.

5. **Update CHANGELOG** for user-facing changes:
   - New features → `### Added` section
   - Bug fixes → `### Fixed` section
   - See [update-documentation](../skills/update-documentation.md) for format

6. **Verify no errors**:
   - Check IDE for compilation errors
   - Ensure `.asmdef` references are correct if adding new namespaces

---

## Related Skills

- [high-performance-csharp](../skills/high-performance-csharp.md) — Zero-allocation patterns (MANDATORY for all code)
- [defensive-programming](../skills/defensive-programming.md) — Robust error handling (MANDATORY for all code)
- [create-test](../skills/create-test.md) — Testing guidelines
- [update-documentation](../skills/update-documentation.md) — Documentation standards
- [create-unity-meta](../skills/create-unity-meta.md) — Meta file generation

---

## Naming Conventions Quick Reference

| Element               | Convention  | Example                     |
| --------------------- | ----------- | --------------------------- |
| Types, public members | PascalCase  | `SerializableDictionary`    |
| Fields, locals        | camelCase   | `keyValue`, `itemCount`     |
| Interfaces            | `I` prefix  | `IResolver`, `ISpatialTree` |
| Type parameters       | `T` prefix  | `TKey`, `TValue`            |
| Events                | `On` prefix | `OnValueChanged`            |
| Constants (public)    | PascalCase  | `DefaultCapacity`           |
