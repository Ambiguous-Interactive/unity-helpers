# avoid-reflection - Part 2

## Split Content

## Adding New Assemblies

When creating a new assembly that needs to access internal members:

### 1. Add InternalsVisibleTo to Source Assembly

Edit the source assembly's `AssemblyInfo.cs`:

```csharp
// In Runtime/AssemblyInfo.cs or Editor/AssemblyInfo.cs
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("YourNewAssemblyName")]
```

### 2. For New Test Assemblies

If creating a new test assembly, add it to ALL relevant `AssemblyInfo.cs` files:

```csharp
// Add to Runtime/AssemblyInfo.cs if testing runtime code
[assembly: InternalsVisibleTo("WallstopStudios.UnityHelpers.Tests.YourNewTests")]

// Add to Editor/AssemblyInfo.cs if testing editor code
[assembly: InternalsVisibleTo("WallstopStudios.UnityHelpers.Tests.YourNewTests")]
```

### 3. For Integration Assemblies

If creating an integration with a third-party library, create a new `AssemblyInfo.cs` in the integration folder following the pattern of existing integrations.

---

## Quick Reference

| Situation                           | Action                             |
| ----------------------------------- | ---------------------------------- |
| Test needs to access private field  | Change field to `internal`         |
| Test needs to call private method   | Change method to `internal`        |
| Need to reference member by name    | Use `nameof()`                     |
| Accessing Unity internals           | Reflection OK, use string          |
| Accessing Odin/Reflex/etc internals | Reflection OK, document why        |
| New test assembly needs access      | Add `InternalsVisibleTo` attribute |

---

## Anti-Patterns to Avoid

```csharp
// ❌ ANTI-PATTERN: Reflection to avoid making something internal
typeof(MyClass).GetField("_data", BindingFlags.NonPublic | BindingFlags.Instance);

// ❌ ANTI-PATTERN: String literals for our member names
var prop = serializedObject.FindProperty("playerHealth");

// ❌ ANTI-PATTERN: Type.GetType with our type names
var type = Type.GetType("WallstopStudios.UnityHelpers.Runtime.MyClass");

// ❌ ANTI-PATTERN: Activator for our types with non-public constructors
var instance = Activator.CreateInstance(typeof(OurClass), true);
```

---

## See Also

- [Defensive Programming](../skills/defensive-programming.md) - General defensive coding practices
- [Create Tests](../skills/create-test.md) - Test creation guidelines
- [High-Performance C#](../skills/high-performance-csharp.md) - Performance considerations (reflection is slow)
