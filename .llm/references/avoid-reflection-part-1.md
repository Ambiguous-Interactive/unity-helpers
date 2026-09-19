# avoid-reflection - Part 1

## Split Content

**Trigger**: Any code that would normally use reflection to access types, fields, properties, or methods within our own codebase.

---

## Core Principle

**Reflection on OUR code is FORBIDDEN unless absolutely impossible to avoid.**

Instead of using reflection to access private/internal members of WallstopStudios code, use `internal` visibility combined with `InternalsVisibleTo` attributes. This provides:

1. **Compile-time safety** - Errors caught at build time, not runtime
2. **Refactoring support** - IDE rename/refactor works correctly
3. **Performance** - Direct calls are faster than reflection
4. **Maintainability** - No magic strings that break silently

---

## Detailed Rules

### ❌ NEVER Use Reflection on Our Code

Do not use these reflection APIs to access WallstopStudios code members:

- `Type.GetField()` / `FieldInfo.GetValue()` / `FieldInfo.SetValue()`
- `Type.GetProperty()` / `PropertyInfo.GetValue()` / `PropertyInfo.SetValue()`
- `Type.GetMethod()` / `MethodInfo.Invoke()`
- `Type.GetType(string typeName)` with our type names
- `Activator.CreateInstance()` with non-public constructors of our types

### ✅ Use Internal + InternalsVisibleTo Instead

Change `private` members to `internal` and rely on the `InternalsVisibleTo` infrastructure already in place.

```csharp
// ❌ FORBIDDEN - Reflection on our code
var field = typeof(OurClass).GetField("_someField", BindingFlags.NonPublic | BindingFlags.Instance);
var value = field.GetValue(instance);

var method = typeof(OurClass).GetMethod("PrivateMethod", BindingFlags.NonPublic | BindingFlags.Static);
method.Invoke(null, args);

// ✅ CORRECT - Change to internal and call directly
// In production code (Runtime/ or Editor/):
internal class OurClass
{
    internal int _someField;  // Changed from private

    internal static void PrivateMethod(int arg) { ... }  // Changed from private
}

// In test code (InternalsVisibleTo already grants access):
var value = instance._someField;  // Direct access
OurClass.PrivateMethod(42);       // Direct call
```

---

## Magic Strings Rule

### ❌ NEVER Use String Literals for Our Code Names

Do not use string literals to reference:

- Field names of our code
- Property names of our code
- Method names of our code
- Type names of our code

### ✅ Use nameof() for Compile-Time Safety

```csharp
// ❌ FORBIDDEN - Magic strings
serializedObject.FindProperty("_health");
typeof(PlayerData).GetProperty("Score");

// ✅ CORRECT - nameof() operator
serializedObject.FindProperty(nameof(PlayerController._health));  // Requires internal visibility
typeof(PlayerData).GetProperty(nameof(PlayerData.Score));
```

### Exception: Unity Internal Properties

Unity's internal serialized property names (like `m_Script`, `m_Name`, `m_LocalPosition`) are acceptable as string literals since we cannot control Unity's internals:

```csharp
// ✅ ACCEPTABLE - Unity internal property names
serializedObject.FindProperty("m_Script");
serializedObject.FindProperty("m_LocalPosition");
```

---

## Acceptable Reflection Cases

Reflection IS acceptable when:

### 1. Accessing External Libraries

When accessing non-public members of Unity, Odin Inspector, Reflex, Zenject, VContainer, or other third-party libraries where no public API exists:

```csharp
// ✅ ACCEPTABLE - External library with no public API
var odinField = typeof(SirenixInspectorType).GetField("internalField", BindingFlags.NonPublic | BindingFlags.Instance);
```

### 2. Testing Reflection Utilities Themselves

The `ReflectionHelpers` library tests need to use reflection to test that the reflection utilities work correctly:

```csharp
// ✅ ACCEPTABLE - Testing the reflection utilities themselves
[Test]
public void ReflectionHelper_GetPrivateField_ReturnsValue()
{
    // This is testing the reflection helper, so reflection is required
    var result = ReflectionHelper.GetFieldValue(target, "_privateField");
    Assert.AreEqual(expected, result);
}
```

### 3. Documented Necessity

When reflection is truly unavoidable, document WHY with a comment:

```csharp
// ✅ ACCEPTABLE - With documentation
// REFLECTION REQUIRED: Unity's SerializedProperty doesn't expose the backing field type
// for generic serialized references, must use reflection to get the actual type.
var typeField = typeof(SerializedProperty).GetField("m_InternalType", BindingFlags.NonPublic | BindingFlags.Instance);
```

---

## InternalsVisibleTo Infrastructure

### Assembly Info Files

The following `AssemblyInfo.cs` files define `InternalsVisibleTo` access:

| File                                              | Purpose                                                      |
| ------------------------------------------------- | ------------------------------------------------------------ |
| `Runtime/AssemblyInfo.cs`                         | Main runtime assembly - grants access to all test assemblies |
| `Editor/AssemblyInfo.cs`                          | Editor assembly - grants access to editor test assemblies    |
| `Runtime/Integrations/Zenject/AssemblyInfo.cs`    | Zenject integration                                          |
| `Runtime/Integrations/VContainer/AssemblyInfo.cs` | VContainer integration                                       |
| `Runtime/Integrations/Reflex/AssemblyInfo.cs`     | Reflex integration                                           |
| `Tests/Runtime/AssemblyInfo.cs`                   | Test assembly - grants access for test utilities             |

### Test Assemblies with Internal Access

The main `Runtime/AssemblyInfo.cs` grants internal access to:

- `WallstopStudios.UnityHelpers.Tests.Editor`
- `WallstopStudios.UnityHelpers.Tests.Runtime`
- `WallstopStudios.UnityHelpers.Tests.Runtime.Random`
- `WallstopStudios.UnityHelpers.Tests.Runtime.Performance`
- `WallstopStudios.UnityHelpers.Tests.Core`
- `WallstopStudios.UnityHelpers.Tests.Runtime.Zenject`
- `WallstopStudios.UnityHelpers.Tests.Runtime.VContainer`
- `WallstopStudios.UnityHelpers.Tests.Runtime.Reflex`
- `WallstopStudios.UnityHelpers.Tests.Editor.VContainer`
- `WallstopStudios.UnityHelpers.Tests.Editor.Zenject`
- `WallstopStudios.UnityHelpers.Tests.Editor.Reflex`
- `WallstopStudios.UnityHelpers.Editor`

---
