# avoid-magic-strings - Part 1

## Split Content

**Trigger**: Any code that references identifiers (field names, method names, property names, type names, class names).

---

## Core Principle

**NEVER use string literals to reference code identifiers. Use compile-time safe alternatives.**

String literals that reference code members are "magic strings" - they appear to work but break silently when code is renamed or refactored. This codebase requires compile-time verification of all member references.

---

## Why This Matters

| Problem with Magic Strings               | Benefit of `nameof()`/`typeof()`                         |
| ---------------------------------------- | -------------------------------------------------------- |
| Break silently when code is renamed      | Causes compile errors if referenced member doesn't exist |
| IDE refactoring tools don't update them  | IDE automatically updates references during rename       |
| No IntelliSense or autocomplete          | Full IntelliSense support                                |
| Easy to introduce typos                  | Compiler catches typos immediately                       |
| Code archaeology required to find usages | Find All References works correctly                      |
| Self-documenting and maintainable        | Explicit connection to actual code                       |

---

## Detailed Rules

### 1. Use `nameof()` for Member Names

Use `nameof()` for all field, property, method, and local variable name references:

```csharp
// ❌ FORBIDDEN - Magic strings
GetMethod("CalculateResult")
GetField("_internalCache")
GetProperty("IsEnabled")
serializedObject.FindProperty("playerHealth");
throw new ArgumentNullException("value");
Debug.Log("Error in ProcessItems method");

// ✅ CORRECT - Compile-time safe
GetMethod(nameof(CalculateResult))
GetField(nameof(_internalCache))  // Requires internal visibility for private fields
GetProperty(nameof(IsEnabled))
serializedObject.FindProperty(nameof(PlayerController._health));
throw new ArgumentNullException(nameof(value));
Debug.Log($"Error in {nameof(ProcessItems)} method");
```

### 2. Use `typeof()` for Type Names

Use `typeof().Name` or `typeof().FullName` for type name references:

```csharp
// ❌ FORBIDDEN - Magic strings
Type.GetType("WallstopStudios.UnityHelpers.SomeClass")
var typeName = "PlayerController";
Log($"Processing type MyNamespace.MyClass");

// ✅ CORRECT - Compile-time safe
typeof(SomeClass)  // Direct type reference when possible
typeof(SomeClass).FullName  // When full name string is needed
var typeName = nameof(PlayerController);
Log($"Processing type {typeof(MyClass).FullName}");
```

### 3. Use Constants for Repeated String Values

When a string value must be used multiple times, define it as a constant:

```csharp
// ❌ BAD - Repeated magic string
if (key == "player_data") { ... }
if (otherKey == "player_data") { ... }
dictionary["player_data"] = value;

// ✅ CORRECT - Centralized constant
private const string PlayerDataKey = "player_data";

if (key == PlayerDataKey) { ... }
if (otherKey == PlayerDataKey) { ... }
dictionary[PlayerDataKey] = value;
```

Generated member names may be unavailable to `nameof()` in the generator and editor assemblies.
Keep those names in one Unity-free constants source, consumed by both emission and discovery. Link
the source into the analyzer project instead of adding a runtime assembly dependency. See
[WProtoGeneratedNames](../../Runtime/Core/Serialization/WallstopProto/WProtoGeneratedNames.cs),
which keeps the generated formatter declarations and subtype discovery in agreement.

For Roslyn syntax, compare tokens and nodes through `SyntaxKind` (`token.IsKind(...)`) instead of
their text. Prefer `SymbolEqualityComparer.Default` when both sides are symbols. When analyzer or
generator logic must compare metadata names, attribute argument keys, or display strings, state
`StringComparison.Ordinal` explicitly so the intended compiler-identity comparison is visible.
Use `StringComparer.Ordinal` for collections keyed by the same names.

---

## Editor Test Patterns

Reaching a serialized property from a test without a string literal -- `nameof()` on a
`protected`/`private` field, `FindPropertyRelative`, nested types, and the
`SerializedPropertyNames` pattern -- is in
[serialized-property-names](../skills/serialized-property-names.md).

---

## Acceptable Magic Strings

The following cases are **exceptions** where string literals are acceptable:

### 1. Unity Internal Properties

Unity's internal serialized property names cannot be referenced via `nameof()`. **However**, define them as constants for consistency:

```csharp
// ✅ BEST - Define constant for Unity internals
private const string ScriptPropertyPath = "m_Script";

SerializedProperty scriptProperty = serializedConfig.FindProperty(ScriptPropertyPath);
if (string.Equals(property.name, ScriptPropertyPath, StringComparison.Ordinal))
{
    continue;
}

// ✅ ACCEPTABLE - Direct string for one-off usage
serializedObject.FindProperty("m_LocalPosition");
serializedObject.FindProperty("Array.size");  // Unity array syntax
```

### 1a. Collection Size Field Names

When checking collection sizes via reflection (for unknown collection types), these patterns are acceptable:

```csharp
// ✅ ACCEPTABLE - Unity/generic collection size field names
property.FindPropertyRelative("Array.size");  // Unity array syntax
property.FindPropertyRelative("_size");       // Generic collection pattern
property.FindPropertyRelative("m_Size");      // Unity internal pattern
```

### 2. External Library Member Names

When accessing members of third-party libraries with no public API:

```csharp
// ✅ ACCEPTABLE - External library internals (document why)
// MAGIC STRING: Odin Inspector internal field, no public API available
var odinField = typeof(SirenixType).GetField("m_InternalValue", BindingFlags.NonPublic | BindingFlags.Instance);
```

### 3. Dynamically Constructed Types

When reflecting on types constructed at runtime (e.g., via `MakeGenericType()`), `nameof()` cannot be used:

```csharp
// ✅ ACCEPTABLE - Dynamically constructed type
Type hashSetType = typeof(HashSet<>).MakeGenericType(elementType);
MethodInfo addMethod = hashSetType.GetMethod("Add", ...);  // Cannot use nameof() here
MethodInfo clearMethod = hashSetType.GetMethod("Clear", ...);
```

### 3a. .NET BCL Type Members

When accessing well-known .NET BCL type members that we don't control:

```csharp
// ✅ ACCEPTABLE - .NET Framework/BCL internal fields (document why)
// System.Random internal state fields (varies by .NET version)
typeof(System.Random).GetField("SeedArray", BindingFlags.NonPublic | BindingFlags.Instance);
typeof(System.Random).GetField("_seedArray", BindingFlags.NonPublic | BindingFlags.Instance);

// ✅ ACCEPTABLE - Standard BCL interface methods
typeof(IComparable<T>).GetMethod("CompareTo", ...);

// ✅ ACCEPTABLE - Task/ValueTask members
taskType.GetMethod("AsTask", ...);  // ValueTask.AsTask()
taskType.GetProperty("Result", ...);  // Task<T>.Result
```
